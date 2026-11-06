using Microsoft.UI.Xaml.Controls;

namespace DevHub.App.Views;

/// <summary>Scroll gestures (native scroll viewers and WebViews) that fold and unfold the hero.</summary>
public sealed partial class ProjectDetailPage
{
    /// <summary>
    /// Lets a group of scroll viewers drive the hero both ways — unless the collapse button
    /// has taken over (see <see cref="AnimateHero"/>), after which scrolling has no
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

                if (!_heroLockedByButton && HeroAnimator.Value < 0.99 && scrollViewer.VerticalOffset > 0)
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
        // Already there or already heading there: nothing to do.
        if (_heroLockedByButton || Math.Abs(HeroAnimator.Target - (collapse ? 1 : 0)) < 0.01)
        {
            return;
        }

        AnimateHero(collapse, lockByButton: false);
    }

    /// <summary>Tells the WebViews' wheel bridge where the hero is, so it knows when to swallow the wheel.</summary>
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
}
