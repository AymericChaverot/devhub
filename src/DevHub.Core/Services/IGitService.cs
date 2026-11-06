namespace DevHub.Core.Services;

public interface IGitService
{
    /// <summary>
    /// Clones <paramref name="url"/> into a new subfolder of <paramref name="parentDirectory"/>
    /// (folder name derived from the URL). Returns the created folder's full path.
    /// </summary>
    Task<string> CloneAsync(string url, string parentDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Current branch, working-tree status, local branches and recent commits.
    /// Null when the folder is not a git repository.
    /// </summary>
    Task<Models.GitRepositoryInfo?> GetRepositoryInfoAsync(
        string path, int historyCount = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Current branch, ahead/behind its upstream and the number of changed files.
    /// Null when the folder is not a git repository.
    /// </summary>
    Task<Models.GitStatusSummary?> GetStatusSummaryAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Checks out a local branch. Throws when the working tree conflicts.</summary>
    Task CheckoutAsync(string path, string branchName, CancellationToken cancellationToken = default);

    /// <summary>Commit DAG laid out into lanes; null when the folder is not a repository.</summary>
    Task<Models.GitGraph?> GetCommitGraphAsync(
        string path, int maxCount = 150, CancellationToken cancellationToken = default);

    /// <summary>Creates a local branch at HEAD and checks it out.</summary>
    Task CreateBranchAsync(string path, string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Network operations run through the git CLI so the user's credential manager
    /// handles authentication, like an IDE would. Returns the command's output.
    /// </summary>
    Task<string> FetchAsync(string path, CancellationToken cancellationToken = default);

    Task<string> PullAsync(string path, CancellationToken cancellationToken = default);

    Task<string> PushAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Pushes a specific branch to a specific remote, optionally setting it as the upstream.</summary>
    Task<string> PushAsync(
        string path, string remoteName, string branchName, bool setUpstream, CancellationToken cancellationToken = default);

    /// <summary>Returns a unified diff (or file preview for untracked files) for a working-tree file.</summary>
    Task<string> GetFileDiffAsync(
        string path, string relativeFilePath, CancellationToken cancellationToken = default);

    /// <summary>Stages all changes and commits them with <paramref name="message"/>.</summary>
    Task CommitAllAsync(string path, string message, CancellationToken cancellationToken = default);

    /// <summary>Stashes working-tree changes (including untracked files).</summary>
    Task<string> StashAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Pops the latest stash entry.</summary>
    Task<string> StashPopAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Whether the git CLI is available (required for fetch/pull/push).</summary>
    Task<bool> IsGitCliAvailableAsync();
}
