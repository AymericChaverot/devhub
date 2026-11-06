using DevHub.App.ViewModels;
using DevHub.Core.Models;
using DevHub.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Serilog;

namespace DevHub.App.Views;

public sealed partial class ProjectDetailPage
{
    private Uri? _remoteWebUrl;
    private bool _initializingIdePicker;
    private bool _idePickerReady;

    private void ShowRemoteButton(string? remoteUrl)
    {
        _remoteWebUrl = ForgeUrls.ToWebUrl(remoteUrl);
        if (_remoteWebUrl is null)
        {
            return;
        }

        var (label, slug) = _remoteWebUrl.Host.ToLowerInvariant() switch
        {
            var h when h.Contains("github") => ("View on GitHub", "github"),
            var h when h.Contains("gitlab") => ("View on GitLab", "gitlab"),
            var h when h.Contains("bitbucket") => ("View on Bitbucket", "bitbucket"),
            _ => ("View remote", "git"),
        };

        RemoteLabel.Text = label;
        // Dark buttons take the white glyph; light theme reads better with the brand color.
        RemoteIcon.Source = ActualTheme == ElementTheme.Dark
            ? BadgeVisuals.WhiteIcon(slug)
            : BadgeVisuals.ColorIcon(slug);
        RemoteButton.Visibility = Visibility.Visible;
    }

    private void Remote_Click(object sender, RoutedEventArgs e) =>
        ShellLauncher.Open(_remoteWebUrl?.ToString());

    private async Task PopulateIdePickerAsync(Project project)
    {
        var ides = await _ides.GetInstalledIdesAsync();
        _initializingIdePicker = true;
        IdePicker.ItemsSource = ides.Select(i => i.DisplayName).ToList();
        IdePicker.Tag = ides;

        if (ides.Count == 0)
        {
            IdePicker.PlaceholderText = Loc.T("Detail_NoIde", "No IDE detected");
            OpenInIdeButton.IsEnabled = false;
            _initializingIdePicker = false;
            return;
        }

        var preferredId = _settings.Settings.ProjectIdePreferences.GetValueOrDefault(project.Path);
        var index = ides.ToList().FindIndex(i => i.Id == preferredId);
        IdePicker.SelectedIndex = index >= 0 ? index : 0;
        _initializingIdePicker = false;

        // First display: no animation, the decoration just appears with the page.
        _ideDecorPath = BadgeVisuals.DecorSvgPath(BadgeVisuals.IdeSlug(ides[IdePicker.SelectedIndex].Id));
        _ideSvg = null;
        IdeDecor.Invalidate();
    }

