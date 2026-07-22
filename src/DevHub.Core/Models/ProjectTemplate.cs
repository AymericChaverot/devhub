namespace DevHub.Core.Models;

/// <summary>How a <see cref="ProjectTemplate"/> produces its files.</summary>
public enum ScaffoldKind
{
    /// <summary>Runs <see cref="ProjectTemplate.CommandTemplate"/> through the shell.</summary>
    CliCommand,

    /// <summary>No universal CLI exists; the scaffolder writes starter files directly.</summary>
    InProcess,
}

/// <summary>A predefined stack the New Project wizard can scaffold.</summary>
public sealed record ProjectTemplate
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Badge/tag shown next to the template, e.g. "TypeScript", "Rust".</summary>
    public required string Language { get; init; }

    public required ScaffoldKind Kind { get; init; }

    /// <summary>Shell command with a "{name}" placeholder. Null for <see cref="ScaffoldKind.InProcess"/>.</summary>
    public string? CommandTemplate { get; init; }

    /// <summary>
    /// Whether the scaffold command creates its own target folder (e.g. "cargo new", which fails
    /// if the folder already exists). When true the command runs in the parent directory instead
    /// of a pre-created one.
    /// </summary>
    public bool CreatesOwnDirectory { get; init; }

    /// <summary>Executable probed with "--version" to decide whether the runtime is installed.</summary>
    public required string CheckExecutable { get; init; }

    /// <summary>Name as it appears in the Environments catalog, for the "Install via Environments" link.</summary>
    public required string RuntimeDisplayName { get; init; }

    /// <summary>Resolves <see cref="CommandTemplate"/> for display and execution.</summary>
    public string ResolveCommand(string projectName) =>
        CommandTemplate?.Replace("{name}", projectName) ?? "";
}
