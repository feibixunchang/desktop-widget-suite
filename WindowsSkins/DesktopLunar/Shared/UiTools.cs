using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Forms.Layout;

namespace Shared;

internal static class UiTools
{
	public static void DrawPanelRing(Graphics g, int width, int height, int radius, Color color)
	{
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Expected O, but got Unknown
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Expected O, but got Unknown
		if (width <= 2 || height <= 2)
		{
			return;
		}
		RectangleF bounds = new RectangleF(0f, 0f, width, height);
		RectangleF bounds2 = new RectangleF(1f, 1f, (float)width - 2f, (float)height - 2f);
		GraphicsPath val = new GraphicsPath();
		try
		{
			SolidBrush val2 = new SolidBrush(color);
			try
			{
				val.AddPath(RoundedRect(bounds, radius), false);
				val.AddPath(RoundedRect(bounds2, Math.Max(1f, (float)radius - 1f)), false);
				val.FillMode = (FillMode)0;
				g.SmoothingMode = (SmoothingMode)4;
				g.FillPath((Brush)(object)val2, val);
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

	public static int CornerRadiusFor(IntPtr handle)
	{
		float num = 96f;
		if (handle != IntPtr.Zero)
		{
			try
			{
				Graphics val = Graphics.FromHwnd(handle);
				try
				{
					num = val.DpiX;
				}
				finally
				{
					((IDisposable)val)?.Dispose();
				}
			}
			catch
			{
			}
		}
		return (int)Math.Round(14f * num / 96f);
	}

	public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Expected O, but got Unknown
		float num = radius * 2f;
		GraphicsPath val = new GraphicsPath();
		if (bounds.Width <= 0f || bounds.Height <= 0f)
		{
			val.AddRectangle(bounds);
			return val;
		}
		if (num > bounds.Width)
		{
			num = bounds.Width;
		}
		if (num > bounds.Height)
		{
			num = bounds.Height;
		}
		val.AddArc(bounds.X, bounds.Y, num, num, 180f, 90f);
		val.AddArc(bounds.Right - num, bounds.Y, num, num, 270f, 90f);
		val.AddArc(bounds.Right - num, bounds.Bottom - num, num, num, 0f, 90f);
		val.AddArc(bounds.X, bounds.Bottom - num, num, num, 90f, 90f);
		val.CloseFigure();
		return val;
	}

	public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
	{
		return RoundedRect(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radius);
	}

	public static void DrawCentered(Graphics graphics, string text, Font font, RectangleF bounds, Color color)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Expected O, but got Unknown
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Expected O, but got Unknown
		SolidBrush val = new SolidBrush(color);
		try
		{
			StringFormat val2 = new StringFormat(StringFormat.GenericTypographic);
			try
			{
				val2.Alignment = (StringAlignment)1;
				val2.LineAlignment = (StringAlignment)1;
				val2.FormatFlags = (StringFormatFlags)4096;
				val2.Trimming = (StringTrimming)3;
				graphics.DrawString(text, font, (Brush)(object)val, bounds, val2);
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

	public static void DrawChipButton(Graphics graphics, RectangleF bounds, string text, Font font, Color tint, int cardAlpha, bool hover)
	{
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Expected O, but got Unknown
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Expected O, but got Unknown
		int num = Math.Max(0, Math.Min(255, cardAlpha));
		GraphicsPath val = RoundedRect(bounds, Math.Max(3f, bounds.Height * 0.28f));
		try
		{
			SolidBrush val2 = new SolidBrush(hover ? Color.FromArgb(Math.Min(160, num + 40), tint) : Color.FromArgb((int)((float)num * 0.24f), 0, 0, 0));
			try
			{
				graphics.FillPath((Brush)(object)val2, val);
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
			Pen val3 = new Pen(hover ? tint : Color.FromArgb((int)((float)num * 0.6f), tint));
			try
			{
				graphics.DrawPath(val3, val);
			}
			finally
			{
				((IDisposable)val3)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		DrawCentered(graphics, text, font, bounds, hover ? Color.White : tint);
	}

	public static void DrawLeftCentered(Graphics graphics, string text, Font font, RectangleF bounds, Color color)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Expected O, but got Unknown
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Expected O, but got Unknown
		SolidBrush val = new SolidBrush(color);
		try
		{
			StringFormat val2 = new StringFormat(StringFormat.GenericTypographic);
			try
			{
				val2.Alignment = (StringAlignment)0;
				val2.LineAlignment = (StringAlignment)1;
				val2.FormatFlags = (StringFormatFlags)20480;
				graphics.DrawString(text, font, (Brush)(object)val, bounds, val2);
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

	public static SizeF MeasureLine(Graphics graphics, string text, Font font)
	{
		return graphics.MeasureString(text, font);
	}

	public static Label Label(string text, float size, Color color)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Expected O, but got Unknown
		return new Label
		{
			Text = text,
			AutoSize = true,
			ForeColor = color,
			Font = Fonts.Ui(Math.Max(12f, size)),
			BackColor = Color.Transparent
		};
	}

	public static int LineHeight()
	{
		Font val = Fonts.Ui(12f);
		try
		{
			return TextRenderer.MeasureText("测Ag中", val).Height;
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	public static int ControlHeight(float scale)
	{
		int num = (int)Math.Round(10f * scale);
		return Math.Max((int)Math.Round(28f * scale), LineHeight() + num);
	}

	public static int ButtonWidth(Graphics graphics, string text, float scale, float minimum)
	{
		Font val = Fonts.Ui(12f);
		try
		{
			float width = graphics.MeasureString(text ?? string.Empty, val).Width;
			return Math.Max((int)Math.Round(minimum * scale), (int)Math.Round(width + 16f * scale));
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	public static int FlowRow(FlowLayoutPanel row, int available, int gap, int itemHeight)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		row.FlowDirection = (FlowDirection)0;
		row.WrapContents = true;
		((Control)row).Width = Math.Max(40, available);
		foreach (Control item in (ArrangedElementCollection)((Control)row).Controls)
		{
			item.Margin = new Padding(0, 0, gap, gap);
		}
		((Control)row).Height = Math.Max(itemHeight + gap, ((Control)row).GetPreferredSize(new Size(((Control)row).Width, 0)).Height);
		return ((Control)row).Height;
	}

	public static Button Button(string text, bool primary)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Expected O, but got Unknown
		Button val = new Button
		{
			Text = text,
			FlatStyle = (FlatStyle)0,
			BackColor = PopupPanel.PanelBlack,
			ForeColor = Theme.Text,
			Font = Fonts.Ui(12f),
			Cursor = Cursors.Hand,
			AutoSize = false,
			Height = ControlHeight(1f),
			UseVisualStyleBackColor = false
		};
		((ButtonBase)val).FlatAppearance.BorderSize = 0;
		((ButtonBase)val).FlatAppearance.MouseOverBackColor = (primary ? Color.FromArgb(52, 104, 150) : PopupPanel.PanelBlackHover);
		return val;
	}

	public static TextBox TextBox(bool multiLine)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Expected O, but got Unknown
		return new TextBox
		{
			BackColor = PopupPanel.PanelBlack,
			ForeColor = Theme.Text,
			BorderStyle = (BorderStyle)1,
			Font = Fonts.Ui(12f),
			Multiline = multiLine
		};
	}

	public static ComboBox ComboBox()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Expected O, but got Unknown
		return new ComboBox
		{
			DropDownStyle = (ComboBoxStyle)2,
			FlatStyle = (FlatStyle)0,
			BackColor = PopupPanel.PanelBlack,
			ForeColor = Theme.Text,
			Font = Fonts.Ui(12f)
		};
	}

	public static ListBox ListBox()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Expected O, but got Unknown
		return new ListBox
		{
			BackColor = PopupPanel.PanelBlack,
			ForeColor = Theme.Text,
			BorderStyle = (BorderStyle)0,
			Font = Fonts.Ui(12f),
			IntegralHeight = false
		};
	}

	public static CheckBox Check(string text, bool value)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected O, but got Unknown
		return new CheckBox
		{
			Text = text,
			Checked = value,
			ForeColor = Theme.TextDim,
			Font = Fonts.Ui(12f),
			BackColor = Color.Transparent,
			FlatStyle = (FlatStyle)0
		};
	}

	public static DialogResult Confirm(Form owner, string title, string message)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		ConfirmDialog confirmDialog = new ConfirmDialog(title, message);
		try
		{
			return confirmDialog.ShowNear(owner);
		}
		finally
		{
			((IDisposable)(object)confirmDialog)?.Dispose();
		}
	}
}
