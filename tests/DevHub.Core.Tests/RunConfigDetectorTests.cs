using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class RunConfigDetectorTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("devhub-run-").FullName;

    [Fact]
    public void ParsePackageJsonScripts_OrdersPreferredScriptsAndFormatsCommand()
    {
        var json = """
            {
              "name": "web-app",
              "scripts": {
                "custom": "node custom.js",
                "build": "vite build",
                "dev": "vite"
              }
            }
            """;

        var pnpmConfigs = RunConfigDetector.ParsePackageJsonScripts(json, "pnpm");
        Assert.Equal(3, pnpmConfigs.Count);
        Assert.Equal("dev", pnpmConfigs[0].Name);
        Assert.Equal("pnpm run dev", pnpmConfigs[0].Command);
        Assert.Equal("build", pnpmConfigs[1].Name);
        Assert.Equal("pnpm run build", pnpmConfigs[1].Command);

        var yarnConfigs = RunConfigDetector.ParsePackageJsonScripts(json, "yarn");
        Assert.Equal("yarn dev", yarnConfigs[0].Command);
    }

    [Fact]
    public void Detect_FindsNodeDotnetRustAndDockerCommands()
    {
        File.WriteAllText(Path.Combine(_tempDir, "pnpm-lock.yaml"), "lockfileVersion: '9.0'");
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), """{"scripts": {"dev": "next dev"}}""");
        File.WriteAllText(Path.Combine(_tempDir, "App.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_tempDir, "Cargo.toml"), "[package]\nname = 'demo'");
        File.WriteAllText(Path.Combine(_tempDir, "docker-compose.yml"), "services: {}");

        var detected = RunConfigDetector.Detect(_tempDir);

        Assert.Contains(detected, c => c.Command == "pnpm run dev");
        Assert.Contains(detected, c => c.Command == "dotnet run");
        Assert.Contains(detected, c => c.Command == "cargo run");
        Assert.Contains(detected, c => c.Command == "docker compose up");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);
}
