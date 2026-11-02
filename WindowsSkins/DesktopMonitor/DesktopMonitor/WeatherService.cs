using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DesktopMonitor;

internal sealed class WeatherService
{
	// 4 秒在"走代理/跨境 RTT 300ms 起步"的网络上太紧了：握手一慢就整块拿不到天气。
	// 放宽到 10 秒，配合下面的磁盘缓存，慢网下也能先有内容再慢慢补新。
	private const int TimeoutMilliseconds = 10000;

	/// <summary>落盘缓存最长可用时效（跨天也没关系，这里没有"今天/明天"这类相对日期）。</summary>
	private static readonly TimeSpan MaximumDiskCacheAge = TimeSpan.FromHours(6);

	private readonly object syncRoot = new object();

	private string cachedText = string.Empty;

	private DateTime lastFetchUtc = DateTime.MinValue;

	public AppSettings Settings { get; }

	public WeatherService(AppSettings settings)
	{
		Settings = settings;
		LoadDiskCache();
	}

	private static string DiskCachePath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
		"DesktopMonitor",
		"weather-cache.txt");

	/// <summary>
	/// 冷启动先把上一次成功取到的天气读回来顶着，
	/// 免得开机后这条信息栏空着（要等网络握手完才有字）。
	/// </summary>
	private void LoadDiskCache()
	{
		try
		{
			if (!File.Exists(DiskCachePath))
			{
				return;
			}
			string[] array = File.ReadAllText(DiskCachePath, System.Text.Encoding.UTF8).Split(new char[1] { '|' }, 2);
			if (array.Length != 2)
			{
				return;
			}
			if (!long.TryParse(array[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
			{
				return;
			}
			DateTime savedUtc = new DateTime(ticks, DateTimeKind.Utc);
			if ((DateTime.UtcNow - savedUtc) > MaximumDiskCacheAge)
			{
				return;
			}
			if (array[1].Length == 0)
			{
				return;
			}
			cachedText = array[1];
			lastFetchUtc = savedUtc;
		}
		catch
		{
		}
	}

	private void SaveDiskCache(string text, DateTime fetchedUtc)
	{
		try
		{
			string directory = Path.GetDirectoryName(DiskCachePath);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}
			File.WriteAllText(DiskCachePath, fetchedUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + text, System.Text.Encoding.UTF8);
		}
		catch
		{
		}
	}

	public void InvalidateCache()
	{
		lock (syncRoot)
		{
			cachedText = string.Empty;
			lastFetchUtc = DateTime.MinValue;
		}
		DeleteDiskCache();
	}

	/// <summary>缓存作废时把盘上的也一起删掉，否则下次开机又会端出旧城市/旧天气。</summary>
	private void DeleteDiskCache()
	{
		try
		{
			if (File.Exists(DiskCachePath))
			{
				File.Delete(DiskCachePath);
			}
		}
		catch
		{
		}
	}

	public void ApplyCity(CityInfo city)
	{
		if (city == null)
		{
			return;
		}
		lock (syncRoot)
		{
			Settings.CityName = (string.IsNullOrEmpty(city.Name) ? "北京市" : city.Name);
			Settings.CityFullName = city.FullName;
			Settings.Latitude = city.Latitude;
			Settings.Longitude = city.Longitude;
			cachedText = string.Empty;
			lastFetchUtc = DateTime.MinValue;
		}
		DeleteDiskCache();
	}

	// 只读缓存，绝不联网。冷启动的第一帧要用它，
	// 免得为了等天气（最长 4 秒）把 CPU/内存/硬盘/网速 一起卡住。
	public string PeekCachedText()
	{
		lock (syncRoot)
		{
			if (cachedText.Length > 0 && (DateTime.UtcNow - lastFetchUtc).TotalMinutes < (double)Settings.WeatherRefreshMinutes)
			{
				return cachedText;
			}
			return string.Empty;
		}
	}

	public string GetWeatherText()
	{
		lock (syncRoot)
		{
			if (cachedText.Length > 0 && (DateTime.UtcNow - lastFetchUtc).TotalMinutes < (double)Settings.WeatherRefreshMinutes)
			{
				return cachedText;
			}
		}
		string text = string.Empty;
		try
		{
			string text2 = HttpHelper.Get(string.Format(CultureInfo.InvariantCulture, "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m,wind_direction_10m&timezone=Asia%2FShanghai", Settings.Latitude, Settings.Longitude), TimeoutMilliseconds);
			int num = text2.IndexOf("\"current\":", StringComparison.Ordinal);
			if (num >= 0)
			{
				string text3 = HttpHelper.ExtractJson(text2, "temperature_2m", num);
				string value = HttpHelper.ExtractJson(text2, "relative_humidity_2m", num);
				string value2 = HttpHelper.ExtractJson(text2, "weather_code", num);
				string value3 = HttpHelper.ExtractJson(text2, "wind_speed_10m", num);
				string value4 = HttpHelper.ExtractJson(text2, "wind_direction_10m", num);
				if (text3.Length > 0)
				{
					text = string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}℃ 湿度{3}% {4}{5}km/h 更新{6}", Settings.CityName, ShortDescription(WeatherText.Describe(ParseInt(value2))), text3.PadLeft(5), ParseInt(value).ToString(CultureInfo.InvariantCulture).PadLeft(3), WeatherText.WindDirection(ParseInt(value4)), ParseInt(value3).ToString(CultureInfo.InvariantCulture).PadLeft(3), DateTime.Now.ToString("HH:mm"));
				}
			}
		}
		catch
		{
		}
		lock (syncRoot)
		{
			if (text.Length > 0)
			{
				cachedText = text;
				lastFetchUtc = DateTime.UtcNow;
				SaveDiskCache(cachedText, lastFetchUtc);
				return cachedText;
			}
			return cachedText;
		}
	}

	public static List<CityInfo> SearchLocal(string keyword, int limit)
	{
		return PlaceCatalog.Search(keyword, limit);
	}

	public static List<CityInfo> SearchOnline(string keyword, int limit)
	{
		List<CityInfo> list = new List<CityInfo>();
		if (string.IsNullOrEmpty(keyword))
		{
			return list;
		}
		try
		{
			string url = "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(keyword.Trim()) + "&count=30&language=zh&format=json";
			list.AddRange(HttpHelper.ParseGeocoding(HttpHelper.Get(url, TimeoutMilliseconds)));
		}
		catch
		{
		}
		if (list.Count > limit)
		{
			list.RemoveRange(limit, list.Count - limit);
		}
		return list;
	}

	public static List<CityInfo> SearchCities(string keyword)
	{
		return SearchLocal(keyword, 40);
	}

	public static List<CityInfo> FindInCatalog(string keyword)
	{
		return PlaceCatalog.Search(keyword, 20);
	}

	private static string ShortDescription(string description)
	{
		return ((description.Length > 2) ? description.Substring(0, 2) : description).PadRight(2);
	}

	private static int ParseInt(string value)
	{
		if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result2))
			{
				return (int)Math.Round(result2, MidpointRounding.AwayFromZero);
			}
			return 0;
		}
		return result;
	}
}
