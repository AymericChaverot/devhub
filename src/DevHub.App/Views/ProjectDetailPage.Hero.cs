using DevHub.App.ViewModels;
using DevHub.Core.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Svg;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using Windows.Foundation;

namespace DevHub.App.Views;

public sealed partial class ProjectDetailPage
{
    private const double FallbackExpandedHeroHeight = 190;
    private const double CollapsedHeroHeight = 68;

    private double _heroProgress = -1;
    private double? _expandedHeroHeightSnapshot;
    private DispatcherQueueTimer? _heroAnimTimer;
    private double _heroAnimStart;
    private double _heroAnimTarget;
    private bool _heroLockedByButton;
    private DateTime _heroAnimStartTime;

    private string? _langDecorPath;
    private string? _ideDecorPath;
    private CanvasSvgDocument? _langSvg;
    private CanvasSvgDocument? _ideSvg;

    /// <summary>Toggles the hero between fully expanded and fully collapsed, animated.</summary>
    private void ToggleHero_Click(object sender, RoutedEventArgs e)
    {
        AnimateHeroCollapse(_heroProgress < 0.5 ? 1 : 0, lockByButton: true);
    }

    /// <summary>
    /// Lets a group of scroll viewers drive the hero both ways — unless the collapse button
    /// has taken over (see <see cref="AnimateHeroCollapse"/>), after which scrolling has no
    /// say at all. While the hero is still open, the first scroll step collapses it instead
    /// of moving the content. The group drives ONE hero, so it only re-expands once every
    /// member is back near its top; otherwise a barely-touched column would snap it open.
    /// Collapsing the hero grows the star-sized content row, which can clamp a viewer's
    /// offset and re-fire ViewChanged without any user scroll; reacting to that would resize
    /// the hero again and loop into a LayoutCycleException. A real gesture never changes the
    /// viewport height, so a viewport change is ignored.
    /// </summary>
    /// <param name="scrolled">Runs after each genuine scroll step (e.g. to page in more data).</param>
    private void CollapseHeroOnScroll(IReadOnlyList<ScrollViewer> group, Action<ScrollViewer>? scrolled = null)
    {
        const double scrollStepThreshold = 4;
        const double nearTopOffset = 8;

        foreach (var scrollViewer in group)
        {
            var lastOffset = scrollViewer.VerticalOffset;
            var lastViewportHeight = scrollViewer.ViewportHeight;

            scrollViewer.ViewChanged += (_, _) =>
            {
                var viewportChanged = Math.Abs(scrollViewer.ViewportHeight - lastViewportHeight) > 0.5;
                lastViewportHeight = scrollViewer.ViewportHeight;
                if (viewportChanged)
                {
                    lastOffset = scrollViewer.VerticalOffset;
                    return;
                }

                if (!_heroLockedByButton && _heroProgress < 0.99 && scrollViewer.VerticalOffset > 0)
                {
                    RequestHeroCollapseFromScroll(collapse: true);
                    lastOffset = 0;
                    scrollViewer.ChangeView(null, 0, null, disableAnimation: true);
                    return;
                }

                var delta = scrollViewer.VerticalOffset - lastOffset;
                lastOffset = scrollViewer.VerticalOffset;
                if (delta > scrollStepThreshold)
                {
                    RequestHeroCollapseFromScroll(collapse: true);
                }
                else if (delta < -scrollStepThreshold && group.All(sv => sv.VerticalOffset <= nearTopOffset))
                {
                    RequestHeroCollapseFromScroll(collapse: false);
                }

                scrolled?.Invoke(scrollViewer);
            };
        }
    }

    private void RequestHeroCollapseFromScroll(bool collapse)
    {
        if (_heroLockedByButton)
        {
            return;
        }

        var target = collapse ? 1.0 : 0.0;
        if (_heroAnimTimer?.IsRunning == true && Math.Abs(_heroAnimTarget - target) < 0.01)
        {
            return;
        }

        if (Math.Abs((_heroProgress < 0 ? 0 : _heroProgress) - target) < 0.01)
        {
            return;
        }

        AnimateHeroCollapse(target, lockByButton: false);
    }

