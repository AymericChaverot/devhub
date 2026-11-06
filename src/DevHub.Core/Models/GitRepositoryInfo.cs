namespace DevHub.Core.Models;

/// <summary>Snapshot of a repository's state for the project page's Git panel.</summary>
public sealed record GitRepositoryInfo
{
    /// <summary>Friendly name of HEAD, e.g. "main"; "(detached)" when HEAD is detached.</summary>
    public required string CurrentBranch { get; init; }

    public bool IsDetached { get; init; }

    /// <summary>Commits ahead of the tracked upstream (0 when no upstream).</summary>
    public int Ahead { get; init; }

    public int Behind { get; init; }

    public int Modified { get; init; }

    public int Added { get; init; }

    public int Removed { get; init; }

    public int Untracked { get; init; }

    public bool IsDirty => Modified + Added + Removed + Untracked > 0;

    public IReadOnlyList<GitBranchInfo> Branches { get; init; } = [];

    public IReadOnlyList<GitCommitInfo> Commits { get; init; } = [];

    /// <summary>Individual modified, added, removed, or untracked files in the working tree.</summary>
    public IReadOnlyList<GitWorkingChange> WorkingChanges { get; init; } = [];

    /// <summary>Number of stashed changesets in the repository.</summary>
    public int StashCount { get; init; }

    /// <summary>Total commits reachable from local branches (what the graph can show).</summary>
    public int TotalCommits { get; init; }
}

public sealed record GitBranchInfo(string Name, bool IsCurrent, bool IsRemote = false);

public sealed record GitCommitInfo(string ShortSha, string Message, string Author, DateTimeOffset When);

public sealed record GitWorkingChange(string RelativePath, string StatusSymbol, string StatusLabel);

/// <summary>
/// The at-a-glance state shown on a project row: cheap to compute for every project,
/// unlike <see cref="GitRepositoryInfo"/> which also walks history and branches.
/// </summary>
public sealed record GitStatusSummary(string Branch, int Ahead, int Behind, int ChangedFiles)
{
    public bool IsDirty => ChangedFiles > 0;
}
