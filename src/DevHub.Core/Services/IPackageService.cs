namespace DevHub.Core.Services;

public sealed record PackageOperationResult(bool Success, string? Message = null);

/// <summary>
/// Installs, uninstalls and inventories packages. Backed by winget; the current
/// implementation shells out to the CLI (a COM-based one can replace it later).
/// </summary>
public interface IPackageService
{
    /// <summary>
    /// All installed packages winget knows about, as winget-id → installed version.
    /// Packages installed outside winget (e.g. the official Node.js installer) can't
    /// always be mapped to a real id in bulk-listing mode; those are keyed instead by
    /// <see cref="NameKey"/> of their display name, as a fallback for exact-id misses.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetInstalledVersionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Fallback dictionary key for the name-based matches described above.</summary>
    static string NameKey(string name) => $"name:{name.Trim().ToLowerInvariant()}";

    /// <summary>Installed version of a single package, or null when not installed.</summary>
    Task<string?> GetInstalledVersionAsync(string wingetId, CancellationToken cancellationToken = default);

    /// <summary>Installs a package. winget performs any UAC elevation itself.</summary>
    Task<PackageOperationResult> InstallAsync(string wingetId, CancellationToken cancellationToken = default);

    Task<PackageOperationResult> UninstallAsync(string wingetId, CancellationToken cancellationToken = default);
}
