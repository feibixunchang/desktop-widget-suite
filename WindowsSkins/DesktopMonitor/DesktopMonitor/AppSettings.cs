using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DesktopMonitor;

internal sealed class AppSettings
{
	public const string DefaultCityName = "北京市";

	public const string DefaultCityFullName = "北京市海淀区中关村南街";

	public const double DefaultLatitude = 39.9042;

	public const double DefaultLongitude = 116.4074;

	public const int DefaultRefreshMinutes = 15;

	public const float DefaultFontSize = 16f;

	public string CityName { get; set; } = "北京市";


	public string CityFullName { get; set; } = "北京市海淀区中关村南街";


	public double Latitude { get; set; } = 39.9042;


	public double Longitude { get; set; } = 116.4074;


	public bool ShowWeather { get; set; } = true;


	public bool CompactMode { get; set; }

	public int WeatherRefreshMinutes { get; set; } = 15;


	public float FontSize { get; set; } = 16f;


	public bool PositionLocked { get; set; }

	public int BackgroundOpacity { get; set; } = 82;


	public int? WindowX { get; set; }

	public int? WindowY { get; set; }

	public bool DesktopLayer { get; set; }

	public bool DarkBackground { get; set; } = true;


	public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopMonitor", "settings.ini");

	public static AppSettings Load()
	{
		AppSettings appSettings = new AppSettings();
		try
		{
			if (File.Exists(FilePath))
			{
				string[] array = File.ReadAllLines(FilePath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				foreach (string text in array)
				{
					if (text.Length != 0 && text[0] != '#' && text[0] != ';')
					{
						int num = text.IndexOf('=');
						if (num > 0)
						{
							string key = text.Substring(0, num).Trim();
							string value = text.Substring(num + 1).Trim();
							appSettings.Apply(key, value);
						}
					}
				}
			}
		}
		catch
		{
		}
		appSettings.Normalize();
		return appSettings;
	}

	public void Save()
	{
		try
		{
			string directoryName = Path.GetDirectoryName(FilePath);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			List<string> contents = new List<string>
			{
				"# 桌面天气CPU网速挂件 配置",
				"[weather]",
				"city=" + CityName,
				"cityFull=" + CityFullName,
				"latitude=" + Latitude.ToString("0.0000", CultureInfo.InvariantCulture),
				"longitude=" + Longitude.ToString("0.0000", CultureInfo.InvariantCulture),
				"showWeather=" + (ShowWeather ? "1" : "0"),
				"compactMode=" + (CompactMode ? "1" : "0"),
				"refreshMinutes=" + WeatherRefreshMinutes.ToString(CultureInfo.InvariantCulture),
				string.Empty,
				"[display]",
				"fontSize=" + FontSize.ToString("0", CultureInfo.InvariantCulture),
				"locked=" + (PositionLocked ? "1" : "0"),
				"desktopLayer=" + (DesktopLayer ? "1" : "0"),
				"darkBackground=" + (DarkBackground ? "1" : "0"),
				"backgroundOpacity=" + BackgroundOpacity.ToString(CultureInfo.InvariantCulture),
				WindowX.HasValue ? ("x=" + WindowX.Value.ToString(CultureInfo.InvariantCulture)) : "x=",
				WindowY.HasValue ? ("y=" + WindowY.Value.ToString(CultureInfo.InvariantCulture)) : "y="
			};
			File.WriteAllLines(FilePath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
		catch
		{
		}
	}

	public void Normalize()
	{
		if (string.IsNullOrEmpty(CityName))
		{
			CityName = "北京市";
		}
		if (string.IsNullOrEmpty(CityFullName))
		{
			CityFullName = "北京市";
		}
		if (Latitude < -90.0 || Latitude > 90.0)
		{
			Latitude = 39.9042;
		}
		if (Longitude < -180.0 || Longitude > 180.0)
		{
			Longitude = 116.4074;
		}
		if (WeatherRefreshMinutes != 5 && WeatherRefreshMinutes != 10 && WeatherRefreshMinutes != 15 && WeatherRefreshMinutes != 30)
		{
			WeatherRefreshMinutes = 15;
		}
		if (FontSize < 10f || FontSize > 28f)
		{
			FontSize = 16f;
		}
	}

	private void Apply(string key, string value)
	{
		string text = key.ToLowerInvariant();
		if (text == null)
		{
			return;
		}
		switch (text.Length)
		{
		case 8:
			switch (text[0])
			{
			case 'c':
				if (text == "cityfull")
				{
					CityFullName = value;
				}
				break;
			case 'l':
			{
				if (text == "latitude" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result6))
				{
					Latitude = result6;
				}
				break;
			}
			case 'f':
			{
				if (text == "fontsize" && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result5))
				{
					FontSize = result5;
				}
				break;
			}
			}
			break;
		case 11:
			switch (text[0])
			{
			case 's':
				if (text == "showweather")
				{
					ShowWeather = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
				}
				break;
			case 'c':
				if (text == "compactmode")
				{
					CompactMode = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
				}
				break;
			}
			break;
		case 14:
			switch (text[0])
			{
			case 'r':
			{
				if (text == "refreshminutes" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result7))
				{
					WeatherRefreshMinutes = result7;
				}
				break;
			}
			case 'd':
				if (text == "darkbackground")
				{
					DarkBackground = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
				}
				break;
			}
			break;
		case 1:
			switch (text[0])
			{
			case 'x':
			{
				if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result3))
				{
					WindowX = result3;
				}
				break;
			}
			case 'y':
			{
				if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2))
				{
					WindowY = result2;
				}
				break;
			}
			}
			break;
		case 4:
			if (text == "city")
			{
				CityName = value;
			}
			break;
		case 9:
		{
			if (text == "longitude" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result4))
			{
				Longitude = result4;
			}
			break;
		}
		case 6:
			if (text == "locked")
			{
				PositionLocked = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
			}
			break;
		case 12:
			if (text == "desktoplayer")
			{
				DesktopLayer = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
			}
			break;
		case 17:
		{
			if (text == "backgroundopacity" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			{
				BackgroundOpacity = ((result >= 0) ? ((result > 100) ? 100 : result) : 0);
			}
			break;
		}
		case 2:
		case 3:
		case 5:
		case 7:
		case 10:
		case 13:
		case 15:
		case 16:
			break;
		}
	}
}
