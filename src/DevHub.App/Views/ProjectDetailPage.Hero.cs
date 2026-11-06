using System.Numerics;
using DevHub.App.Animation;
using DevHub.App.ViewModels;
using DevHub.Core.Models;
using DevHub.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace DevHub.App.Views;

/// <summary>
/// The hero: project identity plus its fold between expanded (0) and collapsed (1).
/// <para>
/// Each frame of the transition only touches the hero's own layout (paddings and the heights
/// of the two folding sections, <see cref="HeroDetails"/> and <see cref="HeroBar"/>); the title
/// shrinks through a scale so its text is never re-laid out. The body below follows the hero's
/// bottom edge with a render-only translation and is re-laid out once, when the transition
/// settles, so the README and graph views don't resize on every frame.
/// </para>
/// </summary>
public sealed partial class ProjectDetailPage
{
    private static readonly TimeSpan HeroTransitionDuration = TimeSpan.FromMilliseconds(420);

    private const double ExpandedPaddingX = 24;
    private const double ExpandedPaddingY = 22;
    private const double CollapsedPaddingX = 16;
    private const double CollapsedPaddingY = 12;
    private const double ExpandedTitleHeight = 40;
    private const double ExpandedTitleFontSize = 38;
    private const double CollapsedTitleFontSize = 24;

    /// <summary>Height of the top row's controls (buttons), which is all a collapsed hero keeps.</summary>
    private const double ControlRowHeight = 32;

    private const double HeroBorderHeight = 2;
    private const double CollapsedHeroHeight = (2 * CollapsedPaddingY) + ControlRowHeight + HeroBorderHeight;

    private ProgressAnimator? _heroAnimator;
    private bool _heroInTransition;
    private bool _heroLockedByButton;

    /// <summary>Hero height the body is currently laid out under (its top margin).</summary>
    private double _bodyTop;

    /// <summary>Natural heights of the folding sections, captured when a transition starts.</summary>
    private double _detailsHeight;
    private double _barHeight;

    private ProgressAnimator HeroAnimator =>
        _heroAnimator ??= new(ApplyHeroProgress, CubicBezierEasing.SmoothDecelerate, HeroTransitionDuration);

    private void ShowProjectIdentity(Project project, string displayPath)
    {
        ProjectName.Text = project.Name;
        ProjectPath.Text = ForgeUrls.TryParseRepo(project.GitRemoteUrl, out var host, out var repo)
            ? $"{displayPath}  ·  {host}/{repo}"
            : displayPath;
    }

