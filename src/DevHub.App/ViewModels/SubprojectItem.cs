using Microsoft.UI.Xaml.Media.Imaging;

namespace DevHub.App.ViewModels;

/// <summary>A subproject shortcut in the hero: identified visually by its tech-stack icons.</summary>
public sealed class SubprojectItem
{
    public SubprojectItem(string name, IReadOnlyList<SvgImageSource> icons)
    {
        Name = name;
        Icons = icons;
    }

    /// <summary>Subfolder name — shown only in the tooltip.</summary>
    public string Name { get; }

    public IReadOnlyList<SvgImageSource> Icons { get; }

    public bool HasIcons => Icons.Count > 0;

    /// <summary>Fallback folder glyph when the subfolder has no detectable stack.</summary>
    public bool HasNoIcons => Icons.Count == 0;
}
