using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Shared;

internal static class UiTools
{
	public static void DrawPanelRing(Graphics g, int width, int height, int radius, Color color)
	{
		if (width <= 2 || height <= 2)
		{
			return;
		}
		RectangleF bounds = new RectangleF(0f, 0f, width, height);
		RectangleF bounds2 = new RectangleF(1f, 1f, (float)width - 2f, (float)height - 2f);
		using GraphicsPath graphicsPath = new GraphicsPath();
		using SolidBrush brush = new SolidBrush(color);
		graphicsPath.AddPath(RoundedRect(bounds, radius), connect: false);
		graphicsPath.AddPath(RoundedRect(bounds2, Math.Max(1f, (float)radius - 1f)), connect: false);
		graphicsPath.FillMode = FillMode.Alternate;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.FillPath(brush, graphicsPath);
	}

	public static int CornerRadiusFor(IntPtr handle)
	{
		float num = 96f;
		if (handle != IntPtr.Zero)
		{
			try
			{
				using Graphics graphics = Graphics.FromHwnd(handle);
				num = graphics.DpiX;
			}
			catch
			{
			}
		}
		return (int)Math.Round(14f * num / 96f);
	}

	public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
	{
		float num = radius * 2f;
		GraphicsPath graphicsPath = new GraphicsPath();
		if (bounds.Width <= 0f || bounds.Height <= 0f)
		{
			graphicsPath.AddRectangle(bounds);
			return graphicsPath;
		}
		if (num > bounds.Width)
		{
			num = bounds.Width;
		}
		if (num > bounds.Height)
		{
			num = bounds.Height;
		}
		graphicsPath.AddArc(bounds.X, bounds.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(bounds.X, bounds.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
	{
		return RoundedRect(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radius);
	}

	public static void DrawCentered(Graphics graphics, string text, Font font, RectangleF bounds, Color color)
	{
		using SolidBrush brush = new SolidBrush(color);
		using StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic);
		stringFormat.Alignment = StringAlignment.Center;
		stringFormat.LineAlignment = StringAlignment.Center;
		stringFormat.FormatFlags = StringFormatFlags.NoWrap;
		stringFormat.Trimming = StringTrimming.EllipsisCharacter;
		graphics.DrawString(text, font, brush, bounds, stringFormat);
	}

	public static void DrawChipButton(Graphics graphics, RectangleF bounds, string text, Font font, Color tint, int cardAlpha, bool hover)
	{
		int num = Math.Max(0, Math.Min(255, cardAlpha));
		using (GraphicsPath path = RoundedRect(bounds, Math.Max(3f, bounds.Height * 0.28f)))
		{
			using (SolidBrush brush = new SolidBrush(hover ? Color.FromArgb(Math.Min(160, num + 40), tint) : Color.FromArgb((int)((float)num * 0.24f), 0, 0, 0)))
			{
				graphics.FillPath(brush, path);
			}
			using Pen pen = new Pen(hover ? tint : Color.FromArgb((int)((float)num * 0.6f), tint));
			graphics.DrawPath(pen, path);
		}
		DrawCentered(graphics, text, font, bounds, hover ? Color.White : tint);
	}

	public static void DrawLeftCentered(Graphics graphics, string text, Font font, RectangleF bounds, Color color)
	{
		using SolidBrush brush = new SolidBrush(color);
		using StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic);
		stringFormat.Alignment = StringAlignment.Near;
		stringFormat.LineAlignment = StringAlignment.Center;
		stringFormat.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
		graphics.DrawString(text, font, brush, bounds, stringFormat);
	}

	public static SizeF MeasureLine(Graphics graphics, string text, Font font)
	{
		return graphics.MeasureString(text, font);
	}

	public static Label Label(string text, float size, Color color)
	{
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
		using Font font = Fonts.Ui(12f);
		return TextRenderer.MeasureText("测Ag中", font).Height;
	}

	public static int ControlHeight(float scale)
	{
		int num = (int)Math.Round(10f * scale);
		return Math.Max((int)Math.Round(28f * scale), LineHeight() + num);
	}

	public static int ButtonWidth(Graphics graphics, string text, float scale, float minimum)
	{
		using Font font = Fonts.Ui(12f);
		float width = graphics.MeasureString(text ?? string.Empty, font).Width;
		return Math.Max((int)Math.Round(minimum * scale), (int)Math.Round(width + 16f * scale));
	}

	public static int FlowRow(FlowLayoutPanel row, int available, int gap, int itemHeight)
	{
		row.FlowDirection = FlowDirection.LeftToRight;
		row.WrapContents = true;
		row.Width = Math.Max(40, available);
		foreach (Control control in row.Controls)
		{
			control.Margin = new Padding(0, 0, gap, gap);
		}
		row.Height = Math.Max(itemHeight + gap, row.GetPreferredSize(new Size(row.Width, 0)).Height);
		return row.Height;
	}

	public static Button Button(string text, bool primary)
	{
		Button button = new Button();
		button.Text = text;
		button.FlatStyle = FlatStyle.Flat;
		button.BackColor = PopupPanel.PanelBlack;
		button.ForeColor = Theme.Text;
		button.Font = Fonts.Ui(12f);
		button.Cursor = Cursors.Hand;
		button.AutoSize = false;
		button.Height = ControlHeight(1f);
		button.UseVisualStyleBackColor = false;
		button.FlatAppearance.BorderSize = 0;
		button.FlatAppearance.MouseOverBackColor = (primary ? Color.FromArgb(52, 104, 150) : PopupPanel.PanelBlackHover);
		return button;
	}

	public static TextBox TextBox(bool multiLine)
	{
		return new TextBox
		{
			BackColor = PopupPanel.PanelBlack,
			ForeColor = Theme.Text,
			BorderStyle = BorderStyle.FixedSingle,
			Font = Fonts.Ui(12f),
			Multiline = multiLine
		};
	}

	public static ComboBox ComboBox()
	{
		return new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			FlatStyle = FlatStyle.Flat,
			BackColor = PopupPanel.PanelBlack,
			ForeColor = Theme.Text,
			Font = Fonts.Ui(12f)
		};
	}

	public static ListBox ListBox()
	{
		return new ListBox
		{
			BackColor = PopupPanel.PanelBlack,
			ForeColor = Theme.Text,
			BorderStyle = BorderStyle.None,
			Font = Fonts.Ui(12f),
			IntegralHeight = false
		};
	}

	public static CheckBox Check(string text, bool value)
	{
		return new CheckBox
		{
			Text = text,
			Checked = value,
			ForeColor = Theme.TextDim,
			Font = Fonts.Ui(12f),
			BackColor = Color.Transparent,
			FlatStyle = FlatStyle.Flat
		};
	}

	public static DialogResult Confirm(Form owner, string title, string message)
	{
		using ConfirmDialog confirmDialog = new ConfirmDialog(title, message);
		return confirmDialog.ShowNear(owner);
	}
}
