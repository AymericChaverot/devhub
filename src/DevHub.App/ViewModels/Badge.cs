using DevHub.Core.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DevHub.App.ViewModels;

/// <summary>A colored chip with an optional brand glyph — used for languages and frameworks.</summary>
public sealed class Badge
{
    private Badge(string text, string colorHex, string? slug)
    {
        Text = text;
        Brush = new SolidColorBrush(BadgeVisuals.ParseColor(colorHex));
        Icon = BadgeVisuals.WhiteIcon(slug);
    }

    public string Text { get; }

    public SolidColorBrush Brush { get; }

    public SvgImageSource? Icon { get; }

    public bool HasIcon => Icon is not null;

    public static Badge For(DetectedLanguage language)
    {
        var (slug, color) = BadgeVisuals.ForLanguage(language.Language);
        var text = language.Version is null ? language.Language : $"{language.Language} {language.Version}";
        return new Badge(text, color, slug);
    }

    public static Badge For(DetectedFramework framework)
    {
        var (slug, color) = BadgeVisuals.ForFramework(framework.Name);
        var text = framework.Version is null ? framework.Name : $"{framework.Name} {framework.Version}";
        return new Badge(text, color, slug);
    }
}
