using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevHub.Core.Services;
using Serilog;

namespace DevHub.App.ViewModels;

public sealed partial class ProjectsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IProjectScanner _scanner;
    private readonly IProjectStore _store;
    private readonly IGitService _git;
    private readonly IFileOperations _fileOperations;

    public ProjectsViewModel(
        ISettingsService settings,
        IProjectScanner scanner,
        IProjectStore store,
        IGitService git,
        IFileOperations fileOperations)
    {
        _settings = settings;
        _scanner = scanner;
        _store = store;
        _git = git;
        _fileOperations = fileOperations;
    }

    public ObservableCollection<ProjectViewModel> Projects { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjectsFolder))]
    [NotifyPropertyChangedFor(nameof(NeedsFolderSelection))]
    public partial string? ProjectsFolderPath { get; set; }

    public bool HasProjectsFolder => ProjectsFolderPath is not null;

    public bool NeedsFolderSelection => ProjectsFolderPath is null;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? BusyMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => ErrorMessage is not null;

    public void ClearError() => ErrorMessage = null;

    /// <summary>Shows the cached list instantly, then refreshes it with a background rescan.</summary>
    public async Task LoadAsync()
    {
        ProjectsFolderPath = _settings.Settings.ProjectsFolderPath;
        if (ProjectsFolderPath is null)
        {
            return;
        }

        var cached = await _store.GetAllAsync();
        ReplaceList(cached.Select(p => new ProjectViewModel(p)));

        await RescanAsync();
    }

    /// <summary>Called after the user picked a folder (from this page or Settings).</summary>
    public async Task SetProjectsFolderAsync(string path)
    {
        _settings.Settings.ProjectsFolderPath = path;
        await _settings.SaveAsync();
        await LoadAsync();
    }

    [RelayCommand]
    public async Task RescanAsync()
    {
        if (ProjectsFolderPath is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyMessage = "Scanning projects…";
        try
        {
            var scanned = await _scanner.ScanAsync(ProjectsFolderPath);
            await _store.ReplaceAllAsync(scanned);
            ReplaceList(scanned.Select(p => new ProjectViewModel(p)));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Project scan failed");
            ErrorMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }

    public async Task AddFromGitAsync(string url)
    {
        if (ProjectsFolderPath is null || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        IsBusy = true;
        BusyMessage = $"Cloning {url.Trim()}…";
        try
        {
            await _git.CloneAsync(url.Trim(), ProjectsFolderPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Clone of {Url} failed", url);
            ErrorMessage = $"Clone failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }

        await RescanAsync();
    }

    /// <summary>Removes the project from DevHub's list only; the folder stays on disk.</summary>
    public async Task RemoveFromListAsync(ProjectViewModel project)
    {
        if (!_settings.Settings.IgnoredProjectPaths.Contains(project.Path, StringComparer.OrdinalIgnoreCase))
        {
            _settings.Settings.IgnoredProjectPaths.Add(project.Path);
            await _settings.SaveAsync();
        }

        Projects.Remove(project);
    }

    /// <summary>Moves the project folder to the Recycle Bin (recoverable, never permanent).</summary>
    public async Task DeleteToRecycleBinAsync(ProjectViewModel project)
    {
        IsBusy = true;
        BusyMessage = $"Moving {project.Name} to the Recycle Bin…";
        try
        {
            await _fileOperations.RecycleAsync(project.Path);
            Projects.Remove(project);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Recycle of {Path} failed", project.Path);
            ErrorMessage = $"Delete failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }

        await RescanAsync();
    }

    private void ReplaceList(IEnumerable<ProjectViewModel> items)
    {
        var ignored = _settings.Settings.IgnoredProjectPaths;
        Projects.Clear();
        foreach (var item in items
            .Where(p => !ignored.Contains(p.Path, StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Projects.Add(item.WithBadges());
        }
    }
}
