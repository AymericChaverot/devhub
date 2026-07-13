using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class GitHubUpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V0.1.0", "0.1.0")]
    [InlineData("2.0.0.15", "2.0.0.15")]
    [InlineData("  v1.0.0 ", "1.0.0")]
    public void ParseVersionTag_ValidTags_Parse(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), GitHubUpdateService.ParseVersionTag(tag));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData("v1")]
    [InlineData("release-candidate")]
    public void ParseVersionTag_NonVersions_ReturnNull(string tag)
    {
        Assert.Null(GitHubUpdateService.ParseVersionTag(tag));
    }

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.0.0", false)] // 3-part vs 4-part must compare equal
    [InlineData("1.0.0.1", "1.0.0", true)]
    [InlineData("0.9.9", "1.0.0", false)]
    public void IsNewer_NormalizesPartCounts(string candidate, string current, bool expected)
    {
        Assert.Equal(
            expected,
            GitHubUpdateService.IsNewer(Version.Parse(candidate), Version.Parse(current)));
    }
}
