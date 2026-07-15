using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Serilog;

namespace DevHub.App.ViewModels;

/// <summary>
/// Pomodoro countdown shown in the navigation pane footer. Wall-clock based (an end
/// timestamp, not tick accumulation) so it stays accurate when the UI is busy.
/// </summary>
public sealed partial class PomodoroViewModel : ObservableObject
{
    private readonly DispatcherQueueTimer _timer;

    private DateTime _endsAtUtc;
    private TimeSpan _total;
    private TimeSpan _pausedRemaining;
    private bool _isBreak;

    public PomodoroViewModel()
    {
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += (_, _) => UpdateRemaining();
        TimeText = "";
        SessionLabel = "";
    }

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

    public void Start(int minutes, string label, bool isBreak = false)
    {
        _total = TimeSpan.FromMinutes(minutes);
        _endsAtUtc = DateTime.UtcNow + _total;
        _isBreak = isBreak;
        SessionLabel = label;
        IsPaused = false;
        IsActive = true;
        UpdateRemaining();
        _timer.Start();
        Log.Information("Pomodoro started: {Label}, {Minutes} min", label, minutes);
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

    private void Complete()
    {
        _timer.Stop();
        IsActive = false;
        IsPaused = false;
        Log.Information("Pomodoro finished: {Label}", SessionLabel);

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
