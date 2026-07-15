using Microsoft.Windows.ApplicationModel.Resources;

namespace DevHub.App;

/// <summary>
/// String localization for code-generated text (XAML uses x:Uid directly).
/// Keys live in Strings\{lang}\Resources.resw; a missing key returns the
/// English fallback passed by the caller, so the app never shows raw keys.
/// </summary>
public static class Loc
{
    private static readonly ResourceLoader Loader = new();

    /// <summary>Culture matching the app display language, for dates and numbers built in code.</summary>
    public static System.Globalization.CultureInfo Culture { get; } = ResolveCulture();

    private static System.Globalization.CultureInfo ResolveCulture()
    {
        try
        {
            return new(Microsoft.Windows.Globalization.ApplicationLanguages.Languages[0]);
        }
        catch (Exception)
        {
            return System.Globalization.CultureInfo.CurrentCulture;
        }
    }

    /// <summary>The localized string, or <paramref name="fallback"/> when the key is missing.</summary>
    public static string T(string key, string fallback)
    {
        var value = GetOrNull(key);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    /// <summary>Format overload: the resource (or fallback) is a composite format string.</summary>
    public static string F(string key, string fallback, params object[] args) =>
        string.Format(T(key, fallback), args);

    /// <summary>Raw lookup; null when the key has no value. Used to bridge DevHub.Core strings.</summary>
    public static string? GetOrNull(string key)
    {
        try
        {
            var value = Loader.GetString(key);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
