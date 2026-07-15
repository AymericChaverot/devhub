using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevHub.Core.Services;
using Serilog;

namespace DevHub.App.ViewModels;

public sealed partial class EnvironmentsViewModel : ObservableObject
{
    public static string AllCategories { get; } = Loc.T("Env_AllCategories", "All categories");

    private readonly ICatalogService _catalog;
    private readonly IPackageService _packages;

    // winget only supports one operation at a time; installs/uninstalls queue here.
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private List<ToolCardViewModel> _allCards = [];

    public EnvironmentsViewModel(ICatalogService catalog, IPackageService packages)
    {
        _catalog = catalog;
        _packages = packages;
        SearchText = "";
        SelectedCategory = AllCategories;
        SelectedStatus = StatusOptions[0];
    }

    /// <summary>Raised whenever <see cref="Groups"/> has been rebuilt (load or filter change).</summary>
    public event EventHandler? GroupsChanged;

    public IReadOnlyList<PackageGroup> Groups { get; private set; } = [];

    public ObservableCollection<string> CategoryOptions { get; } = [AllCategories];

    public IReadOnlyList<string> StatusOptions { get; } =
    [
        Loc.T("Env_StatusAll", "All statuses"),
        Loc.T("Env_StatusInstalled", "Installed"),
        Loc.T("Env_StatusUpdateAvailable", "Update available"),
        Loc.T("Env_StatusNotInstalled", "Not installed"),
    ];

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial string SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string SelectedStatus { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => ErrorMessage is not null;

    /// <summary>True when the catalog is loaded but the current filters match nothing.</summary>
    public bool HasNoResults => _allCards.Count > 0 && Groups.Count == 0;

    /// <summary>True when at least one installed tool has an update winget can apply.</summary>
    public bool HasUpdates => _allCards.SelectMany(c => c.Variants).Any(v => v.AvailableVersion is not null);

    /// <summary>True once the inventory ran and found nothing to update (dashboard "all good" state).</summary>
    public bool IsUpToDate => !IsLoading && _allCards.Count > 0 && !HasUpdates;

    public int UpdateCount => _allCards.SelectMany(c => c.Variants).Count(v => v.AvailableVersion is not null);

    public string UpdateAllLabel => Loc.F("Env_UpdateAll", "Update all ({0})", UpdateCount);

    public void ClearError() => ErrorMessage = null;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsUpToDate));

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    partial void OnSelectedCategoryChanged(string value) => ApplyFilters();

    partial void OnSelectedStatusChanged(string value) => ApplyFilters();

    /// <summary>Builds the catalog cards, then resolves installed versions. Safe to call again (no-op).</summary>
    public async Task LoadAsync()
    {
        if (_allCards.Count > 0)
        {
            return;
        }

        IsLoading = true;
        try
        {
            _allCards = _catalog.GetTools()
                .Select(tool => new ToolCardViewModel(
                    tool,
                    tool.Variants
                        .Select(v => new ToolVariantViewModel(
                            tool.DisplayName, v, EnqueueInstallAsync, EnqueueUninstallAsync, EnqueueUpdateAsync))
                        .ToList()))
                .ToList();

            foreach (var category in _allCards.Select(c => c.Category).Distinct())
            {
                CategoryOptions.Add(category);
            }

            ApplyFilters();

            var installed = await _packages.GetInstalledPackagesAsync();
            foreach (var variant in _allCards.SelectMany(c => c.Variants))
            {
                // Exact id match first; fall back to the display-name key for packages
                // winget could only bulk-match by name (installed outside winget).
                if (installed.TryGetValue(variant.WingetId, out var package)
                    || installed.TryGetValue(IPackageService.NameKey(variant.ToolName), out package))
                {
                    variant.InstalledVersion = package.Version;
                    variant.AvailableVersion = package.AvailableVersion;
                    variant.State = PackageState.Installed;
                }
                else
                {
                    variant.State = PackageState.NotInstalled;
                }
            }

            foreach (var card in _allCards)
            {
                card.SelectDefaultVariant();
            }

            NotifyUpdateSummaryChanged();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load the environments catalog");
            ErrorMessage = Loc.F("Env_WingetError", "Could not query winget for installed packages: {0}", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilters()
    {
        var term = SearchText.Trim();

        var cards = _allCards
            .Where(c => SelectedCategory == AllCategories || c.Category == SelectedCategory)
            .Where(MatchesStatusFilter)
            .Where(c => term.Length == 0 || c.Matches(term));

        Groups = cards
            .GroupBy(c => c.Category)
            .Select(g => new PackageGroup(g.Key, g))
            .ToList();

        OnPropertyChanged(nameof(HasNoResults));
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    // Filter on the option's position, not its (localized) display text.
    private bool MatchesStatusFilter(ToolCardViewModel card)
    {
        if (SelectedStatus == StatusOptions[1])
        {
            return card.Variants.Any(v => v.State == PackageState.Installed);
        }

        if (SelectedStatus == StatusOptions[2])
        {
            return card.Variants.Any(v => v.AvailableVersion is not null);
        }

        if (SelectedStatus == StatusOptions[3])
        {
            return card.Variants.All(v => v.State != PackageState.Installed);
        }

        return true;
    }

    private void NotifyUpdateSummaryChanged()
    {
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(IsUpToDate));
        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(UpdateAllLabel));
    }

    /// <summary>Queues an update for every variant that has one; winget applies them one at a time.</summary>
    [RelayCommand]
    private Task UpdateAllAsync()
    {
        var pending = _allCards.SelectMany(c => c.Variants).Where(v => v.CanUpdate).ToList();
        return Task.WhenAll(pending.Select(EnqueueUpdateAsync));
    }

    private async Task EnqueueInstallAsync(ToolVariantViewModel variant)
    {
        variant.State = PackageState.Queued;
        await _operationGate.WaitAsync();
        try
        {
            variant.State = PackageState.Working;
            Log.Information("Installing {WingetId}", variant.WingetId);
            var result = await _packages.InstallAsync(variant.WingetId);

            if (result.Success)
            {
                variant.InstalledVersion = await _packages.GetInstalledVersionAsync(variant.WingetId);
                variant.State = PackageState.Installed;
                Log.Information("Installed {WingetId} {Version}", variant.WingetId, variant.InstalledVersion);
            }
            else
            {
                variant.State = PackageState.NotInstalled;
                ErrorMessage = $"{variant.ToolName} {variant.Label}: {result.Message}";
                Log.Warning("Install of {WingetId} failed: {Message}", variant.WingetId, result.Message);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task EnqueueUninstallAsync(ToolVariantViewModel variant)
    {
        variant.State = PackageState.Queued;
        await _operationGate.WaitAsync();
        try
        {
            variant.State = PackageState.Working;
            Log.Information("Uninstalling {WingetId}", variant.WingetId);
            var result = await _packages.UninstallAsync(variant.WingetId);

            if (result.Success)
            {
                variant.InstalledVersion = null;
                variant.AvailableVersion = null;
                variant.State = PackageState.NotInstalled;
                NotifyUpdateSummaryChanged();
            }
            else
            {
                variant.State = PackageState.Installed;
                ErrorMessage = $"{variant.ToolName} {variant.Label}: {result.Message}";
                Log.Warning("Uninstall of {WingetId} failed: {Message}", variant.WingetId, result.Message);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task EnqueueUpdateAsync(ToolVariantViewModel variant)
    {
        variant.State = PackageState.Queued;
        await _operationGate.WaitAsync();
        try
        {
            variant.State = PackageState.Working;
            Log.Information("Updating {WingetId}", variant.WingetId);
            var result = await _packages.UpgradeAsync(variant.WingetId);

            if (result.Success)
            {
                variant.InstalledVersion = await _packages.GetInstalledVersionAsync(variant.WingetId);
                variant.AvailableVersion = null;
                Log.Information("Updated {WingetId} to {Version}", variant.WingetId, variant.InstalledVersion);
            }
            else
            {
                ErrorMessage = $"{variant.ToolName} {variant.Label}: {result.Message}";
                Log.Warning("Update of {WingetId} failed: {Message}", variant.WingetId, result.Message);
            }

            // Success or not, the tool is still installed.
            variant.State = PackageState.Installed;
            NotifyUpdateSummaryChanged();
        }
        finally
        {
            _operationGate.Release();
        }
    }
}
