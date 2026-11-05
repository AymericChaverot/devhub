using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevHub.Core.Models;
using DevHub.Core.Services;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Serilog;

namespace DevHub.App.ViewModels;

public sealed record PomodoroHistoryItem(
    string Title,
    string ProjectBadge,
    bool HasProject,
    string DurationText,
    string WhenText);

/// <summary>
/// Pomodoro countdown shown in the navigation pane footer. Wall-clock based (an end
/// timestamp, not tick accumulation) so it stays accurate when the UI is busy.
/// </summary>
public sealed partial class PomodoroViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IProjectStore _projectStore;
    private readonly DispatcherQueueTimer _timer;

    private DateTime _endsAtUtc;
    private TimeSpan _total;
    private TimeSpan _pausedRemaining;
    private int _sessionMinutes;
    private bool _isBreak;
    private string? _sessionProjectName;

    public PomodoroViewModel(ISettingsService settings, IProjectStore projectStore)
    {
        _settings = settings;
        _projectStore = projectStore;
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += (_, _) => UpdateRemaining();
        TimeText = "";
        SessionLabel = "";
        TodaySummaryText = "";
        WeekSummaryText = "";
        NoProjectOption = Loc.T("Pomo_NoProject", "No project");
        ProjectOptions.Add(NoProjectOption);
        SelectedProjectOption = NoProjectOption;
        RefreshStats();
    }

    public string NoProjectOption { get; }

    public ObservableCollection<string> ProjectOptions { get; } = [];

    [ObservableProperty]
    public partial string SelectedProjectOption { get; set; }

    public ObservableCollection<PomodoroHistoryItem> History { get; } = [];

    [ObservableProperty]
    public partial bool HasHistory { get; set; }

    [ObservableProperty]
    public partial string TodaySummaryText { get; set; }

    [ObservableProperty]
    public partial string WeekSummaryText { get; set; }

    /// <summary>A session exists (running or paused); the pane widget is expanded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsActive { get; set; }

    public bool IsIdle => !IsActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseGlyph))]
    [NotifyPropertyChangedFor(nameof(PauseTooltip))]
    public partial bool IsPaused { get; set; }

    public string PauseGlyph => IsPaused ? "\uE768" : "\uE769"; // play / pause

    public string PauseTooltip => IsPaused
        ? Loc.T("Pomo_Resume", "Resume")
        : Loc.T("Pomo_Pause", "Pause");

    [ObservableProperty]
    public partial string TimeText { get; set; }

    /// <summary>Whole minutes left (rounded up) — feeds the nav item's InfoBadge.</summary>
    [ObservableProperty]
    public partial int RemainingMinutes { get; set; }

    [ObservableProperty]
    public partial string SessionLabel { get; set; }

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    public async Task LoadProjectsAsync()
    {
        try
        {
            var current = SelectedProjectOption;
            var projects = await _projectStore.GetAllAsync();
            var ignored = _settings.Settings.IgnoredProjectPaths;
            var names = projects
                .Where(p => !ignored.Contains(p.Path, StringComparer.OrdinalIgnoreCase))
                .Select(p => p.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            ProjectOptions.Clear();
            ProjectOptions.Add(NoProjectOption);
            foreach (var name in names)
            {
                ProjectOptions.Add(name);
            }

            SelectedProjectOption = ProjectOptions.Contains(current) ? current : NoProjectOption;
            RefreshStats();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load projects for Pomodoro picker");
        }
    }

    public void Start(int minutes, string label, bool isBreak = false, string? projectName = null)
    {
        _sessionMinutes = minutes;
        _total = TimeSpan.FromMinutes(minutes);
        _endsAtUtc = DateTime.UtcNow + _total;
        _isBreak = isBreak;
        var chosenProject = projectName
            ?? (SelectedProjectOption != NoProjectOption ? SelectedProjectOption : null);
        _sessionProjectName = isBreak ? null : chosenProject;
        SessionLabel = _sessionProjectName is { Length: > 0 }
            ? $"{label} · {_sessionProjectName}"
            : label;
        IsPaused = false;
        IsActive = true;
        UpdateRemaining();
        _timer.Start();
        Log.Information("Pomodoro started: {Label}, {Minutes} min", SessionLabel, minutes);
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (!IsActive)
        {
            return;
        }

        if (IsPaused)
        {
            _endsAtUtc = DateTime.UtcNow + _pausedRemaining;
            _timer.Start();
            IsPaused = false;
        }
        else
        {
            _pausedRemaining = _endsAtUtc - DateTime.UtcNow;
            _timer.Stop();
            IsPaused = true;
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _timer.Stop();
        IsActive = false;
        IsPaused = false;
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        _settings.Settings.PomodoroHistory.Clear();
        await _settings.SaveAsync();
        RefreshStats();
    }

    public void RefreshStats()
    {
        var records = _settings.Settings.PomodoroHistory;
        var nowLocal = DateTime.Now;
        var todayStartUtc = nowLocal.Date.ToUniversalTime();
        var weekStartUtc = nowLocal.Date.AddDays(-6).ToUniversalTime();

        var focusRecords = records.Where(r => !r.IsBreak).ToList();
        var todayFocus = focusRecords.Where(r => r.CompletedAtUtc >= todayStartUtc).ToList();
        var weekFocus = focusRecords.Where(r => r.CompletedAtUtc >= weekStartUtc).ToList();

        TodaySummaryText = Loc.F(
            "Pomo_StatsFormat",
            "{0} session(s) · {1} min",
            todayFocus.Count,
            todayFocus.Sum(r => r.DurationMinutes));
        WeekSummaryText = Loc.F(
            "Pomo_StatsFormat",
            "{0} session(s) · {1} min",
            weekFocus.Count,
            weekFocus.Sum(r => r.DurationMinutes));

        History.Clear();
        foreach (var item in records.OrderByDescending(r => r.CompletedAtUtc).Take(15))
        {
            var localTime = item.CompletedAtUtc.ToLocalTime();
            var whenText = localTime.Date == nowLocal.Date
                ? localTime.ToString("HH:mm", Loc.Culture)
                : localTime.ToString("dd/MM HH:mm", Loc.Culture);
            History.Add(new PomodoroHistoryItem(
                item.Label,
                item.ProjectName ?? "",
                !string.IsNullOrWhiteSpace(item.ProjectName),
                $"{item.DurationMinutes} min",
                whenText));
        }

        HasHistory = History.Count > 0;
    }

    private void UpdateRemaining()
    {
        var remaining = _endsAtUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            Complete();
            return;
        }

        TimeText = remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Minutes:00}:{remaining.Seconds:00}";
        RemainingMinutes = (int)Math.Ceiling(remaining.TotalMinutes);
        ProgressPercent = _total > TimeSpan.Zero
            ? (1 - remaining.TotalSeconds / _total.TotalSeconds) * 100
            : 0;
    }

    private async void Complete()
    {
        _timer.Stop();
        IsActive = false;
        IsPaused = false;
        Log.Information("Pomodoro finished: {Label}", SessionLabel);

        _settings.Settings.PomodoroHistory.Add(new PomodoroSessionRecord
        {
            CompletedAtUtc = DateTime.UtcNow,
            DurationMinutes = Math.Max(1, _sessionMinutes),
            Label = SessionLabel,
            IsBreak = _isBreak,
            ProjectName = _sessionProjectName,
        });
        if (_settings.Settings.PomodoroHistory.Count > 200)
        {
            _settings.Settings.PomodoroHistory.RemoveRange(0, _settings.Settings.PomodoroHistory.Count - 200);
        }

        await _settings.SaveAsync();
        RefreshStats();

        try
        {
            var message = _isBreak
                ? Loc.T("Pomo_ToastBreakOver", "Break's over — back to it!")
                : Loc.T("Pomo_ToastFocusDone", "Nice work! Time for a break.");
            AppNotificationManager.Default.Show(
                new AppNotificationBuilder()
                    .AddText(Loc.F("Pomo_ToastFinished", "{0} session finished", SessionLabel))
                    .AddText(message)
                    .BuildNotification());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not show the pomodoro toast notification");
        }
    }
}
