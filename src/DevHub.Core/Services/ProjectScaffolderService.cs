using System.Text.RegularExpressions;
using DevHub.Core.Models;

namespace DevHub.Core.Services;

public sealed partial class ProjectScaffolderService : IProjectScaffolderService
{
    private const string DefaultBranch = "main";

    private readonly IProjectStore _store;
    private readonly IIdeService _ides;
    private readonly ISettingsService _settings;
    private readonly IGitService _git;

    public ProjectScaffolderService(IProjectStore store, IIdeService ides, ISettingsService settings, IGitService git)
    {
        _store = store;
        _ides = ides;
        _settings = settings;
        _git = git;
    }

    public Task<bool> IsRuntimeAvailableAsync(ProjectTemplate template, CancellationToken cancellationToken = default) =>
        CliProcessRunner.IsAvailableAsync(template.CheckExecutable, cancellationToken);

    public async Task<ScaffoldResult> ScaffoldAsync(
        ScaffoldRequest request, IProgress<ScaffoldProgress> progress, CancellationToken cancellationToken = default)
    {
        if (!NameRegex().IsMatch(request.Name))
        {
            return new ScaffoldResult(
                false, null, [], "The project name can only contain letters, digits, '.', '_' and '-'.");
        }

        var outcomes = new List<ScaffoldStepOutcome>();
        var currentStep = ScaffoldStep.CreateDirectory;
        try
        {
            await RunStepAsync(ScaffoldStep.CreateDirectory, progress, outcomes, () =>
            {
                CreateTargetDirectory(request);
                return Task.FromResult(request.TargetPath);
            });

            currentStep = ScaffoldStep.RunScaffoldCommand;
            if (request.Template is { } template)
            {
                var scaffoldCwd = template.CreatesOwnDirectory
                    ? Path.GetDirectoryName(request.TargetPath) ?? request.TargetPath
                    : request.TargetPath;
                await RunStepAsync(ScaffoldStep.RunScaffoldCommand, progress, outcomes, () => RunScaffoldCommandAsync(
                    template, request.Name, request.TargetPath, scaffoldCwd,
                    line => progress.Report(new ScaffoldProgress(ScaffoldStep.RunScaffoldCommand, ScaffoldStepStatus.Running, line)),
                    cancellationToken));
            }
            else
            {
                progress.Report(new ScaffoldProgress(ScaffoldStep.RunScaffoldCommand, ScaffoldStepStatus.Skipped));
            }

            var hasRemote = request.InitializeGit && request.RemoteUrl is { Length: > 0 };

            currentStep = ScaffoldStep.GitInit;
            if (request.InitializeGit)
            {
                await RunStepAsync(ScaffoldStep.GitInit, progress, outcomes, async () =>
                {
                    var init = await CliProcessRunner.RunAsync("git", "init", request.TargetPath, null, cancellationToken);
                    var rename = await CliProcessRunner.RunAsync(
                        "git", $"branch -M {DefaultBranch}", request.TargetPath, null, cancellationToken);
                    return $"{init}\n{rename}".Trim();
                });
            }
            else
            {
                progress.Report(new ScaffoldProgress(ScaffoldStep.GitInit, ScaffoldStepStatus.Skipped));
            }

            currentStep = ScaffoldStep.GitRemoteAdd;
            if (hasRemote)
            {
                await RunStepAsync(ScaffoldStep.GitRemoteAdd, progress, outcomes, () => CliProcessRunner.RunAsync(
                    "git", $"remote add origin {request.RemoteUrl}", request.TargetPath, null, cancellationToken));
            }
            else
            {
                progress.Report(new ScaffoldProgress(ScaffoldStep.GitRemoteAdd, ScaffoldStepStatus.Skipped));
            }

            currentStep = ScaffoldStep.GitCommit;
            if (request.InitializeGit)
            {
                await RunStepAsync(ScaffoldStep.GitCommit, progress, outcomes, async () =>
                {
                    await CliProcessRunner.RunAsync("git", "add -A", request.TargetPath, null, cancellationToken);
                    return await CliProcessRunner.RunAsync(
                        "git", """commit -m "feat: initial scaffolding" """, request.TargetPath, null, cancellationToken);
                });
            }
            else
            {
                progress.Report(new ScaffoldProgress(ScaffoldStep.GitCommit, ScaffoldStepStatus.Skipped));
            }

            currentStep = ScaffoldStep.GitPush;
            if (hasRemote && request.PublishInitialCommit)
            {
                await RunStepAsync(ScaffoldStep.GitPush, progress, outcomes, () =>
                    _git.PushAsync(request.TargetPath, "origin", DefaultBranch, setUpstream: true, cancellationToken));
            }
            else
            {
                progress.Report(new ScaffoldProgress(ScaffoldStep.GitPush, ScaffoldStepStatus.Skipped));
            }

            currentStep = ScaffoldStep.RegisterProject;
            Project? project = null;
            await RunStepAsync(ScaffoldStep.RegisterProject, progress, outcomes, async () =>
            {
                project = new Project
                {
                    Name = request.Name,
                    Path = request.TargetPath,
                    GitRemoteUrl = hasRemote ? request.RemoteUrl : null,
                    IsGitRepository = request.InitializeGit,
                    LastScannedAt = DateTime.UtcNow,
                    Languages = LanguageDetector.Detect(request.TargetPath),
                    Frameworks = FrameworkDetector.Detect(request.TargetPath),
                };
                await _store.UpsertAsync(project, cancellationToken);

                if (request.PreferredIde is not null)
                {
                    _settings.Settings.ProjectIdePreferences[project.Path] = request.PreferredIde.Id;
                    await _settings.SaveAsync(cancellationToken);
                }

                return project.Path;
            });

            currentStep = ScaffoldStep.LaunchIde;
            if (request.PreferredIde is not null)
            {
                await RunStepAsync(ScaffoldStep.LaunchIde, progress, outcomes, async () =>
                {
                    await _ides.OpenAsync(request.PreferredIde, request.TargetPath);
                    return request.PreferredIde.DisplayName;
                });
            }
            else
            {
                progress.Report(new ScaffoldProgress(ScaffoldStep.LaunchIde, ScaffoldStepStatus.Skipped));
            }

            return new ScaffoldResult(true, project, outcomes, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            outcomes.Add(new ScaffoldStepOutcome(currentStep, false, ex.Message));
            progress.Report(new ScaffoldProgress(currentStep, ScaffoldStepStatus.Failed, ex.Message));
            return new ScaffoldResult(false, null, outcomes, ex.Message);
        }
    }

    private static async Task RunStepAsync(
        ScaffoldStep step,
        IProgress<ScaffoldProgress> progress,
        List<ScaffoldStepOutcome> outcomes,
        Func<Task<string>> action)
    {
        progress.Report(new ScaffoldProgress(step, ScaffoldStepStatus.Running));
        var output = await action();
        outcomes.Add(new ScaffoldStepOutcome(step, true, output));
        progress.Report(new ScaffoldProgress(step, ScaffoldStepStatus.Succeeded, output));
    }

    /// <summary>
    /// Templates like "cargo new"/"create-next-app"/"dotnet new -o" create their own target
    /// folder and fail if it already exists, so those never get a pre-created directory.
    /// </summary>
    private static void CreateTargetDirectory(ScaffoldRequest request)
    {
        if (Directory.Exists(request.TargetPath) && Directory.EnumerateFileSystemEntries(request.TargetPath).Any())
        {
            throw new InvalidOperationException($"'{request.TargetPath}' already exists and is not empty.");
        }

        if (request.Template?.CreatesOwnDirectory == true)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.TargetPath) ?? request.TargetPath);
        }
        else
        {
            Directory.CreateDirectory(request.TargetPath);
        }
    }

    private static Task<string> RunScaffoldCommandAsync(
        ProjectTemplate template,
        string projectName,
        string targetPath,
        string workingDirectory,
        Action<string> onOutputLine,
        CancellationToken cancellationToken)
    {
        if (template.Kind == ScaffoldKind.InProcess)
        {
            return WritePythonStarterAsync(targetPath, cancellationToken);
        }

        var command = template.ResolveCommand(projectName);

        // Routed through PowerShell (matching WindowsTerminalService's precedent) because npm/npx
        // resolve to .cmd/.ps1 shims on Windows, which Process.Start can't launch directly.
        return CliProcessRunner.RunAsync(
            "powershell.exe",
            $"""-NoProfile -ExecutionPolicy Bypass -Command "{command}" """,
            workingDirectory,
            onOutputLine,
            cancellationToken);
    }

    /// <summary>No universal "python new" CLI exists, so this writes a minimal starter in-process.</summary>
    private static async Task<string> WritePythonStarterAsync(string targetPath, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(targetPath, "main.py"),
            "def main():\n    print(\"Hello, world!\")\n\n\nif __name__ == \"__main__\":\n    main()\n",
            cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(targetPath, "requirements.txt"), "", cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(targetPath, ".gitignore"), "__pycache__/\n*.pyc\n.venv/\n", cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(targetPath, "README.md"), $"# {Path.GetFileName(targetPath)}\n", cancellationToken);
        return "Created main.py, requirements.txt, .gitignore, README.md";
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex NameRegex();
}
