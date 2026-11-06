using DevHub.App.ViewModels;
using DevHub.Core.Models;
using DevHub.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevHub.App.Views;

public sealed partial class ProjectDetailPage
{
    private readonly System.Collections.ObjectModel.ObservableCollection<SubprojectItem> _subprojects = [];

    private async void RefreshSubprojectButtons()
    {
        if (_project is null)
        {
            return;
        }

        SubprojectsList.ItemsSource ??= _subprojects;
        _subprojects.Clear();

        // Settings list order == display order (drag & drop rewrites it).
        var names = _settings.Settings.ProjectSubprojects
            .GetValueOrDefault(_project.Path, [])
            .Where(name => Directory.Exists(Path.Combine(_project.Path, name)))
            .ToList();

        SubprojectsPanel.Visibility = names.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateHeroDensity();

        var dark = ActualTheme == ElementTheme.Dark;

        foreach (var name in names)
        {
            var subPath = Path.Combine(_project.Path, name);
            var slugs = await Task.Run(() => LanguageDetector.Detect(subPath)
                .Select(l => BadgeVisuals.ForLanguage(l.Language).Slug)
                .Concat(FrameworkDetector.Detect(subPath)
                    .Select(f => BadgeVisuals.ForFramework(f.Name).Slug))
                .Where(s => s is not null)
                .Distinct()
                .Take(3)
                .ToList());

            var icons = slugs.Select(s => BadgeVisuals.TechIcon(s, dark)!).ToList();
            _subprojects.Add(new SubprojectItem(name, icons));
        }
    }

    private async void Subproject_ButtonClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SubprojectItem item)
        {
            await NavigateToSubprojectAsync(item.Name);
        }
    }

    private async void ManageSubprojects_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var current = _settings.Settings.ProjectSubprojects.GetValueOrDefault(_project.Path, []);
        var checkboxes = Directory.EnumerateDirectories(_project.Path)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !name.StartsWith('.'))
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new CheckBox
            {
                Content = name,
                IsChecked = current.Contains(name!, StringComparer.OrdinalIgnoreCase),
            })
            .ToList();

        if (checkboxes.Count == 0)
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Loc.T("Detail_NoSubfoldersTitle", "No subfolders"),
                Content = Loc.T("Detail_NoSubfoldersText", "This project has no subfolders that could become subprojects."),
                CloseButtonText = Loc.T("Common_Ok", "OK"),
            }.ShowAsync();
            return;
        }

        var panel = new StackPanel { Spacing = 4 };
        foreach (var checkbox in checkboxes)
        {
            panel.Children.Add(checkbox);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.T("Detail_SubprojectsTitle", "Subprojects"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 420, MinWidth = 360 },
            PrimaryButtonText = Loc.T("Common_Save", "Save"),
            CloseButtonText = Loc.T("Common_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var selectedSet = checkboxes
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Content)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Keep the user's custom order for existing entries; append newly checked ones.
        var selected = current.Where(selectedSet.Contains)
            .Concat(selectedSet.Except(current, StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
            .ToList();

        if (selected.Count > 0)
        {
            _settings.Settings.ProjectSubprojects[_project.Path] = selected;
        }
        else
        {
            _settings.Settings.ProjectSubprojects.Remove(_project.Path);
        }

        await _settings.SaveAsync();
        RefreshSubprojectButtons();
    }

    private async Task NavigateToSubprojectAsync(string name)
    {
        if (_project is null)
        {
            return;
        }

        var subPath = Path.Combine(_project.Path, name);
        if (!Directory.Exists(subPath))
        {
            return;
        }

        // Detect the subfolder's own stack on demand — the scanner only inspects project roots.
        var (languages, frameworks) = await Task.Run(
            () => (LanguageDetector.Detect(subPath), FrameworkDetector.Detect(subPath)));

        var subproject = new Project
        {
            Name = name,
            Path = subPath,
            // Same repository as the parent.
            GitRemoteUrl = _project.GitRemoteUrl,
            IsGitRepository = _project.IsGitRepository,
            LastScannedAt = DateTime.UtcNow,
            Languages = languages,
            Frameworks = frameworks,
        };

        var relative = Path.Combine(_project.Name, name);
        Frame.Navigate(typeof(ProjectDetailPage), new ProjectDetailArgs(subproject, relative));
    }
}
