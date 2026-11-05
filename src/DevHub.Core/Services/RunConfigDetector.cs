using System.Text.Json;
using DevHub.Core.Models;

namespace DevHub.Core.Services;

/// <summary>
/// Inspects a project directory and suggests ready-to-use <see cref="RunConfig"/> commands
/// based on package manifests, lockfiles, and project files.
/// </summary>
public static class RunConfigDetector
{
    private static readonly string[] PreferredNpmScripts =
        ["dev", "start", "serve", "preview", "build", "test", "lint"];

    public static IReadOnlyList<RunConfig> Detect(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            return [];
        }

        var suggestions = new List<RunConfig>();
        void AddIfMissing(string name, string command)
        {
            if (!suggestions.Any(s => string.Equals(s.Command, command, StringComparison.OrdinalIgnoreCase)))
            {
                suggestions.Add(new RunConfig { Name = name, Command = command });
            }
        }

        try
        {
            // 1. Node / JS / TS (package.json)
            var packageJsonPath = Path.Combine(projectPath, "package.json");
            if (File.Exists(packageJsonPath))
            {
                var pkgManager = DetectNodePackageManager(projectPath);
                var json = File.ReadAllText(packageJsonPath);
                foreach (var cfg in ParsePackageJsonScripts(json, pkgManager))
                {
                    AddIfMissing(cfg.Name, cfg.Command);
                }
            }

            // 2. .NET (.sln, .slnx, .csproj)
            var hasDotnet = Directory.EnumerateFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly).Any()
                || Directory.EnumerateFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly).Any()
                || Directory.EnumerateFiles(projectPath, "*.slnx", SearchOption.TopDirectoryOnly).Any();
            if (hasDotnet)
            {
                AddIfMissing("run", "dotnet run");
                AddIfMissing("build", "dotnet build");
                AddIfMissing("test", "dotnet test");
            }

            // 3. Rust (Cargo.toml)
            if (File.Exists(Path.Combine(projectPath, "Cargo.toml")))
            {
                AddIfMissing("run", "cargo run");
                AddIfMissing("test", "cargo test");
                AddIfMissing("build", "cargo build");
            }

            // 4. Go (go.mod)
            if (File.Exists(Path.Combine(projectPath, "go.mod")))
            {
                AddIfMissing("run", "go run .");
                AddIfMissing("test", "go test ./...");
            }

            // 5. Python
            var useUv = File.Exists(Path.Combine(projectPath, "uv.lock"));
            var pyPrefix = useUv ? "uv run python" : "python";
            if (File.Exists(Path.Combine(projectPath, "manage.py")))
            {
                AddIfMissing("runserver", $"{pyPrefix} manage.py runserver");
            }
            else if (File.Exists(Path.Combine(projectPath, "main.py")))
            {
                AddIfMissing("main", $"{pyPrefix} main.py");
            }
            else if (File.Exists(Path.Combine(projectPath, "app.py")))
            {
                AddIfMissing("app", $"{pyPrefix} app.py");
            }

            if (File.Exists(Path.Combine(projectPath, "pyproject.toml"))
                || Directory.Exists(Path.Combine(projectPath, "tests")))
            {
                if (File.Exists(Path.Combine(projectPath, "pyproject.toml"))
                    || File.Exists(Path.Combine(projectPath, "pytest.ini")))
                {
                    AddIfMissing("pytest", useUv ? "uv run pytest" : "pytest");
                }
            }

            // 6. Docker Compose
            string[] composeFiles = ["docker-compose.yml", "docker-compose.yaml", "compose.yml", "compose.yaml"];
            if (composeFiles.Any(f => File.Exists(Path.Combine(projectPath, f))))
            {
                AddIfMissing("compose up", "docker compose up");
            }
        }
        catch
        {
            // Ignore unreadable files and return whatever was collected so far.
        }

        return suggestions;
    }

    public static string DetectNodePackageManager(string projectPath)
    {
        if (File.Exists(Path.Combine(projectPath, "pnpm-lock.yaml")))
        {
            return "pnpm";
        }

        if (File.Exists(Path.Combine(projectPath, "yarn.lock")))
        {
            return "yarn";
        }

        if (File.Exists(Path.Combine(projectPath, "bun.lockb"))
            || File.Exists(Path.Combine(projectPath, "bun.lock")))
        {
            return "bun";
        }

        return "npm";
    }

    public static IReadOnlyList<RunConfig> ParsePackageJsonScripts(string packageJsonContent, string packageManager = "npm")
    {
        var results = new List<RunConfig>();
        try
        {
            using var doc = JsonDocument.Parse(packageJsonContent, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            if (!doc.RootElement.TryGetProperty("scripts", out var scriptsElement)
                || scriptsElement.ValueKind != JsonValueKind.Object)
            {
                return results;
            }

            var scriptNames = scriptsElement.EnumerateObject()
                .Select(p => p.Name)
                .ToList();

            var ordered = scriptNames
                .OrderBy(name =>
                {
                    var idx = Array.FindIndex(
                        PreferredNpmScripts,
                        s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
                    return idx >= 0 ? idx : 100;
                })
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(8);

            foreach (var script in ordered)
            {
                var cmd = packageManager == "yarn"
                    ? $"yarn {script}"
                    : $"{packageManager} run {script}";
                results.Add(new RunConfig { Name = script, Command = cmd });
            }
        }
        catch
        {
            // Malformed package.json
        }

        return results;
    }
}
