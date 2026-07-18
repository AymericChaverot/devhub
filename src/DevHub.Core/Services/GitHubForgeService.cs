using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevHub.Core.Models;

namespace DevHub.Core.Services;

/// <summary>GitHub REST API v3, authenticated with a personal access token.</summary>
public sealed class GitHubForgeService : IForgeService
{
    private const string ApiBase = "https://api.github.com";

    private readonly HttpClient _http;

    public GitHubForgeService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DevHub", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public ForgeKind Kind => ForgeKind.GitHub;

    public string DisplayName => "GitHub";

    public string Host => "github.com";

    public string TokenHelpUrl => "https://github.com/settings/tokens";

    public string TokenScopeHint => CoreStrings.Get(
        "Core_GitHubScopeHint",
        "Classic token with the \"repo\" scope recommended. Fine-grained tokens need Issues + Pull requests read access on all your repositories (the reviews feed may stay empty with them).");

    public async Task<ForgeUser?> GetCurrentUserAsync(string token, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync($"{ApiBase}/user", token, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        var login = root.GetProperty("login").GetString() ?? "";
        return new ForgeUser(
            login,
            root.TryGetProperty("name", out var name) ? name.GetString() ?? login : login,
            root.TryGetProperty("avatar_url", out var avatar) ? avatar.GetString() ?? "" : "",
            root.TryGetProperty("html_url", out var url) ? url.GetString() ?? "" : "");
    }

    public async Task<IReadOnlyList<ForgeItem>> GetFeedAsync(
        ForgeFeed feed, string token, string login, CancellationToken cancellationToken = default)
    {
        // The /issues endpoint works with both classic and fine-grained tokens; the
        // search API only sees what classic tokens can. Review requests have no
        // non-search REST endpoint, so that feed still goes through search.
        if (feed == ForgeFeed.RecentlyClosed)
        {
            var closedPrs = await ListMyIssuesAsync("created", pullRequests: true, "closed", token, cancellationToken);
            var closedIssues = await ListMyIssuesAsync("assigned", pullRequests: false, "closed", token, cancellationToken);
            return [.. closedPrs.Concat(closedIssues).OrderByDescending(i => i.UpdatedAt)];
        }

        return feed switch
        {
            ForgeFeed.ReviewRequests => await SearchAsync(
                $"is:pr is:open review-requested:{login}", ForgeItemKind.PullRequest, token, cancellationToken),
            ForgeFeed.AuthoredPullRequests => await ListMyIssuesAsync(
                "created", pullRequests: true, "open", token, cancellationToken),
            _ => await ListMyIssuesAsync("assigned", pullRequests: false, "open", token, cancellationToken),
        };
    }

    private async Task<IReadOnlyList<ForgeItem>> SearchAsync(
        string query, ForgeItemKind kind, string token, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            $"{ApiBase}/search/issues?q={Uri.EscapeDataString(query)}"
                + "&sort=updated&order=desc&per_page=20&advanced_search=true",
            token,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var results = new List<ForgeItem>();
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            results.Add(ParseItem(item, kind, RepoFromRepositoryUrl(item)));
        }

        return results;
    }

    /// <summary>
    /// GET /issues with a filter ("assigned", "created"...). The endpoint mixes issues
    /// and PRs (PR rows carry a "pull_request" key); <paramref name="pullRequests"/>
    /// picks which side to keep.
    /// </summary>
    private async Task<IReadOnlyList<ForgeItem>> ListMyIssuesAsync(
        string filter, bool pullRequests, string state, string token, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            $"{ApiBase}/issues?filter={filter}&state={state}&sort=updated&direction=desc&per_page=30",
            token,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var results = new List<ForgeItem>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("pull_request", out var pr) != pullRequests)
            {
                continue;
            }

            var kind = pullRequests ? ForgeItemKind.PullRequest : ForgeItemKind.Issue;

            // A closed PR is "Merged" once pull_request.merged_at is set; closed issues have no such concept.
            var itemState = state != "closed"
                ? ForgeItemState.Open
                : pullRequests && pr.TryGetProperty("merged_at", out var merged) && merged.ValueKind == JsonValueKind.String
                    ? ForgeItemState.Merged
                    : ForgeItemState.Closed;

