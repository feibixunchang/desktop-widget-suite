using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WidgetHub;

public static class WidgetSettings
{
	public sealed class Profile
	{
		public string ConfigPath = "";

		public bool Ini;

		public List<SettingDef> Defs = new List<SettingDef>();
	}

	private static SettingDef Num(string key, string label, int min, int max, int step = 1)
	{
		return new SettingDef
		{
			Key = key,
			Label = label,
			Kind = "num",
			Min = min,
			Max = max,
			Step = step
		};
	}

	private static SettingDef Bool(string key, string label)
	{
		return new SettingDef
		{
			Key = key,
			Label = label,
			Kind = "bool"
		};
	}

	private static SettingDef Combo(string key, string label, string[] options, int offset = 0)
	{
		return new SettingDef
		{
			Key = key,
			Label = label,
			Kind = "combo",
			Options = options,
			ComboOffset = offset
		};
	}

	public static Profile? For(Skin s)
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string exe = s.ExeName ?? "";
		string[] array = new string[7] { "DesktopCalendar", "DesktopTimer", "DesktopVideo", "DesktopWallet", "RssWidget", "DesktopNotes", "DesktopPhoto" };
		if (Array.IndexOf(array, s.Id) >= 0 || array.Any((string x) => exe.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
		{
			return new Profile
			{
				ConfigPath = Path.Combine(folderPath, s.Id, "settings.json"),
				Defs = new List<SettingDef>
				{
					Num("FontSize", "字号", 8, 48),
					Num("Opacity", "背景不透明度", 0, 100),
					Bool("DesktopLayer", "贴在桌面层（不被程序遮挡）"),
					Bool("TopMost", "总在最前"),
					Bool("Locked", "锁定位置"),
					Bool("HideOnFullscreen", "全屏程序时隐藏")
				}
			};
		}
		if (exe.IndexOf("WeatherWidget48", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return new Profile
			{
				ConfigPath = Path.Combine(folderPath2, "WeatherWidget48", "settings.json"),
				Defs = new List<SettingDef>
				{
					Num("FirstLineFontSize", "主行字号", 12, 48),
					Num("SecondLineFontSize", "副行字号", 8, 30),
					Num("OpacityPercent", "背景不透明度", 0, 100),
					Bool("DesktopLayer", "贴在桌面层（不被程序遮挡）"),
					Bool("TopMost", "总在最前"),
					Bool("PauseFullscreen", "全屏程序时暂停")
				}
			};
		}
		if (exe.IndexOf("NeonMouseTrail", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return new Profile
			{
				ConfigPath = TrailConfig.FilePath,
				Ini = true,
				Defs = new List<SettingDef>
				{
					Combo("Effect", "特效", TrailConfig.Effects, 1),
					Combo("Palette", "配色", TrailConfig.Palettes),
					Combo("PerfMode", "性能", TrailConfig.PerfModes),
					Num("Density", "密度", 1, 10),
					Num("SizeScale", "粒子大小", 1, 10),
					Num("Brightness", "亮度", 1, 10),
					Num("Trail", "拖尾长度", 1, 10),
					Num("HueSpeed", "变色速度", 0, 200),
					Bool("ClickBurst", "点击时爆裂"),
					Bool("OnlyWhenMoving", "仅鼠标移动时显示"),
					Bool("Enabled", "启用特效")
				}
			};
		}
		if (exe.IndexOf("DesktopMusic", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return new Profile
			{
				ConfigPath = Path.Combine(folderPath, "DesktopMusic", "settings.json"),
				Defs = new List<SettingDef>
				{
					Num("FontSize", "字号", 8, 36),
					Num("Opacity", "背景不透明度", 0, 100),
					Num("Volume", "音量", 0, 100),
					Bool("Muted", "静音"),
					Bool("TopMost", "总在最前"),
					Bool("Locked", "锁定位置"),
					Bool("HideOnFullscreen", "全屏程序时隐藏"),
					Bool("StartWithWindows", "开机自动启动")
				}
			};
		}
		if (exe.IndexOf("DesktopRadio", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return new Profile
			{
				ConfigPath = Path.Combine(folderPath, "DesktopRadio", "settings.json"),
				Defs = new List<SettingDef>
				{
					Num("FontSize", "字号", 8, 36),
					Num("Opacity", "背景不透明度", 0, 100),
					Num("Volume", "音量", 0, 100),
					Bool("TopMost", "总在最前"),
					Bool("Locked", "锁定位置"),
					Bool("HideOnFullscreen", "全屏程序时隐藏"),
					Bool("StartWithWindows", "开机自动启动"),
					Bool("AutoPlayOnStart", "启动即播放")
				}
			};
		}
		if (exe.IndexOf("DesktopMonitor", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			string text = Path.Combine(folderPath2, "Programs", "DesktopMonitor", "settings.ini");
			string text2 = Path.Combine(folderPath, "DesktopMonitor", "settings.ini");
			string configPath = (File.Exists(text) ? text : text2);
			return new Profile
			{
				ConfigPath = configPath,
				Ini = true,
				Defs = new List<SettingDef>
				{
					Num("FontSize", "字号", 8, 36),
					Num("Opacity", "背景不透明度", 0, 100),
					Bool("TopMost", "总在最前"),
					Bool("Locked", "锁定位置"),
					Bool("HideOnFullscreen", "全屏程序时隐藏")
				}
			};
		}
		return null;
	}

	public static Dictionary<string, string> ReadIni(string path)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			if (!File.Exists(path))
			{
				return dictionary;
			}
			string[] array = File.ReadAllLines(path);
			for (int i = 0; i < array.Length; i++)
			{
				string text = array[i].Trim();
				if (text.Length != 0 && !text.StartsWith("#") && !text.StartsWith("["))
				{
					int num = text.IndexOf('=');
					if (num > 0)
					{
						dictionary[text.Substring(0, num).Trim()] = text.Substring(num + 1).Trim();
					}
				}
			}
		}
		catch
		{
		}
		return dictionary;
	}

	public static void WriteIni(string path, Dictionary<string, string> values)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("# 由 WidgetHub 写入");
			foreach (KeyValuePair<string, string> value in values)
			{
				stringBuilder.AppendLine(value.Key + "=" + value.Value);
			}
			File.WriteAllText(path, stringBuilder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
		catch
		{
		}
	}

	public static Dictionary<string, string> Read(Profile p)
	{
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Invalid comparison between Unknown and I4
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			if (!File.Exists(p.ConfigPath))
			{
				return dictionary;
			}
			if (p.Ini)
			{
				foreach (KeyValuePair<string, string> item in ReadIni(p.ConfigPath))
				{
					dictionary[item.Key] = item.Value;
				}
				return dictionary;
			}
			JsonNode obj = JsonNode.Parse(File.ReadAllText(p.ConfigPath), (JsonNodeOptions?)null, default(JsonDocumentOptions));
			JsonObject val = (JsonObject)(object)((obj is JsonObject) ? obj : null);
			if (val == null)
			{
				return dictionary;
			}
			foreach (KeyValuePair<string, JsonNode> item2 in val)
			{
				if (item2.Value != null)
				{
					dictionary[item2.Key] = (((int)item2.Value.GetValueKind() == 3) ? item2.Value.GetValue<string>() : item2.Value.ToJsonString((JsonSerializerOptions)null).Trim(new char[1] { '"' }));
				}
			}
		}
		catch
		{
		}
		return dictionary;
	}

	public static void Write(Profile p, Dictionary<string, string> values)
	{
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Expected O, but got Unknown
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f8: Expected O, but got Unknown
		if (p.Ini)
		{
			Dictionary<string, string> dictionary = ReadIni(p.ConfigPath);
			foreach (KeyValuePair<string, string> value in values)
			{
				dictionary[value.Key] = value.Value;
			}
			WriteIni(p.ConfigPath, dictionary);
			return;
		}
		try
		{
			JsonObject val = File.Exists(p.ConfigPath) ? (JsonObject)JsonNode.Parse(File.ReadAllText(p.ConfigPath)) : new JsonObject((JsonNodeOptions?)null);
			foreach (KeyValuePair<string, string> kv in values)
			{
				SettingDef settingDef = p.Defs.FirstOrDefault((SettingDef d) => string.Equals(d.Key, kv.Key, StringComparison.OrdinalIgnoreCase));
				if (settingDef == null)
				{
					continue;
				}
				if (settingDef.Kind == "bool")
				{
					((JsonNode)val)[settingDef.Key] = JsonValue.Create(kv.Value == "1" || string.Equals(kv.Value, "true", StringComparison.OrdinalIgnoreCase));
				}
				else if (settingDef.Kind == "num")
				{
					if (int.TryParse(kv.Value, out var result))
					{
						((JsonNode)val)[settingDef.Key] = JsonValue.Create(result);
					}
				}
				else
				{
					((JsonNode)val)[settingDef.Key] = JsonValue.Create(kv.Value);
				}
			}
			Directory.CreateDirectory(Path.GetDirectoryName(p.ConfigPath));
			File.WriteAllText(p.ConfigPath, ((JsonNode)val).ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch
		{
		}
	}
}
