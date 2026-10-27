using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WidgetHub;

public static class TrailConfig
{
	public static readonly string[] Effects = new string[10] { "彩虹烟雾", "炫彩粒子", "霓虹光带", "星河星尘", "极光幻雾", "彩虹泡泡", "彗星拖尾", "火焰流光", "幻彩方块", "光晕脉冲" };

	public static readonly string[] Palettes = new string[8] { "彩虹", "极光", "霓虹", "火焰", "冰蓝", "黄金", "星云", "糖果" };

	public static string[] PerfModes = new string[3] { "节能", "均衡", "流畅" };

	public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NeonMouseTrail", "config.ini");

	public static Dictionary<string, string> Load()
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			if (!File.Exists(FilePath))
			{
				return dictionary;
			}
			string[] array = File.ReadAllLines(FilePath);
			for (int i = 0; i < array.Length; i++)
			{
				string text = array[i].Trim();
				if (text.Length != 0 && !text.StartsWith("#"))
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

	public static void Save(Dictionary<string, string> map)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("# 炫彩鼠标尾巴 配置文件（由 WidgetHub 写入）");
			foreach (KeyValuePair<string, string> item in map)
			{
				stringBuilder.AppendLine(item.Key + "=" + item.Value);
			}
			File.WriteAllText(FilePath, stringBuilder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
		catch
		{
		}
	}

	public static int GetInt(Dictionary<string, string> map, string key, int fallback)
	{
		if (!map.TryGetValue(key, out string value) || !int.TryParse(value, out var result))
		{
			return fallback;
		}
		return result;
	}
}
