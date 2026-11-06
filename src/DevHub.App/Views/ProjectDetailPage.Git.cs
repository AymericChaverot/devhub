using DevHub.App.ViewModels;
using DevHub.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace DevHub.App.Views;

public sealed partial class ProjectDetailPage
{
    private const int GraphPageSize = 100;
    private const string SummarySeparator = "  ·  ";

    private readonly System.Collections.ObjectModel.ObservableCollection<GraphCommitRow> _graphRows = [];
    private bool _initializingBranches;
    private IReadOnlyList<Core.Models.GitBranchInfo> _branchInfos = [];
    private int _graphCount;
    private int _graphMaxLane;
    private bool _graphComplete;
    private bool _graphLoadingMore;
    private bool _graphScrollHooked;

    private async Task LoadGitAsync(Project project)
    {
        if (!project.IsGitRepository)
        {
            GitTab.Visibility = Visibility.Collapsed;
            return;
        }

        var info = await _git.GetRepositoryInfoAsync(project.Path);
        if (info is null)
        {
            GitTab.Visibility = Visibility.Collapsed;
            return;
        }

        GitTab.Visibility = Visibility.Visible;

        _initializingBranches = true;
        _branchInfos = info.Branches;
        BranchPicker.ItemsSource = info.Branches
            .Select(b => b.IsRemote ? $"{b.Name}  ·  remote" : b.Name)
            .ToList();
        BranchPicker.SelectedIndex = info.Branches.ToList().FindIndex(b => b.IsCurrent);
        BranchPicker.PlaceholderText = info.CurrentBranch;
        BranchPicker.IsEnabled = !info.IsDetached && info.Branches.Count > 1;
        _initializingBranches = false;

        AheadBehindRun.Text = (info.Ahead, info.Behind) switch
        {
            (0, 0) => "",
            (var a, 0) => $"↑{a}{SummarySeparator}",
            (0, var b) => $"↓{b}{SummarySeparator}",
            var (a, b) => $"↑{a} ↓{b}{SummarySeparator}",
        };

        var branchWord = info.Branches.Count == 1
            ? Loc.T("Detail_BranchWord", "branch")
            : Loc.T("Detail_BranchesWord", "branches");
        var commitWord = info.TotalCommits == 1
            ? Loc.T("Detail_CommitWord", "commit")
            : Loc.T("Detail_CommitsWord", "commits");
        var summary = new List<string>
        {
            $"{info.TotalCommits:N0} {commitWord}",
            $"{info.Branches.Count} {branchWord}",
        };

        if (info.IsDirty)
        {
            (int Count, string Text)[] changes =
            [
                (info.Modified, Loc.F("Detail_GitModified", "{0} modified", info.Modified)),
                (info.Added, Loc.F("Detail_GitAdded", "{0} added", info.Added)),
                (info.Removed, Loc.F("Detail_GitRemoved", "{0} removed", info.Removed)),
                (info.Untracked, Loc.F("Detail_GitUntracked", "{0} untracked", info.Untracked)),
            ];
            summary.AddRange(changes.Where(c => c.Count > 0).Select(c => c.Text));
            ToggleChangesLabel.Text = $"{Loc.T("Detail_Changes", "Changes")} ({info.WorkingChanges.Count})";
            ToggleChangesButton.Visibility = Visibility.Visible;
        }
        else
        {
            summary.Add(Loc.T("Detail_CleanTree", "Clean working tree"));
            ToggleChangesButton.Visibility = Visibility.Collapsed;
            ShowWorkingChanges(false);
        }

        WorkingChangesList.Items.Clear();
        WorkingDiffOutput.Blocks.Clear();
        foreach (var change in info.WorkingChanges)
        {
            WorkingChangesList.Items.Add(new ListViewItem
            {
                Content = $"{change.StatusSymbol}  {change.RelativePath}",
                Tag = change,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas"),
                FontSize = 12,
            });
        }

        if (WorkingChangesList.Items.Count > 0)
        {
            WorkingChangesList.SelectedIndex = 0;
        }

        StashPopButton.Label = info.StashCount > 0 ? $"Pop ({info.StashCount})" : "Pop";

        _graphCount = GraphPageSize;
        var graph = await _git.GetCommitGraphAsync(project.Path, _graphCount);
        _graphComplete = graph is null || graph.Rows.Count < _graphCount;
        _graphMaxLane = graph?.MaxLane ?? 0;
        _graphRows.Clear();
        foreach (var row in graph?.Rows ?? [])
        {
            _graphRows.Add(new GraphCommitRow(row, _graphMaxLane));
        }

        GraphList.ItemsSource ??= _graphRows;
        HookGraphScroll();

        var cliAvailable = await _git.IsGitCliAvailableAsync();
        FetchButton.IsEnabled = PullButton.IsEnabled = PushButton.IsEnabled = cliAvailable;
        StashButton.IsEnabled = cliAvailable && info.IsDirty;
        StashPopButton.IsEnabled = cliAvailable && info.StashCount > 0;
        if (!cliAvailable)
        {
            summary.Add(Loc.T("Detail_NoGitCli", "git CLI not found — fetch/pull/push unavailable"));
        }

        GitDetailsRun.Text = string.Join(SummarySeparator, summary);
    }

