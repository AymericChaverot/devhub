using DevHub.Core.Models;

namespace DevHub.Core.Services;

/// <summary>
/// Fixed list of scaffold templates. Kept as code rather than an embedded catalog.json (unlike
/// <see cref="ICatalogService"/>) because each row carries executable semantics
/// (command template, directory behavior) rather than just display data.
/// </summary>
public sealed class ProjectTemplateCatalog : IProjectTemplateCatalog
{
    private static readonly IReadOnlyList<ProjectTemplate> Templates =
    [
        new ProjectTemplate
        {
            Id = "nextjs",
            DisplayName = "Next.js",
            Language = "TypeScript",
            Kind = ScaffoldKind.CliCommand,
            CommandTemplate = "npx create-next-app@latest {name} --typescript --eslint --app --use-npm",
            CreatesOwnDirectory = true,
            CheckExecutable = "node",
            RuntimeDisplayName = "Node.js",
        },
        new ProjectTemplate
        {
            Id = "react-vite",
            DisplayName = "React (Vite)",
            Language = "TypeScript",
            Kind = ScaffoldKind.CliCommand,
            CommandTemplate = "npm create vite@latest {name} -- --template react-ts",
            CreatesOwnDirectory = true,
            CheckExecutable = "node",
            RuntimeDisplayName = "Node.js",
        },
        new ProjectTemplate
        {
            Id = "rust-cargo",
            DisplayName = "Rust / Cargo",
            Language = "Rust",
            Kind = ScaffoldKind.CliCommand,
            CommandTemplate = "cargo new {name}",
            CreatesOwnDirectory = true,
            CheckExecutable = "cargo",
            RuntimeDisplayName = "Rust",
        },
        new ProjectTemplate
        {
            Id = "go",
            DisplayName = "Go",
            Language = "Go",
            Kind = ScaffoldKind.CliCommand,
            CommandTemplate = "go mod init {name}",
            CreatesOwnDirectory = false,
            CheckExecutable = "go",
            RuntimeDisplayName = "Go",
        },
        new ProjectTemplate
        {
            Id = "python",
            DisplayName = "Python",
            Language = "Python",
            Kind = ScaffoldKind.InProcess,
            CommandTemplate = null,
            CreatesOwnDirectory = false,
            CheckExecutable = "python",
            RuntimeDisplayName = "Python",
        },
        new ProjectTemplate
        {
            Id = "dotnet",
            DisplayName = ".NET Core",
            Language = "C#",
            Kind = ScaffoldKind.CliCommand,
            CommandTemplate = "dotnet new webapi -o {name}",
            CreatesOwnDirectory = true,
            CheckExecutable = "dotnet",
            RuntimeDisplayName = ".NET SDK",
        },
    ];

    public IReadOnlyList<ProjectTemplate> GetTemplates() => Templates;
}
