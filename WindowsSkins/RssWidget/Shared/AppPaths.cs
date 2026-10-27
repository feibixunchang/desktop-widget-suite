using System;
using System.IO;

namespace Shared;

internal static class AppPaths
{
	private static string appId = "DesktopWidget";

	public static string AppId
	{
		get
		{
			return appId;
		}
		set
		{
			appId = (string.IsNullOrEmpty(value) ? "DesktopWidget" : value);
		}
	}

	public static string DataDirectory
	{
		get
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppId);
			if (!Directory.Exists(text))
			{
				Directory.CreateDirectory(text);
			}
			return text;
		}
	}

	public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
}
