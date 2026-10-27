using System;
using System.IO;

namespace WidgetHub;

public static class Log
{
	private static readonly string File_ = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WidgetHub", "log.txt");

	private static readonly object Gate = new object();

	public static void Reset()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(File_));
			File.WriteAllText(File_, string.Format("=== WidgetHub {0:yyyy-MM-dd HH:mm:ss} dpi={1} ===\r\n", DateTime.Now, Environment.GetEnvironmentVariable("__none")));
		}
		catch
		{
		}
	}

	public static void W(string msg)
	{
		try
		{
			lock (Gate)
			{
				File.AppendAllText(File_, $"{DateTime.Now:HH:mm:ss.fff} {msg}\r\n");
			}
		}
		catch
		{
		}
	}
}
