using DevHub.Core.Models;

namespace DevHub.Core.Services;

/// <summary>
/// Pure helpers for semicolon-delimited variables (PATH, PATHEXT, PSModulePath):
/// splitting, joining, and per-entry diagnostics (missing on disk, duplicates).
/// </summary>
public static class EnvPathAnalyzer
{
    // Always shown as a list even with a single entry; anything else needs 2+ entries.
    private static readonly HashSet<string> KnownListNames =
        new(StringComparer.OrdinalIgnoreCase) { "PATH", "PATHEXT", "PSModulePath" };

    /// <summary>True when the variable should be edited as a list of entries instead of raw text.</summary>
    public static bool IsMultiValued(string name, string value) =>
        KnownListNames.Contains(name) || Split(value).Count > 1;

    /// <summary>Entries of a delimited value; empty segments and surrounding whitespace are dropped.</summary>
    public static IReadOnlyList<string> Split(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Rebuilds the registry value from a list of entries.</summary>
    public static string Join(IEnumerable<string> entries) =>
        string.Join(';', entries.Select(e => e.Trim()).Where(e => e.Length > 0));

    /// <summary>
    /// Diagnostics for each entry, in order. An entry is only flagged as missing when it
    /// looks like an absolute path after %VAR% expansion (PATHEXT-style ".EXE" entries are
    /// never flagged); duplicates are compared case-insensitively, ignoring a trailing slash.
    /// </summary>
    public static IReadOnlyList<EnvEntryInfo> Analyze(IEnumerable<string> entries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<EnvEntryInfo>();

        foreach (var entry in entries)
        {
            var expanded = Environment.ExpandEnvironmentVariables(entry);
            var exists = !LooksLikePath(expanded) || Directory.Exists(expanded) || File.Exists(expanded);
            var isDuplicate = !seen.Add(expanded.TrimEnd('\\', '/'));
            result.Add(new EnvEntryInfo(entry, exists, isDuplicate));
        }

        return result;
    }

    private static bool LooksLikePath(string value)
    {
        try
        {
            return Path.IsPathFullyQualified(value);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
