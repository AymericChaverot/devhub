using Microsoft.UI.Xaml.Media.Imaging;

namespace DevHub.App.ViewModels;

/// <summary>File-type icon and syntax-highlighting language lookup for the Files tab.</summary>
public static class FileVisuals
{
    /// <summary>Well-known file names beat extension matching.</summary>
    private static readonly Dictionary<string, string> SpecialFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["package.json"] = "npm",
        ["package-lock.json"] = "npm",
        ["tsconfig.json"] = "typescript",
        ["cargo.toml"] = "rust",
        ["cargo.lock"] = "rust",
        ["go.mod"] = "go",
        ["go.sum"] = "go",
        ["dockerfile"] = "docker",
        ["docker-compose.yml"] = "docker",
        ["docker-compose.yaml"] = "docker",
        [".gitignore"] = "git",
        [".gitattributes"] = "git",
        ["gemfile"] = "ruby",
        ["composer.json"] = "php",
    };

    private static readonly Dictionary<string, string> ExtensionIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        [".ts"] = "typescript",
        [".tsx"] = "typescript",
        [".js"] = "javascript",
        [".jsx"] = "javascript",
        [".mjs"] = "javascript",
        [".cjs"] = "javascript",
        [".rs"] = "rust",
        [".py"] = "python",
        [".go"] = "go",
        [".cs"] = "dotnet",
        [".csproj"] = "dotnet",
        [".sln"] = "dotnet",
        [".slnx"] = "dotnet",
        [".kt"] = "kotlin",
        [".kts"] = "kotlin",
        [".java"] = "java",
        [".php"] = "php",
        [".rb"] = "ruby",
        [".vue"] = "vuedotjs",
        [".svelte"] = "svelte",
        [".astro"] = "astro",
        [".html"] = "html5",
        [".htm"] = "html5",
        [".css"] = "css",
        [".scss"] = "sass",
        [".sass"] = "sass",
        [".json"] = "json",
        [".md"] = "markdown",
        [".yml"] = "yaml",
        [".yaml"] = "yaml",
        [".toml"] = "toml",
        [".sh"] = "gnubash",
        [".bash"] = "gnubash",
        [".ps1"] = "powershell",
        [".psm1"] = "powershell",
        [".gradle"] = "gradle",
        [".zig"] = "zig",
    };

    private static readonly Dictionary<string, string> ExtensionLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        [".ts"] = "typescript",
        [".tsx"] = "typescript",
        [".js"] = "javascript",
        [".jsx"] = "javascript",
        [".mjs"] = "javascript",
        [".cjs"] = "javascript",
        [".rs"] = "rust",
        [".py"] = "python",
        [".go"] = "go",
        [".cs"] = "csharp",
        [".kt"] = "kotlin",
        [".kts"] = "kotlin",
        [".java"] = "java",
        [".php"] = "php",
        [".rb"] = "ruby",
        [".html"] = "xml",
        [".htm"] = "xml",
        [".xml"] = "xml",
        [".xaml"] = "xml",
        [".csproj"] = "xml",
        [".svg"] = "xml",
        [".css"] = "css",
        [".scss"] = "scss",
        [".json"] = "json",
        [".md"] = "markdown",
        [".yml"] = "yaml",
        [".yaml"] = "yaml",
        [".toml"] = "ini",
        [".sh"] = "bash",
        [".bash"] = "bash",
        [".ps1"] = "powershell",
        [".psm1"] = "powershell",
        [".gradle"] = "groovy",
        [".sql"] = "sql",
        [".c"] = "c",
        [".h"] = "c",
        [".cpp"] = "cpp",
        [".hpp"] = "cpp",
        [".txt"] = "plaintext",
        [".log"] = "plaintext",
        [".lock"] = "plaintext",
    };

    /// <summary>
    /// Brand icon for a file, or null when no specific icon applies. In dark theme,
    /// brightened variants are preferred (white for pure-black brands like Markdown).
    /// </summary>
    public static SvgImageSource? IconFor(string fileName, bool darkTheme)
    {
        var slug = SpecialFiles.GetValueOrDefault(fileName)
            ?? ExtensionIcons.GetValueOrDefault(Path.GetExtension(fileName));
        return slug is null ? null : BadgeVisuals.TechIcon(slug, darkTheme);
    }

    /// <summary>highlight.js language class for a file; empty string lets hljs auto-detect.</summary>
    public static string LanguageFor(string fileName)
    {
        if (fileName.Equals("dockerfile", StringComparison.OrdinalIgnoreCase))
        {
            return "dockerfile";
        }

        return ExtensionLanguages.GetValueOrDefault(Path.GetExtension(fileName), "");
    }
}
