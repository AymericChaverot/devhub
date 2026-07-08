using DevHub.Core.Models;

namespace DevHub.App.ViewModels;

/// <summary>One row of the Projects list. Immutable — the list is rebuilt on every rescan.</summary>
public sealed class ProjectViewModel
{
    public ProjectViewModel(Project project)
    {
        Project = project;
    }

    public Project Project { get; }

    public string Name => Project.Name;

    public string Path => Project.Path;

    public bool IsGitRepository => Project.IsGitRepository;

    public string? GitRemoteUrl => Project.GitRemoteUrl;

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
}
