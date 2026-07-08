using DevHub.Core.Models;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DevHub.App.ViewModels;

/// <summary>
/// One hero row: the brand logo in its real colors, followed by the name and version.
/// Brands too dark to read in dark theme use a lightened glyph variant.
/// </summary>
public sealed class TechLine
{
    private TechLine(string? slug, string name, string? version, bool darkTheme)
    {
        Icon = BadgeVisuals.TechIcon(slug, darkTheme);
        Text = version is null ? name : $"{name} {version}";
    }

    public SvgImageSource? Icon { get; }

    public bool HasIcon => Icon is not null;

    public string Text { get; }

    public static TechLine For(DetectedLanguage language, bool darkTheme) =>
        new(BadgeVisuals.ForLanguage(language.Language).Slug, language.Language, language.Version, darkTheme);

    public static TechLine For(DetectedFramework framework, bool darkTheme) =>
        new(BadgeVisuals.ForFramework(framework.Name).Slug, framework.Name, framework.Version, darkTheme);
}