    /// <summary>
    /// Extends the graph window when the list is scrolled near its bottom, and drives the
    /// collapsing hero. The ListView's internal ScrollViewer isn't realized (its control
    /// template hasn't run) while the Git tab starts hidden, so this retries on every
    /// layout pass until the tab becomes visible and the template is applied.
    /// </summary>
    private void HookGraphScroll()
    {
        if (_graphScrollHooked)
        {
            return;
        }

        var scrollViewer = FindScrollViewer(GraphList);
        if (scrollViewer is null)
        {
            GraphList.LayoutUpdated -= GraphList_LayoutUpdated;
            GraphList.LayoutUpdated += GraphList_LayoutUpdated;
            return;
        }

        _graphScrollHooked = true;
        GraphList.LayoutUpdated -= GraphList_LayoutUpdated;
        CollapseHeroOnScroll([scrollViewer], scrolled: sv =>
        {
            if (sv.VerticalOffset >= sv.ScrollableHeight - 400)
            {
                _ = LoadMoreGraphAsync();
            }
        });
    }

    private void GraphList_LayoutUpdated(object? sender, object e) => HookGraphScroll();

    private async Task LoadMoreGraphAsync()
    {
        if (_graphComplete || _graphLoadingMore || _project is null)
        {
            return;
        }

        _graphLoadingMore = true;
        try
        {
            _graphCount += 150;
            var graph = await _git.GetCommitGraphAsync(_project.Path, _graphCount);
            if (graph is null)
            {
                return;
            }

            _graphComplete = graph.Rows.Count < _graphCount;

            // The layout is deterministic, so a wider window only appends rows —
            // unless a new lane appeared, which changes the graph column width.
            if (graph.MaxLane != _graphMaxLane)
            {
                _graphMaxLane = graph.MaxLane;
                _graphRows.Clear();
            }

            foreach (var row in graph.Rows.Skip(_graphRows.Count))
            {
                _graphRows.Add(new GraphCommitRow(row, _graphMaxLane));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading more commits failed");
        }
        finally
        {
            _graphLoadingMore = false;
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private async void BranchPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingBranches
            || _project is null
            || BranchPicker.SelectedIndex < 0
            || BranchPicker.SelectedIndex >= _branchInfos.Count)
        {
            return;
        }

        var branch = _branchInfos[BranchPicker.SelectedIndex].Name;
        if (_branchInfos[BranchPicker.SelectedIndex].IsCurrent)
        {
            return;
        }

        try
        {
            Log.Information("Checking out {Branch} in {Path}", branch, _project.Path);
            await _git.CheckoutAsync(_project.Path, branch);
            ShowGitResult(Loc.F("Detail_SwitchedBranch", "Switched to branch '{0}'.", branch), success: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Checkout of {Branch} failed", branch);
            ShowGitResult(ex.Message, success: false);
        }

        // Reload regardless of outcome so the picker reflects the repository's true state.
        await LoadGitAsync(_project);
    }

    private async void NewBranch_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var nameBox = new TextBox { PlaceholderText = "feature/my-branch", MinWidth = 320 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.T("Detail_CreateBranchTitle", "Create branch"),
            Content = nameBox,
            PrimaryButtonText = Loc.T("Detail_CreateSwitch", "Create & switch"),
            CloseButtonText = Loc.T("Common_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || nameBox.Text.Trim() is not { Length: > 0 } name)
        {
            return;
        }

        try
        {
            await _git.CreateBranchAsync(_project.Path, name);
            ShowGitResult(Loc.F("Detail_CreatedBranch", "Created and switched to '{0}'.", name), success: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Branch creation failed");
            ShowGitResult(ex.Message, success: false);
        }

        await LoadGitAsync(_project);
    }

    private void Fetch_Click(object sender, RoutedEventArgs e) => _ = RunGitOperationAsync("Fetch", _git.FetchAsync);

    private void Pull_Click(object sender, RoutedEventArgs e) => _ = RunGitOperationAsync("Pull", _git.PullAsync);

    private void Push_Click(object sender, RoutedEventArgs e) => _ = RunGitOperationAsync("Push", _git.PushAsync);

    private void Stash_Click(object sender, RoutedEventArgs e) => _ = RunGitOperationAsync("Stash", _git.StashAsync);

    private void StashPop_Click(object sender, RoutedEventArgs e) => _ = RunGitOperationAsync("Stash pop", _git.StashPopAsync);

    private void ToggleWorkingChanges_Click(object sender, RoutedEventArgs e) =>
        ShowWorkingChanges(ToggleChangesButton.IsChecked == true);

    /// <summary>Opens or closes the changes card, keeping its toggle button in step.</summary>
    private void ShowWorkingChanges(bool show)
    {
        ToggleChangesButton.IsChecked = show;
        WorkingChangesCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void WorkingChangesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_project is null
            || (WorkingChangesList.SelectedItem as ListViewItem)?.Tag is not GitWorkingChange change)
        {
            return;
        }

        WorkingDiffOutput.Blocks.Clear();
        try
        {
            var diff = await _git.GetFileDiffAsync(_project.Path, change.RelativePath);
            var removedBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 0xCF, 0x22, 0x2E));
            var addedBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 0x2D, 0xA4, 0x4E));

            foreach (var line in diff.Replace("\r\n", "\n").Split('\n').Take(400))
            {
                var paragraph = new Microsoft.UI.Xaml.Documents.Paragraph();
                var run = new Microsoft.UI.Xaml.Documents.Run { Text = line };
                if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
                {
                    run.Foreground = addedBrush;
                }
                else if (line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
                {
                    run.Foreground = removedBrush;
                }

                paragraph.Inlines.Add(run);
                WorkingDiffOutput.Blocks.Add(paragraph);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load diff for {File}", change.RelativePath);
        }
    }

    private async void QuickCommit_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var message = QuickCommitMessageBox.Text.Trim();
        if (message.Length == 0)
        {
            ShowGitResult(Loc.T("Detail_EmptyCommitMsg", "Enter a commit message first."), success: false);
            return;
        }

        try
        {
            await _git.CommitAllAsync(_project.Path, message);
            QuickCommitMessageBox.Text = "";
            ShowGitResult(Loc.F("Detail_Committed", "Committed: {0}", message), success: true);
            await LoadGitAsync(_project);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Quick commit failed");
            ShowGitResult(ex.Message, success: false);
        }
    }

    private async Task RunGitOperationAsync(
        string operationName, Func<string, CancellationToken, Task<string>> operation)
    {
        if (_project is null || GitBusyRing.IsActive)
        {
            return;
        }

        GitBusyRing.IsActive = true;
        FetchButton.IsEnabled = PullButton.IsEnabled = PushButton.IsEnabled = false;
        try
        {
            Log.Information("{Operation} in {Path}", operationName, _project.Path);
            var output = await operation(_project.Path, CancellationToken.None);
            var lastLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
            ShowGitResult(Loc.F("Detail_GitOpDone", "{0} done. {1}", operationName, lastLine ?? "").Trim(), success: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Operation} failed", operationName);
            ShowGitResult(ex.Message, success: false);
        }
        finally
        {
            GitBusyRing.IsActive = false;
            await LoadGitAsync(_project);
        }
    }

    private void ShowGitResult(string message, bool success)
    {
        GitOpInfo.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        GitOpInfo.Message = message;
        GitOpInfo.IsOpen = true;
    }
}
