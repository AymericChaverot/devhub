using System.Net;
using System.Text.Json;
using DevHub.Core.Models;

namespace DevHub.Core.Services;

/// <summary>GitLab REST API v4 (gitlab.com), authenticated with a personal access token.</summary>
public sealed class GitLabForgeService : IForgeService
{
    private const string ApiBase = "https://gitlab.com/api/v4";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public ForgeKind Kind => ForgeKind.GitLab;

    public string DisplayName => "GitLab";

    public string Host => "gitlab.com";

    public string TokenHelpUrl => "https://gitlab.com/-/user_settings/personal_access_tokens";

    public string TokenScopeHint => "Personal access token with the \"read_api\" scope.";

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
        var username = root.GetProperty("username").GetString() ?? "";
        return new ForgeUser(
            username,
            root.TryGetProperty("name", out var name) ? name.GetString() ?? username : username,
            root.TryGetProperty("avatar_url", out var avatar) ? avatar.GetString() ?? "" : "",
            root.TryGetProperty("web_url", out var url) ? url.GetString() ?? "" : "");
    }

    public async Task<IReadOnlyList<ForgeItem>> GetFeedAsync(
        ForgeFeed feed, string token, string login, CancellationToken cancellationToken = default)
    {
        var user = Uri.EscapeDataString(login);
        var (path, kind) = feed switch
        {
            ForgeFeed.ReviewRequests =>
                ($"merge_requests?state=opened&scope=all&reviewer_username={user}", ForgeItemKind.PullRequest),
            ForgeFeed.AuthoredPullRequests =>
                ($"merge_requests?state=opened&scope=all&author_username={user}", ForgeItemKind.PullRequest),
            _ => ($"issues?state=opened&scope=all&assignee_username={user}", ForgeItemKind.Issue),
        };

        using var response = await SendAsync(
            $"{ApiBase}/{path}&order_by=updated_at&sort=desc&per_page=20", token, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ParseItemsAsync(response, kind, repoFullName: null, cancellationToken);
    }

    public async Task<ForgeRepoInfo?> GetRepositoryAsync(
        string token, string repoFullName, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            $"{ApiBase}/projects/{Uri.EscapeDataString(repoFullName)}", token, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        return new ForgeRepoInfo(
            repoFullName,
            root.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            root.TryGetProperty("star_count", out var stars) ? stars.GetInt32() : 0,
            root.TryGetProperty("forks_count", out var forks) ? forks.GetInt32() : 0,
            root.TryGetProperty("web_url", out var url) ? url.GetString() ?? "" : "");
    }

    public async Task<IReadOnlyList<ForgeItem>> GetRepoPullRequestsAsync(
        string token, string repoFullName, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            $"{ApiBase}/projects/{Uri.EscapeDataString(repoFullName)}/merge_requests"
                + "?state=opened&order_by=updated_at&sort=desc&per_page=30",
            token,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        return await ParseItemsAsync(response, ForgeItemKind.PullRequest, repoFullName, cancellationToken);
    }

    public async Task<IReadOnlyList<ForgeItem>> GetRepoIssuesAsync(
        string token, string repoFullName, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            $"{ApiBase}/projects/{Uri.EscapeDataString(repoFullName)}/issues"
                + "?state=opened&order_by=updated_at&sort=desc&per_page=30",
            token,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        return await ParseItemsAsync(response, ForgeItemKind.Issue, repoFullName, cancellationToken);
    }

    public async Task<IReadOnlyList<ForgeWorkflowRun>> GetRepoWorkflowRunsAsync(
        string token, string repoFullName, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            $"{ApiBase}/projects/{Uri.EscapeDataString(repoFullName)}/pipelines"
                + "?order_by=updated_at&sort=desc&per_page=15",
            token,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var results = new List<ForgeWorkflowRun>();
        foreach (var pipeline in doc.RootElement.EnumerateArray())
        {
            var id = pipeline.GetProperty("id").GetInt64();
            var iid = pipeline.TryGetProperty("iid", out var i) ? i.GetInt64() : id;
            var name = pipeline.TryGetProperty("name", out var n) ? n.GetString() : null;
            var sha = pipeline.TryGetProperty("sha", out var s) ? s.GetString() ?? "" : "";
            var updatedAt = pipeline.GetProperty("updated_at").GetDateTime();
            var status = pipeline.GetProperty("status").GetString() ?? "";

            // The list payload has no duration; created→updated approximates it for finished runs.
            TimeSpan? duration = null;
            if (status is "success" or "failed"
                && pipeline.TryGetProperty("created_at", out var created)
                && created.ValueKind == JsonValueKind.String)
            {
                duration = updatedAt - created.GetDateTime();
            }

            results.Add(new ForgeWorkflowRun(
                ForgeKind.GitLab,
                string.IsNullOrWhiteSpace(name) ? "Pipeline" : name!,
                sha.Length >= 8 ? sha[..8] : sha,
                iid,
                pipeline.TryGetProperty("ref", out var reference) ? reference.GetString() ?? "" : "",
                pipeline.TryGetProperty("source", out var source) ? source.GetString() ?? "" : "",
                MapPipelineStatus(status),
                pipeline.GetProperty("web_url").GetString() ?? "",
                updatedAt,
                duration));
        }

        return results;
    }

    private static ForgeRunStatus MapPipelineStatus(string status) => status switch
    {
        "success" => ForgeRunStatus.Success,
        "failed" => ForgeRunStatus.Failed,
        "canceled" or "cancelled" => ForgeRunStatus.Cancelled,
        "created" or "pending" or "running" or "preparing" or "waiting_for_resource" => ForgeRunStatus.Running,
        _ => ForgeRunStatus.Other,
    };

    private static async Task<IReadOnlyList<ForgeItem>> ParseItemsAsync(
        HttpResponseMessage response, ForgeItemKind kind, string? repoFullName, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var results = new List<ForgeItem>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            // references.full is "group/project!42" (MR) or "group/project#42" (issue);
            // repo-scoped calls already know the repo, feeds derive it from the reference.
            var repo = repoFullName;
            if (repo is null
                && item.TryGetProperty("references", out var references)
                && references.TryGetProperty("full", out var full))
            {
                repo = (full.GetString() ?? "").Split('!', '#')[0];
            }

            results.Add(new ForgeItem(
                ForgeKind.GitLab,
                kind,
                repo ?? "",
                item.GetProperty("iid").GetInt64(),
                item.GetProperty("title").GetString() ?? "",
                item.GetProperty("author").GetProperty("username").GetString() ?? "",
                item.GetProperty("web_url").GetString() ?? "",
                item.GetProperty("updated_at").GetDateTime(),
                item.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True));
        }

        return results;
    }

    private Task<HttpResponseMessage> SendAsync(string url, string token, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("PRIVATE-TOKEN", token);
        return _http.SendAsync(request, cancellationToken);
    }
}