    private void ShowTechStack(Project project)
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var stack = new List<TechLine>(
        [
            .. project.Languages.Select(l => TechLine.For(l, dark)),
            .. project.Frameworks.Select(f => TechLine.For(f, dark)),
        ]);
        TechList.ItemsSource = stack;
        HeroMiniStack.ItemsSource = stack;
        ShowTechDecoration(project);
    }

    /// <summary>Puts the hero back to fully expanded, without animation (a newly opened project).</summary>
    private void ResetHero()
    {
        HeroAnimator.JumpTo(0);
        SettleHero(collapsed: false);
    }

    private void ToggleHero_Click(object sender, RoutedEventArgs e) =>
        AnimateHero(collapse: HeroAnimator.Target < 0.5, lockByButton: true);

    /// <summary>
    /// Starts (or retargets) the fold. Once the button has been used, it owns the hero:
    /// scrolling no longer folds or unfolds it, on any tab, for the rest of this page's life.
    /// </summary>
    private void AnimateHero(bool collapse, bool lockByButton)
    {
        _heroLockedByButton |= lockByButton;
        BeginHeroTransition(collapse);
        SyncWebViewsHeroState(collapse ? "collapsing" : "expanding");
        HeroAnimator.AnimateTo(collapse ? 1 : 0, () => CompleteHeroTransition(collapse));
    }

    private void BeginHeroTransition(bool collapse)
    {
        if (!_heroInTransition)
        {
            _heroInTransition = true;
            _detailsHeight = MeasureNaturalHeight(HeroDetails, DetailsAvailableWidth());
            _barHeight = MeasureNaturalHeight(HeroBar, HeroCard.ActualWidth - HeroBorderHeight);

            // Pin the sections to their animated height before the next layout pass shows them in full.
            ApplyHeroProgress(HeroAnimator.Value);
        }

        if (collapse)
        {
            // The body will slide up by the height the hero loses: let it reach that much further
            // down now, so its bottom never uncovers the page. It is re-laid out right here, once,
            // at the size it will keep when collapsed.
            BodyPanel.Margin = new Thickness(0, _bodyTop, 0, -Math.Max(0, _bodyTop - CollapsedHeroHeight));
        }
    }

    private void CompleteHeroTransition(bool collapsed)
    {
        _heroInTransition = false;
        SettleHero(collapsed);

        // Settle the final hero size now, so the body is re-laid out exactly once.
        HeroCard.UpdateLayout();
        PlaceBodyBelowHero(HeroCard.ActualHeight);
        SyncWebViewsHeroState(collapsed ? "collapsed" : "expanded");
    }

    /// <summary>Steady state: folding sections back to natural size, or out of the layout entirely.</summary>
    private void SettleHero(bool collapsed)
    {
        foreach (var section in new FrameworkElement[] { HeroDetails, HeroBar })
        {
            section.Height = double.NaN;
            section.Clip = null;
            section.Opacity = 1;
            section.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        }

        ToolTipService.SetToolTip(
            HeroCollapseButton,
            collapsed ? Loc.T("Detail_ExpandHero", "Expand") : Loc.T("Detail_CollapseHero", "Collapse"));
    }

    /// <summary>Lays the hero out for <paramref name="progress"/> (0 = expanded, 1 = collapsed).</summary>
    private void ApplyHeroProgress(double progress)
    {
        var p = progress;

        HeroTop.Padding = new Thickness(
            Lerp(ExpandedPaddingX, CollapsedPaddingX, p),
            Lerp(ExpandedPaddingY, CollapsedPaddingY, p),
            Lerp(ExpandedPaddingX, CollapsedPaddingX, p),
            Lerp(ExpandedPaddingY, CollapsedPaddingY, p));

        // The title box shrinks to the control row; the text inside follows through a scale.
        ProjectNameHost.Height = Lerp(ExpandedTitleHeight, ControlRowHeight, p);
        var titleScale = Lerp(1, CollapsedTitleFontSize / ExpandedTitleFontSize, p);
        ProjectNameScale.ScaleX = ProjectNameScale.ScaleY = titleScale;
        ProjectNameScale.CenterY = ProjectName.ActualHeight / 2;

        // The mini stack hugs the shrinking title and fades in during the second half.
        HeroMiniStackTranslate.X = -ProjectName.ActualWidth * (1 - titleScale);
        HeroMiniStack.Opacity = Phase(p, 0.5, 1);

        if (_heroInTransition)
        {
            // Content fades out over the first half of the fold, so nothing is seen being clipped.
            var contentOpacity = 1 - Phase(p, 0, 0.55);
            Fold(HeroDetails, _detailsHeight * (1 - p), contentOpacity);
            Fold(HeroBar, _barHeight * (1 - p), contentOpacity);
        }

        HeroCollapseIconRotation.Angle = 180 * p;
        ApplyDecorProgress(p);
    }

    private void HeroCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The decorative logos intentionally overflow; crop them at the card bounds.
        HeroCard.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        PlaceDecorations(e.NewSize);

        if (_heroInTransition)
        {
            // Render-only: the body tracks the hero's bottom edge without a layout pass.
            BodyPanel.Translation = new Vector3(0, (float)(e.NewSize.Height - _bodyTop), 0);
        }
        else
        {
            PlaceBodyBelowHero(e.NewSize.Height);
        }
    }

    private void PlaceBodyBelowHero(double heroHeight)
    {
        _bodyTop = heroHeight;
        BodyPanel.Margin = new Thickness(0, heroHeight, 0, 0);
        BodyPanel.Translation = Vector3.Zero;
    }

    /// <summary>Sets a folding section's animated height, clipping whatever no longer fits.</summary>
    private static void Fold(FrameworkElement section, double height, double opacity)
    {
        section.Height = height;
        section.Opacity = opacity;
        section.Clip = new RectangleGeometry { Rect = new Rect(0, 0, Math.Max(section.ActualWidth, 0), height) };
    }

    /// <summary>The height <paramref name="section"/> takes when fully shown, at the given width.</summary>
    private static double MeasureNaturalHeight(FrameworkElement section, double width)
    {
        section.Visibility = Visibility.Visible;
        section.Height = double.NaN;
        section.Measure(new Size(Math.Max(width, 0), double.PositiveInfinity));
        return section.DesiredSize.Height;
    }

    /// <summary>Width of the details row: everything right of the back button.</summary>
    private double DetailsAvailableWidth() =>
        HeroTop.ActualWidth - HeroTop.Padding.Left - HeroTop.Padding.Right
        - HeroBackButton.ActualWidth - HeroTop.ColumnSpacing;

    private static double Lerp(double from, double to, double t) => from + ((to - from) * t);

    /// <summary>Remaps <paramref name="t"/> so it runs from 0 to 1 across [start, end] only.</summary>
    private static double Phase(double t, double start, double end) => Math.Clamp((t - start) / (end - start), 0, 1);
}
