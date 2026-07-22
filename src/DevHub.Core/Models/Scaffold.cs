namespace DevHub.Core.Models;

/// <summary>A step of <see cref="Services.IProjectScaffolderService.ScaffoldAsync"/>'s sequence.</summary>
public enum ScaffoldStep
{
    CreateDirectory,
    RunScaffoldCommand,
    GitInit,
    GitRemoteAdd,
    GitCommit,
    GitPush,
    RegisterProject,
    LaunchIde,
}

public enum ScaffoldStepStatus
{
    Running,
    Succeeded,
    Failed,

    /// <summary>Step didn't apply to this request (e.g. no remote URL was given).</summary>
    Skipped,
}

/// <summary>
/// One progress update: a step transition, or (for <see cref="ScaffoldStep.RunScaffoldCommand"/>)
/// a single captured output line while it's still <see cref="ScaffoldStepStatus.Running"/>.
/// </summary>
public sealed record ScaffoldProgress(ScaffoldStep Step, ScaffoldStepStatus Status, string? Detail = null);

/// <summary>Everything the wizard collected, ready for <see cref="Services.IProjectScaffolderService"/>.</summary>
public sealed record ScaffoldRequest
{
    public required string Name { get; init; }

    public required string TargetPath { get; init; }

    /// <summary>Null means "no stack" — an empty folder is created and scaffolding is skipped.</summary>
    public ProjectTemplate? Template { get; init; }

    public bool InitializeGit { get; init; }

    /// <summary>Origin URL, whether pasted or created via a connected forge account. Null = no remote.</summary>
    public string? RemoteUrl { get; init; }

    public bool PublishInitialCommit { get; init; }

    public IdeInfo? PreferredIde { get; init; }
}

public sealed record ScaffoldStepOutcome(ScaffoldStep Step, bool Success, string Output);

public sealed record ScaffoldResult(
    bool Success,
    Project? Project,
    IReadOnlyList<ScaffoldStepOutcome> Steps,
    string? ErrorMessage);
