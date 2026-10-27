using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace WidgetHub;

public class ModernButton : Button
{
	public Color NormalColor = Color.FromArgb(59, 130, 246);

	public Color HoverColor = Color.FromArgb(37, 99, 235);

	public int CornerRadius = 8;

	private bool hovered;

	public ModernButton()
	{
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Expected O, but got Unknown
		base.SetStyle((ControlStyles)139282, true);
		((ButtonBase)this).FlatStyle = (FlatStyle)0;
		((ButtonBase)this).FlatAppearance.BorderSize = 0;
		((Control)this).BackColor = Color.White;
		((Control)this).ForeColor = Color.White;
		((Control)this).Font = new Font("Microsoft YaHei", 10f);
		((Control)this).Cursor = Cursors.Hand;
	}

	internal static GraphicsPath RoundPath(Rectangle r, int radius)
	{
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_014c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0244: Unknown result type (might be due to invalid IL or missing references)
		//IL_024b: Expected O, but got Unknown
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected O, but got Unknown
		float num = Math.Max(0f, Math.Min(radius, (float)Math.Min(r.Width, r.Height) / 2f));
		if (num <= 0.5f)
		{
			GraphicsPath val = new GraphicsPath();
			val.AddRectangle(r);
			return val;
		}
		float num2 = num * 0.5523f;
		GraphicsPath val2 = new GraphicsPath();
		val2.StartFigure();
		val2.AddLine((float)r.Left + num, (float)r.Top, (float)r.Right - num, (float)r.Top);
		val2.AddBezier((float)r.Right - num, (float)r.Top, (float)r.Right - num + num2, (float)r.Top, (float)r.Right, (float)r.Top + num - num2, (float)r.Right, (float)r.Top + num);
		val2.AddLine((float)r.Right, (float)r.Top + num, (float)r.Right, (float)r.Bottom - num);
		val2.AddBezier((float)r.Right, (float)r.Bottom - num, (float)r.Right, (float)r.Bottom - num + num2, (float)r.Right - num + num2, (float)r.Bottom, (float)r.Right - num, (float)r.Bottom);
		val2.AddLine((float)r.Right - num, (float)r.Bottom, (float)r.Left + num, (float)r.Bottom);
		val2.AddBezier((float)r.Left + num, (float)r.Bottom, (float)r.Left + num - num2, (float)r.Bottom, (float)r.Left, (float)r.Bottom - num + num2, (float)r.Left, (float)r.Bottom - num);
		val2.AddLine((float)r.Left, (float)r.Bottom - num, (float)r.Left, (float)r.Top + num);
		val2.AddBezier((float)r.Left, (float)r.Top + num, (float)r.Left, (float)r.Top + num - num2, (float)r.Left + num - num2, (float)r.Top, (float)r.Left + num, (float)r.Top);
		val2.CloseFigure();
		return val2;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Expected O, but got Unknown
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Expected O, but got Unknown
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = (SmoothingMode)4;
		graphics.TextRenderingHint = (TextRenderingHint)5;
		SolidBrush val = new SolidBrush(((Control)this).BackColor);
		try
		{
			graphics.FillRectangle((Brush)(object)val, ((Control)this).ClientRectangle);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		Rectangle r = new Rectangle(0, 0, ((Control)this).Width - 1, ((Control)this).Height - 1);
		if (r.Width > 0 && r.Height > 0)
		{
			GraphicsPath val2 = RoundPath(r, CornerRadius);
			try
			{
				SolidBrush val3 = new SolidBrush((!((Control)this).Enabled) ? Color.FromArgb(203, 213, 225) : (hovered ? HoverColor : NormalColor));
				try
				{
					graphics.FillPath((Brush)(object)val3, val2);
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
		TextRenderer.DrawText((IDeviceContext)(object)graphics, ((Control)this).Text, ((Control)this).Font, ((Control)this).ClientRectangle, ((Control)this).Enabled ? ((Control)this).ForeColor : Color.FromArgb(248, 250, 252), (TextFormatFlags)268435493);
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		hovered = true;
		((Control)this).Invalidate();
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		hovered = false;
		((Control)this).Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnTextChanged(EventArgs e)
	{
		((Control)this).Invalidate();
		base.OnTextChanged(e);
	}
}
