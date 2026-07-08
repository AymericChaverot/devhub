namespace DevHub.Core.Models;

/// <summary>An IDE or editor detected on this machine.</summary>
public sealed record IdeInfo
{
    /// <summary>Stable identifier persisted in per-project preferences, e.g. "vscode".</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string ExecutablePath { get; init; }
}
