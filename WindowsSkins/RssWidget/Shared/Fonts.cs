using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Shared;

internal static class Fonts
{
	public const float PopupSize = 12f;

	public const float PopupTitle = 13.5f;

	public const float PopupBig = 15f;

	private static readonly string[] Preferred = new string[22]
	{
		"方正宋黑_GBK", "方正宋黑简体", "方正宋黑_GB18030", "方正宋黑", "微软雅黑", "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun", "Microsoft JhengHei",
		"PMingLiU", "MingLiU", "NSimSun", "Malgun Gothic", "Yu Gothic", "Meiryo", "MS Gothic", "MS Mincho", "SimSun-ExtB", "Segoe UI",
		"Tahoma", "Arial"
	};

	private static string familyName = string.Empty;

	public static string FamilyName
	{
		get
		{
			if (familyName.Length == 0)
			{
				Resolve();
			}
			return familyName;
		}
	}

	private static void Resolve()
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			FontFamily[] families = FontFamily.Families;
			for (int i = 0; i < families.Length; i++)
			{
				string name = families[i].Name;
				if (!dictionary.ContainsKey(name))
				{
					dictionary[name] = name;
				}
			}
		}
		catch
		{
		}
		string[] preferred = Preferred;
		foreach (string key in preferred)
		{
			if (dictionary.TryGetValue(key, out var value) && CanRenderCjk(value))
			{
				familyName = value;
				return;
			}
		}
		try
		{
			FontFamily[] families = FontFamily.Families;
			foreach (FontFamily fontFamily in families)
			{
				if (CanRenderCjk(fontFamily.Name))
				{
					familyName = fontFamily.Name;
					return;
				}
			}
		}
		catch
		{
		}
		familyName = FontFamily.GenericSansSerif.Name;
	}

	private static bool CanRenderCjk(string familyName)
	{
		try
		{
			using FontFamily family = new FontFamily(familyName);
			using Font font = new Font(family, 18f, FontStyle.Regular, GraphicsUnit.Point);
			using Bitmap bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
			using Graphics graphics = Graphics.FromImage(bitmap);
			using StringFormat format = new StringFormat(StringFormat.GenericTypographic);
			graphics.Clear(Color.Transparent);
			graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
			graphics.DrawString("中", font, Brushes.White, 1f, 1f, format);
			for (int i = 0; i < 32; i++)
			{
				for (int j = 0; j < 32; j++)
				{
					if (bitmap.GetPixel(j, i).A > 0)
					{
						return true;
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}

	public static Font Widget(float size)
	{
		return new Font(FamilyName, size, FontStyle.Regular, GraphicsUnit.Point);
	}

	public static Font WidgetBold(float size)
	{
		return new Font(FamilyName, size, FontStyle.Bold, GraphicsUnit.Point);
	}

	public static Font Ui(float size)
	{
		return new Font(FamilyName, size, FontStyle.Regular, GraphicsUnit.Point);
	}

	public static Font UiBold(float size)
	{
		return new Font(FamilyName, size, FontStyle.Bold, GraphicsUnit.Point);
	}

	public static float Fit(Graphics graphics, string text, float size, float limit, float minSize)
	{
		while (size > minSize)
		{
			using (Font font = Ui(size))
			{
				if (graphics.MeasureString(text, font).Width <= limit)
				{
					return size;
				}
			}
			size -= 0.5f;
		}
		return size;
	}
}
