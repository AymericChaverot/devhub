namespace DevHub.Core.Models;

/// <summary>A framework detected in a project (e.g. React, ASP.NET Core, Tauri).</summary>
public sealed record DetectedFramework
{
    public required string Name { get; init; }

    /// <summary>Declared dependency version when available (cleaned of ^/~ range prefixes).</summary>
    public string? Version { get; init; }

    public required string EvidenceFile { get; init; }
}
