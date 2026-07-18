namespace DevHub.Core.Models;

/// <summary>Where an environment variable lives in the registry.</summary>
public enum EnvVariableScope
{
    /// <summary>Per-user variables under HKCU\Environment; writable without elevation.</summary>
    User,

    /// <summary>Machine-wide variables under HKLM\...\Session Manager\Environment; writes require elevation.</summary>
    Machine,
}

/// <summary>
/// A single environment variable as stored in the registry. <paramref name="Value"/> is the
/// raw (unexpanded) string; <paramref name="IsExpandable"/> reflects a REG_EXPAND_SZ kind.
/// </summary>
public sealed record EnvVariable(string Name, string Value, EnvVariableScope Scope, bool IsExpandable);

/// <summary>One entry of a semicolon-delimited variable (PATH, PATHEXT, …) with its diagnostics.</summary>
public sealed record EnvEntryInfo(string Value, bool Exists, bool IsDuplicate);

/// <summary>JSON snapshot of the user and system environment blocks for backup / restore.</summary>
public sealed class EnvVariableBackup
{
    public DateTimeOffset ExportedAt { get; set; }

    public string MachineName { get; set; } = "";

    public Dictionary<string, string> User { get; set; } = [];

    public Dictionary<string, string> System { get; set; } = [];
}
