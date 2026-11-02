using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeatherWidget48;

/// <summary>
/// 上一次成功取到的天气文字，落盘保存。
/// 目的是让冷启动（开机、管理器重启）第一帧就有内容，
/// 而不是一上来就 "天气获取失败" —— 挂件本身没有网络也该先给用户看点什么。
/// 缓存带地点指纹（经纬度+地名），换了城市自动作废；超过时效也不再显示。
/// </summary>
public sealed class WeatherCacheStore
{
    /// <summary>缓存最长可用时效。超过就宁可显示"正在获取"也不给用户看过期天气。</summary>
    private static readonly TimeSpan MaximumCacheAge = TimeSpan.FromHours(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public WeatherCacheStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WeatherWidget48",
            "weather-cache.json");
    }

    public string FilePath { get; }

    /// <summary>
    /// 读取仍然有效且地点匹配的缓存。任何异常都当作"没有缓存"，绝不影响启动。
    /// </summary>
    public CachedWeather? Load(GeoLocation location)
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var cached = JsonSerializer.Deserialize<CachedWeather>(json, JsonOptions);
            if (cached is null
                || string.IsNullOrEmpty(cached.FirstLine)
                || string.IsNullOrEmpty(cached.SecondLine))
            {
                return null;
            }

            if (!TryParseSavedAt(cached.SavedAt, out var savedAt))
            {
                return null;
            }

            if (DateTime.Now - savedAt > MaximumCacheAge)
            {
                return null;
            }

            // 第二行里有"今天/明天"这种相对日期，跨了午夜再拿出来显示就会标错，
            // 所以只允许用当天的缓存。
            if (savedAt.Date != DateTime.Now.Date)
            {
                return null;
            }

            // 地点变了（用户换城市、或缓存是别人机器上拷来的）一律作废
            if (!LocationMatches(cached, location))
            {
                return null;
            }

            return cached;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>保存一份成功结果。写失败无所谓，不影响正常显示。</summary>
    public void Save(GeoLocation location, string firstLine, string secondLine)
    {
        try
        {
            var cached = new CachedWeather
            {
                LocationName = location.Name,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                FirstLine = firstLine,
                SecondLine = secondLine,
                SavedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
            };

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(cached, JsonOptions), new UTF8Encoding(false));
                File.Copy(temporaryPath, FilePath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            // 缓存只是锦上添花，写不进去就算了
        }
    }

    private static bool LocationMatches(CachedWeather cached, GeoLocation location)
    {
        // 0.01 度约等于 1 公里，够判断"是不是同一个地点"了
        var closeEnough = Math.Abs(cached.Latitude - location.Latitude) < 0.01
            && Math.Abs(cached.Longitude - location.Longitude) < 0.01;
        if (!closeEnough)
        {
            return false;
        }

        return string.IsNullOrEmpty(cached.LocationName)
            || string.Equals(cached.LocationName, location.Name, StringComparison.Ordinal);
    }

    private static bool TryParseSavedAt(string? value, out DateTime savedAt)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out savedAt);
    }

    public sealed class CachedWeather
    {
        public string? LocationName { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public string? FirstLine { get; set; }

        public string? SecondLine { get; set; }

        public string? SavedAt { get; set; }
    }
}
