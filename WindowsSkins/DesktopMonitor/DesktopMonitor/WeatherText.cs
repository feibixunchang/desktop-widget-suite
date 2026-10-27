using System;

namespace DesktopMonitor;

internal static class WeatherText
{
	public static string Describe(int code)
	{
		if (code == 0)
		{
			return "晴";
		}
		if (code <= 2 || code == 3)
		{
			return "多云";
		}
		if (code <= 48)
		{
			return "雾";
		}
		if (code <= 59)
		{
			return "小雨";
		}
		if (code <= 69)
		{
			return "雨";
		}
		if (code <= 79)
		{
			return "雪";
		}
		if (code <= 82)
		{
			return "阵雨";
		}
		if (code <= 86)
		{
			return "阵雪";
		}
		if (code <= 99)
		{
			return "雷雨";
		}
		return "未知";
	}

	public static string WindDirection(int degrees)
	{
		string[] array = new string[8] { "北风", "东北风", "东风", "东南风", "南风", "西南风", "西风", "西北风" };
		int num = (int)Math.Round((double)((degrees % 360 + 360) % 360) / 45.0, MidpointRounding.AwayFromZero) % array.Length;
		return array[num];
	}
}
