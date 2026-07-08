namespace DevHub.Core.Models;

/// <summary>
/// User-facing settings, persisted as JSON in %LOCALAPPDATA%\DevHub\settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Absolute path of the folder DevHub scans for projects. Null until the user picks one.</summary>
    public string? ProjectsFolderPath { get; set; }

    /// <summary>Identifier of the IDE used by the default "Open" action (e.g. "vscode").</summary>
    public string? PreferredIdeId { get; set; }

    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Folders the user removed from the Projects list; the scanner skips them.</summary>
    public List<string> IgnoredProjectPaths { get; set; } = [];

    /// <summary>Per-project IDE choice: project path → IDE id (e.g. "vscode").</summary>
    public Dictionary<string, string> ProjectIdePreferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Monorepo subdivision: project path → subfolder names promoted to subprojects.</summary>
    public Dictionary<string, List<string>> ProjectSubprojects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
