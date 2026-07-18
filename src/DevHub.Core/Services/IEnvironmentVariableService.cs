using DevHub.Core.Models;

namespace DevHub.Core.Services;

public interface IEnvironmentVariableService
{
    /// <summary>True when the current process runs elevated (can write machine variables directly).</summary>
    bool IsElevated { get; }

    /// <summary>All variables of a scope, sorted by name, with raw (unexpanded) values.</summary>
    IReadOnlyList<EnvVariable> GetVariables(EnvVariableScope scope);

    /// <summary>
    /// Creates or updates a variable, then broadcasts WM_SETTINGCHANGE. Machine-scope writes from a
    /// non-elevated process go through an elevated helper (one UAC prompt); throws
    /// <see cref="OperationCanceledException"/> when the user declines the prompt.
    /// </summary>
    Task SetVariableAsync(EnvVariableScope scope, string name, string value, CancellationToken cancellationToken = default);

    /// <summary>Deletes a variable, then broadcasts WM_SETTINGCHANGE. Same elevation rules as SetVariableAsync.</summary>
    Task DeleteVariableAsync(EnvVariableScope scope, string name, CancellationToken cancellationToken = default);

    /// <summary>Notifies running applications (shells, editors) that the environment changed.</summary>
    void BroadcastChange();

    /// <summary>Writes a JSON snapshot of both scopes to <paramref name="filePath"/>.</summary>
    Task ExportAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Reads a backup file without applying it (for a preview / confirmation step).</summary>
    Task<EnvVariableBackup> ReadBackupAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores variables from a backup (merge: existing names are overwritten, extra ones kept),
    /// then broadcasts once. Returns the number of variables written.
    /// </summary>
    Task<int> RestoreAsync(EnvVariableBackup backup, bool restoreUser, bool restoreSystem, CancellationToken cancellationToken = default);
}
