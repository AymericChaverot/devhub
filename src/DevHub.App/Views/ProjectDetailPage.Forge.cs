using DevHub.App.ViewModels;
using DevHub.Core.Models;
using DevHub.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevHub.App.Views;

public sealed partial class ProjectDetailPage
{
    private ForgeAccountViewModel? _forgeAccount;
    private string? _forgeRepo;
    private string? _forgeWebUrl;
    private bool _forgeLoaded;
    private bool _forgeScrollHooked;

    /// <summary>Shows the GitHub/GitLab tab when the remote belongs to a connected account.</summary>
    private void SetupForgeTab(Project project)
    {
        _forgeAccount = null;
        _forgeRepo = null;
        _forgeWebUrl = null;
        _forgeLoaded = false;
        ForgeTab.Visibility = Visibility.Collapsed;
        ForgeAccountAvatar.Visibility = Visibility.Collapsed;

        if (!ForgeUrls.TryParseRepo(project.GitRemoteUrl, out var host, out var repo))
        {
            return;
        }

        if (_forgeAccounts.ConnectedForHost(host) is not { } account)
        {
            return;
        }

        _forgeAccount = account;
        _forgeRepo = repo;
        ForgeTab.Text = account.DisplayName;
        ForgeOpenRepoLabel.Text = Loc.F("Detail_OpenOnForge", "Open on {0}", account.DisplayName);
        ForgeTab.Visibility = Visibility.Visible;

        ForgeAccountAvatar.DisplayName = account.Login;
        ForgeAccountAvatar.ProfilePicture = account.AvatarImage;
        ToolTipService.SetToolTip(ForgeAccountAvatar, account.ViewingAsTooltip);
        ForgeAccountAvatar.Visibility = Visibility.Visible;
    }

    private async Task LoadForgeAsync()
    {
        if (_forgeAccount is null || _forgeRepo is null)
        {
            return;
        }

        var isGitLab = _forgeAccount.Kind == Core.Models.ForgeKind.GitLab;
        var prsLabel = isGitLab
            ? Loc.T("Detail_OpenMrs", "Open merge requests")
            : Loc.T("Detail_OpenPrs", "Open pull requests");
        var runsLabel = isGitLab
            ? Loc.T("Detail_Pipelines", "Pipelines")
            : Loc.T("Detail_WorkflowRuns", "Workflow runs");
        var issuesLabel = Loc.T("Detail_OpenIssues", "Open issues");
        ForgeRepoName.Text = _forgeRepo;
        ForgePrsHeader.Text = prsLabel;
        ForgeIssuesHeader.Text = issuesLabel;
        ForgeRunsHeader.Text = runsLabel;
        ForgeLoading.IsActive = true;
        try
        {
            var repoTask = _forgeAccount.GetRepositoryAsync(_forgeRepo);
            var prsTask = _forgeAccount.GetRepoPullRequestsAsync(_forgeRepo);
            var issuesTask = _forgeAccount.GetRepoIssuesAsync(_forgeRepo);
            var runsTask = _forgeAccount.GetRepoWorkflowRunsAsync(_forgeRepo);
            await Task.WhenAll(repoTask, prsTask, issuesTask, runsTask);

            var info = await repoTask;
            ForgeRepoDescription.Text = info?.Description ?? "";
            ForgeRepoDescription.Visibility =
                string.IsNullOrWhiteSpace(info?.Description) ? Visibility.Collapsed : Visibility.Visible;
            ForgeRepoStats.Text = info is null ? "" : $"★ {info.Stars}   ⑂ {info.Forks}";
            _forgeWebUrl = info?.WebUrl;

            var prs = (await prsTask).Select(i => new ForgeItemViewModel(i, showRepo: false)).ToList();
            ForgePrsHeader.Text = $"{prsLabel} ({prs.Count})";
            ForgePrList.ItemsSource = prs;
            ForgePrsEmpty.Visibility = prs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var issues = (await issuesTask).Select(i => new ForgeItemViewModel(i, showRepo: false)).ToList();
            ForgeIssuesHeader.Text = $"{issuesLabel} ({issues.Count})";
            ForgeIssueList.ItemsSource = issues;
            ForgeIssuesEmpty.Visibility = issues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var runs = (await runsTask).Select(r => new ForgeRunViewModel(r)).ToList();
            ForgeRunsHeader.Text = $"{runsLabel} ({runs.Count})";
            ForgeRunList.ItemsSource = runs;
            ForgeRunsEmpty.Visibility = runs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            ForgeLoading.IsActive = false;
        }
    }

    private void ForgeItem_Click(object sender, RoutedEventArgs e) =>
        ShellLauncher.Open((sender as FrameworkElement)?.Tag as string);

    private void ForgeOpenRepo_Click(object sender, RoutedEventArgs e) =>
        ShellLauncher.Open(_forgeWebUrl ?? _remoteWebUrl?.ToString());

    /// <summary>
    /// Same auto-collapse as the Git/README tabs: the columns' scroll drives the hero, both
    /// ways, exactly like scrolling a column's offset up or down always did before — unless
    /// the button has taken over (see AnimateHeroCollapse), in which case scroll has no say
    /// over the hero at all anymore. All three columns drive the SAME hero, so whichever is
    /// furthest along wins — otherwise a barely-touched column sitting near its own top
    /// would snap the hero back open the moment the pointer moved to it. Collapsing the hero
    /// also grows this same box (Grid.Row="1" is star-sized), which shrinks a column's
    /// ScrollableHeight and can force-clamp its VerticalOffset, re-firing ViewChanged
    /// without the user having scrolled; reacting to that resizes the hero again and can
    /// loop within a layout pass into a LayoutCycleException crash. A real scroll gesture
    /// never changes the viewport height, only the offset, so that's the signal that tells
    /// the two apart.
    /// </summary>
    private void HookForgeScroll()
    {
        if (_forgeScrollHooked)
        {
            return;
        }

        _forgeScrollHooked = true;
        var scrollViewers = new[] { ForgePrScroll, ForgeIssueScroll, ForgeRunScroll };
        var lastOffsets = scrollViewers.ToDictionary(sv => sv, sv => sv.VerticalOffset);
        var lastViewportHeights = scrollViewers.ToDictionary(sv => sv, sv => sv.ViewportHeight);

        foreach (var scrollViewer in scrollViewers)
        {
            scrollViewer.ViewChanged += (_, _) =>
            {
                var viewportChanged = Math.Abs(scrollViewer.ViewportHeight - lastViewportHeights[scrollViewer]) > 0.5;
                lastViewportHeights[scrollViewer] = scrollViewer.ViewportHeight;
                if (viewportChanged)
                {
                    lastOffsets[scrollViewer] = scrollViewer.VerticalOffset;
                    return;
                }

                var delta = scrollViewer.VerticalOffset - lastOffsets[scrollViewer];
                if (!_heroLockedByButton && _heroProgress < 0.99 && scrollViewer.VerticalOffset > 0)
                {
                    RequestHeroCollapseFromScroll(collapse: true);
                    lastOffsets[scrollViewer] = 0;
                    scrollViewer.ChangeView(null, 0, null, disableAnimation: true);
                    return;
                }

                lastOffsets[scrollViewer] = scrollViewer.VerticalOffset;
                if (delta > 4)
                {
                    RequestHeroCollapseFromScroll(collapse: true);
                }
                else if (delta < -4 && scrollViewers.All(sv => sv.VerticalOffset <= 8))
                {
                    RequestHeroCollapseFromScroll(collapse: false);
                }
            };
        }
    }
}
