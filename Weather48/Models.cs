using System.Text.Json.Serialization;

namespace WeatherWidget48;

public sealed record GeoLocation(
    string Name,
    string Admin1,
    string Admin2,
    string Country,
    double Latitude,
    double Longitude)
{
    public static GeoLocation Default => new("北京市", "北京市", "北京市", "中国", 39.9042, 116.4074);

    public bool HasValidCoordinates =>
        Compat.IsFinite(Latitude) && Latitude is >= -90 and <= 90
        && Compat.IsFinite(Longitude) && Longitude is >= -180 and <= 180;

    public string DisplayName
    {
        get
        {
            var parts = new[] { Name, Admin2, Admin1, Country }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal);
            return string.Join(" · ", parts);
        }
    }
}

public sealed record CurrentWeather(
    double Temperature,
    double Humidity,
    int WeatherCode,
    double WindSpeed,
    int WindDirection,
    int? PrecipitationProbability);

public sealed record DailyWeather(
    DateTime Date,
    int WeatherCode,
    double MinimumTemperature,
    double MaximumTemperature,
    TimeSpan Sunrise,
    TimeSpan Sunset);

public sealed record WeatherData(
    GeoLocation Location,
    CurrentWeather Current,
    IReadOnlyList<DailyWeather> Daily,
    DateTimeOffset FetchedAt);

internal sealed class WeatherApiResponse
{
    [JsonPropertyName("current")]
    public CurrentWeatherApi? Current { get; set; }

    [JsonPropertyName("hourly")]
    public HourlyWeatherApi? Hourly { get; set; }

    [JsonPropertyName("daily")]
    public DailyWeatherApi? Daily { get; set; }
}

internal sealed class CurrentWeatherApi
{
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("temperature_2m")]
    public double? Temperature { get; set; }

    [JsonPropertyName("relative_humidity_2m")]
    public double? Humidity { get; set; }

    [JsonPropertyName("weather_code")]
    public int? WeatherCode { get; set; }

    [JsonPropertyName("wind_speed_10m")]
    public double? WindSpeed { get; set; }

    [JsonPropertyName("wind_direction_10m")]
    public int? WindDirection { get; set; }
}

internal sealed class HourlyWeatherApi
{
    [JsonPropertyName("time")]
    public List<string>? Time { get; set; }

    [JsonPropertyName("precipitation_probability")]
    public List<int?>? PrecipitationProbability { get; set; }
}

internal sealed class DailyWeatherApi
{
    [JsonPropertyName("time")]
    public List<string>? Time { get; set; }

    [JsonPropertyName("weather_code")]
    public List<int?>? WeatherCode { get; set; }

    [JsonPropertyName("temperature_2m_min")]
    public List<double?>? MinimumTemperature { get; set; }

    [JsonPropertyName("temperature_2m_max")]
    public List<double?>? MaximumTemperature { get; set; }

    [JsonPropertyName("sunrise")]
    public List<string?>? Sunrise { get; set; }

    [JsonPropertyName("sunset")]
    public List<string?>? Sunset { get; set; }
}

internal sealed class GeocodingApiResponse
{
    [JsonPropertyName("results")]
    public List<GeocodingResultApi>? Results { get; set; }
}

internal sealed class GeocodingResultApi
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("admin1")]
    public string? Admin1 { get; set; }

    [JsonPropertyName("admin2")]
    public string? Admin2 { get; set; }
}
