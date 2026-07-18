using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class EnvPathAnalyzerTests
{
    [Fact]
    public void Split_DropsEmptySegmentsAndWhitespace()
    {
        var entries = EnvPathAnalyzer.Split(@"C:\a;;  C:\b ; ");

        Assert.Equal([@"C:\a", @"C:\b"], entries);
    }

    [Fact]
    public void Join_RoundTripsEntries()
    {
        Assert.Equal(@"C:\a;C:\b", EnvPathAnalyzer.Join([@"C:\a", " ", @"C:\b"]));
    }

    [Theory]
    [InlineData("PATH", @"C:\only-one", true)]
    [InlineData("PATHEXT", ".EXE", true)]
    [InlineData("PSModulePath", @"C:\mods", true)]
    [InlineData("JAVA_HOME", @"C:\jdk", false)]
    [InlineData("CUSTOM_LIST", @"C:\a;C:\b", true)]
    public void IsMultiValued_ByNameOrEntryCount(string name, string value, bool expected)
    {
        Assert.Equal(expected, EnvPathAnalyzer.IsMultiValued(name, value));
    }

    [Fact]
    public void Analyze_FlagsMissingDirectories()
    {
        var existing = Path.GetTempPath();
        var missing = Path.Combine(Path.GetTempPath(), $"devhub-missing-{Guid.NewGuid():N}");

        var result = EnvPathAnalyzer.Analyze([existing, missing]);

        Assert.True(result[0].Exists);
        Assert.False(result[1].Exists);
    }

    [Fact]
    public void Analyze_DoesNotFlagNonPathEntries()
    {
        // PATHEXT-style entries are not paths and must never show the missing badge.
        var result = EnvPathAnalyzer.Analyze([".COM", ".EXE"]);

        Assert.All(result, e => Assert.True(e.Exists));
    }

    [Fact]
    public void Analyze_FlagsDuplicatesCaseInsensitivelyIgnoringTrailingSlash()
    {
        var result = EnvPathAnalyzer.Analyze([@"C:\Tools", @"c:\tools\", @"C:\Other"]);

        Assert.False(result[0].IsDuplicate);
        Assert.True(result[1].IsDuplicate);
        Assert.False(result[2].IsDuplicate);
    }

    [Fact]
    public void Analyze_ExpandsVariablesBeforeComparing()
    {
        var result = EnvPathAnalyzer.Analyze(["%TEMP%", Environment.GetEnvironmentVariable("TEMP")!]);

        Assert.True(result[0].Exists);
        Assert.True(result[1].IsDuplicate);
    }
}
