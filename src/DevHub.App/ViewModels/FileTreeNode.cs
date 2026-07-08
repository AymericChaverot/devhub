using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DevHub.App.ViewModels;

/// <summary>One entry of the Files tree, with a lazily loaded child collection.</summary>
public sealed class FileTreeNode
{
    public FileTreeNode(string name, string path, bool isDirectory, bool darkTheme)
    {
        Name = name;
        Path = path;
        IsDirectory = isDirectory;
        Icon = isDirectory ? null : FileVisuals.IconFor(name, darkTheme);
    }

    public string Name { get; }

    public string Path { get; }

    public bool IsDirectory { get; }

    /// <summary>Brand icon for known file types; the template falls back to a glyph.</summary>
    public SvgImageSource? Icon { get; }

    public bool HasIcon => Icon is not null;

    public bool ShowsGlyph => Icon is null;

    /// <summary>Folder or generic-document glyph, used when no brand icon applies.</summary>
    public string Glyph => IsDirectory ? "" : "";

    public bool ChildrenLoaded { get; set; }

    public ObservableCollection<FileTreeNode> Children { get; } = [];
}
