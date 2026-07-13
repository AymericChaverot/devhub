using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DevHub.Core.Models;
using DevHub.Core.Services;
using Serilog;

namespace DevHub.App.ViewModels;

/// <summary>One usage gauge on the dashboard (memory or a drive).</summary>
public sealed record UsageStat(string Title, string DetailText, string PercentText, double Percent);

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IWeatherService _weather;
    private readonly IProjectStore _store;
    private readonly ISettingsService _settings;

    public DashboardViewModel(
        ISystemInfoService systemInfo,
        IWeatherService weather,
        IProjectStore store,
        ISettingsService settings,
        EnvironmentsViewModel environments,
        ForgeAccountsViewModel forgeAccounts)
    {
        _systemInfo = systemInfo;
        _weather = weather;
        _store = store;
        _settings = settings;
        Environments = environments;
        ForgeAccounts = forgeAccounts;
        ReviewsFeedLabel = "Reviews";
        MyPrsFeedLabel = "My pull requests";
        IssuesFeedLabel = "Issues";
    }

    /// <summary>The Environments singleton; the "Tool updates" card binds straight to it.</summary>
    public EnvironmentsViewModel Environments { get; }

    public ForgeAccountsViewModel ForgeAccounts { get; }

    public string Greeting
    {
        get
        {
            var greeting = DateTime.Now.Hour switch
            {
                < 6 => "Good night",
                < 12 => "Good morning",
                < 18 => "Good afternoon",
                _ => "Good evening",
            };
            return Machine is { UserFirstName.Length: > 0 }
                ? $"{greeting}, {Machine.UserFirstName}"
                : greeting;
        }
    }

    public string TodayText
    {
        get
        {
            var text = DateTime.Now.ToString("dddd d MMMM yyyy");
            return char.ToUpper(text[0]) + text[1..];
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Greeting))]
    [NotifyPropertyChangedFor(nameof(OsText))]
    [NotifyPropertyChangedFor(nameof(OsName))]
    [NotifyPropertyChangedFor(nameof(OsBuildText))]
    [NotifyPropertyChangedFor(nameof(CpuText))]
    [NotifyPropertyChangedFor(nameof(CpuDetailText))]
    [NotifyPropertyChangedFor(nameof(GpuText))]
    public partial MachineInfo? Machine { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuPercent))]
    [NotifyPropertyChangedFor(nameof(CpuPercentText))]
    [NotifyPropertyChangedFor(nameof(MemoryPercent))]
    [NotifyPropertyChangedFor(nameof(MemoryPercentText))]
    [NotifyPropertyChangedFor(nameof(MemoryDetailText))]
    [NotifyPropertyChangedFor(nameof(UptimeText))]
    [NotifyPropertyChangedFor(nameof(DriveStats))]
    public partial ResourceUsage? Usage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWeather))]
    [NotifyPropertyChangedFor(nameof(WeatherEmoji))]
    [NotifyPropertyChangedFor(nameof(WeatherTempText))]
    [NotifyPropertyChangedFor(nameof(WeatherConditionText))]
    [NotifyPropertyChangedFor(nameof(WeatherPlaceText))]
    [NotifyPropertyChangedFor(nameof(WeatherRangeText))]
    [NotifyPropertyChangedFor(nameof(WeatherDetailText))]
    public partial WeatherInfo? Weather { get; set; }

    public string OsText => Machine is null ? "" : $"{Machine.OsName} · {Machine.OsBuild} · {Machine.MachineName}";

    public string OsName => Machine?.OsName ?? "";

    public string OsBuildText => Machine is null ? "" : $"{Machine.OsBuild} · {Machine.MachineName}";

    public string CpuText => Machine?.CpuName ?? "";

    public string CpuDetailText =>
        Machine is null ? "" : $"{Machine.CpuCores} cores · {Machine.CpuThreads} threads";

    public string GpuText => Machine is null ? "" : string.Join("\n", Machine.GpuNames);

    public double CpuPercent => Usage?.CpuUsagePercent ?? 0;

    public string CpuPercentText => Usage is null ? "–" : $"{Usage.CpuUsagePercent:0}%";

    public double MemoryPercent =>
        Usage is { TotalMemoryGb: > 0 } ? Usage.UsedMemoryGb / Usage.TotalMemoryGb * 100 : 0;

    public string MemoryPercentText => Usage is null ? "–" : $"{MemoryPercent:0}%";

    public string MemoryDetailText =>
        Usage is null ? "" : $"{Usage.UsedMemoryGb:0.0} / {Usage.TotalMemoryGb:0.0} GB";

    public string UptimeText
    {
        get
        {
            if (Usage is null)
            {
                return "";
            }

            var up = Usage.Uptime;
            return up.TotalHours >= 1
                ? $"Up {(int)up.TotalHours} h {up.Minutes:00} min"
                : $"Up {up.Minutes} min";
        }
    }

    public IReadOnlyList<UsageStat> DriveStats =>
        Usage?.Drives
            .Select(d => new UsageStat(
                d.Label is null ? d.Name : $"{d.Name} · {d.Label}",
                $"{d.TotalGb - d.UsedGb:0} GB free of {d.TotalGb:0} GB",
                $"{d.UsedRatio * 100:0}%",
                d.UsedRatio * 100))
            .ToList() ?? [];

    public bool HasWeather => Weather is not null;

    public string WeatherEmoji => Weather?.ConditionEmoji ?? "";

    public string WeatherTempText => Weather is null ? "" : $"{Weather.TemperatureC:0}°";

    public string WeatherConditionText => Weather?.ConditionText ?? "";

    /// <summary>"Lyon, France" — whichever parts the location source provided.</summary>
    public string WeatherPlaceText =>
        Weather is null
            ? ""
            : string.Join(", ", new[] { Weather.City, Weather.Country }.Where(p => p.Length > 0));

    public string WeatherRangeText =>
        Weather is null ? "" : $"{Weather.TodayMinC:0}° / {Weather.TodayMaxC:0}° · Feels like {Weather.FeelsLikeC:0}°";

    public string WeatherDetailText =>
        Weather is null ? "" : $"Wind {Weather.WindKmh:0} km/h · Humidity {Weather.HumidityPercent}%";

    public ObservableCollection<ProjectViewModel> RecentProjects { get; } = [];

    /// <summary>The rows of the currently selected feed.</summary>
    public ObservableCollection<ForgeItemViewModel> WorkItems { get; } = [];

    private readonly Dictionary<ForgeFeed, List<ForgeItemViewModel>> _feeds = new()
    {
        [ForgeFeed.ReviewRequests] = [],
        [ForgeFeed.AuthoredPullRequests] = [],
        [ForgeFeed.AssignedIssues] = [],
    };

    /// <summary>0 = review requests, 1 = my PRs, 2 = assigned issues (SelectorBar order).</summary>
    [ObservableProperty]
    public partial int SelectedFeedIndex { get; set; }

    partial void OnSelectedFeedIndexChanged(int value) => ShowSelectedFeed();

    private ForgeFeed SelectedFeed => (ForgeFeed)SelectedFeedIndex;

    [ObservableProperty]
    public partial bool IsLoadingWork { get; set; }

    [ObservableProperty]
    public partial bool HasWorkItems { get; set; }

    /// <summary>No forge account connected yet — the card suggests going to Settings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFeedBar))]
    public partial bool ShowConnectHint { get; set; }

    public bool ShowFeedBar => !ShowConnectHint;

    /// <summary>Accounts connected and the selected feed is empty.</summary>
    [ObservableProperty]
    public partial bool ShowWorkAllClear { get; set; }

    [ObservableProperty]
    public partial string ReviewsFeedLabel { get; set; }

    [ObservableProperty]
    public partial string MyPrsFeedLabel { get; set; }

    [ObservableProperty]
    public partial string IssuesFeedLabel { get; set; }

    public string WorkEmptyText => SelectedFeed switch
    {
        ForgeFeed.ReviewRequests => "No reviews waiting for you. Nice.",
        ForgeFeed.AuthoredPullRequests => "You have no open pull requests.",
        _ => "No issues assigned to you.",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoRecentProjects))]
    public partial bool HasRecentProjects { get; set; }

    public bool HasNoRecentProjects => !HasRecentProjects;

    /// <summary>Loads machine facts, usage, recent projects, then kicks off the tools inventory.</summary>
    public async Task LoadAsync()
    {
        // Network fetches; they run alongside the local loads and fill their cards when they land.
        _ = LoadWeatherAsync();
        _ = LoadWorkAsync();

        try
        {
            Machine = await _systemInfo.GetMachineInfoAsync();
            await RefreshUsageAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read system info for the dashboard");
        }

        await LoadRecentProjectsAsync();

        // No-op after the first run; populates the "Tool updates" card.
        await Environments.LoadAsync();
    }

    /// <summary>Re-reads CPU/memory/disk numbers; the page calls this on a timer.</summary>
    public async Task RefreshUsageAsync()
    {
        try
        {
            Usage = await _systemInfo.GetResourceUsageAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to refresh resource usage");
        }
    }

    private async Task LoadWorkAsync()
    {
        if (!ForgeAccounts.AnyConnected)
        {
            ShowConnectHint = true;
            ShowWorkAllClear = false;
            HasWorkItems = false;
            return;
        }

        ShowConnectHint = false;
        IsLoadingWork = true;
        try
        {
            // One aggregated fetch per feed, all feeds and accounts in parallel.
            var feeds = _feeds.Keys.ToList();
            var fetches = feeds.Select(async feed =>
            {
                var all = new List<ForgeItem>();
                foreach (var account in ForgeAccounts.Accounts)
                {
                    all.AddRange(await account.GetFeedAsync(feed));
                }

                return (feed, items: all
                    .OrderByDescending(i => i.UpdatedAt)
                    .Take(15)
                    .Select(i => new ForgeItemViewModel(i))
                    .ToList());
            });

            foreach (var (feed, items) in await Task.WhenAll(fetches))
            {
                _feeds[feed] = items;
            }

            ReviewsFeedLabel = $"Reviews ({_feeds[ForgeFeed.ReviewRequests].Count})";
            MyPrsFeedLabel = $"My pull requests ({_feeds[ForgeFeed.AuthoredPullRequests].Count})";
            IssuesFeedLabel = $"Issues ({_feeds[ForgeFeed.AssignedIssues].Count})";
            ShowSelectedFeed();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load the work feeds for the dashboard");
        }
        finally
        {
            IsLoadingWork = false;
        }
    }

    private void ShowSelectedFeed()
    {
        WorkItems.Clear();
        foreach (var item in _feeds[SelectedFeed])
        {
            WorkItems.Add(item);
        }

        HasWorkItems = WorkItems.Count > 0;
        ShowWorkAllClear = !HasWorkItems && !ShowConnectHint;
        OnPropertyChanged(nameof(WorkEmptyText));
    }

    private async Task LoadWeatherAsync()
    {
        var weather = await _weather.GetCurrentAsync();
        if (weather is null)
        {
            Log.Information("Weather unavailable (offline or endpoint unreachable); hiding the card");
            return;
        }

        Weather = weather;
    }

    private async Task LoadRecentProjectsAsync()
    {
        try
        {
            var opened = _settings.Settings.ProjectLastOpened;
            var ignored = _settings.Settings.IgnoredProjectPaths;
            var projects = (await _store.GetAllAsync())
                .Where(p => !ignored.Contains(p.Path, StringComparer.OrdinalIgnoreCase))
                .Where(p => opened.ContainsKey(p.Path))
                .OrderByDescending(p => opened[p.Path])
                .Take(6)
                .ToList();

            RecentProjects.Clear();
            foreach (var project in projects)
            {
                RecentProjects.Add(new ProjectViewModel(project).WithBadges());
            }

            HasRecentProjects = RecentProjects.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load recent projects for the dashboard");
        }
    }
}
