using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class FrameworkDetectorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("devhub-fw-").FullName;

    private void Write(string relativePath, string content)
    {
        var full = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void Detects_ReactWithCleanedVersion()
    {
        Write("package.json", """{ "dependencies": { "react": "^18.3.1" } }""");

        var fw = Assert.Single(FrameworkDetector.Detect(_dir));

        Assert.Equal("React", fw.Name);
        Assert.Equal("18.3.1", fw.Version);
    }

    [Fact]
    public void NextJs_SuppressesReact_ButKeepsOthers()
    {
        Write("package.json", """
            { "dependencies": { "next": "15.1.0", "react": "^19.0.0" },
              "devDependencies": { "vite": "^6.0.0" } }
            """);

        var result = FrameworkDetector.Detect(_dir);

        Assert.Equal(["Next.js", "Vite"], result.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void SvelteKit_SuppressesSvelte()
    {
        Write("package.json", """{ "devDependencies": { "@sveltejs/kit": "^2.0.0", "svelte": "^5.0.0" } }""");

        var result = FrameworkDetector.Detect(_dir);

        Assert.Equal("SvelteKit", Assert.Single(result).Name);
    }

    [Fact]
    public void Detects_RustFrameworks_FromCargoToml()
    {
        Write("Cargo.toml", """
            [package]
            name = "tui-app"

            [dependencies]
            ratatui = "0.29"
            crossterm = "0.28"
            """);

        var fw = Assert.Single(FrameworkDetector.Detect(_dir));

        Assert.Equal("Ratatui", fw.Name);
    }

    [Fact]
    public void Detects_AspNetCore_And_WinUI_FromCsproj()
    {
        Write("Api/Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");
        Write("App/App.csproj", "<Project><PropertyGroup><UseWinUI>true</UseWinUI></PropertyGroup></Project>");

        var result = FrameworkDetector.Detect(_dir);

        Assert.Equal(["ASP.NET Core", "WinUI 3"], result.Select(f => f.Name).Order().ToArray());
    }

    [Fact]
    public void Detects_Django_FromRequirementsTxt()
    {
        Write("requirements.txt", "Django==5.1\npsycopg2\n");

        Assert.Equal("Django", Assert.Single(FrameworkDetector.Detect(_dir)).Name);
    }

    [Fact]
    public void Detects_Rails_And_Laravel()
    {
        Write("Gemfile", "source 'https://rubygems.org'\ngem 'rails', '~> 8.0'\n");
        Write("composer.json", """{ "require": { "laravel/framework": "^11.0" } }""");

        var result = FrameworkDetector.Detect(_dir);

        Assert.Equal(["Laravel", "Rails"], result.Select(f => f.Name).Order().ToArray());
    }

    [Theory]
    [InlineData("^18.3.1", "18.3.1")]
    [InlineData("~5.0.0", "5.0.0")]
    [InlineData(">=1.2", "1.2")]
    [InlineData("workspace:*", null)]
    [InlineData("1.x || 2.x", null)]
    public void CleanVersion_NormalizesRangePrefixes(string input, string? expected)
    {
        Assert.Equal(expected, FrameworkDetector.CleanVersion(input));
    }

    [Fact]
    public void EmptyFolder_DetectsNothing()
    {
        Assert.Empty(FrameworkDetector.Detect(_dir));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