    private async void IdePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingIdePicker || _project is null || SelectedIde() is not { } ide)
        {
            return;
        }

        _settings.Settings.ProjectIdePreferences[_project.Path] = ide.Id;
        await _settings.SaveAsync();

        // Smooth swap: fade the decoration out, repoint it, fade it back in.
        await AnimateOpacityAsync(IdeDecor, 0, TimeSpan.FromMilliseconds(140));
        _ideDecorPath = BadgeVisuals.DecorSvgPath(BadgeVisuals.IdeSlug(ide.Id));
        _ideSvg = null;
        IdeDecor.Invalidate();
        await AnimateOpacityAsync(IdeDecor, 0.20, TimeSpan.FromMilliseconds(280));
    }

    private static Task AnimateOpacityAsync(UIElement element, double to, TimeSpan duration)
    {
        var completion = new TaskCompletionSource();
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard { Children = { animation } };
        storyboard.Completed += (_, _) => completion.TrySetResult();
        storyboard.Begin();
        return completion.Task;
    }

    private IdeInfo? SelectedIde() =>
        IdePicker.Tag is IReadOnlyList<IdeInfo> ides && IdePicker.SelectedIndex >= 0 && IdePicker.SelectedIndex < ides.Count
            ? ides[IdePicker.SelectedIndex]
            : null;

    private async void OpenInIde_Click(object sender, RoutedEventArgs e)
    {
        if (_project is not null && SelectedIde() is { } ide)
        {
            Log.Information("Opening {Path} in {Ide}", _project.Path, ide.Id);
            await _ides.OpenAsync(ide, _project.Path);
        }
    }

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_project is not null)
        {
            ShellLauncher.OpenFolder(_project.Path);
        }
    }

    private string GetActiveTerminalShell()
    {
        if (_project is not null
            && _settings.Settings.ProjectTerminalShells.TryGetValue(_project.Path, out var projectShell)
            && !string.IsNullOrWhiteSpace(projectShell))
        {
            return projectShell;
        }

        return string.IsNullOrWhiteSpace(_settings.Settings.DefaultTerminalShell)
            ? "powershell"
            : _settings.Settings.DefaultTerminalShell;
    }

    private void RebuildTerminalShellMenu()
    {
        TerminalShellFlyout.Items.Clear();
        var activeShell = GetActiveTerminalShell();

        foreach (var option in WindowsTerminalService.SupportedShells)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = option.DisplayName,
                Tag = option.Id,
                IsChecked = string.Equals(option.Id, activeShell, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += async (s, _) =>
            {
                if (_project is null || (s as FrameworkElement)?.Tag is not string shellId)
                {
                    return;
                }

                _settings.Settings.ProjectTerminalShells[_project.Path] = shellId;
                await _settings.SaveAsync();
                RebuildTerminalShellMenu();
                _terminal.OpenTerminal(_project.Path, "", shellId);
            };
            TerminalShellFlyout.Items.Add(item);
        }

        var activeDisplayName = WindowsTerminalService.SupportedShells
            .FirstOrDefault(s => string.Equals(s.Id, activeShell, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? "PowerShell";
        ToolTipService.SetToolTip(
            TerminalSplitButton,
            $"{Loc.T("Detail_Terminal", "Terminal")} ({activeDisplayName})");
    }

    private void OpenInTerminal_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        if (_project is not null)
        {
            _terminal.OpenTerminal(_project.Path, "", GetActiveTerminalShell());
        }
    }

    private void RefreshPinState()
    {
        if (_project is null)
        {
            return;
        }

        var isPinned = _settings.Settings.PinnedProjectPaths.Contains(_project.Path, StringComparer.OrdinalIgnoreCase);
        PinProjectIcon.Glyph = isPinned ? "\uE735" : "\uE734";
    }

    private async void TogglePinProject_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var pinned = _settings.Settings.PinnedProjectPaths;
        var idx = pinned.FindIndex(p => string.Equals(p, _project.Path, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            pinned.RemoveAt(idx);
        }
        else
        {
            pinned.Add(_project.Path);
        }

        await _settings.SaveAsync();
        RefreshPinState();
        App.Services.GetRequiredService<ProjectsViewModel>().ApplyFilterAndSort();
    }

    private async void CleanProjectDisk_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var report = await Task.Run(() => ProjectDiskAnalyzer.Analyze(_project.Path));
        if (report.CleanableFolders.Count == 0)
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Loc.T("Detail_DiskUsageTitle", "Project disk usage"),
                Content = Loc.F(
                    "Detail_NoCleanableText",
                    "Total size: {0}\n\nNo heavy dependency or build folders (node_modules, bin, obj, target, .venv…) were found.",
                    report.FormattedTotalSize),
                CloseButtonText = Loc.T("Common_Ok", "OK"),
            }.ShowAsync();
            return;
        }

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.F(
                "Detail_DiskSummary",
                "Total project size: {0}  ·  Reclaimable: {1}",
                report.FormattedTotalSize,
                report.FormattedCleanableSize),
            TextWrapping = TextWrapping.Wrap,
        });

        var checkboxes = new List<(CheckBox Box, CleanableFolderInfo Folder)>();
        foreach (var folder in report.CleanableFolders)
        {
            var cb = new CheckBox
            {
                Content = $"{folder.RelativePath}  ({folder.FormattedSize} · {folder.Category})",
                IsChecked = true,
            };
            checkboxes.Add((cb, folder));
            panel.Children.Add(cb);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.T("Detail_CleanDiskTitle", "Clean dependencies & build artifacts"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 420, MinWidth = 420 },
            PrimaryButtonText = Loc.T("Proj_MoveToBin", "Move to Recycle Bin"),
            CloseButtonText = Loc.T("Common_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var selectedFolders = checkboxes.Where(c => c.Box.IsChecked == true).Select(c => c.Folder).ToList();
        if (selectedFolders.Count == 0)
        {
            return;
        }

        var fileOperations = App.Services.GetRequiredService<IFileOperations>();
        foreach (var folder in selectedFolders)
        {
            try
            {
                if (Directory.Exists(folder.FullPath))
                {
                    await fileOperations.RecycleAsync(folder.FullPath);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not recycle {Folder}", folder.FullPath);
            }
        }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var nameBox = new TextBox { Text = _project.Name, MinWidth = 360 };
        nameBox.SelectAll();

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.T("Detail_RenameTitle", "Rename project"),
            Content = nameBox,
            PrimaryButtonText = Loc.T("Detail_RenameButton", "Rename"),
            CloseButtonText = Loc.T("Common_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || nameBox.Text.Trim() is not { Length: > 0 } newName
            || newName == _project.Name)
        {
            return;
        }

        try
        {
            var fileOperations = App.Services.GetRequiredService<IFileOperations>();
            var newPath = await fileOperations.RenameFolderAsync(_project.Path, newName);
            Log.Information("Renamed {OldPath} to {NewPath}", _project.Path, newPath);

            // Cascade: per-project settings are keyed by path.
            if (_settings.Settings.MoveProjectPath(_project.Path, newPath))
            {
                await _settings.SaveAsync();
            }

            _project = _project with { Name = newName, Path = newPath };
            ProjectName.Text = newName;
            ProjectPath.Text = newPath;

            // The list keeps its own snapshot; refresh it in the background.
            _ = App.Services.GetRequiredService<ProjectsViewModel>().RescanAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Rename of {Path} failed", _project.Path);
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Loc.T("Detail_RenameFailed", "Could not rename the project"),
                Content = ex.Message,
                CloseButtonText = Loc.T("Common_Ok", "OK"),
            }.ShowAsync();
        }
    }
}
