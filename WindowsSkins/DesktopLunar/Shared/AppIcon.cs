using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace Shared;

internal static class AppIcon
{
	private static Icon LoadFromAssembly(Assembly assembly, int size)
	{
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Expected O, but got Unknown
		if (assembly == null)
		{
			return null;
		}
		try
		{
			string[] manifestResourceNames = assembly.GetManifestResourceNames();
			Array.Sort(manifestResourceNames, (IComparer<string>?)StringComparer.OrdinalIgnoreCase);
			string[] array = manifestResourceNames;
			foreach (string text in array)
			{
				if (!text.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				using Stream stream = assembly.GetManifestResourceStream(text);
				if (stream == null)
				{
					continue;
				}
				using MemoryStream memoryStream = new MemoryStream();
				stream.CopyTo(memoryStream);
				memoryStream.Position = 0L;
				return new Icon((Stream)memoryStream, size, size);
			}
		}
		catch
		{
		}
		return null;
	}

	public static Icon Load(int size)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		Icon val = LoadFromAssembly(Assembly.GetEntryAssembly(), size);
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
