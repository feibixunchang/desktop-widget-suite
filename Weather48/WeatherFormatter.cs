using System.Globalization;

namespace WeatherWidget48;

public static class WeatherFormatter
{
    public static string FormatCurrentLine(WeatherData weather)
    {
        var current = weather.Current;
        var today = weather.Daily[0];
        var precipitationProbability = current.PrecipitationProbability is int probability
            ? $"降水概率 {probability:F0}%"
            : "降水概率 --";
        return string.Join(
            " | ",
            weather.Location.Name,
            WeatherCodeText.Describe(current.WeatherCode),
            $"{current.Temperature:F1}\u2103",
            $"湿度 {current.Humidity:F0}%",
            precipitationProbability,
            $"{WindDirectionText.Describe(current.WindDirection)} {current.WindSpeed:F1} km/h",
            $"日出 {today.Sunrise:hh\\:mm}",
            $"日落 {today.Sunset:hh\\:mm}",
            $"最后更新 {weather.FetchedAt.ToLocalTime():HH\\:mm}");
    }

    public static string FormatForecastLine(WeatherData weather)
    {
        return string.Join(
            " | ",
            weather.Daily.Take(7).Select((forecast, index) =>
            {
                var day = index switch
                {
                    0 => "今天",
                    1 => "明天",
                    _ => forecast.Date.ToString("MM-dd", CultureInfo.InvariantCulture)
                };
                var minimum = Math.Round(forecast.MinimumTemperature, MidpointRounding.AwayFromZero);
                var maximum = Math.Round(forecast.MaximumTemperature, MidpointRounding.AwayFromZero);
                var temperatures = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:F0}°/{1:F0}°",
                    minimum,
                    maximum);
                return $"{day} {WeatherCodeText.Describe(forecast.WeatherCode)} {temperatures}";
            }));
    }
}

public static class WeatherCodeText
{
    public static string Describe(int code) => code switch
    {
        0 => "晴",
        1 => "晴间多云",
        2 => "多云",
        3 => "阴",
        45 => "雾",
        48 => "雾凇",
        51 => "小毛毛雨",
        53 => "毛毛雨",
        55 => "大毛毛雨",
        56 => "冻毛毛雨",
        57 => "强冻毛毛雨",
        61 => "小雨",
        63 => "中雨",
        65 => "大雨",
        66 => "冻雨",
        67 => "强冻雨",
        71 => "小雪",
        73 => "中雪",
        75 => "大雪",
        77 => "米雪",
        80 => "小阵雨",
        81 => "阵雨",
        82 => "强阵雨",
        85 => "小阵雪",
        86 => "大阵雪",
        95 => "雷阵雨",
        96 => "雷阵雨伴小冰雹",
        99 => "雷阵雨伴大冰雹",
        _ => "未知天气"
    };
}

public static class WindDirectionText
{
    private static readonly string[] Directions =
    {
        "北风",
        "东北风",
        "东风",
        "东南风",
        "南风",
        "西南风",
        "西风",
        "西北风"
    };

    public static string Describe(int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        var index = (int)Math.Round(normalized / 45.0, MidpointRounding.AwayFromZero) % Directions.Length;
        return Directions[index];
    }
}
