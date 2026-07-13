using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class OpenMeteoWeatherServiceTests
{
    [Theory]
    [InlineData(0, "Clear sky")]
    [InlineData(2, "Partly cloudy")]
    [InlineData(3, "Overcast")]
    [InlineData(45, "Fog")]
    [InlineData(48, "Fog")]
    [InlineData(53, "Drizzle")]
    [InlineData(63, "Rain")]
    [InlineData(75, "Snow")]
    [InlineData(81, "Rain showers")]
    [InlineData(86, "Snow showers")]
    [InlineData(95, "Thunderstorm")]
    public void DescribeWeatherCode_MapsKnownWmoCodes(int code, string expectedText)
    {
        var (text, emoji) = OpenMeteoWeatherService.DescribeWeatherCode(code);

        Assert.Equal(expectedText, text);
        Assert.NotEmpty(emoji);
    }

    [Fact]
    public void DescribeWeatherCode_UnknownCode_FallsBackGracefully()
    {
        var (text, emoji) = OpenMeteoWeatherService.DescribeWeatherCode(42);

        Assert.Equal("—", text);
        Assert.NotEmpty(emoji);
    }
}
