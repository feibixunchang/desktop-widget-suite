using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace DesktopMonitor;

internal static class AppIcon
{
	public const string ResourceName = "desktopmonitor.ico";

	public static Icon Load(int size)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Expected O, but got Unknown
		Icon val = FromResource(size);
		if (val != null)
		{
			return val;
		}
		val = FromExecutable(size);
		if (val != null)
		{
			return val;
		}
		return (Icon)SystemIcons.Application.Clone();
	}

	private static Icon FromResource(int size)
	{
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Expected O, but got Unknown
		try
		{
			using Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream("desktopmonitor.ico");
			if (stream != null)
			{
				using (MemoryStream memoryStream = new MemoryStream())
				{
					stream.CopyTo(memoryStream);
					memoryStream.Position = 0L;
					return new Icon((Stream)memoryStream, size, size);
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static Icon FromExecutable(int size)
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Expected O, but got Unknown
		try
		{
			string text = ((Assembly.GetEntryAssembly() == null) ? null : Assembly.GetEntryAssembly().Location);
			if (string.IsNullOrEmpty(text) || !File.Exists(text))
			{
				return null;
			}
			Icon val = ((size <= 32) ? Icon.ExtractAssociatedIcon(text) : null);
			if (val != null)
			{
				return val;
			}
			Bitmap val2 = new Bitmap(size, size);
			try
			{
				Graphics val3 = Graphics.FromImage((Image)(object)val2);
				try
				{
					val3.Clear(Color.Transparent);
					Icon val4 = Icon.ExtractAssociatedIcon(text);
					if (val4 != null)
					{
						Bitmap val5 = val4.ToBitmap();
						try
						{
							val3.DrawImage((Image)(object)val5, new Rectangle(0, 0, size, size));
						}
						finally
						{
							((IDisposable)val5)?.Dispose();
						}
						val4.Dispose();
					}
					return Icon.FromHandle(val2.GetHicon());
				}
				finally
				{
					((IDisposable)val3)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		catch
		{
			return null;
		}
	}
}
