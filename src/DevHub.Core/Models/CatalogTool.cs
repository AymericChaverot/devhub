namespace DevHub.Core.Models;

/// <summary>A development tool in the Environments catalog, possibly available in several versions.</summary>
public sealed class CatalogTool
{
    public required string DisplayName { get; init; }

    /// <summary>Group header on the Environments page, e.g. "Languages &amp; runtimes".</summary>
    public required string Category { get; init; }

    public string? Description { get; init; }

    /// <summary>Short text shown in the card's icon tile, e.g. "Js", "Py". Defaults to the first letters of the name.</summary>
    public string? IconText { get; init; }

    /// <summary>Brand SVG under Assets/ToolIcons (white glyph). When present it replaces the monogram.</summary>
    public string? IconFile { get; init; }

    /// <summary>Icon tile background as #RRGGBB. Defaults to a neutral color when omitted.</summary>
    public string? AccentColor { get; init; }

    /// <summary>
    /// Installable versions, newest first. Each variant is a distinct winget package,
    /// so several versions (e.g. JDK 21 and 17) can be installed side by side.
    /// </summary>
    public required List<ToolVariant> Variants { get; init; }
}

public sealed class ToolVariant
{
    /// <summary>Exact winget package identifier (case-sensitive, matched with --exact).</summary>
    public required string WingetId { get; init; }

    /// <summary>Version shown in the picker, e.g. "21 (LTS)". Single-variant tools use the default.</summary>
    public string Label { get; init; } = "Latest";
}
