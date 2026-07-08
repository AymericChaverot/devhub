using CommunityToolkit.Mvvm.ComponentModel;
using DevHub.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DevHub.App.ViewModels;

/// <summary>One card on the Environments page: a tool with one or more installable versions.</summary>
public sealed partial class ToolCardViewModel : ObservableObject
{
    public ToolCardViewModel(CatalogTool tool, IReadOnlyList<ToolVariantViewModel> variants)
    {
        Tool = tool;
        Variants = variants;
        IconBrush = new SolidColorBrush(ParseColor(tool.AccentColor));
        SelectedVariant = variants[0];
    }

    public CatalogTool Tool { get; }

    public string DisplayName => Tool.DisplayName;

    public string Category => Tool.Category;

    public string? Description => Tool.Description;

    public IReadOnlyList<ToolVariantViewModel> Variants { get; }

    public bool HasMultipleVariants => Variants.Count > 1;

    public string IconText => Tool.IconText ?? new string([.. Tool.DisplayName.Take(2)]);

    public SolidColorBrush IconBrush { get; }

    public bool HasIconImage => Tool.IconFile is not null;

    public bool ShowsMonogram => Tool.IconFile is null;

    public Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? IconImage =>
        Tool.IconFile is null
            ? null
            : new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(
                new Uri($"ms-appx:///Assets/ToolIcons/{Tool.IconFile}"));

    [ObservableProperty]
    public partial ToolVariantViewModel SelectedVariant { get; set; }

    /// <summary>Selects the first installed version so the card reflects reality after inventory.</summary>
    public void SelectDefaultVariant() =>
        SelectedVariant = Variants.FirstOrDefault(v => v.State == PackageState.Installed) ?? Variants[0];

    public bool Matches(string searchTerm) =>
        Contains(DisplayName, searchTerm)
        || Contains(Description, searchTerm)
        || Contains(Category, searchTerm)
        || Variants.Any(v => Contains(v.WingetId, searchTerm) || Contains(v.Label, searchTerm));

    private static bool Contains(string? text, string term) =>
        text?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;

    private static Color ParseColor(string? hex)
    {
        if (hex is ['#', .. { Length: 6 }])
        {
            return Color.FromArgb(
                255,
                Convert.ToByte(hex[1..3], 16),
                Convert.ToByte(hex[3..5], 16),
                Convert.ToByte(hex[5..7], 16));
        }

        return Colors.SlateGray;
    }
}
