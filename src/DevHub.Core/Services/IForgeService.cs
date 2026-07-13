using DevHub.Core.Models;

namespace DevHub.Core.Services;

/// <summary>
/// A code-hosting service (GitHub, GitLab) accessed with a personal access token.
/// One implementation per <see cref="ForgeKind"/>; consumers get all of them via DI.
/// </summary>
public interface IForgeService
{
    ForgeKind Kind { get; }

    /// <summary>"GitHub" / "GitLab" — also the token-store key and settings key.</summary>
    string DisplayName { get; }

    /// <summary>Host of repositories this forge serves, e.g. "github.com".</summary>
    string Host { get; }

    /// <summary>Where the user can create a personal access token.</summary>
    string TokenHelpUrl { get; }

    /// <summary>Which scopes/permissions the token needs.</summary>
    string TokenScopeHint { get; }

    /// <summary>The account the token belongs to, or null when the token is invalid.</summary>
    Task<ForgeUser?> GetCurrentUserAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>One of the user's activity feeds (open items only), newest first.</summary>
    Task<IReadOnlyList<ForgeItem>> GetFeedAsync(
        ForgeFeed feed, string token, string login, CancellationToken cancellationToken = default);

    /// <summary>Header facts about a repository, or null when it can't be reached with this token.</summary>
    Task<ForgeRepoInfo?> GetRepositoryAsync(
        string token, string repoFullName, CancellationToken cancellationToken = default);

    /// <summary>Open PRs/MRs of a repository, newest first. Empty when unreachable.</summary>
    Task<IReadOnlyList<ForgeItem>> GetRepoPullRequestsAsync(
        string token, string repoFullName, CancellationToken cancellationToken = default);

    /// <summary>Open issues of a repository, newest first. Empty when unreachable.</summary>
    Task<IReadOnlyList<ForgeItem>> GetRepoIssuesAsync(
        string token, string repoFullName, CancellationToken cancellationToken = default);

    /// <summary>Recent CI runs (GitHub Actions / GitLab pipelines), newest first. Empty when unreachable.</summary>
    Task<IReadOnlyList<ForgeWorkflowRun>> GetRepoWorkflowRunsAsync(
        string token, string repoFullName, CancellationToken cancellationToken = default);
}
