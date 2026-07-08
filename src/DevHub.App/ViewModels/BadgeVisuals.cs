using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace DevHub.App.ViewModels;

/// <summary>
/// Icon + chip color lookup for languages, frameworks and IDEs.
/// Chip colors are brand-derived but darkened where the brand is too light for white glyphs.
/// </summary>
public static class BadgeVisuals
{
    private sealed record Visual(string? Slug, string Color);

    private static readonly Dictionary<string, Visual> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Node.js"] = new("nodedotjs", "#43853D"),
        ["TypeScript"] = new("typescript", "#3178C6"),
        ["Rust"] = new("rust", "#B7410E"),
        ["Go"] = new("go", "#00758D"),
        [".NET"] = new("dotnet", "#512BD4"),
        ["Python"] = new("python", "#3776AB"),
        ["Java"] = new("java", "#5382A1"),
        ["Kotlin"] = new("kotlin", "#7F52FF"),
        // Legacy tag from caches written before Java and Kotlin were separated.
        ["Java/Kotlin"] = new("java", "#5382A1"),
        ["PHP"] = new("php", "#6B6FA8"),
        ["Ruby"] = new("ruby", "#CC342D"),
        ["C/C++"] = new("cplusplus", "#00599C"),
    };

    private static readonly Dictionary<string, Visual> Frameworks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["React"] = new("react", "#087EA4"),
        ["Next.js"] = new("nextdotjs", "#1A1A1A"),
        ["Nuxt"] = new("nuxt", "#00915F"),
        ["Angular"] = new("angular", "#C3002F"),
        ["Svelte"] = new("svelte", "#E63900"),
        ["SvelteKit"] = new("svelte", "#E63900"),
        ["Vue.js"] = new("vuedotjs", "#2F8560"),
        ["Astro"] = new("astro", "#EA580C"),
        ["SolidJS"] = new("solid", "#2C4F7C"),
        ["NestJS"] = new("nestjs", "#D6234A"),
        ["Express"] = new("express", "#464646"),
        ["Fastify"] = new("fastify", "#2A2A2A"),
        ["Electron"] = new("electron", "#47848F"),
        ["Tauri"] = new("tauri", "#0F7683"),
        ["Vite"] = new("vite", "#5C64E0"),
        ["Gatsby"] = new("gatsby", "#663399"),
        ["Remix"] = new("remix", "#2A2A2A"),
        ["Django"] = new("django", "#0C4B33"),
        ["Flask"] = new("flask", "#3B3B3B"),
        ["FastAPI"] = new("fastapi", "#047857"),
        ["Rails"] = new("rubyonrails", "#C00000"),
        ["Laravel"] = new("laravel", "#E32B20"),
        ["Symfony"] = new("symfony", "#1A1A1A"),
        ["Bevy"] = new("bevy", "#3A3A3E"),
        ["Ratatui"] = new(null, "#3E5622"),
        ["Axum"] = new(null, "#6E4A2D"),
        ["Actix Web"] = new(null, "#2A2A2A"),
        ["Rocket"] = new(null, "#D33847"),
        ["ASP.NET Core"] = new("dotnet", "#512BD4"),
        ["WinUI 3"] = new(null, "#0067C0"),
        [".NET MAUI"] = new("dotnet", "#512BD4"),
        ["Avalonia"] = new(null, "#8B44AC"),
    };

    private static readonly Dictionary<string, string> IdeSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vscode"] = "visualstudiocode",
        ["visualstudio"] = "visualstudio",
        ["cursor"] = "cursor",
        ["zed"] = "zedindustries",
        ["sublime"] = "sublimetext",
        ["idea"] = "intellijidea",
        ["webstorm"] = "webstorm",
        ["rider"] = "rider",
        ["pycharm"] = "pycharm",
        ["clion"] = "clion",
        ["goland"] = "goland",
        ["phpstorm"] = "phpstorm",
        ["rustrover"] = "jetbrains",
    };

    public static (string? Slug, string Color) ForLanguage(string name) =>
        Languages.TryGetValue(name, out var v) ? (v.Slug, v.Color) : (null, "#708090");

    public static (string? Slug, string Color) ForFramework(string name) =>
        Frameworks.TryGetValue(name, out var v) ? (v.Slug, v.Color) : (null, "#708090");

    public static string? IdeSlug(string ideId) => IdeSlugs.GetValueOrDefault(ideId);

    /// <summary>White glyph, for chips.</summary>
    public static SvgImageSource? WhiteIcon(string? slug) =>
        slug is null ? null : new SvgImageSource(new Uri($"ms-appx:///Assets/ToolIcons/{slug}.svg"));

    /// <summary>Brand-colored glyph at full resolution (chips, buttons, tech lines).</summary>
    public static SvgImageSource? ColorIcon(string? slug) =>
        slug is null ? null : new SvgImageSource(new Uri($"ms-appx:///Assets/ToolIcons/{slug}-color.svg"));

    /// <summary>File path of the brand-colored SVG used by the blurred hero decoration.</summary>
    public static string? DecorSvgPath(string? slug)
    {
        if (slug is null)
        {
            return null;
        }

        var color = Path.Combine(AppContext.BaseDirectory, "Assets", "ToolIcons", $"{slug}-color.svg");
        return File.Exists(color) ? color : null;
    }

    /// <summary>
    /// Small tech-line glyph: brand colors normally, but in dark theme brands that are
    /// too dark to read (Next.js black, .NET deep purple...) use a lightened variant.
    /// </summary>
    public static SvgImageSource? TechIcon(string? slug, bool darkTheme)
    {
        if (slug is null)
        {
            return null;
        }

        if (darkTheme
            && File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "ToolIcons", $"{slug}-light.svg")))
        {
            return new SvgImageSource(new Uri($"ms-appx:///Assets/ToolIcons/{slug}-light.svg"));
        }

        return ColorIcon(slug);
    }

    public static Color ParseColor(string hex) =>
        Color.FromArgb(
            255,
            Convert.ToByte(hex[1..3], 16),
            Convert.ToByte(hex[3..5], 16),
            Convert.ToByte(hex[5..7], 16));
}
