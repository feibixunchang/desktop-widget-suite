using System;
using System.Drawing;

namespace Shared;

internal static class Theme
{
	public static readonly Color Key = Color.FromArgb(1, 2, 3);

	public static readonly Color Text = Color.FromArgb(235, 240, 245);

	public static readonly Color TextDim = Color.FromArgb(150, 158, 168);

	public static readonly Color TextFaint = Color.FromArgb(110, 118, 128);

	public static readonly Color Panel = Color.FromArgb(30, 34, 40);

	public static readonly Color PanelSoft = Color.FromArgb(40, 45, 52);

	public static readonly Color Line = Color.FromArgb(70, 78, 88);

	public static readonly Color Accent = Color.FromArgb(80, 170, 255);

	public static readonly Color AccentSoft = Color.FromArgb(38, 78, 112);

	public static readonly Color Weekend = Color.FromArgb(255, 150, 130);

	public static readonly Color Holiday = Color.FromArgb(255, 110, 110);

	public static readonly Color Work = Color.FromArgb(120, 200, 255);

	public static readonly Color Lunar = Color.FromArgb(168, 176, 188);

	public static readonly Color Term = Color.FromArgb(120, 220, 170);

	public static readonly Color Today = Color.FromArgb(46, 108, 168);

	public static readonly Color WarmYellow = Color.FromArgb(255, 205, 90);

	public static readonly Color SkyBlue = Color.FromArgb(120, 190, 255);

	public static readonly Color Income = Color.FromArgb(255, 205, 90);

	public static readonly Color Expense = Color.FromArgb(255, 140, 120);

	public static readonly Color Balance = Color.FromArgb(120, 190, 255);

	public static readonly Color Note = Color.FromArgb(255, 210, 90);

	public static readonly string[] NoteColors = new string[8] { "#FFD25A", "#7FB2FF", "#7CDCA0", "#FF9A8B", "#C79BFF", "#67D8D8", "#FFB3D9", "#E8E8E8" };

	public static Color FromHex(string hex)
	{
		try
		{
			if (string.IsNullOrEmpty(hex))
			{
				return Note;
			}
			string text = hex.TrimStart('#');
			if (text.Length == 3)
			{
				text = new string(new char[6]
				{
					text[0],
					text[0],
					text[1],
					text[1],
					text[2],
					text[2]
				});
			}
			if (text.Length != 6)
			{
				return Note;
			}
			int red = Convert.ToInt32(text.Substring(0, 2), 16);
			int green = Convert.ToInt32(text.Substring(2, 2), 16);
			int blue = Convert.ToInt32(text.Substring(4, 2), 16);
			return Color.FromArgb(red, green, blue);
		}
		catch
		{
			return Note;
		}
	}

	public static string ToHex(Color color)
	{
		return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
	}
}
