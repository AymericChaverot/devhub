using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class WingetListParserTests
{
    [Fact]
    public void Parse_EnglishOutput_ExtractsIdsAndVersions()
    {
        const string output = """
            Name                     Id                        Version      Available    Source
            --------------------------------------------------------------------------------------
            Git                      Git.Git                   2.50.0       2.50.1       winget
            Node.js LTS              OpenJS.NodeJS.LTS         22.16.0                   winget
            Some Local Thing         ARP\Machine\X64\Thing     1.0
            """;

        var result = WingetListParser.Parse(output);

        Assert.Equal("2.50.0", result["Git.Git"]);
        Assert.Equal("22.16.0", result["OpenJS.NodeJS.LTS"]);
        Assert.Equal("1.0", result[@"ARP\Machine\X64\Thing"]);
    }

    [Fact]
    public void Parse_FrenchOutput_ExtractsIdsAndVersions()
    {
        const string output = """
            Nom                      ID                        Version      Disponible   Source
            --------------------------------------------------------------------------------------
            Git                      Git.Git                   2.50.0       2.50.1       winget
            PowerShell 7             Microsoft.PowerShell      7.5.1                     winget
            """;

        var result = WingetListParser.Parse(output);

        Assert.Equal("2.50.0", result["Git.Git"]);
        Assert.Equal("7.5.1", result["Microsoft.PowerShell"]);
    }

    [Fact]
    public void Parse_SkipsRowsWithTruncatedIds()
    {
        const string output = """
            Name                     Id                        Version      Source
            ------------------------------------------------------------------------
            A very long package name SomeCompany.SomeVeryLon…  3.1.4        winget
            Git                      Git.Git                   2.50.0       winget
            """;

        var result = WingetListParser.Parse(output);

        Assert.Single(result);
        Assert.Equal("2.50.0", result["Git.Git"]);
    }

    [Fact]
    public void Parse_IgnoresSpinnerArtifactsBeforeTable()
    {
        const string output = "   \b\b\b-\r  \\\r" + """

            Name        Id           Version   Source
            -------------------------------------------
            Git         Git.Git      2.50.0    winget
            """;

        var result = WingetListParser.Parse(output);

        Assert.Equal("2.50.0", result["Git.Git"]);
    }

    [Fact]
    public void Parse_EmptyOrGarbageOutput_ReturnsEmpty()
    {
        Assert.Empty(WingetListParser.Parse(""));
        Assert.Empty(WingetListParser.Parse("No installed package found matching input criteria."));
    }

    [Fact]
    public void ParseEntries_CapturesNameAlongsideIdAndVersion()
    {
        // Packages installed outside winget (e.g. the official Node.js installer) get a
        // synthetic ARP id in bulk-listing mode, even though `--id --exact` resolves fine.
        const string output = """
            Name                     Id                          Version      Source
            ----------------------------------------------------------------------------
            Node.js LTS              ARP\Machine\X64\{6178C0C7}  24.18.0
            Git                      Git.Git                     2.50.0       winget
            """;

        var entries = WingetListParser.ParseEntries(output);

        var node = Assert.Single(entries, e => e.Name == "Node.js LTS");
        Assert.Equal("24.18.0", node.Version);
        Assert.True(WingetListParser.IsSyntheticId(node.Id));

        var git = Assert.Single(entries, e => e.Name == "Git");
        Assert.Equal("Git.Git", git.Id);
        Assert.False(WingetListParser.IsSyntheticId(git.Id));
    }

    [Fact]
    public void ParseEntries_FiveColumnTable_CapturesAvailableVersion()
    {
        const string output = """
            Name                     Id                        Version      Available    Source
            --------------------------------------------------------------------------------------
            Git                      Git.Git                   2.50.0       2.50.1       winget
            Node.js LTS              OpenJS.NodeJS.LTS         22.16.0                   winget
            """;

        var entries = WingetListParser.ParseEntries(output);

        var git = Assert.Single(entries, e => e.Id == "Git.Git");
        Assert.Equal("2.50.1", git.Available);

        var node = Assert.Single(entries, e => e.Id == "OpenJS.NodeJS.LTS");
        Assert.Null(node.Available);
    }

    [Fact]
    public void ParseEntries_FourColumnTable_HasNoAvailableVersion()
    {
        // With four columns the last one is Source, not Available.
        const string output = """
            Name                     Id                        Version      Source
            ------------------------------------------------------------------------
            Git                      Git.Git                   2.50.0       winget
            """;

        var entries = WingetListParser.ParseEntries(output);

        var git = Assert.Single(entries);
        Assert.Null(git.Available);
    }

    [Theory]
    [InlineData(@"ARP\Machine\X64\{123}", true)]
    [InlineData(@"MSIX\SomePackage", true)]
    [InlineData(@"MSSTORE\9NBLGGH4NNS1", true)]
    [InlineData("Git.Git", false)]
    [InlineData("OpenJS.NodeJS.LTS", false)]
    public void IsSyntheticId_DetectsNonCatalogIds(string id, bool expected)
    {
        Assert.Equal(expected, WingetListParser.IsSyntheticId(id));
    }
}
