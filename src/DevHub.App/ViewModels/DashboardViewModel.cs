using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DevHub.Core.Models;
using DevHub.Core.Services;
using Serilog;

namespace DevHub.App.ViewModels;

/// <summary>
/// One usage gauge on the dashboard (a drive). Mutable and observable so refreshes
/// update the existing card in place — rebuilding it would visibly reset the bar to 0.
/// </summary>
public sealed partial class UsageStat : ObservableObject
{
    public UsageStat(string title, string detailText, string percentText, double percent)
    {
        Title = title;
        DetailText = detailText;
        PercentText = percentText;
        Percent = percent;
    }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string DetailText { get; set; }

    [ObservableProperty]
    public partial string PercentText { get; set; }

    [ObservableProperty]
    public partial double Percent { get; set; }
}

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
        ReviewsFeedLabel = Loc.T("Dash_FeedReviews", "Reviews");
        MyPrsFeedLabel = Loc.T("Dash_FeedMyPrs", "My pull requests");
        IssuesFeedLabel = Loc.T("Dash_FeedIssues", "Issues");
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
                < 6 => Loc.T("Dash_GreetingNight", "Good night"),
                < 12 => Loc.T("Dash_GreetingMorning", "Good morning"),
                < 18 => Loc.T("Dash_GreetingAfternoon", "Good afternoon"),
                _ => Loc.T("Dash_GreetingEvening", "Good evening"),
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
            var text = DateTime.Now.ToString("dddd d MMMM yyyy", Loc.Culture);
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
    public partial ResourceUsage? Usage { get; set; }

    partial void OnUsageChanged(ResourceUsage? value) => UpdateDriveStats();

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
        Machine is null ? "" : Loc.F("Dash_CpuDetail", "{0} cores · {1} threads", Machine.CpuCores, Machine.CpuThreads);

    public string GpuText => Machine is null ? "" : string.Join("\n", Machine.GpuNames);

    public double CpuPercent => Usage?.CpuUsagePercent ?? 0;

    public string CpuPercentText => Usage is null ? "–" : $"{Usage.CpuUsagePercent:0}%";

    public double MemoryPercent =>
        Usage is { TotalMemoryGb: > 0 } ? Usage.UsedMemoryGb / Usage.TotalMemoryGb * 100 : 0;

    public string MemoryPercentText => Usage is null ? "–" : $"{MemoryPercent:0}%";

    public string MemoryDetailText =>
        Usage is null ? "" : Loc.F("Dash_MemoryDetail", "{0:0.0} / {1:0.0} GB", Usage.UsedMemoryGb, Usage.TotalMemoryGb);

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
                ? Loc.F("Dash_UptimeHours", "Up {0} h {1:00} min", (int)up.TotalHours, up.Minutes)
                : Loc.F("Dash_UptimeMinutes", "Up {0} min", up.Minutes);
        }
    }

    public ObservableCollection<UsageStat> DriveStats { get; } = [];

    /// <summary>Syncs the drive cards in place; replacing the list would reset the bars to 0.</summary>
    private void UpdateDriveStats()
    {
        var drives = Usage?.Drives ?? [];
        while (DriveStats.Count > drives.Count)
        {
            DriveStats.RemoveAt(DriveStats.Count - 1);
        }

        for (var i = 0; i < drives.Count; i++)
        {
            var d = drives[i];
            var title = d.Label is null ? d.Name : $"{d.Name} · {d.Label}";
            var detail = Loc.F("Dash_DriveFree", "{0:0} GB free of {1:0} GB", d.TotalGb - d.UsedGb, d.TotalGb);
            var percent = d.UsedRatio * 100;

            if (i < DriveStats.Count)
            {
                DriveStats[i].Title = title;
                DriveStats[i].DetailText = detail;
                DriveStats[i].PercentText = $"{percent:0}%";
                DriveStats[i].Percent = percent;
            }
            else
            {
                DriveStats.Add(new UsageStat(title, detail, $"{percent:0}%", percent));
            }
        }
    }

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
        Weather is null
            ? ""
            : Loc.F("Dash_WeatherRange", "{0:0}° / {1:0}° · Feels like {2:0}°", Weather.TodayMinC, Weather.TodayMaxC, Weather.FeelsLikeC);

    public string WeatherDetailText =>
        Weather is null
            ? ""
            : Loc.F("Dash_WeatherDetail", "Wind {0:0} km/h · Humidity {1}%", Weather.WindKmh, Weather.HumidityPercent);

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
        ForgeFeed.ReviewRequests => Loc.T("Dash_NoReviews", "No reviews waiting for you. Nice."),
        ForgeFeed.AuthoredPullRequests => Loc.T("Dash_NoPrs", "You have no open pull requests."),
        _ => Loc.T("Dash_NoIssues", "No issues assigned to you."),
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

            ReviewsFeedLabel = $"{Loc.T("Dash_FeedReviews", "Reviews")} ({_feeds[ForgeFeed.ReviewRequests].Count})";
            MyPrsFeedLabel = $"{Loc.T("Dash_FeedMyPrs", "My pull requests")} ({_feeds[ForgeFeed.AuthoredPullRequests].Count})";
            IssuesFeedLabel = $"{Loc.T("Dash_FeedIssues", "Issues")} ({_feeds[ForgeFeed.AssignedIssues].Count})";
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
