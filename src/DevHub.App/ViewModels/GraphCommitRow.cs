using DevHub.App.Controls;
using DevHub.Core.Models;

namespace DevHub.App.ViewModels;

/// <summary>One row of the Git tab: graph geometry plus commit details.</summary>
public sealed class GraphCommitRow
{
    public GraphCommitRow(GitGraphRow row, int maxLane)
    {
        Row = row;
        GraphWidth = (maxLane + 1) * CommitGraphCanvas.LaneWidth;
        WhenText = Humanize(row.When);
    }

    /// <summary>Typed as object so the canvas dependency property binds without casts.</summary>
    public object Row { get; }

    private GitGraphRow Typed => (GitGraphRow)Row;

    public string ShortSha => Typed.ShortSha;

    public string Message => Typed.Message;

    public string Author => Typed.Author;

    public string WhenText { get; }

    public double GraphWidth { get; }

    public IReadOnlyList<string> Refs => Typed.Refs;

    public bool HasRefs => Typed.Refs.Count > 0;

    internal static string Humanize(DateTimeOffset when)
    {
        var age = DateTimeOffset.Now - when;
        return age switch
        {
            { TotalMinutes: < 1 } => Loc.T("Forge_JustNow", "just now"),
            { TotalHours: < 1 } => Loc.F("Forge_MinutesAgo", "{0} min ago", (int)age.TotalMinutes),
            { TotalDays: < 1 } => Loc.F("Forge_HoursAgo", "{0} h ago", (int)age.TotalHours),
            { TotalDays: < 2 } => Loc.T("Forge_Yesterday", "yesterday"),
            { TotalDays: < 30 } => Loc.F("Forge_DaysAgoLong", "{0} days ago", (int)age.TotalDays),
            _ => when.ToString("d MMM yyyy", Loc.Culture),
        };
    }
}
