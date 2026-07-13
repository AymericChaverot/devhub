namespace DevHub.Core.Models;

/// <summary>A code-hosting service DevHub can connect to.</summary>
public enum ForgeKind
{
    GitHub,
    GitLab,
}

/// <summary>What a <see cref="ForgeItem"/> is.</summary>
public enum ForgeItemKind
{
    PullRequest,
    Issue,
}

/// <summary>Personal activity feeds a forge can answer for the signed-in user.</summary>
public enum ForgeFeed
{
    /// <summary>Open PRs/MRs where my review is requested.</summary>
    ReviewRequests,

    /// <summary>Open PRs/MRs I authored.</summary>
    AuthoredPullRequests,

    /// <summary>Open issues assigned to me.</summary>
    AssignedIssues,
}

/// <summary>The authenticated account on a forge.</summary>
public sealed record ForgeUser(
    string Login,
    string DisplayName,
    string AvatarUrl,
    string ProfileUrl);

/// <summary>A pull/merge request or an issue.</summary>
public sealed record ForgeItem(
    ForgeKind Forge,
    ForgeItemKind Kind,
    string RepoFullName,
    long Number,
    string Title,
    string Author,
    string WebUrl,
    DateTime UpdatedAt,
    bool IsDraft);

/// <summary>Header facts about a remote repository.</summary>
public sealed record ForgeRepoInfo(
    string FullName,
    string? Description,
    int Stars,
    int Forks,
    string WebUrl);

/// <summary>Outcome of a CI run, normalized across forges.</summary>
public enum ForgeRunStatus
{
    Success,
    Failed,
    Running,
    Cancelled,
    Other,
}

/// <summary>A GitHub Actions workflow run or a GitLab pipeline.</summary>
public sealed record ForgeWorkflowRun(
    ForgeKind Forge,
    string Name,
    string Title,
    long Number,
    string Branch,
    string Event,
    ForgeRunStatus Status,
    string WebUrl,
    DateTime UpdatedAt,
    TimeSpan? Duration);
