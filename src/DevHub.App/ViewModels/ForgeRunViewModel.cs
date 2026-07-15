using DevHub.Core.Models;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DevHub.App.ViewModels;

/// <summary>One CI run row (GitHub Actions workflow run / GitLab pipeline).</summary>
public sealed class ForgeRunViewModel
{
    // GitHub's status palette; readable on light and dark backgrounds.
    private static readonly Color SuccessColor = Color.FromArgb(255, 0x2D, 0xA4, 0x4E);
    private static readonly Color FailedColor = Color.FromArgb(255, 0xCF, 0x22, 0x2E);
    private static readonly Color RunningColor = Color.FromArgb(255, 0x09, 0x69, 0xDA);
    private static readonly Color NeutralColor = Color.FromArgb(255, 0x6E, 0x77, 0x81);

    private readonly ForgeWorkflowRun _run;

    public ForgeRunViewModel(ForgeWorkflowRun run) => _run = run;

    public string Name => _run.Name;

    public string Title => _run.Title;

    public bool HasTitle => _run.Title.Length > 0;

    public string WebUrl => _run.WebUrl;

    /// <summary>"#42 · main · push · 3 min 12 s" — only the parts the forge provided.</summary>
    public string DetailText
    {
        get
        {
            var parts = new List<string>();
            if (_run.Number > 0)
            {
                parts.Add($"#{_run.Number}");
            }

            if (_run.Branch.Length > 0)
            {
                parts.Add(_run.Branch);
            }

            if (_run.Event.Length > 0)
            {
                parts.Add(_run.Event.Replace('_', ' '));
            }

            if (_run.Duration is { } duration)
            {
                parts.Add(FormatDuration(duration));
            }

            return string.Join(" · ", parts);
        }
    }

    public string TimeText =>
        $"{_run.UpdatedAt.ToLocalTime().ToString("d MMM HH:mm", Loc.Culture)} · {ForgeItemViewModel.RelativeTime(_run.UpdatedAt)}";

    public string StatusLabel => _run.Status switch
    {
        ForgeRunStatus.Success => Loc.T("Forge_RunSuccess", "Success"),
        ForgeRunStatus.Failed => Loc.T("Forge_RunFailed", "Failed"),
        ForgeRunStatus.Running => Loc.T("Forge_RunRunning", "Running"),
        ForgeRunStatus.Cancelled => Loc.T("Forge_RunCancelled", "Cancelled"),
        _ => Loc.T("Forge_RunDone", "Done"),
    };

    public string StatusGlyph => _run.Status switch
    {
        ForgeRunStatus.Success => "\uE73E",   // check mark
        ForgeRunStatus.Failed => "\uE711",    // cross
        ForgeRunStatus.Running => "\uE895",   // sync arrows
        ForgeRunStatus.Cancelled => "\uE738", // remove
        _ => "\uE9CE",                        // question mark
    };

    public Brush StatusBrush => new SolidColorBrush(StatusColor);

    /// <summary>The status color at 15% opacity — the pill's background.</summary>
    public Brush StatusBackground => new SolidColorBrush(StatusColor with { A = 0x26 });

    private Color StatusColor => _run.Status switch
    {
        ForgeRunStatus.Success => SuccessColor,
        ForgeRunStatus.Failed => FailedColor,
        ForgeRunStatus.Running => RunningColor,
        _ => NeutralColor,
    };

    private static string FormatDuration(TimeSpan duration) => duration switch
    {
        { TotalSeconds: < 60 } => $"{(int)duration.TotalSeconds} s",
        { TotalHours: < 1 } => $"{(int)duration.TotalMinutes} min {duration.Seconds:00} s",
        _ => $"{(int)duration.TotalHours} h {duration.Minutes:00} min",
    };
}
