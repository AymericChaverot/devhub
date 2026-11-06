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

    /// <summary>The PR, issue, and CI columns all drive the same collapsing hero.</summary>
    private void HookForgeScroll()
    {
        if (!_forgeScrollHooked)
        {
            _forgeScrollHooked = true;
            CollapseHeroOnScroll([ForgePrScroll, ForgeIssueScroll, ForgeRunScroll]);
        }
    }
}