            results.Add(ParseItem(item, kind, RepoFromRepositoryUrl(item), itemState));
        }

        return results;
    }

    /// <summary>"https://api.github.com/repos/{owner}/{repo}" → "{owner}/{repo}".</summary>
    private static string RepoFromRepositoryUrl(JsonElement item)
    {
        var repoUrl = item.GetProperty("repository_url").GetString() ?? "";
        return repoUrl[(repoUrl.IndexOf("/repos/", StringComparison.Ordinal) + "/repos/".Length)..];
    }

    public async Task<ForgeRepoInfo?> GetRepositoryAsync(
        string token, string repoFullName, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync($"{ApiBase}/repos/{repoFullName}", token, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        return new ForgeRepoInfo(
            repoFullName,
            root.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            root.TryGetProperty("stargazers_count", out var stars) ? stars.GetInt32() : 0,
            root.TryGetProperty("forks_count", out var forks) ? forks.GetInt32() : 0,
            root.TryGetProperty("html_url", out var url) ? url.GetString() ?? "" : "");
    }

    public async Task<IReadOnlyList<ForgeItem>> GetRepoPullRequestsAsync(
        string token, string repoFullName, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            $"{ApiBase}/repos/{repoFullName}/pulls?state=open&sort=updated&direction=desc&per_page=30",
            token,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return [.. doc.RootElement.EnumerateArray()
            .Select(item => ParseItem(item, ForgeItemKind.PullRequest, repoFullName))];
    }

    public async Task<IReadOnlyList<ForgeItem>> GetRepoIssuesAsync(
        string token, string repoFullName, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            $"{ApiBase}/repos/{repoFullName}/issues?state=open&sort=updated&direction=desc&per_page=30",
            token,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        // The issues endpoint also returns PRs; those rows carry a "pull_request" key.
        return [.. doc.RootElement.EnumerateArray()
            .Where(item => !item.TryGetProperty("pull_request", out _))
            .Select(item => ParseItem(item, ForgeItemKind.Issue, repoFullName))];
    }

    public async Task<IReadOnlyList<ForgeWorkflowRun>> GetRepoWorkflowRunsAsync(
        string token, string repoFullName, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            $"{ApiBase}/repos/{repoFullName}/actions/runs?per_page=15",
            token,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var results = new List<ForgeWorkflowRun>();
        foreach (var run in doc.RootElement.GetProperty("workflow_runs").EnumerateArray())
        {
            var name = run.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var title = run.TryGetProperty("display_title", out var t) ? t.GetString() ?? "" : "";
            var status = run.GetProperty("status").GetString() ?? "";
            var updatedAt = run.GetProperty("updated_at").GetDateTime();

            // Wall-clock duration of finished runs (start → last update).
            TimeSpan? duration = null;
            if (status == "completed"
                && run.TryGetProperty("run_started_at", out var started)
                && started.ValueKind == JsonValueKind.String)
            {
                duration = updatedAt - started.GetDateTime();
            }

            results.Add(new ForgeWorkflowRun(
                ForgeKind.GitHub,
                name.Length > 0 ? name : title,
                title == name ? "" : title,
                run.TryGetProperty("run_number", out var number) ? number.GetInt64() : 0,
                run.TryGetProperty("head_branch", out var branch) ? branch.GetString() ?? "" : "",
                run.TryGetProperty("event", out var evt) ? evt.GetString() ?? "" : "",
                MapRunStatus(status, run.TryGetProperty("conclusion", out var c) ? c.GetString() : null),
                run.GetProperty("html_url").GetString() ?? "",
                updatedAt,
                duration));
        }

        return results;
    }

    /// <summary>status is "completed" once done; the outcome then lives in conclusion.</summary>
    private static ForgeRunStatus MapRunStatus(string status, string? conclusion)
    {
        if (status != "completed")
        {
            return ForgeRunStatus.Running;
        }

        return conclusion switch
        {
            "success" => ForgeRunStatus.Success,
            "failure" or "timed_out" or "startup_failure" => ForgeRunStatus.Failed,
            "cancelled" => ForgeRunStatus.Cancelled,
            _ => ForgeRunStatus.Other,
        };
    }

    private static ForgeItem ParseItem(
        JsonElement item, ForgeItemKind kind, string repoFullName, ForgeItemState state = ForgeItemState.Open) =>
        new(
            ForgeKind.GitHub,
            kind,
            repoFullName,
            item.GetProperty("number").GetInt64(),
            item.GetProperty("title").GetString() ?? "",
            item.GetProperty("user").GetProperty("login").GetString() ?? "",
            item.GetProperty("html_url").GetString() ?? "",
            item.GetProperty("updated_at").GetDateTime(),
            item.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True,
            state);

    private Task<HttpResponseMessage> SendAsync(string url, string token, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _http.SendAsync(request, cancellationToken);
    }
}
