using CommunityToolkit.Mvvm.ComponentModel;
using DevHub.Core.Models;

namespace DevHub.App.ViewModels;

/// <summary>One row of the Projects list.</summary>
public sealed partial class ProjectViewModel : ObservableObject
{
    public ProjectViewModel(Project project, bool isPinned = false)
    {
        Project = project;
        IsPinned = isPinned;
    }

    public Project Project { get; }

    public string Name => Project.Name;

    public string Path => Project.Path;

    public bool IsGitRepository => Project.IsGitRepository;

    public string? GitRemoteUrl => Project.GitRemoteUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinGlyph))]
    public partial bool IsPinned { get; set; }

    public string PinGlyph => IsPinned ? "\uE735" : "\uE734";

    /// <summary>"github.com" for https and ssh remotes; empty when there is no remote.</summary>
    public string RemoteHost
    {
        get
        {
            if (Project.GitRemoteUrl is not { } url)
            {
                return "";
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0)
            {
                return uri.Host;
            }

            // ssh shorthand: git@github.com:user/repo.git
            var at = url.IndexOf('@');
            var colon = url.IndexOf(':', at + 1);
            return at >= 0 && colon > at ? url[(at + 1)..colon] : "";
        }
    }

    public bool HasRemote => RemoteHost.Length > 0;

    /// <summary>Branch and sync state; null until loaded in the background (or not a repository).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGitStatus))]
    [NotifyPropertyChangedFor(nameof(BranchName))]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyPropertyChangedFor(nameof(ChangesText))]
    [NotifyPropertyChangedFor(nameof(ChangesToolTip))]
    [NotifyPropertyChangedFor(nameof(HasAhead))]
    [NotifyPropertyChangedFor(nameof(AheadText))]
    [NotifyPropertyChangedFor(nameof(AheadToolTip))]
    [NotifyPropertyChangedFor(nameof(HasBehind))]
    [NotifyPropertyChangedFor(nameof(BehindText))]
    [NotifyPropertyChangedFor(nameof(BehindToolTip))]
    public partial GitStatusSummary? GitStatus { get; set; }

    public bool HasGitStatus => GitStatus is not null;

    public string BranchName => GitStatus?.Branch ?? "";

    public bool HasChanges => GitStatus?.IsDirty == true;

    public string ChangesText => $"{GitStatus?.ChangedFiles ?? 0}";

    public string ChangesToolTip =>
        Loc.F("Proj_GitChanges", "{0} changed file(s)", GitStatus?.ChangedFiles ?? 0);

    public bool HasAhead => GitStatus?.Ahead > 0;

    public string AheadText => $"↑{GitStatus?.Ahead ?? 0}";

    public string AheadToolTip => Loc.F("Proj_GitAhead", "{0} commit(s) to push", GitStatus?.Ahead ?? 0);

    public bool HasBehind => GitStatus?.Behind > 0;

    public string BehindText => $"↓{GitStatus?.Behind ?? 0}";

    public string BehindToolTip => Loc.F("Proj_GitBehind", "{0} commit(s) to pull", GitStatus?.Behind ?? 0);

    public IReadOnlyList<Badge> Badges { get; private set; } = [];

    /// <summary>Badge construction touches XAML brushes, so it runs lazily on the UI thread.</summary>
    public ProjectViewModel WithBadges()
    {
        Badges =
        [
            .. Project.Languages.Select(Badge.For),
            .. Project.Frameworks.Select(Badge.For),
        ];
        return this;
    }

    public bool Matches(string searchTerm) =>
        Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
        || Path.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
        || RemoteHost.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
        || Project.Languages.Any(l => l.Language.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
        || Project.Frameworks.Any(f => f.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));

    public bool HasTechnology(string techName) =>
        Project.Languages.Any(l => string.Equals(l.Language, techName, StringComparison.OrdinalIgnoreCase))
        || Project.Frameworks.Any(f => string.Equals(f.Name, techName, StringComparison.OrdinalIgnoreCase));
}
