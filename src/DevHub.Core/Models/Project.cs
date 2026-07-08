namespace DevHub.Core.Models;

/// <summary>A project folder inside the user's Projects directory.</summary>
public sealed record Project
{
    /// <summary>Folder name; the display name for now.</summary>
    public required string Name { get; init; }

    /// <summary>Absolute path — also the identity of a project.</summary>
    public required string Path { get; init; }

    /// <summary>URL of the git "origin" remote, when the folder is a git repository with one.</summary>
    public string? GitRemoteUrl { get; init; }

    /// <summary>Whether the folder is a git repository (even without a remote).</summary>
    public bool IsGitRepository { get; init; }

    public DateTime LastScannedAt { get; init; }

    /// <summary>Languages detected from marker files (root + one level deep).</summary>
    public IReadOnlyList<DetectedLanguage> Languages { get; init; } = [];

    /// <summary>Frameworks detected from manifests (React, ASP.NET Core, Tauri...).</summary>
    public IReadOnlyList<DetectedFramework> Frameworks { get; init; } = [];
}
