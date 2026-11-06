using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DevHub.App.Views;

/// <summary>
/// Keeps the hero usable in narrow windows: when the buttons don't fit next to the title, their
/// secondary labels go first ("Visual Studio", the terminal shell, the run config), then every
/// label, leaving icon buttons with tooltips, and last the buttons move below the title.
/// Decided by measuring the real content, so it holds for any language, IDE or project name.
/// </summary>
public sealed partial class ProjectDetailPage
{
    /// <summary>Room the title keeps next to the buttons before they shed their labels.</summary>
    private const double MinTitleWidth = 180;

    /// <summary>Room the subproject shortcuts keep next to the tool buttons.</summary>
    private const double MinSubprojectsWidth = 160;

    /// <summary>The run configuration the Run button starts, shown next to it when there's room.</summary>
    private string? _runConfigName;

    private enum HeroDensity
    {
        Full,
        Reduced,
        Compact,

        /// <summary>Compact, with the buttons moved below the title so it keeps its room.</summary>
        Stacked,
    }

    /// <summary>Each label and the density from which it is hidden.</summary>
    private (TextBlock Label, HeroDensity HiddenFrom)[] HeroLabels =>
    [
        (OpenInIdeName, HeroDensity.Reduced),
        (TerminalShellName, HeroDensity.Reduced),
        (RunButtonLabel, HeroDensity.Compact),
        (OpenInIdeLabel, HeroDensity.Compact),
        (ExplorerLabel, HeroDensity.Compact),
        (TerminalLabel, HeroDensity.Compact),
        (RemoteLabel, HeroDensity.Compact),
    ];

    /// <summary>Picks the fullest density that fits; call it whenever the card width or a label changes.</summary>
    private void UpdateHeroDensity()
    {
        var width = HeroCard.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        foreach (var density in Enum.GetValues<HeroDensity>())
        {
            ApplyHeroDensity(density);
            if (density == HeroDensity.Stacked || HeroFits(width))
            {
                break;
            }
        }
    }

    private void ApplyHeroDensity(HeroDensity density)
    {
        // "Run · dev" keeps only "Run" once space gets short.
        var runLabel = Loc.T("Detail_Run", "Run");
        RunButtonLabel.Text = density == HeroDensity.Full && _runConfigName is { } config
            ? $"{runLabel} · {config}"
            : runLabel;

        foreach (var (label, hiddenFrom) in HeroLabels)
        {
            label.Visibility = density < hiddenFrom && label.Text.Length > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Icon-only buttons say what they do on hover.
        var compact = density >= HeroDensity.Compact;
        ToolTipService.SetToolTip(ExplorerButton, compact ? ExplorerLabel.Text : null);
        ToolTipService.SetToolTip(
            OpenInIdeButton,
            density == HeroDensity.Full ? null : $"{OpenInIdeLabel.Text} {OpenInIdeName.Text}");

        // Stacked: the buttons take their own row under the title, above the path and chips.
        var stacked = density == HeroDensity.Stacked;
        Grid.SetRow(HeroActions, stacked ? 1 : 0);
        Grid.SetColumn(HeroActions, stacked ? 1 : 2);
        Grid.SetColumnSpan(HeroActions, stacked ? 2 : 1);
        HeroActions.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        HeroActions.Margin = new Thickness(0, stacked ? 8 : 0, 0, 0);
        Grid.SetRow(HeroDetails, stacked ? 2 : 1);
    }

    private bool HeroFits(double cardWidth)
    {
        var top = HeroTop.Padding.Left + HeroTop.Padding.Right
            + HeroBackButton.ActualWidth + (2 * HeroTop.ColumnSpacing)
            + MinTitleWidth + DesiredWidth(HeroActions);

        var subprojects = SubprojectsPanel.Visibility == Visibility.Visible
            ? MinSubprojectsWidth + SubprojectsPanel.Margin.Left
            : 0;
        var bar = HeroBar.Padding.Left + HeroBar.Padding.Right
            + DesiredWidth(HeroTools) + subprojects
            + (2 * HeroBar.ColumnSpacing) + DesiredWidth(PinProjectButton);

        return top <= cardWidth && bar <= cardWidth;
    }

    private static double DesiredWidth(UIElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize.Width;
    }
}
