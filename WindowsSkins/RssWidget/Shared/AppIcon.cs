using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace Shared;

internal static class AppIcon
{
	private static Icon LoadFromAssembly(Assembly assembly, int size)
	{
		if (assembly == null)
		{
			return null;
		}
		try
		{
			string[] manifestResourceNames = assembly.GetManifestResourceNames();
			Array.Sort(manifestResourceNames, StringComparer.OrdinalIgnoreCase);
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
				return new Icon(memoryStream, size, size);
			}
		}
		catch
		{
		}
		return null;
	}

	public static Icon Load(int size)
	{
		Icon icon = LoadFromAssembly(Assembly.GetEntryAssembly(), size);
		if (icon != null)
		{
			return icon;
		}
		icon = FromExecutable(size);
		if (icon != null)
		{
			return icon;
		}
		return (Icon)SystemIcons.Application.Clone();
	}

	private static Icon FromExecutable(int size)
	{
		try
		{
			string text = ((Assembly.GetEntryAssembly() == null) ? null : Assembly.GetEntryAssembly().Location);
			if (string.IsNullOrEmpty(text) || !File.Exists(text))
			{
				return null;
			}
			Icon icon = ((size <= 32) ? Icon.ExtractAssociatedIcon(text) : null);
			if (icon != null)
			{
				return icon;
			}
			using Bitmap bitmap = new Bitmap(size, size);
			using Graphics graphics = Graphics.FromImage(bitmap);
			graphics.Clear(Color.Transparent);
			Icon icon2 = Icon.ExtractAssociatedIcon(text);
			if (icon2 != null)
			{
				using (Bitmap image = icon2.ToBitmap())
				{
					graphics.DrawImage(image, new Rectangle(0, 0, size, size));
				}
				icon2.Dispose();
			}
			return Icon.FromHandle(bitmap.GetHicon());
		}
		catch
		{
			return null;
		}
	}
}
