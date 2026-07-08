using System.Text.Json;
using System.Text.RegularExpressions;
using DevHub.Core.Models;

namespace DevHub.Core.Services;

/// <summary>
/// Detects frameworks from project manifests (root + one level deep, like languages).
/// JavaScript gets the deepest coverage; other ecosystems check their main manifests.
/// </summary>
public static class FrameworkDetector
{
    /// <summary>Dependency key in package.json → framework name, in priority order.</summary>
    private static readonly (string Dependency, string Name)[] JsRules =
    [
        ("next", "Next.js"),
        ("nuxt", "Nuxt"),
        ("@remix-run/react", "Remix"),
        ("gatsby", "Gatsby"),
        ("@angular/core", "Angular"),
        ("@sveltejs/kit", "SvelteKit"),
        ("svelte", "Svelte"),
        ("solid-js", "SolidJS"),
        ("astro", "Astro"),
        ("react", "React"),
        ("vue", "Vue.js"),
        ("@nestjs/core", "NestJS"),
        ("express", "Express"),
        ("fastify", "Fastify"),
        ("electron", "Electron"),
        ("@tauri-apps/api", "Tauri"),
        ("vite", "Vite"),
    ];

    /// <summary>Meta-frameworks imply their base library; the base is not reported separately.</summary>
    private static readonly Dictionary<string, string[]> Implies = new()
    {
        ["Next.js"] = ["React"],
        ["Remix"] = ["React"],
        ["Gatsby"] = ["React"],
        ["Nuxt"] = ["Vue.js"],
        ["SvelteKit"] = ["Svelte"],
    };

    private static readonly (string Pattern, string Name)[] CargoRules =
    [
        (@"(?m)^\s*tauri\s*=", "Tauri"),
        (@"(?m)^\s*axum\s*=", "Axum"),
        (@"(?m)^\s*actix-web\s*=", "Actix Web"),
        (@"(?m)^\s*rocket\s*=", "Rocket"),
        (@"(?m)^\s*bevy\s*=", "Bevy"),
        (@"(?m)^\s*ratatui\s*=", "Ratatui"),
    ];

    private static readonly (string Pattern, string Name)[] PythonRules =
    [
        (@"(?im)^\s*django\b|""django[>=~\[]", "Django"),
        (@"(?im)^\s*flask\b|""flask[>=~\[]", "Flask"),
        (@"(?im)^\s*fastapi\b|""fastapi[>=~\[]", "FastAPI"),
    ];

    public static IReadOnlyList<DetectedFramework> Detect(string projectDirectory)
    {
        var found = new List<DetectedFramework>();
        DetectIn(projectDirectory, found);
        foreach (var sub in SafeSubdirectories(projectDirectory))
        {
            DetectIn(sub, found);
        }

        // Drop base libraries implied by a detected meta-framework (React under Next.js...).
        var implied = found
            .SelectMany(f => Implies.GetValueOrDefault(f.Name, []))
            .ToHashSet();
        return found.Where(f => !implied.Contains(f.Name)).ToList();
    }

    private static void DetectIn(string dir, List<DetectedFramework> found)
    {
        void Add(string name, string? version, string evidence)
        {
            if (!found.Any(f => f.Name == name))
            {
                found.Add(new DetectedFramework { Name = name, Version = CleanVersion(version), EvidenceFile = evidence });
            }
        }

        DetectJavaScript(dir, Add);
        DetectFromFile(dir, "Cargo.toml", CargoRules, Add);
        DetectFromFile(dir, "pyproject.toml", PythonRules, Add);
        DetectFromFile(dir, "requirements.txt", PythonRules, Add);
        DetectRuby(dir, Add);
        DetectPhp(dir, Add);
        DetectDotNet(dir, Add);
    }

    private static void DetectJavaScript(string dir, Action<string, string?, string> add)
    {
        var packageJson = Path.Combine(dir, "package.json");
        if (!File.Exists(packageJson))
        {
            return;
        }

        Dictionary<string, string> dependencies = [];
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
            foreach (var section in new[] { "dependencies", "devDependencies" })
            {
                if (document.RootElement.TryGetProperty(section, out var deps)
                    && deps.ValueKind == JsonValueKind.Object)
                {
                    foreach (var dep in deps.EnumerateObject())
                    {
                        dependencies.TryAdd(dep.Name, dep.Value.ToString());
                    }
                }
            }
        }
        catch (JsonException)
        {
            return;
        }

        foreach (var (dependency, name) in JsRules)
        {
            if (dependencies.TryGetValue(dependency, out var version))
            {
                add(name, version, "package.json");
            }
        }
    }

    private static void DetectFromFile(
        string dir, string fileName, (string Pattern, string Name)[] rules, Action<string, string?, string> add)
    {
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
        {
            return;
        }

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return;
        }

        foreach (var (pattern, name) in rules)
        {
            if (Regex.IsMatch(content, pattern))
            {
                add(name, null, fileName);
            }
        }
    }

    private static void DetectRuby(string dir, Action<string, string?, string> add)
    {
        var gemfile = Path.Combine(dir, "Gemfile");
        if (File.Exists(gemfile) && Regex.IsMatch(File.ReadAllText(gemfile), @"(?m)^\s*gem\s+['""]rails['""]"))
        {
            add("Rails", null, "Gemfile");
        }
    }

    private static void DetectPhp(string dir, Action<string, string?, string> add)
    {
        var composer = Path.Combine(dir, "composer.json");
        if (!File.Exists(composer))
        {
            return;
        }

        var content = File.ReadAllText(composer);
        if (content.Contains("laravel/framework"))
        {
            add("Laravel", null, "composer.json");
        }

        if (content.Contains("symfony/framework-bundle"))
        {
            add("Symfony", null, "composer.json");
        }
    }

    private static void DetectDotNet(string dir, Action<string, string?, string> add)
    {
        foreach (var csproj in SafeEnumerateFiles(dir, "*.csproj"))
        {
            var content = File.ReadAllText(csproj);
            var evidence = Path.GetFileName(csproj);
            if (content.Contains("Microsoft.NET.Sdk.Web"))
            {
                add("ASP.NET Core", null, evidence);
            }

            if (Regex.IsMatch(content, @"<UseWinUI>\s*true", RegexOptions.IgnoreCase))
            {
                add("WinUI 3", null, evidence);
            }

            if (Regex.IsMatch(content, @"<UseMaui>\s*true", RegexOptions.IgnoreCase))
            {
                add(".NET MAUI", null, evidence);
            }

            if (Regex.IsMatch(content, @"Include=""Avalonia"""))
            {
                add("Avalonia", null, evidence);
            }
        }
    }

    /// <summary>"^18.3.1" → "18.3.1"; ranges and workspace protocols are dropped.</summary>
    internal static string? CleanVersion(string? version)
    {
        if (version is null)
        {
            return null;
        }

        var cleaned = version.Trim().TrimStart('^', '~', '>', '<', '=', 'v').Trim();
        return cleaned.Length > 0 && !cleaned.Contains(' ') && !cleaned.Contains('|') && char.IsDigit(cleaned[0])
            ? cleaned
            : null;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string dir, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(dir, pattern);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeSubdirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory)
                .Where(d => !Path.GetFileName(d).StartsWith('.'));
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
