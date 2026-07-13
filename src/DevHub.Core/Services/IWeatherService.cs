namespace DevHub.Core.Services;

/// <summary>Current conditions at the configured (or IP-approximated) location.</summary>
public sealed record WeatherInfo(
    string City,
    string Country,
    double TemperatureC,
    double FeelsLikeC,
    double TodayMinC,
    double TodayMaxC,
    double WindKmh,
    int HumidityPercent,
    string ConditionText,
    string ConditionEmoji,
    int WmoCode,
    bool IsDay);

/// <summary>
/// Fetches the current weather for the dashboard. Implementations must be resilient:
/// no network, blocked endpoint or bad payload returns null, never throws.
/// </summary>
public interface IWeatherService
{
    Task<WeatherInfo?> GetCurrentAsync(CancellationToken cancellationToken = default);
}
