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

    /// <summary>UI language: "System" or a BCP-47 tag ("en-US", "fr-FR"). Applied at startup.</summary>
    public string Language { get; set; } = "System";

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

    /// <summary>Project paths pinned as favorites by the user.</summary>
    public List<string> PinnedProjectPaths { get; set; } = [];

    /// <summary>Completed Pomodoro sessions for statistics and history.</summary>
    public List<PomodoroSessionRecord> PomodoroHistory { get; set; } = [];

    /// <summary>Default shell used by the Terminal button ("powershell", "pwsh", "cmd", "git-bash", "wsl").</summary>
    public string DefaultTerminalShell { get; set; } = "powershell";

    /// <summary>Per-project terminal shell override (project path → shell id).</summary>
    public Dictionary<string, string> ProjectTerminalShells { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Re-keys every per-project setting after a project folder was moved or renamed.
    /// Returns true when anything changed (i.e. the settings need saving).
    /// </summary>
    public bool MoveProjectPath(string oldPath, string newPath)
    {
        var changed = MoveKey(ProjectIdePreferences, oldPath, newPath);
        changed |= MoveKey(ProjectSubprojects, oldPath, newPath);
        changed |= MoveKey(ProjectLastOpened, oldPath, newPath);
        changed |= MoveKey(ProjectRunConfigs, oldPath, newPath);
        changed |= MoveKey(ProjectTerminalShells, oldPath, newPath);

        var pinIndex = PinnedProjectPaths.FindIndex(p => string.Equals(p, oldPath, StringComparison.OrdinalIgnoreCase));
        if (pinIndex >= 0)
        {
            PinnedProjectPaths[pinIndex] = newPath;
            changed = true;
        }

        return changed;
    }

    private static bool MoveKey<TValue>(Dictionary<string, TValue> map, string oldKey, string newKey)
    {
        if (!map.Remove(oldKey, out var value))
        {
            return false;
        }

        map[newKey] = value;
        return true;
    }
}

/// <summary>A completed Pomodoro session recorded in user settings.</summary>
public sealed class PomodoroSessionRecord
{
    public DateTime CompletedAtUtc { get; set; }

    public int DurationMinutes { get; set; }

    public string Label { get; set; } = "";

    public bool IsBreak { get; set; }

    public string? ProjectName { get; set; }
}

/// <summary>A named way to launch a project, e.g. ("dev", "npm run dev").</summary>
public sealed class RunConfig
{
    public string Name { get; set; } = "";

    public string Command { get; set; } = "";
}
