namespace DevHub.Core.Models;

/// <summary>A language/stack detected in a project by marker files.</summary>
public sealed record DetectedLanguage
{
    /// <summary>Display name, e.g. "Rust", "Node.js".</summary>
    public required string Language { get; init; }

    /// <summary>Declared version when a manifest states one (e.g. "1.79", "&gt;=3.11"), else null.</summary>
    public string? Version { get; init; }

    /// <summary>Marker file that triggered the detection, e.g. "Cargo.toml".</summary>
    public required string EvidenceFile { get; init; }
}
