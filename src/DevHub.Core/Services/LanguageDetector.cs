using System.Text.Json;
using System.Text.RegularExpressions;
using DevHub.Core.Models;

namespace DevHub.Core.Services;

/// <summary>
/// Detects languages by marker files at the project root and one level deep
/// (for monorepos). The rule table is data-driven: adding a language is one
/// entry in <see cref="Rules"/> plus a test.
/// </summary>
public static class LanguageDetector
{
    /// <param name="Accept">Extra check on the marker file (directory, filePath) — e.g. Kotlin
    /// requires the build file to actually mention Kotlin. Null accepts any marker match.</param>
    private sealed record Rule(
        string Language,
        string[] MarkerPatterns,
        Func<string, string?>? ResolveVersion,
        Func<string, string, bool>? Accept = null);

    private static readonly Rule[] Rules =
    [
        new("Node.js", ["package.json"], dir =>
            JsonValue(Path.Combine(dir, "package.json"), "engines", "node")
            ?? FileContent(dir, ".nvmrc")
            ?? FileContent(dir, ".node-version")),
        new("TypeScript", ["tsconfig.json"], dir =>
            FrameworkDetector.CleanVersion(JsonValue(Path.Combine(dir, "package.json"), "devDependencies", "typescript")
                ?? JsonValue(Path.Combine(dir, "package.json"), "dependencies", "typescript"))),
        new("Rust", ["Cargo.toml"], dir =>
            RegexMatch(Path.Combine(dir, "rust-toolchain.toml"), @"channel\s*=\s*""([^""]+)""")
            ?? FileContent(dir, "rust-toolchain")
            ?? RegexMatch(Path.Combine(dir, "Cargo.toml"), @"rust-version\s*=\s*""([^""]+)""")),
        new("Go", ["go.mod"], dir =>
            RegexMatch(Path.Combine(dir, "go.mod"), @"(?m)^go\s+(\S+)")),
        new(".NET", ["*.csproj", "*.sln", "*.slnx"], dir =>
            CsprojFiles(dir)
                .Select(f => RegexMatch(f, "<TargetFrameworks?>([^<;]+)"))
                .FirstOrDefault(v => v is not null)),
        new("Python", ["pyproject.toml", "requirements.txt"], dir =>
            RegexMatch(Path.Combine(dir, "pyproject.toml"), @"requires-python\s*=\s*""([^""]+)""")
            ?? FileContent(dir, ".python-version")),
        new("Java", ["pom.xml", "build.gradle", "build.gradle.kts", "*.java"], dir =>
            RegexMatch(Path.Combine(dir, "pom.xml"), @"<java\.version>([^<]+)"),
            (dir, file) => file.EndsWith(".java", StringComparison.OrdinalIgnoreCase)
                || !FileContains(file, "kotlin")),
        new("Kotlin", ["*.kt", "build.gradle.kts", "build.gradle", "pom.xml"], ResolveKotlinVersion,
            (dir, file) => file.EndsWith(".kt", StringComparison.OrdinalIgnoreCase)
                || FileContains(file, "kotlin")),
        new("PHP", ["composer.json"], dir =>
            JsonValue(Path.Combine(dir, "composer.json"), "require", "php")),
        new("Ruby", ["Gemfile"], dir => FileContent(dir, ".ruby-version")),
        new("C/C++", ["CMakeLists.txt", "Makefile"], null),
    ];

    public static IReadOnlyList<DetectedLanguage> Detect(string projectDirectory)
    {
        var found = new List<DetectedLanguage>();
        DetectIn(projectDirectory, found);

        // One level deep, for monorepos — first hit per language wins (root preferred).
        foreach (var sub in SafeSubdirectories(projectDirectory))
        {
            DetectIn(sub, found);
        }

        return found;
    }

    private static void DetectIn(string directory, List<DetectedLanguage> found)
    {
        foreach (var rule in Rules)
        {
            if (found.Any(l => l.Language == rule.Language))
            {
                continue;
            }

            var evidence = rule.MarkerPatterns
                .SelectMany(pattern => SafeEnumerateFiles(directory, pattern))
                .FirstOrDefault(file => rule.Accept?.Invoke(directory, file) != false);
            if (evidence is null)
            {
                continue;
            }

            string? version = null;
            try
            {
                version = rule.ResolveVersion?.Invoke(directory);
            }
            catch
            {
                // A malformed manifest never blocks detection — we just omit the version.
            }

            found.Add(new DetectedLanguage
            {
                Language = rule.Language,
                Version = version?.Trim(),
                EvidenceFile = Path.GetFileName(evidence),
            });
        }
    }

    /// <summary>*.csproj at the root, then up to two directory levels down (src/App/App.csproj layouts).</summary>
    private static IEnumerable<string> CsprojFiles(string dir)
    {
        foreach (var f in SafeEnumerateFiles(dir, "*.csproj"))
        {
            yield return f;
        }

        foreach (var level1 in SafeSubdirectories(dir))
        {
            foreach (var f in SafeEnumerateFiles(level1, "*.csproj"))
            {
                yield return f;
            }

            foreach (var level2 in SafeSubdirectories(level1))
            {
                foreach (var f in SafeEnumerateFiles(level2, "*.csproj"))
                {
                    yield return f;
                }
            }
        }
    }

    private static bool FileContains(string filePath, string needle)
    {
        try
        {
            return File.Exists(filePath)
                && File.ReadAllText(filePath).Contains(needle, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>kotlin("jvm") version "2.1.0" / kotlin_version = "1.9.24" / kotlin.version property.</summary>
    private static string? ResolveKotlinVersion(string dir)
    {
        foreach (var file in new[] { "build.gradle.kts", "build.gradle", "pom.xml" })
        {
            var path = Path.Combine(dir, file);
            var version =
                RegexMatch(path, @"kotlin\(""(?:jvm|multiplatform|android)""\)\s+version\s+""([^""]+)""")
                ?? RegexMatch(path, @"kotlin[_.-]version[""']?\s*[=:>]+\s*[""']?([\d][\d.]*)")
                ?? RegexMatch(path, @"<kotlin\.version>([^<]+)");
            if (version is not null)
            {
                return version;
            }
        }

        return null;
    }

    private static string? FileContent(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var content = File.ReadAllText(path).Trim();
        return content.Length is > 0 and < 40 ? content : null;
    }

    private static string? RegexMatch(string filePath, string pattern)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var match = Regex.Match(File.ReadAllText(filePath), pattern);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Reads e.g. engines.node from package.json; null when absent or invalid.</summary>
    private static string? JsonValue(string filePath, string objectName, string propertyName)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            return document.RootElement.TryGetProperty(objectName, out var obj)
                && obj.ValueKind == JsonValueKind.Object
                && obj.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern);
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
