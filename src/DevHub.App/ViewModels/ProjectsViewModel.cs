using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevHub.Core.Services;
using Serilog;

namespace DevHub.App.ViewModels;

public sealed partial class ProjectsViewModel : ObservableObject
{
    public static string AllTechnologies { get; } = Loc.T("Proj_AllTech", "All technologies");

    private readonly ISettingsService _settings;
    private readonly IProjectScanner _scanner;
    private readonly IProjectStore _store;
    private readonly IGitService _git;
    private readonly IFileOperations _fileOperations;
    private List<ProjectViewModel> _allProjects = [];

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
        SearchText = "";
        SelectedTechnology = AllTechnologies;
        SelectedSort = SortOptions[0];
    }

    public ObservableCollection<ProjectViewModel> Projects { get; } = [];

    public ObservableCollection<string> TechnologyOptions { get; } = [AllTechnologies];

    public IReadOnlyList<string> SortOptions { get; } =
    [
        Loc.T("Proj_SortPinnedName", "Pinned & Name"),
        Loc.T("Proj_SortRecent", "Recently opened"),
        Loc.T("Proj_SortName", "Name (A–Z)"),
    ];

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial string SelectedTechnology { get; set; }

    [ObservableProperty]
    public partial string SelectedSort { get; set; }

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

    partial void OnSearchTextChanged(string value) => ApplyFilterAndSort();

    partial void OnSelectedTechnologyChanged(string value) => ApplyFilterAndSort();

    partial void OnSelectedSortChanged(string value) => ApplyFilterAndSort();

    /// <summary>Shows the cached list instantly, then refreshes it with a background rescan.</summary>
    public async Task LoadAsync()
    {
        ProjectsFolderPath = _settings.Settings.ProjectsFolderPath;
        if (ProjectsFolderPath is null)
        {
            return;
        }

        var cached = await _store.GetAllAsync();
        ReplaceList(cached.Select(p => new ProjectViewModel(
            p,
            _settings.Settings.PinnedProjectPaths.Contains(p.Path, StringComparer.OrdinalIgnoreCase))));

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
        BusyMessage = Loc.T("Proj_Scanning", "Scanning projects…");
        try
        {
            var scanned = await _scanner.ScanAsync(ProjectsFolderPath);
            await _store.ReplaceAllAsync(scanned);
            ReplaceList(scanned.Select(p => new ProjectViewModel(
                p,
                _settings.Settings.PinnedProjectPaths.Contains(p.Path, StringComparer.OrdinalIgnoreCase))));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Project scan failed");
            ErrorMessage = Loc.F("Proj_ScanFailed", "Scan failed: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }

    public async Task TogglePinAsync(ProjectViewModel project)
    {
        var pinned = _settings.Settings.PinnedProjectPaths;
        var existingIdx = pinned.FindIndex(p => string.Equals(p, project.Path, StringComparison.OrdinalIgnoreCase));
        if (existingIdx >= 0)
        {
            pinned.RemoveAt(existingIdx);
            project.IsPinned = false;
        }
        else
        {
            pinned.Add(project.Path);
            project.IsPinned = true;
        }

        await _settings.SaveAsync();
        ApplyFilterAndSort();
    }

    public async Task AddFromGitAsync(string url)
    {
        if (ProjectsFolderPath is null || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        IsBusy = true;
        BusyMessage = Loc.F("Proj_Cloning", "Cloning {0}…", url.Trim());
        try
        {
            await _git.CloneAsync(url.Trim(), ProjectsFolderPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Clone of {Url} failed", url);
            ErrorMessage = Loc.F("Proj_CloneFailed", "Clone failed: {0}", ex.Message);
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

        _allProjects.Remove(project);
        Projects.Remove(project);
    }

    /// <summary>Moves the project folder to the Recycle Bin (recoverable, never permanent).</summary>
    public async Task DeleteToRecycleBinAsync(ProjectViewModel project)
    {
        IsBusy = true;
        BusyMessage = Loc.F("Proj_Recycling", "Moving {0} to the Recycle Bin…", project.Name);
        try
        {
            await _fileOperations.RecycleAsync(project.Path);
            _allProjects.Remove(project);
            Projects.Remove(project);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Recycle of {Path} failed", project.Path);
            ErrorMessage = Loc.F("Proj_DeleteFailed", "Delete failed: {0}", ex.Message);
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
        _allProjects = items
            .Where(p => !ignored.Contains(p.Path, StringComparer.OrdinalIgnoreCase))
            .Select(p => p.WithBadges())
            .ToList();

        var currentTech = SelectedTechnology;
        var techs = _allProjects
            .SelectMany(p => p.Project.Languages.Select(l => l.Language)
                .Concat(p.Project.Frameworks.Select(f => f.Name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        TechnologyOptions.Clear();
        TechnologyOptions.Add(AllTechnologies);
        foreach (var tech in techs)
        {
            TechnologyOptions.Add(tech);
        }

        SelectedTechnology = TechnologyOptions.Contains(currentTech) ? currentTech : AllTechnologies;
        ApplyFilterAndSort();
    }

    public void ApplyFilterAndSort()
    {
        var term = SearchText.Trim();
        var pinned = _settings.Settings.PinnedProjectPaths;
        var lastOpened = _settings.Settings.ProjectLastOpened;

        foreach (var p in _allProjects)
        {
            p.IsPinned = pinned.Contains(p.Path, StringComparer.OrdinalIgnoreCase);
        }

        var filtered = _allProjects
            .Where(p => string.IsNullOrEmpty(SelectedTechnology)
                || SelectedTechnology == AllTechnologies
                || p.HasTechnology(SelectedTechnology))
            .Where(p => term.Length == 0 || p.Matches(term));

        IEnumerable<ProjectViewModel> sorted = SelectedSort == SortOptions[1]
            ? filtered
                .OrderByDescending(p => lastOpened.GetValueOrDefault(p.Path, DateTime.MinValue))
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            : SelectedSort == SortOptions[2]
                ? filtered.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                : filtered
                    .OrderByDescending(p => p.IsPinned)
                    .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);

        Projects.Clear();
        foreach (var item in sorted)
        {
            Projects.Add(item);
        }
    }
}
