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
    private bool _suppressFilter;
    private string? _loadedFolderPath;
    private CancellationTokenSource? _gitStatusRefresh;

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

    partial void OnSearchTextChanged(string value)
    {
        if (!_suppressFilter)
        {
            ApplyFilterAndSort();
        }
    }

    partial void OnSelectedTechnologyChanged(string value)
    {
        if (!_suppressFilter)
        {
            ApplyFilterAndSort();
        }
    }

    partial void OnSelectedSortChanged(string value)
    {
        if (!_suppressFilter)
        {
            ApplyFilterAndSort();
        }
    }

    /// <summary>Shows the cached list instantly; only runs an initial background scan once per folder.</summary>
    public async Task LoadAsync()
    {
        ProjectsFolderPath = _settings.Settings.ProjectsFolderPath;
        if (ProjectsFolderPath is null)
        {
            return;
        }

        // If already loaded for this folder during the current session, just sync pin/sort state without flashing.
        if (_allProjects.Count > 0 && string.Equals(_loadedFolderPath, ProjectsFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            ApplyFilterAndSort();

            // Coming back to the page: repositories may have changed meanwhile.
            _ = RefreshGitStatusesAsync();
            return;
        }

        _loadedFolderPath = ProjectsFolderPath;
        var cached = await _store.GetAllAsync();
        if (cached.Count > 0)
        {
            ReplaceList(cached.Select(p => new ProjectViewModel(
                p,
                _settings.Settings.PinnedProjectPaths.Contains(p.Path, StringComparer.OrdinalIgnoreCase))));
        }

        await ScanCoreAsync(showBusyIndicator: cached.Count == 0);
    }

    /// <summary>Called after the user picked a folder (from this page or Settings).</summary>
    public async Task SetProjectsFolderAsync(string path)
    {
        _settings.Settings.ProjectsFolderPath = path;
        _loadedFolderPath = null;
        await _settings.SaveAsync();
        await LoadAsync();
    }

    [RelayCommand]
    public Task RescanAsync() => ScanCoreAsync(showBusyIndicator: true);

    private async Task ScanCoreAsync(bool showBusyIndicator)
    {
        if (ProjectsFolderPath is null || IsBusy)
        {
            return;
        }

        if (showBusyIndicator)
        {
            IsBusy = true;
            BusyMessage = Loc.T("Proj_Scanning", "Scanning projects…");
        }

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
            if (showBusyIndicator)
            {
                IsBusy = false;
                BusyMessage = null;
            }
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
        var existingByPath = _allProjects.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);

        _allProjects = items
            .Where(p => !ignored.Contains(p.Path, StringComparer.OrdinalIgnoreCase))
            .Select(p =>
            {
                if (existingByPath.TryGetValue(p.Path, out var existing)
                    && existing.Name == p.Name
                    && existing.IsGitRepository == p.IsGitRepository
                    && existing.GitRemoteUrl == p.GitRemoteUrl
                    && existing.Project.Languages.Count == p.Project.Languages.Count
                    && existing.Project.Frameworks.Count == p.Project.Frameworks.Count)
                {
                    existing.IsPinned = p.IsPinned;
                    return existing;
                }

                return p.WithBadges();
            })
            .ToList();

        var currentTech = SelectedTechnology;
        var desiredTechs = new List<string> { AllTechnologies };
        desiredTechs.AddRange(_allProjects
            .SelectMany(p => p.Project.Languages.Select(l => l.Language)
                .Concat(p.Project.Frameworks.Select(f => f.Name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase));

        _suppressFilter = true;
        try
        {
            if (!TechnologyOptions.SequenceEqual(desiredTechs, StringComparer.Ordinal))
            {
                TechnologyOptions.Clear();
                foreach (var tech in desiredTechs)
                {
                    TechnologyOptions.Add(tech);
                }
            }

            SelectedTechnology = TechnologyOptions.Contains(currentTech) ? currentTech : AllTechnologies;
        }
        finally
        {
            _suppressFilter = false;
        }

        ApplyFilterAndSort();
        _ = RefreshGitStatusesAsync();
    }

    /// <summary>
    /// Fills in each repository's branch and sync state in the background, a few at a time.
    /// A newer refresh cancels the previous one, so only the latest list gets updated.
    /// </summary>
    private async Task RefreshGitStatusesAsync()
    {
        const int MaxConcurrentRepositories = 4;

        _gitStatusRefresh?.Cancel();
        var refresh = _gitStatusRefresh = new CancellationTokenSource();
        using var throttle = new SemaphoreSlim(MaxConcurrentRepositories);

        await Task.WhenAll(_allProjects.Where(p => p.IsGitRepository).Select(async project =>
        {
            await throttle.WaitAsync();
            try
            {
                if (refresh.IsCancellationRequested)
                {
                    return;
                }

                // Resumes on the UI thread, so the row can be updated directly.
                var status = await _git.GetStatusSummaryAsync(project.Path, refresh.Token);
                if (!refresh.IsCancellationRequested)
                {
                    project.GitStatus = status;
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer refresh.
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not read git status of {Path}", project.Path);
            }
            finally
            {
                throttle.Release();
            }
        }));
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

        var sortedList = (SelectedSort == SortOptions[1]
            ? filtered
                .OrderByDescending(p => lastOpened.GetValueOrDefault(p.Path, DateTime.MinValue))
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            : SelectedSort == SortOptions[2]
                ? filtered.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                : filtered
                    .OrderByDescending(p => p.IsPinned)
                    .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            .ToList();

        if (Projects.SequenceEqual(sortedList))
        {
            return;
        }

        Projects.Clear();
        foreach (var item in sortedList)
        {
            Projects.Add(item);
        }
    }
}
