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
			foreach (FontFamily val in families)
			{
				if (CanRenderCjk(val.Name))
				{
					familyName = val.Name;
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
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Expected O, but got Unknown
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Expected O, but got Unknown
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Expected O, but got Unknown
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Expected O, but got Unknown
		try
		{
			FontFamily val = new FontFamily(familyName);
			try
			{
				Font val2 = new Font(val, 18f, (FontStyle)0, (GraphicsUnit)3);
				try
				{
					Bitmap val3 = new Bitmap(32, 32, (PixelFormat)2498570);
					try
					{
						Graphics val4 = Graphics.FromImage((Image)(object)val3);
						try
						{
							StringFormat val5 = new StringFormat(StringFormat.GenericTypographic);
							try
							{
								val4.Clear(Color.Transparent);
								val4.TextRenderingHint = (TextRenderingHint)4;
								val4.DrawString("中", val2, Brushes.White, 1f, 1f, val5);
								for (int i = 0; i < 32; i++)
								{
									for (int j = 0; j < 32; j++)
									{
										if (val3.GetPixel(j, i).A > 0)
										{
											return true;
										}
									}
								}
							}
							finally
							{
								((IDisposable)val5)?.Dispose();
							}
						}
						finally
						{
							((IDisposable)val4)?.Dispose();
						}
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
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		catch
		{
		}
		return false;
	}

	public static Font Widget(float size)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Expected O, but got Unknown
		return new Font(FamilyName, size, (FontStyle)0, (GraphicsUnit)3);
	}

	public static Font WidgetBold(float size)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Expected O, but got Unknown
		return new Font(FamilyName, size, (FontStyle)1, (GraphicsUnit)3);
	}

	public static Font Ui(float size)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Expected O, but got Unknown
		return new Font(FamilyName, size, (FontStyle)0, (GraphicsUnit)3);
	}

	public static Font UiBold(float size)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Expected O, but got Unknown
		return new Font(FamilyName, size, (FontStyle)1, (GraphicsUnit)3);
	}

	public static float Fit(Graphics graphics, string text, float size, float limit, float minSize)
	{
		while (size > minSize)
		{
			Font val = Ui(size);
			try
			{
				if (graphics.MeasureString(text, val).Width <= limit)
				{
					return size;
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			size -= 0.5f;
		}
		return size;
	}
}
