using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class FuzzyMatcherTests
{
    [Theory]
    [InlineData("devhub", "devhub")]
    [InlineData("DEV", "devhub")]
    [InlineData("hub", "devhub")]
    [InlineData("dvhb", "devhub")]
    [InlineData("ptc", "ProjectTemplateCatalog")]
    [InlineData("dev term", "devhub — Open in Terminal")]
    [InlineData("term dev", "devhub — Open in Terminal")]
    public void Score_Matches(string query, string text)
    {
        Assert.NotNull(FuzzyMatcher.Score(query, text));
    }

    [Theory]
    [InlineData("xyz", "devhub")]
    [InlineData("bhd", "devhub")]
    [InlineData("dev xyz", "devhub — Open in Terminal")]
    public void Score_RejectsNonMatches(string query, string text)
    {
        Assert.Null(FuzzyMatcher.Score(query, text));
    }

    [Fact]
    public void Score_EmptyQuery_MatchesEverything()
    {
        Assert.Equal(0, FuzzyMatcher.Score("  ", "anything"));
    }

    [Theory]
    [InlineData("devhub", "devhub", "devhub-site")]
    [InlineData("dev", "devhub", "my-devhub")]
    [InlineData("term", "Open in Terminal", "Determine")]
    [InlineData("hub", "my-hub", "github")]
    [InlineData("dh", "dev-hub", "dashboard")]
    public void Score_RanksBetterMatchFirst(string query, string better, string worse)
    {
        Assert.True(FuzzyMatcher.Score(query, better) > FuzzyMatcher.Score(query, worse));
    }

    [Fact]
    public void Score_SubsequenceRanksBelowSubstring()
    {
        Assert.True(FuzzyMatcher.Score("hub", "zhub") > FuzzyMatcher.Score("hub", "h-u-b"));
    }
}
