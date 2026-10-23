using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace WeatherWidget48;

public sealed class WeatherApiClient
{
    private const string ForecastEndpoint = "https://api.open-meteo.com/v1/forecast";
    private const string GeocodingEndpoint = "https://geocoding-api.open-meteo.com/v1/search";
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;

    public WeatherApiClient(HttpClient? httpClient = null, TimeSpan? requestTimeout = null)
    {
        _httpClient = httpClient ?? CreateHttpClient();
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(20);
    }

    public async Task<WeatherData> GetWeatherAsync(GeoLocation location, CancellationToken cancellationToken)
    {
        if (!location.HasValidCoordinates)
        {
            throw new ArgumentException("地点坐标无效。", nameof(location));
        }

        var url = FormattableString.Invariant(
            $"{ForecastEndpoint}?latitude={location.Latitude:F6}&longitude={location.Longitude:F6}&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m,wind_direction_10m&hourly=precipitation_probability&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset&forecast_days=7&timezone=auto&temperature_unit=celsius&wind_speed_unit=kmh");
        var (statusCode, body) = await GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!IsSuccessStatusCode(statusCode))
        {
            throw new InvalidOperationException(CreateHttpError(statusCode, body));
        }

        return WeatherResponseParser.Parse(body, location);
    }

    public async Task<IReadOnlyList<GeoLocation>> SearchLocationsAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<GeoLocation>();
        }

        var encodedQuery = Uri.EscapeDataString(query.Trim());
        var url = $"{GeocodingEndpoint}?name={encodedQuery}&count=20&language=zh&format=json";
        var (statusCode, body) = await GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!IsSuccessStatusCode(statusCode))
        {
            throw new InvalidOperationException(CreateHttpError(statusCode, body));
        }

        return GeocodingResponseParser.Parse(body);
    }

    private async Task<(System.Net.HttpStatusCode StatusCode, string Body)> GetAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = new CancellationTokenSource(_requestTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        try
        {
            using var response = await _httpClient.GetAsync(url, linkedCancellation.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"网络请求超时（{_requestTimeout.TotalSeconds:F0} 秒）。");
        }
    }

    private static bool IsSuccessStatusCode(System.Net.HttpStatusCode statusCode)
    {
        return (int)statusCode is >= 200 and < 300;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WeatherWidget48", "1.0.7"));
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("zh-CN"));
        return client;
    }

    private static string CreateHttpError(System.Net.HttpStatusCode statusCode, string body)
    {
        var reason = body.Length > 240 ? body.Substring(0, 240) : body;
        return $"天气服务返回 {(int)statusCode} {statusCode}：{reason}";
    }
}

public static class WeatherResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static WeatherData Parse(string json, GeoLocation location)
    {
        var response = JsonSerializer.Deserialize<WeatherApiResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("天气服务返回了空数据。");
        var current = response.Current
            ?? throw new InvalidOperationException("天气服务未返回当前天气。");
        var daily = response.Daily
            ?? throw new InvalidOperationException("天气服务未返回每日预报。");

        ValidateLengths(daily);
        if (daily.Time!.Count < 7)
        {
            throw new InvalidOperationException("天气服务返回的七天预报不完整。");
        }

        var currentWeather = new CurrentWeather(
            Required(current.Temperature, "当前温度"),
            Required(current.Humidity, "当前湿度"),
            Required(current.WeatherCode, "当前天气代码"),
            Required(current.WindSpeed, "当前风速"),
            Required(current.WindDirection, "当前风向"),
            ParseCurrentPrecipitationProbability(response.Hourly, current.Time));

        var forecasts = new List<DailyWeather>(7);
        for (var index = 0; index < 7; index++)
        {
            forecasts.Add(new DailyWeather(
                ParseDate(RequiredString(daily.Time![index], "预报日期")),
                Required(daily.WeatherCode![index], "预报天气代码"),
                Required(daily.MinimumTemperature![index], "最低温度"),
                Required(daily.MaximumTemperature![index], "最高温度"),
                ParseTime(RequiredString(daily.Sunrise![index], "日出时间")),
                ParseTime(RequiredString(daily.Sunset![index], "日落时间"))));
        }

        return new WeatherData(location, currentWeather, forecasts, DateTimeOffset.Now);
    }

    private static int? ParseCurrentPrecipitationProbability(HourlyWeatherApi? hourly, string? currentTime)
    {
        if (hourly?.Time is null || hourly.PrecipitationProbability is null)
        {
            return null;
        }

        if (hourly.Time.Count != hourly.PrecipitationProbability.Count)
        {
            throw new InvalidOperationException("天气服务返回的逐小时降水概率数据不完整。");
        }

        if (!TryParseHourlyTime(currentTime, out var target))
        {
            return null;
        }

        for (var index = 0; index < hourly.Time.Count; index++)
        {
            if (TryParseHourlyTime(hourly.Time[index], out var time)
                && time.Year == target.Year
                && time.Month == target.Month
                && time.Day == target.Day
                && time.Hour == target.Hour)
            {
                return hourly.PrecipitationProbability[index];
            }
        }

        return null;
    }

    private static bool TryParseHourlyTime(string? value, out DateTime time)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out time);
    }

    private static void ValidateLengths(DailyWeatherApi daily)
    {
        var arrays = new[]
        {
            daily.Time?.Count ?? 0,
            daily.WeatherCode?.Count ?? 0,
            daily.MinimumTemperature?.Count ?? 0,
            daily.MaximumTemperature?.Count ?? 0,
            daily.Sunrise?.Count ?? 0,
            daily.Sunset?.Count ?? 0
        };

        if (arrays.Any(count => count == 0))
        {
            throw new InvalidOperationException("天气服务返回的预报字段不完整。");
        }

        var expectedLength = daily.Time!.Count;
        if (arrays.Any(count => count != expectedLength))
        {
            throw new InvalidOperationException("天气服务返回的预报字段长度不一致。");
        }
    }

    private static T Required<T>(T? value, string fieldName) where T : struct
    {
        return value ?? throw new InvalidOperationException($"天气服务未返回{fieldName}。");
    }

    private static string RequiredString(string? value, string fieldName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"天气服务未返回{fieldName}。")
            : value;
    }

    private static DateTime ParseDate(string value)
    {
        if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new InvalidOperationException($"无法识别预报日期：{value}");
    }

    private static TimeSpan ParseTime(string value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
        {
            return dateTime.TimeOfDay;
        }

        throw new InvalidOperationException($"无法识别时间：{value}");
    }
}

public static class GeocodingResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<GeoLocation> Parse(string json)
    {
        var response = JsonSerializer.Deserialize<GeocodingApiResponse>(json, JsonOptions);
        if (response?.Results is null)
        {
            return Array.Empty<GeoLocation>();
        }

        return response.Results
            .Where(result => !string.IsNullOrWhiteSpace(result.Name)
                && result.Latitude is >= -90 and <= 90
                && result.Longitude is >= -180 and <= 180)
            .Select(result => new GeoLocation(
                result.Name!.Trim(),
                result.Admin1?.Trim() ?? string.Empty,
                result.Admin2?.Trim() ?? string.Empty,
                result.Country?.Trim() ?? string.Empty,
                result.Latitude!.Value,
                result.Longitude!.Value))
            .GroupBy(location => $"{location.Latitude:F5},{location.Longitude:F5}")
            .Select(group => group.First())
            .ToArray();
    }
}
