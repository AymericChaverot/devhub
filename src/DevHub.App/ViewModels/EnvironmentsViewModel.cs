using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DevHub.Core.Services;
using Serilog;

namespace DevHub.App.ViewModels;

public sealed partial class EnvironmentsViewModel : ObservableObject
{
    public const string AllCategories = "All categories";

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

    public IReadOnlyList<string> StatusOptions { get; } = ["All statuses", "Installed", "Not installed"];

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

    public void ClearError() => ErrorMessage = null;

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
                        .Select(v => new ToolVariantViewModel(tool.DisplayName, v, EnqueueInstallAsync, EnqueueUninstallAsync))
                        .ToList()))
                .ToList();

            foreach (var category in _allCards.Select(c => c.Category).Distinct())
            {
                CategoryOptions.Add(category);
            }

            ApplyFilters();

            var installed = await _packages.GetInstalledVersionsAsync();
            foreach (var variant in _allCards.SelectMany(c => c.Variants))
            {
                // Exact id match first; fall back to the display-name key for packages
                // winget could only bulk-match by name (installed outside winget).
                if (installed.TryGetValue(variant.WingetId, out var version)
                    || installed.TryGetValue(IPackageService.NameKey(variant.ToolName), out version))
                {
                    variant.InstalledVersion = version;
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
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load the environments catalog");
            ErrorMessage = $"Could not query winget for installed packages: {ex.Message}";
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

    private bool MatchesStatusFilter(ToolCardViewModel card) => SelectedStatus switch
    {
        "Installed" => card.Variants.Any(v => v.State == PackageState.Installed),
        "Not installed" => card.Variants.All(v => v.State != PackageState.Installed),
        _ => true,
    };

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
                variant.State = PackageState.NotInstalled;
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
}
