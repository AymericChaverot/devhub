using DevHub.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

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

    public bool IsPullRequest => _item.Kind == ForgeItemKind.PullRequest;

    public bool IsIssue => _item.Kind == ForgeItemKind.Issue;

    /// <summary>Open items are green (GitHub-style); drafts are toned down.</summary>
    public Brush IconBrush => (Brush)Application.Current.Resources[
        _item.IsDraft ? "TextFillColorSecondaryBrush" : "SystemFillColorSuccessBrush"];

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
