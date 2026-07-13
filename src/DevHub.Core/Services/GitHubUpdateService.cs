using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DevHub.Core.Services;

/// <summary>
/// Reads the latest GitHub Release of the DevHub repository (public — no token needed)
/// and downloads its MSIX installer. Never throws from the check: a network failure or
/// a missing release simply reports "no update".
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string Repository = "AymericChaverot/devhub";

    private readonly HttpClient _http;

    public GitHubUpdateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DevHub", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(
        Version currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(
                $"https://api.github.com/repos/{Repository}/releases/latest", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // No release published yet.
                return null;
            }

            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var latest = ParseVersionTag(tag);
            if (latest is null || !IsNewer(latest, currentVersion))
            {
                return null;
            }

            var (assetUrl, assetName) = PickInstallerAsset(root);
            return new UpdateInfo(
                latest,
                root.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } title
                    ? title
                    : tag,
                root.TryGetProperty("body", out var body) ? body.GetString() : null,
                root.TryGetProperty("html_url", out var url) ? url.GetString() ?? "" : "",
                assetUrl,
                assetName);
        }
        catch (Exception)
        {
            // Offline, rate-limited, or unexpected payload — never block the app for this.
            return null;
        }
    }

    public async Task<string> DownloadUpdateAsync(
        UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (update.AssetUrl is null || update.AssetName is null)
        {
            throw new InvalidOperationException("This release has no installer asset to download.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "DevHub-Update");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, update.AssetName);

        using var response = await _http.GetAsync(
            update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(destination);

        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;
            if (total is > 0)
            {
                progress?.Report((double)written / total.Value);
            }
        }

        return destination;
    }

    /// <summary>"v1.2.3" or "1.2.3.4" → the Version; null when the tag is not a version.</summary>
    public static Version? ParseVersionTag(string tag)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out var version) ? version : null;
    }

    /// <summary>Version-compares with missing parts normalized to 0 ("1.2.3" == "1.2.3.0").</summary>
    public static bool IsNewer(Version candidate, Version current) =>
        Normalize(candidate) > Normalize(current);

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    private static (string? Url, string? Name) PickInstallerAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets))
        {
            return (null, null);
        }

        // Prefer the bundle; fall back to a plain .msix.
        foreach (var extension in new[] { ".msixbundle", ".msix" })
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return (asset.GetProperty("browser_download_url").GetString(), name);
                }
            }
        }

        return (null, null);
    }
}
