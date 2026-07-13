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

    /// <summary>Last time each project was opened in DevHub (path → UTC); feeds the dashboard.</summary>
    public Dictionary<string, DateTime> ProjectLastOpened { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Connected forge accounts: forge name ("GitHub"/"GitLab") → login.
    /// Tokens are NOT here — they live in the Windows Credential Manager.
    /// </summary>
    public Dictionary<string, string> ConnectedForgeLogins { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Run configurations per project (path → named launch commands).</summary>
    public Dictionary<string, List<RunConfig>> ProjectRunConfigs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>City for the dashboard weather; null/empty = locate automatically from the IP.</summary>
    public string? WeatherLocation { get; set; }
}

/// <summary>A named way to launch a project, e.g. ("dev", "npm run dev").</summary>
public sealed class RunConfig
{
    public string Name { get; set; } = "";

    public string Command { get; set; } = "";
}
