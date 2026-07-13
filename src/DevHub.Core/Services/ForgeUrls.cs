namespace DevHub.Core.Services;

/// <summary>Parses git remote URLs into forge coordinates (host, "owner/repo").</summary>
public static class ForgeUrls
{
    /// <summary>
    /// Extracts the host and full repo path from an https or ssh git remote.
    /// "https://github.com/user/repo.git" → ("github.com", "user/repo");
    /// "git@gitlab.com:group/sub/repo.git" → ("gitlab.com", "group/sub/repo").
    /// </summary>
    public static bool TryParseRepo(string? remoteUrl, out string host, out string repoFullName)
    {
        host = "";
        repoFullName = "";
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return false;
        }

        var url = remoteUrl.Trim();
        string path;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0)
        {
            host = uri.Host;
            path = uri.AbsolutePath;
        }
        else
        {
            // ssh shorthand: git@host:owner/repo.git
            var at = url.IndexOf('@');
            var colon = url.IndexOf(':', at + 1);
            if (at < 0 || colon <= at)
            {
                return false;
            }

            host = url[(at + 1)..colon];
            path = url[(colon + 1)..];
        }

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4];
        }

        if (path.Length == 0 || !path.Contains('/'))
        {
            return false;
        }

        repoFullName = path;
        return true;
    }
}
