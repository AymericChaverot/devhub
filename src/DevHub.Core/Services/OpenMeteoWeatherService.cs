using System.Text.Json;

namespace DevHub.Core.Services;

/// <summary>
/// Weather via keyless public APIs: the location comes either from the city the user
/// configured (Open-Meteo geocoding) or from the public IP (ipwho.is), then Open-Meteo
/// returns current conditions. Results are cached briefly, keyed by the configured
/// location so a settings change takes effect on the next load.
/// </summary>
public sealed class OpenMeteoWeatherService : IWeatherService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(20);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ISettingsService _settings;

    private WeatherInfo? _cached;
    private string _cachedLocationKey = "";
    private DateTime _cachedAtUtc;

    public OpenMeteoWeatherService(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task<WeatherInfo?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var configured = _settings.Settings.WeatherLocation?.Trim() ?? "";
        if (_cached is not null
            && _cachedLocationKey == configured
            && DateTime.UtcNow - _cachedAtUtc < CacheDuration)
        {
            return _cached;
        }

        try
        {
            var (latitude, longitude, city, country) = configured.Length > 0
                ? await GeocodeAsync(configured, cancellationToken)
                : await LocateFromIpAsync(cancellationToken);
            var weather = await GetWeatherAsync(latitude, longitude, city, country, cancellationToken);
            _cached = weather;
            _cachedLocationKey = configured;
            _cachedAtUtc = DateTime.UtcNow;
            return weather;
        }
        catch (Exception)
        {
            // Offline, endpoint blocked, or an unknown city — the dashboard simply hides the card.
            return null;
        }
    }

    /// <summary>Resolves a user-typed city via Open-Meteo's geocoding API.</summary>
    private async Task<(double Latitude, double Longitude, string City, string Country)> GeocodeAsync(
        string query, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(
            $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=1",
            cancellationToken));
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"No place found for '{query}'");
        }

        var place = results[0];
        return (
            place.GetProperty("latitude").GetDouble(),
            place.GetProperty("longitude").GetDouble(),
            place.TryGetProperty("name", out var name) ? name.GetString() ?? query : query,
            place.TryGetProperty("country", out var country) ? country.GetString() ?? "" : "");
    }

    private async Task<(double Latitude, double Longitude, string City, string Country)> LocateFromIpAsync(
        CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(
            await _http.GetStringAsync("https://ipwho.is/", cancellationToken));
        var root = doc.RootElement;
        if (root.TryGetProperty("success", out var success) && !success.GetBoolean())
        {
            throw new InvalidOperationException("ipwho.is could not resolve the location");
        }

        return (
            root.GetProperty("latitude").GetDouble(),
            root.GetProperty("longitude").GetDouble(),
            root.TryGetProperty("city", out var city) ? city.GetString() ?? "" : "",
            root.TryGetProperty("country", out var country) ? country.GetString() ?? "" : "");
    }

    private async Task<WeatherInfo> GetWeatherAsync(
        double latitude, double longitude, string city, string country, CancellationToken cancellationToken)
    {
        var url = "https://api.open-meteo.com/v1/forecast"
            + $"?latitude={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            + $"&longitude={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day"
            + "&daily=temperature_2m_max,temperature_2m_min"
            + "&forecast_days=1&timezone=auto";

        using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, cancellationToken));
        var current = doc.RootElement.GetProperty("current");
        var daily = doc.RootElement.GetProperty("daily");
        var code = current.GetProperty("weather_code").GetInt32();
        var isDay = !current.TryGetProperty("is_day", out var day) || day.GetInt32() == 1;
        var (conditionText, conditionEmoji) = DescribeWeatherCode(code, isDay);

        return new WeatherInfo(
            city,
            country,
            current.GetProperty("temperature_2m").GetDouble(),
            current.GetProperty("apparent_temperature").GetDouble(),
            daily.GetProperty("temperature_2m_min")[0].GetDouble(),
            daily.GetProperty("temperature_2m_max")[0].GetDouble(),
            current.GetProperty("wind_speed_10m").GetDouble(),
            current.GetProperty("relative_humidity_2m").GetInt32(),
            conditionText,
            conditionEmoji,
            code,
            isDay);
    }

    /// <summary>Maps a WMO weather code (Open-Meteo's format) to display text + emoji.</summary>
    public static (string Text, string Emoji) DescribeWeatherCode(int code, bool isDay = true) => code switch
    {
        0 => (CoreStrings.Get("Core_WeatherClear", "Clear sky"), isDay ? "☀️" : "🌙"),
        1 => (CoreStrings.Get("Core_WeatherMainlyClear", "Mainly clear"), isDay ? "🌤️" : "🌙"),
        2 => (CoreStrings.Get("Core_WeatherPartlyCloudy", "Partly cloudy"), "⛅"),
        3 => (CoreStrings.Get("Core_WeatherOvercast", "Overcast"), "☁️"),
        45 or 48 => (CoreStrings.Get("Core_WeatherFog", "Fog"), "🌫️"),
        >= 51 and <= 57 => (CoreStrings.Get("Core_WeatherDrizzle", "Drizzle"), "🌦️"),
        >= 61 and <= 67 => (CoreStrings.Get("Core_WeatherRain", "Rain"), "🌧️"),
        >= 71 and <= 77 => (CoreStrings.Get("Core_WeatherSnow", "Snow"), "🌨️"),
        80 or 81 or 82 => (CoreStrings.Get("Core_WeatherRainShowers", "Rain showers"), "🌧️"),
        85 or 86 => (CoreStrings.Get("Core_WeatherSnowShowers", "Snow showers"), "🌨️"),
        95 or 96 or 99 => (CoreStrings.Get("Core_WeatherThunderstorm", "Thunderstorm"), "⛈️"),
        _ => ("—", "🌡️"),
    };
}
