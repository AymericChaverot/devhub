using Microsoft.UI.Xaml.Media.Imaging;

namespace DevHub.App.ViewModels;

/// <summary>A subproject shortcut in the hero tool bar: folder name plus its tech-stack icons.</summary>
public sealed class SubprojectItem
{
    public SubprojectItem(string name, IReadOnlyList<SvgImageSource> icons)
    {
        Name = name;
        Icons = icons;
    }

    /// <summary>Subfolder name, shown on the shortcut and in its tooltip.</summary>
    public string Name { get; }

    public IReadOnlyList<SvgImageSource> Icons { get; }

    public bool HasIcons => Icons.Count > 0;

    /// <summary>Fallback folder glyph when the subfolder has no detectable stack.</summary>
    public bool HasNoIcons => Icons.Count == 0;
}
