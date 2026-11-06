using DevHub.App.ViewModels;
using DevHub.Core.Models;
using DevHub.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;

namespace DevHub.App.Views;

public sealed partial class ProjectDetailPage
{
    private Uri? _remoteWebUrl;
    private IReadOnlyList<IdeInfo> _installedIdes = [];
    private IdeInfo? _selectedIde;

    private void ShowRemoteButton(string? remoteUrl)
    {
        _remoteWebUrl = ForgeUrls.ToWebUrl(remoteUrl);
        if (_remoteWebUrl is null)
        {
            return;
        }

        var (name, slug) = _remoteWebUrl.Host.ToLowerInvariant() switch
        {
            var h when h.Contains("github") => ("GitHub", "github"),
            var h when h.Contains("gitlab") => ("GitLab", "gitlab"),
            var h when h.Contains("bitbucket") => ("Bitbucket", "bitbucket"),
            _ => (Loc.T("Detail_Remote", "Remote"), "git"),
        };

        RemoteLabel.Text = name;
        ToolTipService.SetToolTip(RemoteButton, Loc.F("Detail_ViewOn", "View on {0}", name));
        // Dark buttons take the white glyph; light theme reads better with the brand color.
        RemoteIcon.Source = ActualTheme == ElementTheme.Dark
            ? BadgeVisuals.WhiteIcon(slug)
            : BadgeVisuals.ColorIcon(slug);
        RemoteButton.Visibility = Visibility.Visible;
    }

    private void Remote_Click(object sender, RoutedEventArgs e) =>
        ShellLauncher.Open(_remoteWebUrl?.ToString());

    private async Task PopulateIdeMenuAsync(Project project)
    {
        _installedIdes = await _ides.GetInstalledIdesAsync();
        var preferredId = _settings.Settings.ProjectIdePreferences.GetValueOrDefault(project.Path);
        _selectedIde = _installedIdes.FirstOrDefault(i => i.Id == preferredId) ?? _installedIdes.FirstOrDefault();

        RebuildIdeMenu();
        ShowSelectedIde();
        ShowIdeDecoration(_selectedIde);
    }

    private void RebuildIdeMenu()
    {
        IdeFlyout.Items.Clear();
        foreach (var ide in _installedIdes)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = ide.DisplayName,
                GroupName = "ide",
                IsChecked = ide == _selectedIde,
                Tag = ide,
            };
            if (IdeIcon(ide) is { } icon)
            {
                item.Icon = new ImageIcon { Source = icon };
            }

            item.Click += async (s, _) => await SelectAndOpenIdeAsync((IdeInfo)((FrameworkElement)s).Tag);
            IdeFlyout.Items.Add(item);
        }
    }

    private void ShowSelectedIde()
    {
        OpenInIdeButton.IsEnabled = _selectedIde is not null;
        OpenInIdeName.Text = _selectedIde?.DisplayName ?? Loc.T("Detail_NoIde", "No IDE detected");
        OpenInIdeIcon.Source = _selectedIde is null ? null : IdeIcon(_selectedIde);
        OpenInIdeIcon.Visibility = OpenInIdeIcon.Source is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private SvgImageSource? IdeIcon(IdeInfo ide) =>
        BadgeVisuals.TechIcon(BadgeVisuals.IdeSlug(ide.Id), ActualTheme == ElementTheme.Dark);

    /// <summary>Picking an IDE in the menu makes it this project's default and opens the project in it.</summary>
    private async Task SelectAndOpenIdeAsync(IdeInfo ide)
    {
        if (_project is null)
        {
            return;
        }

        var changed = ide != _selectedIde;
        _selectedIde = ide;
        ShowSelectedIde();
        await OpenSelectedIdeAsync();

        if (changed)
        {
            _settings.Settings.ProjectIdePreferences[_project.Path] = ide.Id;
            await _settings.SaveAsync();
            await CrossfadeIdeDecorationAsync(ide);
        }
    }

    private async void OpenInIde_Click(SplitButton sender, SplitButtonClickEventArgs args) =>
        await OpenSelectedIdeAsync();

    private async Task OpenSelectedIdeAsync()
    {
        if (_project is not null && _selectedIde is { } ide)
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

        TerminalShellName.Text = WindowsTerminalService.SupportedShells
            .FirstOrDefault(s => string.Equals(s.Id, activeShell, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? "PowerShell";
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
            ShowProjectIdentity(_project, newPath);

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
