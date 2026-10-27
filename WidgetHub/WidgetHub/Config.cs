using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WidgetHub;

public class Config
{
	public bool AutoStartManager { get; set; }

	public string UpdateToken { get; set; } = "";


	public bool HideWidgetTrayIcons { get; set; }

	public List<Skin> Skins { get; set; } = new List<Skin>();


	public static string Path_ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WidgetHub", "config.json");

	public static Config Load()
	{
		try
		{
			if (File.Exists(Path_))
			{
				Config config = JsonSerializer.Deserialize<Config>(File.ReadAllText(Path_), (JsonSerializerOptions)null);
				if (config != null)
				{
					config.Normalize();
					return config;
				}
			}
		}
		catch
		{
		}
		return Defaults();
	}

	public static Config? TryLoad()
	{
		try
		{
			if (File.Exists(Path_))
			{
				Config config = JsonSerializer.Deserialize<Config>(File.ReadAllText(Path_), (JsonSerializerOptions)null);
				if (config != null)
				{
					config.Normalize();
					return config;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	public void Normalize()
	{
		if (Skins == null)
		{
			Skins = Defaults().Skins;
		}
		foreach (Skin skin in Defaults().Skins)
		{
			bool flag = false;
			foreach (Skin skin2 in Skins)
			{
				if (string.Equals(skin2.Id, skin.Id, StringComparison.OrdinalIgnoreCase))
				{
					flag = true;
					break;
				}
				if (string.Equals(Path.GetFileNameWithoutExtension(skin2.ExeName ?? ""), Path.GetFileNameWithoutExtension(skin.ExeName ?? ""), StringComparison.OrdinalIgnoreCase))
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				Skins.Add(skin);
			}
		}
		foreach (Skin skin3 in Skins)
		{
			if (string.Equals(skin3.Id, "NeonMouseTrail", StringComparison.OrdinalIgnoreCase))
			{
				skin3.Positionless = true;
			}
			if (skin3.X >= 0 && skin3.Y >= 0 && !skin3.PositionSaved)
			{
				skin3.PositionSaved = true;
			}
		}
	}

	public void Save()
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Expected O, but got Unknown
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(Path_));
			File.WriteAllText(Path_, JsonSerializer.Serialize<Config>(this, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch
		{
		}
	}

	public static Config Defaults()
	{
		List<Skin> skins = new List<Skin>
		{
			new Skin
			{
				Name = "北京市天气",
				Folder = "WidgetHubSuite\\Skins\\WeatherWidget48",
				ExeName = "WeatherWidget48.exe"
			},
			new Skin
			{
				Name = "桌面万年历",
				Folder = "WidgetHubSuite\\Skins\\DesktopCalendar",
				ExeName = "DesktopCalendar.exe"
			},
			new Skin
			{
				Name = "天气CPU网速",
				Folder = "WidgetHubSuite\\Skins\\DesktopMonitor",
				ExeName = "DesktopMonitor.exe"
			},
			new Skin
			{
				Name = "桌面音乐",
				Folder = "WidgetHubSuite\\Skins\\DesktopMusic",
				ExeName = "DesktopMusic.exe"
			},
			new Skin
			{
				Name = "桌面收音机",
				Folder = "WidgetHubSuite\\Skins\\DesktopRadio",
				ExeName = "DesktopRadio.exe"
			},
			new Skin
			{
				Name = "倒计时闹钟",
				Folder = "WidgetHubSuite\\Skins\\DesktopTimer",
				ExeName = "DesktopTimer.exe"
			},
			new Skin
			{
				Name = "桌面视频",
				Folder = "WidgetHubSuite\\Skins\\DesktopVideo",
				ExeName = "DesktopVideo.exe"
			},
			new Skin
			{
				Name = "桌面记账",
				Folder = "WidgetHubSuite\\Skins\\DesktopWallet",
				ExeName = "DesktopWallet.exe"
			},
			new Skin
			{
				Name = "桌面RSS",
				Folder = "WidgetHubSuite\\Skins\\RssWidget",
				ExeName = "RssWidget.exe"
			},
			new Skin
			{
				Name = "桌面记事",
				Folder = "WidgetHubSuite\\Skins\\DesktopNotes",
				ExeName = "DesktopNotes.exe"
			},
			new Skin
			{
				Name = "桌面相框",
				Folder = "WidgetHubSuite\\Skins\\DesktopPhoto",
				ExeName = "DesktopPhoto.exe"
			},
			new Skin
			{
				Name = "桌面阴历",
				Folder = "WidgetHubSuite\\Skins\\DesktopLunar",
				ExeName = "DesktopLunar.exe"
			},
			new Skin
			{
				Name = "炫彩鼠标尾巴",
				Folder = "WidgetHubSuite\\Skins\\NeonMouseTrail",
				ExeName = "NeonMouseTrail.exe",
				Positionless = true
			}
		};
		return new Config
		{
			Skins = skins
		};
	}
}
