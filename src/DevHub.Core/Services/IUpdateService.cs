namespace DevHub.Core.Services;

/// <summary>A newer release available on GitHub.</summary>
public sealed record UpdateInfo(
    Version Version,
    string Title,
    string? Notes,
    string ReleaseUrl,
    string? AssetUrl,
    string? AssetName);

/// <summary>Checks GitHub Releases for a newer DevHub and downloads its installer.</summary>
public interface IUpdateService
{
    /// <summary>
    /// The newest release when it is strictly newer than <paramref name="currentVersion"/>;
    /// null when up to date, when no release exists yet, or when GitHub can't be reached.
    /// </summary>
    Task<UpdateInfo?> CheckForUpdateAsync(Version currentVersion, CancellationToken cancellationToken = default);

    /// <summary>Downloads the update's installer asset to a temp file and returns its path.</summary>
    Task<string> DownloadUpdateAsync(
        UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
