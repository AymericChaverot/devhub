using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevHub.Core.Models;
using DevHub.Core.Services;
using Serilog;

namespace DevHub.App.ViewModels;

public enum RemoteMode
{
    None,
    PasteUrl,
    CreateViaForge,
}

public sealed partial class NewProjectWizardViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IProjectScaffolderService _scaffolder;
    private readonly IIdeService _ides;

    public NewProjectWizardViewModel(
        ISettingsService settings,
        IProjectTemplateCatalog templates,
        IProjectScaffolderService scaffolder,
        IIdeService ides,
        ForgeAccountsViewModel forgeAccounts)
    {
        _settings = settings;
        _scaffolder = scaffolder;
        _ides = ides;
        ForgeAccounts = forgeAccounts.Accounts;
        Templates = templates.GetTemplates();
    }

    /// <summary>Raised after a successful <see cref="CreateAsync"/>; the page navigates back on it.</summary>
    public event EventHandler? ScaffoldCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(StepTitle))]
    [NotifyPropertyChangedFor(nameof(ShowBackButton))]
    [NotifyPropertyChangedFor(nameof(ShowNextButton))]
    public partial int CurrentStep { get; set; } = 1;

    public bool IsStep1 => CurrentStep == 1;

    public bool IsStep2 => CurrentStep == 2;

    public bool IsStep3 => CurrentStep == 3;

    public bool IsLastStep => CurrentStep == 3;

    public bool ShowBackButton => CurrentStep > 1;

    public bool ShowNextButton => CurrentStep < 3;

    public string StepTitle => CurrentStep switch
    {
        1 => Loc.T("Wizard_Step1Title", "1. Project metadata"),
        2 => Loc.T("Wizard_Step2Title", "2. Stack"),
        _ => Loc.T("Wizard_Step3Title", "3. Git & forge"),
    };

    // Step 1 — metadata
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScaffoldCommandPreview))]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string? Path { get; set; }

    public ObservableCollection<IdeInfo> AvailableIdes { get; } = [];

    [ObservableProperty]
    public partial IdeInfo? SelectedIde { get; set; }

    // Step 2 — stack
    public IReadOnlyList<ProjectTemplate> Templates { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScaffoldCommandPreview))]
    [NotifyPropertyChangedFor(nameof(ShowRuntimeWarning))]
    public partial ProjectTemplate? SelectedTemplate { get; set; }

    [ObservableProperty]
    public partial bool IsCheckingRuntime { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRuntimeWarning))]
    public partial bool IsRuntimeAvailable { get; set; } = true;

    public bool ShowRuntimeWarning => SelectedTemplate is not null && !IsCheckingRuntime && !IsRuntimeAvailable;

    public string ScaffoldCommandPreview => SelectedTemplate switch
    {
        null => Loc.T("Wizard_NoStackPreview", "No stack — an empty folder is created."),
        { Kind: ScaffoldKind.InProcess } => Loc.T("Wizard_NoCommand", "No command — starter files are created directly."),
        var template => template.ResolveCommand(string.IsNullOrWhiteSpace(Name) ? "my-app" : Name.Trim()),
    };

    private CancellationTokenSource? _runtimeCheckCts;

    partial void OnSelectedTemplateChanged(ProjectTemplate? value)
    {
        // Cancel any check still in flight so rapidly clicking through templates doesn't pile up
        // concurrent process spawns (each one blocking on OS PATH/shim resolution).
        _runtimeCheckCts?.Cancel();
        _runtimeCheckCts?.Dispose();
        _runtimeCheckCts = null;

        if (value is null)
        {
            IsCheckingRuntime = false;
            IsRuntimeAvailable = true;
            return;
        }

        var cts = new CancellationTokenSource();
        _runtimeCheckCts = cts;
        _ = CheckRuntimeAsync(value, cts.Token);
    }

    private async Task CheckRuntimeAsync(ProjectTemplate template, CancellationToken cancellationToken)
    {
        IsCheckingRuntime = true;
        try
        {
            var available = await _scaffolder.IsRuntimeAvailableAsync(template, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                IsRuntimeAvailable = available;
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection; that call owns IsCheckingRuntime/IsRuntimeAvailable now.
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Checking runtime availability for {Template} failed", template.Id);
            if (!cancellationToken.IsCancellationRequested)
            {
                IsRuntimeAvailable = true; // don't block the wizard on a check that itself failed
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsCheckingRuntime = false;
            }
        }
    }

    // Step 3 — git & forge
    [ObservableProperty]
    public partial bool InitializeGit { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRemoteNone))]
    [NotifyPropertyChangedFor(nameof(IsRemotePasteUrl))]
    [NotifyPropertyChangedFor(nameof(IsRemoteCreateViaForge))]
    [NotifyPropertyChangedFor(nameof(HasRemote))]
    public partial RemoteMode RemoteMode { get; set; } = RemoteMode.None;

    public bool IsRemoteNone => RemoteMode == RemoteMode.None;

    public bool IsRemotePasteUrl => RemoteMode == RemoteMode.PasteUrl;

    public bool IsRemoteCreateViaForge => RemoteMode == RemoteMode.CreateViaForge;

    public bool HasRemote => RemoteMode != RemoteMode.None;

    public bool HasForgeAccounts => ForgeAccounts.Count > 0;

    public bool NoForgeAccounts => ForgeAccounts.Count == 0;

    [ObservableProperty]
    public partial string RemoteUrlInput { get; set; } = "";

    public IReadOnlyList<ForgeAccountViewModel> ForgeAccounts { get; }

    [ObservableProperty]
    public partial ForgeAccountViewModel? SelectedForgeAccount { get; set; }

    [ObservableProperty]
    public partial string NewRepoName { get; set; } = "";

    [ObservableProperty]
    public partial bool NewRepoIsPrivate { get; set; } = true;

    [ObservableProperty]
    public partial bool PublishInitialCommit { get; set; }

    // Execution
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    public partial bool IsBusy { get; set; }

    public bool CanCreate => !IsBusy;

    [ObservableProperty]
    public partial string? BusyMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => ErrorMessage is not null;

    public void ClearError() => ErrorMessage = null;

    public ObservableCollection<string> LogLines { get; } = [];

    public async Task LoadAsync()
    {
        Path = _settings.Settings.ProjectsFolderPath;

        var ides = await _ides.GetInstalledIdesAsync();
        AvailableIdes.Clear();
        foreach (var ide in ides)
        {
            AvailableIdes.Add(ide);
        }

        var preferredId = _settings.Settings.PreferredIdeId;
        SelectedIde = AvailableIdes.FirstOrDefault(i => i.Id == preferredId) ?? AvailableIdes.FirstOrDefault();
    }

    [RelayCommand]
    private void Next()
    {
        if (CurrentStep == 1 && (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Path)))
        {
            ErrorMessage = Loc.T("Wizard_NeedNameAndPath", "Enter a project name and a folder first.");
            return;
        }

        ErrorMessage = null;
        CurrentStep = Math.Min(3, CurrentStep + 1);
    }

    [RelayCommand]
    private void Back() => CurrentStep = Math.Max(1, CurrentStep - 1);

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Path))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        LogLines.Clear();
        BusyMessage = Loc.T("Wizard_Starting", "Starting…");
        try
        {
            string? remoteUrl = RemoteMode switch
            {
                RemoteMode.PasteUrl => string.IsNullOrWhiteSpace(RemoteUrlInput) ? null : RemoteUrlInput.Trim(),
                RemoteMode.CreateViaForge => await CreateForgeRepoAsync(),
                _ => null,
            };

            var request = new ScaffoldRequest
            {
                Name = Name.Trim(),
                TargetPath = System.IO.Path.Combine(Path, Name.Trim()),
                Template = SelectedTemplate,
                InitializeGit = InitializeGit,
                RemoteUrl = remoteUrl,
                PublishInitialCommit = PublishInitialCommit && remoteUrl is not null,
                PreferredIde = SelectedIde,
            };

            var result = await _scaffolder.ScaffoldAsync(request, new Progress<ScaffoldProgress>(OnProgress));
            if (result.Success)
            {
                Log.Information("Scaffolded project {Name} at {Path}", request.Name, request.TargetPath);
                ScaffoldCompleted?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = result.ErrorMessage ?? Loc.T("Wizard_Failed", "Scaffolding failed.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Creating the project failed");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }

    [RelayCommand]
    private void Cancel() => ScaffoldCompleted?.Invoke(this, EventArgs.Empty);

    private async Task<string?> CreateForgeRepoAsync()
    {
        if (SelectedForgeAccount is null)
        {
            throw new InvalidOperationException(Loc.T("Wizard_NoForgeAccount", "Choose a connected account first."));
        }

        var repoName = string.IsNullOrWhiteSpace(NewRepoName) ? Name.Trim() : NewRepoName.Trim();
        var repo = await SelectedForgeAccount.CreateRepositoryAsync(repoName, NewRepoIsPrivate);
        return repo.CloneUrl.Length > 0 ? repo.CloneUrl : repo.WebUrl;
    }

    private void OnProgress(ScaffoldProgress progress)
    {
        switch (progress.Status)
        {
            case ScaffoldStepStatus.Running:
                BusyMessage = DescribeStep(progress.Step);
                if (progress.Detail is { Length: > 0 } detail)
                {
                    LogLines.Add(detail);
                }

                break;
            case ScaffoldStepStatus.Succeeded:
                LogLines.Add(Loc.F("Wizard_StepDone", "Done: {0}", DescribeStep(progress.Step)));
                break;
            case ScaffoldStepStatus.Failed:
                LogLines.Add(Loc.F("Wizard_StepFailed", "Failed: {0} — {1}", DescribeStep(progress.Step), progress.Detail ?? ""));
                break;
        }
    }

    private static string DescribeStep(ScaffoldStep step) => step switch
    {
        ScaffoldStep.CreateDirectory => Loc.T("Wizard_StepCreateDir", "Creating project folder…"),
        ScaffoldStep.RunScaffoldCommand => Loc.T("Wizard_StepScaffold", "Running scaffold command…"),
        ScaffoldStep.GitInit => Loc.T("Wizard_StepGitInit", "Initializing git…"),
        ScaffoldStep.GitRemoteAdd => Loc.T("Wizard_StepGitRemote", "Linking remote…"),
        ScaffoldStep.GitCommit => Loc.T("Wizard_StepGitCommit", "Creating initial commit…"),
        ScaffoldStep.GitPush => Loc.T("Wizard_StepGitPush", "Pushing to remote…"),
        ScaffoldStep.RegisterProject => Loc.T("Wizard_StepRegister", "Registering project…"),
        ScaffoldStep.LaunchIde => Loc.T("Wizard_StepLaunchIde", "Opening IDE…"),
        _ => step.ToString(),
    };
}