    /// <summary>Eases <see cref="_heroProgress"/> from its current value to <paramref name="target"/>.</summary>
    private void AnimateHeroCollapse(double target, bool lockByButton = false)
    {
        _heroAnimTimer?.Stop();
        _heroAnimStart = Math.Clamp(_heroProgress < 0 ? 0 : _heroProgress, 0, 1);
        _heroAnimTarget = target;
        _heroAnimStartTime = DateTime.UtcNow;

        if (lockByButton)
        {
            // The button hands the user explicit manual control: once pressed, scrolling has no
            // further say over the hero at all, on any tab, for the rest of this page's life.
            _heroLockedByButton = true;
        }

        SyncWebViewsHeroState(target > 0.5 ? "collapsing" : "expanding");

        _heroAnimTimer ??= DispatcherQueue.CreateTimer();
        _heroAnimTimer.Interval = TimeSpan.FromMilliseconds(15);
        _heroAnimTimer.Tick -= HeroAnimTimer_Tick;
        _heroAnimTimer.Tick += HeroAnimTimer_Tick;
        _heroAnimTimer.Start();
    }

    private void HeroAnimTimer_Tick(object? sender, object e)
    {
        const double durationMs = 240;
        var t = Math.Clamp((DateTime.UtcNow - _heroAnimStartTime).TotalMilliseconds / durationMs, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        SetHeroCollapse(t >= 1 ? _heroAnimTarget : _heroAnimStart + ((_heroAnimTarget - _heroAnimStart) * eased));
        if (t >= 1)
        {
            _heroAnimTimer!.Stop();
            SyncWebViewsHeroState(_heroAnimTarget > 0.5 ? "collapsed" : "expanded");
        }
    }

    private void SyncWebViewsHeroState(string state)
    {
        var lockedJs = _heroLockedByButton ? "true" : "false";
        var script = $"window.__heroState = '{state}'; window.__heroLocked = {lockedJs};";
        if (ReadmeView.CoreWebView2 is not null)
        {
            _ = ReadmeView.CoreWebView2.ExecuteScriptAsync(script);
        }

        if (FileView.CoreWebView2 is not null)
        {
            _ = FileView.CoreWebView2.ExecuteScriptAsync(script);
        }
    }

    /// <summary>
    /// 0 = full hero, 1 = collapsed to just the title.
    /// </summary>
    private void SetHeroCollapse(double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        if (Math.Abs(p - _heroProgress) < 0.005 && p > 0.001 && p < 0.999)
        {
            return;
        }

        _heroProgress = p;
        var inv = 1 - p;

        ProjectName.FontSize = 40 - (14 * p);
        ProjectName.LineHeight = 52 - (18 * p);
        HeroTitleStack.Spacing = 10 * inv;

        ProjectPath.Opacity = inv;
        ProjectPath.MaxHeight = (22 * inv) + 0.01;
        TechList.Opacity = inv;
        TechList.MaxHeight = (240 * inv) + 0.01;

        ActionsRow.MinHeight = 32 * inv;
        ActionsRow.MaxHeight = (40 * inv) + 0.01;
        RemoteButton.Opacity = inv;
        RemoteButton.IsHitTestVisible = p < 0.5;
        HeroTopButtons.Opacity = inv;
        HeroTopButtons.MaxHeight = (480 * inv) + 0.01;
        HeroTopButtons.IsHitTestVisible = p < 0.5;

        var fullyCollapsed = p >= 0.999;
        ProjectPath.Visibility = fullyCollapsed ? Visibility.Collapsed : Visibility.Visible;
        TechList.Visibility = fullyCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ActionsRow.Visibility = fullyCollapsed ? Visibility.Collapsed : Visibility.Visible;
        HeroTitleStack.VerticalAlignment = fullyCollapsed ? VerticalAlignment.Center : VerticalAlignment.Top;

        // Equal top, bottom, and left padding (24px expanded -> 12px collapsed) so the
        // BackButton always has the exact same margin on top, bottom, and left when minimized.
        const double expandedPadding = 24;
        const double collapsedPadding = 12;
        var edgePadding = expandedPadding - ((expandedPadding - collapsedPadding) * p);
        HeroContentGrid.Padding = new Thickness(edgePadding, edgePadding, 224, edgePadding);

        double heroCardHeight;
        if (p <= 0.001)
        {
            HeroCard.MinHeight = 150;
            HeroCard.Height = double.NaN;
            _expandedHeroHeightSnapshot = null;
            heroCardHeight = HeroCard.ActualHeight > 0 ? HeroCard.ActualHeight : FallbackExpandedHeroHeight;
        }
        else
        {
            _expandedHeroHeightSnapshot ??=
                HeroCard.ActualHeight > 0 ? HeroCard.ActualHeight : FallbackExpandedHeroHeight;
            var expanded = _expandedHeroHeightSnapshot.Value;
            heroCardHeight = expanded - ((expanded - CollapsedHeroHeight) * p);

            HeroCard.MinHeight = 0;
            HeroCard.Height = heroCardHeight;
        }

        UpdateHeroQuickActionsPosition(heroCardHeight);

        const double naturalButtonHeight = 32;
        const double naturalButtonWidth = 36;
        if (p <= 0.001)
        {
            BackButton.Height = double.NaN;
            BackButton.Width = double.NaN;
            BackButton.VerticalAlignment = VerticalAlignment.Top;
        }
        else
        {
            var targetSize = Math.Max(naturalButtonHeight, CollapsedHeroHeight - (collapsedPadding * 2));
            BackButton.Height = naturalButtonHeight + ((targetSize - naturalButtonHeight) * p);
            BackButton.Width = naturalButtonWidth + ((targetSize - naturalButtonWidth) * p);
            BackButton.VerticalAlignment = p > 0.5 ? VerticalAlignment.Center : VerticalAlignment.Top;
        }

        LangDecorTransform.ScaleX = LangDecorTransform.ScaleY = 1 + (0.45 * p);
        LangDecorTransform.TranslateX = 70 * p;
        LangDecorTransform.TranslateY = -45 * p;
        LangDecorTransform.Rotation = -10 + (16 * p);
        IdeDecorTransform.ScaleX = IdeDecorTransform.ScaleY = 1 + (0.4 * p);
        IdeDecorTransform.TranslateX = -60 * p;
        IdeDecorTransform.TranslateY = 35 * p;
        IdeDecorTransform.Rotation = 10 - (16 * p);
        LangDecor.Opacity = 0.20 * (1 - (0.35 * p));
        IdeDecor.Opacity = 0.20 * (1 - (0.35 * p));

        HeroCollapseIcon.Glyph = p < 0.5 ? "" : "";
        ToolTipService.SetToolTip(
            HeroCollapseButton,
            p < 0.5
                ? Loc.T("Detail_CollapseHero", "Collapse")
                : Loc.T("Detail_ExpandHero", "Expand"));
    }

    /// <summary>Bridges the WebView's wheel and scroll gestures out to the collapsing hero.</summary>
    private async Task HookWebViewScrollAsync(WebView2 view)
    {
        await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            """
            (function () {
                window.__heroState = 'expanded';
                window.__heroLocked = false;
                window.addEventListener('wheel', function (e) {
                    try {
                        if (window.__heroLocked) return;
                        if (e.deltaY > 0) {
                            if (window.__heroState === 'expanded' || window.__heroState === 'collapsing') {
                                e.preventDefault();
                                if (window.__heroState === 'expanded') {
                                    window.__heroState = 'collapsing';
                                    window.chrome.webview.postMessage('collapse');
                                }
                                return;
                            }
                        } else if (e.deltaY < 0 && (window.scrollY || 0) <= 2) {
                            if (window.__heroState === 'collapsed' || window.__heroState === 'expanding') {
                                e.preventDefault();
                                if (window.__heroState === 'collapsed') {
                                    window.__heroState = 'expanding';
                                    window.chrome.webview.postMessage('expand');
                                }
                                return;
                            }
                        }
                    } catch (err) { }
                }, { passive: false });
            })();
            """);
        view.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            var msg = e.TryGetWebMessageAsString();
            if (msg == "collapse")
            {
                RequestHeroCollapseFromScroll(collapse: true);
            }
            else if (msg == "expand")
            {
                RequestHeroCollapseFromScroll(collapse: false);
            }
        };
    }

    private void ShowTechDecoration(Project project)
    {
        // Framework logo preferred over language; among those, prefer brands whose
        // color actually shows on the card (near-black logos vanish on dark Mica).
        var candidates = project.Frameworks
            .Select(f => BadgeVisuals.ForFramework(f.Name).Slug)
            .Concat(project.Languages.Select(l => BadgeVisuals.ForLanguage(l.Language).Slug))
            .Where(s => s is not null)
            .ToList();

        var slug = candidates.FirstOrDefault(s => !HasLightVariant(s!)) ?? candidates.FirstOrDefault();
        _langDecorPath = BadgeVisuals.DecorSvgPath(slug);
        _langSvg = null;
        LangDecor.Invalidate();
    }

    /// <summary>A "-light" variant exists exactly for brands that are too dark to see.</summary>
    private static bool HasLightVariant(string slug) =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "ToolIcons", $"{slug}-light.svg"));

    private void LangDecor_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
        DrawBlurredSvg(sender, args, _langDecorPath, ref _langSvg);

    private void IdeDecor_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
        DrawBlurredSvg(sender, args, _ideDecorPath, ref _ideSvg);

    /// <summary>Draws the SVG into a command list and runs it through a real gaussian blur.</summary>
    private static void DrawBlurredSvg(
        CanvasControl sender, CanvasDrawEventArgs args, string? svgPath, ref CanvasSvgDocument? cache)
    {
        if (svgPath is null)
        {
            return;
        }

        try
        {
            cache ??= CanvasSvgDocument.LoadFromXml(sender, File.ReadAllText(svgPath));

            // Padding around the glyph so the blur can bleed without hard clipping.
            var side = (float)Math.Min(sender.ActualWidth, sender.ActualHeight);
            var padding = side * 0.15f;
            var glyphSide = side - (2 * padding);

            var commands = new CanvasCommandList(sender);
            using (var session = commands.CreateDrawingSession())
            {
                session.DrawSvg(cache, new Windows.Foundation.Size(glyphSide, glyphSide), padding, padding);
            }

            args.DrawingSession.DrawImage(new GaussianBlurEffect
            {
                Source = commands,
                BlurAmount = side / 28f,
                BorderMode = EffectBorderMode.Soft,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not draw hero decoration {Path}", svgPath);
        }
    }

    private void HeroCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The decorative logos intentionally overflow; crop them at the card bounds.
        HeroCard.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
        };
        UpdateHeroQuickActionsPosition();
    }

    private void HeroLayout_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateHeroQuickActionsPosition();

    /// <summary>
    /// Positions <see cref="HeroQuickActions"/> relative to <see cref="TechList"/> and
    /// <see cref="ActionsRow"/>, keeping it hidden until layout metrics are valid so it
    /// never flashes at (0,0) in the top-left of the hero card.
    /// </summary>
    private void UpdateHeroQuickActionsPosition(double? targetHeroHeight = null)
    {
        if (!_idePickerReady || HeroCard.ActualWidth <= 0 || HeroQuickActions.ActualWidth <= 0)
        {
            return;
        }

        var naturalX = TechList.TransformToVisual(HeroCard).TransformPoint(new Point(0, 0)).X;
        var naturalY = ActionsRow.TransformToVisual(HeroCard).TransformPoint(new Point(0, 0)).Y;
        if (naturalX <= 0 || naturalY <= 0)
        {
            return;
        }

        var p = Math.Clamp(_heroProgress < 0 ? 0 : _heroProgress, 0, 1);
        var inv = 1 - p;

        const double quickActionsSpacing = 8;
        const double quickActionsRightMargin = 54;
        var qaWidth = HeroQuickActions.ActualWidth;
        var qaHeight = HeroQuickActions.ActualHeight > 0 ? HeroQuickActions.ActualHeight : 32;
        var heroCardHeight = targetHeroHeight
            ?? (HeroCard.ActualHeight > 0 ? HeroCard.ActualHeight : FallbackExpandedHeroHeight);

        var collapsedX = Math.Max(naturalX, HeroCard.ActualWidth - quickActionsRightMargin - qaWidth);
        var collapsedY = (heroCardHeight / 2) - (qaHeight / 2);

        HeroQuickActionsTransform.X = naturalX + (p * (collapsedX - naturalX));
        HeroQuickActionsTransform.Y = naturalY + (p * (collapsedY - naturalY));

        var targetLeftMargin = (qaWidth + quickActionsSpacing) * inv;
        if (Math.Abs(ActionsRow.Margin.Left - targetLeftMargin) > 0.5)
        {
            ActionsRow.Margin = new Thickness(targetLeftMargin, 6, 0, 0);
        }

        HeroQuickActions.Opacity = 1;
    }
}
