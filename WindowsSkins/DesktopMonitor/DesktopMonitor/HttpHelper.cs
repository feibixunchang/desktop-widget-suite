using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace DesktopMonitor;

internal static class HttpHelper
{
	public static string Get(string url, int timeoutMilliseconds)
	{
		HttpWebRequest obj = (HttpWebRequest)WebRequest.Create(url);
		obj.Timeout = timeoutMilliseconds;
		obj.ReadWriteTimeout = timeoutMilliseconds;
		obj.Accept = "application/json";
		obj.UserAgent = "DesktopMonitor/1.1";
		using HttpWebResponse httpWebResponse = (HttpWebResponse)obj.GetResponse();
		using StreamReader streamReader = new StreamReader(httpWebResponse.GetResponseStream(), Encoding.UTF8);
		return streamReader.ReadToEnd();
	}

	public static string ExtractJson(string json, string key, int startIndex)
	{
		if (string.IsNullOrEmpty(json))
		{
			return string.Empty;
		}
		string text = "\"" + key + "\":";
		int num = json.IndexOf(text, startIndex, StringComparison.Ordinal);
		if (num < 0)
		{
			return string.Empty;
		}
		for (num += text.Length; num < json.Length && json[num] == ' '; num++)
		{
		}
		if (num >= json.Length)
		{
			return string.Empty;
		}
		if (json[num] == '"')
		{
			int num2 = json.IndexOf('"', num + 1);
			if (num2 >= 0)
			{
				return json.Substring(num + 1, num2 - num - 1);
			}
			return string.Empty;
		}
		int i;
		for (i = num; i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']'; i++)
		{
		}
		return json.Substring(num, i - num).Trim();
	}

	public static List<CityInfo> ParseGeocoding(string json)
	{
		List<CityInfo> list = new List<CityInfo>();
		if (string.IsNullOrEmpty(json))
		{
			return list;
		}
		int num = json.IndexOf("\"results\"", StringComparison.Ordinal);
		if (num < 0)
		{
			return list;
		}
		num = json.IndexOf('[', num);
		if (num < 0)
		{
			return list;
		}
		int num2 = 0;
		int num3 = -1;
		for (int i = num; i < json.Length; i++)
		{
			switch (json[i])
			{
			case '{':
				if (num2 == 0)
				{
					num3 = i + 1;
				}
				num2++;
				continue;
			case '}':
				num2--;
				if (num2 == 0 && num3 > 0)
				{
					string json2 = json.Substring(num3, i - num3);
					CityInfo cityInfo = new CityInfo
					{
						Name = ExtractJson(json2, "name", 0),
						Admin1 = ExtractJson(json2, "admin1", 0),
						Admin2 = ExtractJson(json2, "admin2", 0),
						Country = ExtractJson(json2, "country", 0),
						CountryCode = ExtractJson(json2, "country_code", 0),
						Latitude = ParseDouble(ExtractJson(json2, "latitude", 0)),
						Longitude = ParseDouble(ExtractJson(json2, "longitude", 0))
					};
					if (cityInfo.Name.Length > 0)
					{
						list.Add(cityInfo);
					}
					num3 = -1;
				}
				continue;
			default:
				continue;
			case ']':
				break;
			}
			break;
		}
		return list;
	}

	private static double ParseDouble(string value)
	{
		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return 0.0;
		}
		return result;
	}
}
