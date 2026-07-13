using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class ForgeUrlsTests
{
    [Theory]
    [InlineData("https://github.com/user/repo.git", "github.com", "user/repo")]
    [InlineData("https://github.com/user/repo", "github.com", "user/repo")]
    [InlineData("git@github.com:user/repo.git", "github.com", "user/repo")]
    [InlineData("https://gitlab.com/group/subgroup/repo.git", "gitlab.com", "group/subgroup/repo")]
    [InlineData("git@gitlab.com:group/repo.git", "gitlab.com", "group/repo")]
    [InlineData("ssh://git@github.com/user/repo.git", "github.com", "user/repo")]
    public void TryParseRepo_ValidRemotes_ExtractsHostAndPath(string url, string expectedHost, string expectedRepo)
    {
        Assert.True(ForgeUrls.TryParseRepo(url, out var host, out var repo));
        Assert.Equal(expectedHost, host);
        Assert.Equal(expectedRepo, repo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://github.com/")]
    [InlineData("C:\\local\\bare\\repo.git")]
    public void TryParseRepo_InvalidRemotes_ReturnsFalse(string? url)
    {
        Assert.False(ForgeUrls.TryParseRepo(url, out _, out _));
    }
}
