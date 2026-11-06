using DevHub.Core.Models;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DevHub.App.ViewModels;

/// <summary>
/// One detected technology in the hero: the brand logo in its real colors, its name and version.
/// Brands too dark to read in dark theme use a lightened glyph variant.
/// </summary>
public sealed class TechLine
{
    private TechLine(string? slug, string name, string? version, bool darkTheme)
    {
        Icon = BadgeVisuals.TechIcon(slug, darkTheme);
        Name = name;
        Version = version ?? "";
    }

    public SvgImageSource? Icon { get; }

    public bool HasIcon => Icon is not null;

    public string Name { get; }

    public string Version { get; }

    public bool HasVersion => Version.Length > 0;

    public static TechLine For(DetectedLanguage language, bool darkTheme) =>
        new(BadgeVisuals.ForLanguage(language.Language).Slug, language.Language, language.Version, darkTheme);

    public static TechLine For(DetectedFramework framework, bool darkTheme) =>
        new(BadgeVisuals.ForFramework(framework.Name).Slug, framework.Name, framework.Version, darkTheme);
}
