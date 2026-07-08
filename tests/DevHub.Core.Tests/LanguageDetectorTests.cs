using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class LanguageDetectorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("devhub-lang-").FullName;

    private void Write(string relativePath, string content)
    {
        var full = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void Detects_NodeWithEnginesVersion()
    {
        Write("package.json", """{ "name": "x", "engines": { "node": ">=22" } }""");

        var result = LanguageDetector.Detect(_dir);

        var node = Assert.Single(result);
        Assert.Equal("Node.js", node.Language);
        Assert.Equal(">=22", node.Version);
        Assert.Equal("package.json", node.EvidenceFile);
    }

    [Fact]
    public void Detects_NodeVersionFromNvmrc_WhenEnginesAbsent()
    {
        Write("package.json", """{ "name": "x" }""");
        Write(".nvmrc", "22.4.0\n");

        var result = LanguageDetector.Detect(_dir);

        Assert.Equal("22.4.0", Assert.Single(result).Version);
    }

    [Fact]
    public void Detects_RustWithToolchainChannel()
    {
        Write("Cargo.toml", "[package]\nname = \"x\"");
        Write("rust-toolchain.toml", "[toolchain]\nchannel = \"1.79\"");

        var result = LanguageDetector.Detect(_dir);

        var rust = Assert.Single(result);
        Assert.Equal("Rust", rust.Language);
        Assert.Equal("1.79", rust.Version);
    }

    [Fact]
    public void Detects_GoDirectiveVersion()
    {
        Write("go.mod", "module example.com/x\n\ngo 1.22.1\n");

        var go = Assert.Single(LanguageDetector.Detect(_dir));

        Assert.Equal("Go", go.Language);
        Assert.Equal("1.22.1", go.Version);
    }

    [Fact]
    public void Detects_DotNetTargetFramework()
    {
        Write("App.csproj", "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var dotnet = Assert.Single(LanguageDetector.Detect(_dir));

        Assert.Equal(".NET", dotnet.Language);
        Assert.Equal("net10.0", dotnet.Version);
    }

    [Fact]
    public void Detects_PythonRequiresVersion()
    {
        Write("pyproject.toml", "[project]\nrequires-python = \">=3.11\"");

        var python = Assert.Single(LanguageDetector.Detect(_dir));

        Assert.Equal("Python", python.Language);
        Assert.Equal(">=3.11", python.Version);
    }

    [Fact]
    public void Detects_TypeScriptAlongsideNode()
    {
        Write("package.json", """{ "name": "x" }""");
        Write("tsconfig.json", "{}");

        var result = LanguageDetector.Detect(_dir);

        Assert.Equal(["Node.js", "TypeScript"], result.Select(l => l.Language).Order().ToArray());
    }

    [Fact]
    public void Detects_MarkersOneLevelDeep_ForMonorepos()
    {
        Write("backend/go.mod", "module x\n\ngo 1.22\n");
        Write("frontend/package.json", """{ "name": "web" }""");

        var result = LanguageDetector.Detect(_dir);

        Assert.Equal(["Go", "Node.js"], result.Select(l => l.Language).Order().ToArray());
    }

    [Fact]
    public void PlainMavenProject_IsJava_NotKotlin()
    {
        Write("pom.xml", "<project><properties><java.version>21</java.version></properties></project>");

        var result = LanguageDetector.Detect(_dir);

        var java = Assert.Single(result);
        Assert.Equal("Java", java.Language);
        Assert.Equal("21", java.Version);
    }

    [Fact]
    public void GradleKtsWithKotlinPlugin_IsKotlin_NotJava()
    {
        Write("build.gradle.kts", """
            plugins {
                kotlin("jvm") version "2.1.0"
            }
            """);

        var result = LanguageDetector.Detect(_dir);

        var kotlin = Assert.Single(result);
        Assert.Equal("Kotlin", kotlin.Language);
        Assert.Equal("2.1.0", kotlin.Version);
    }

    [Fact]
    public void MixedProject_DetectsBothJavaAndKotlin()
    {
        Write("build.gradle", "apply plugin: 'org.jetbrains.kotlin.jvm'\nkotlin_version = '1.9.24'");
        Write("Main.java", "class Main {}");

        var result = LanguageDetector.Detect(_dir);

        Assert.Equal(["Java", "Kotlin"], result.Select(l => l.Language).Order().ToArray());
    }

    [Fact]
    public void MalformedManifest_StillDetectsLanguage_WithoutVersion()
    {
        Write("package.json", "{ this is not json !!");

        var node = Assert.Single(LanguageDetector.Detect(_dir));

        Assert.Equal("Node.js", node.Language);
        Assert.Null(node.Version);
    }

    [Fact]
    public void EmptyFolder_DetectsNothing()
    {
        Assert.Empty(LanguageDetector.Detect(_dir));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
