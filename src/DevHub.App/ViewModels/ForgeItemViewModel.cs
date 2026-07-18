using DevHub.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DevHub.App.ViewModels;

/// <summary>A PR/MR or issue row (dashboard feeds and the project forge tab).</summary>
public sealed class ForgeItemViewModel
{
    private readonly ForgeItem _item;

    public ForgeItemViewModel(ForgeItem item, bool showRepo = true)
    {
        _item = item;
        ShowRepo = showRepo;
    }

    /// <summary>False on the project page, where every row is the same repo.</summary>
    public bool ShowRepo { get; }

    public string Title => _item.Title;

    public string WebUrl => _item.WebUrl;

    public string ForgeName => _item.Forge == ForgeKind.GitHub ? "GitHub" : "GitLab";

    public bool IsDraft => _item.IsDraft;

    public bool IsClosed => _item.State == ForgeItemState.Closed;

    public bool IsMerged => _item.State == ForgeItemState.Merged;

    public bool IsPullRequest => _item.Kind == ForgeItemKind.PullRequest;

    public bool IsIssue => _item.Kind == ForgeItemKind.Issue;

    /// <summary>Draft/Closed/Merged, whichever applies; empty for a plain open item.</summary>
    public string StateBadgeText => _item.State switch
    {
        ForgeItemState.Merged => Loc.T("Common_MergedBadge", "Merged"),
        ForgeItemState.Closed => Loc.T("Common_ClosedBadge", "Closed"),
        _ => _item.IsDraft ? Loc.T("Common_DraftBadge", "Draft") : "",
    };

    public bool HasStateBadge => StateBadgeText.Length > 0;

    /// <summary>Open items are green (GitHub-style), merged purple, closed red; drafts are toned down.</summary>
    public Brush IconBrush
    {
        get
        {
            if (_item.State == ForgeItemState.Merged)
            {
                return new SolidColorBrush(Color.FromArgb(0xFF, 0x82, 0x50, 0xDF));
            }

            var key = _item.State switch
            {
                ForgeItemState.Closed => "SystemFillColorCriticalBrush",
                _ => _item.IsDraft ? "TextFillColorSecondaryBrush" : "SystemFillColorSuccessBrush",
            };
            return (Brush)Application.Current.Resources[key];
        }
    }

    public string MetaText
    {
        get
        {
            // GitLab prefixes MRs with '!' and issues with '#'; GitHub always uses '#'.
            var symbol = _item is { Forge: ForgeKind.GitLab, Kind: ForgeItemKind.PullRequest } ? "!" : "#";
            var reference = $"{symbol}{_item.Number}";
            var prefix = ShowRepo ? $"{_item.RepoFullName} {reference}" : reference;
            return Loc.F("Forge_ItemMeta", "{0} · by {1} · updated {2}", prefix, _item.Author, RelativeTime(_item.UpdatedAt));
        }
    }

    internal static string RelativeTime(DateTime utc)
    {
        var age = DateTime.UtcNow - utc.ToUniversalTime();
        return age switch
        {
            { TotalMinutes: < 1 } => Loc.T("Forge_JustNow", "just now"),
            { TotalHours: < 1 } => Loc.F("Forge_MinutesAgo", "{0} min ago", (int)age.TotalMinutes),
            { TotalDays: < 1 } => Loc.F("Forge_HoursAgo", "{0} h ago", (int)age.TotalHours),
            { TotalDays: < 30 } => Loc.F("Forge_DaysAgo", "{0} d ago", (int)age.TotalDays),
            _ => utc.ToLocalTime().ToString("d MMM yyyy", Loc.Culture),
        };
    }
}
