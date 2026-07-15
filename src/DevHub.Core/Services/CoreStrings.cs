namespace DevHub.Core.Services;

/// <summary>
/// Localization bridge for user-facing strings produced inside DevHub.Core.
/// Core stays free of UI dependencies: the app installs a lookup at startup;
/// without one (unit tests, tools) the English fallbacks are returned as-is.
/// </summary>
public static class CoreStrings
{
    /// <summary>Set by the app at startup; returns null when a key has no translation.</summary>
    public static Func<string, string?>? Lookup { get; set; }

    public static string Get(string key, string fallback)
    {
        var value = Lookup?.Invoke(key);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    /// <summary>Format overload: the resource (or fallback) is a composite format string.</summary>
    public static string Format(string key, string fallback, params object[] args) =>
        string.Format(Get(key, fallback), args);
}
