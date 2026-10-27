using System;
using System.Collections.Generic;
using System.Globalization;

namespace DesktopMonitor;

internal sealed class WeatherService
{
	private const int TimeoutMilliseconds = 4000;

	private readonly object syncRoot = new object();

	private string cachedText = string.Empty;

	private DateTime lastFetchUtc = DateTime.MinValue;

	public AppSettings Settings { get; }

	public WeatherService(AppSettings settings)
	{
		Settings = settings;
	}

	public void InvalidateCache()
	{
		lock (syncRoot)
		{
			cachedText = string.Empty;
			lastFetchUtc = DateTime.MinValue;
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
			string text2 = HttpHelper.Get(string.Format(CultureInfo.InvariantCulture, "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m,wind_direction_10m&timezone=Asia%2FShanghai", Settings.Latitude, Settings.Longitude), 4000);
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
			list.AddRange(HttpHelper.ParseGeocoding(HttpHelper.Get(url, 4000)));
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
