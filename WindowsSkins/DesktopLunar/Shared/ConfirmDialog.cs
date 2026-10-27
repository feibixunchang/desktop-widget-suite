using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Shared;

internal sealed class ConfirmDialog : Form
{
	private readonly Label body;

	private readonly Label titleLabel;

	private readonly FlowLayoutPanel buttons;

	private int cornerRadiusApplied;

	public ConfirmDialog(string title, string message)
	{
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0109: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0123: Unknown result type (might be due to invalid IL or missing references)
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Expected O, but got Unknown
		//IL_01d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dd: Expected O, but got Unknown
		((Control)this).Text = title;
		((Form)this).FormBorderStyle = (FormBorderStyle)0;
		((Form)this).StartPosition = (FormStartPosition)0;
		((Form)this).ShowInTaskbar = false;
		((Form)this).MinimizeBox = false;
		((Form)this).MaximizeBox = false;
		((Control)this).BackColor = PopupPanel.PanelBlack;
		((Control)this).ForeColor = Theme.Text;
		((Control)this).Font = Fonts.Ui(12f);
		((Control)this).Padding = new Padding(18, 16, 18, 14);
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)0;
		DoubleBuffered = true;
		SetStyle((ControlStyles)8194, true);
		int width = 360;
		titleLabel = UiTools.Label(title, 14f, Theme.Text);
		((Control)titleLabel).Font = Fonts.UiBold(14f);
		body = UiTools.Label(message, 12f, Theme.TextDim);
		((Control)body).AutoSize = false;
		body.AutoEllipsis = false;
		Button val = UiTools.Button("删除", primary: true);
		val.DialogResult = (DialogResult)6;
		Button val2 = UiTools.Button("取消", primary: false);
		val2.DialogResult = (DialogResult)7;
		buttons = new FlowLayoutPanel
		{
			FlowDirection = (FlowDirection)2,
			WrapContents = false,
			AutoSize = true,
			Dock = (DockStyle)2,
			Height = UiTools.ControlHeight(1f)
		};
		((Control)buttons).Controls.Add((Control)(object)val);
		((Control)buttons).Controls.Add((Control)(object)val2);
		((Control)body).Dock = (DockStyle)5;
		((Control)titleLabel).Dock = (DockStyle)1;
		((Control)this).Controls.Add((Control)(object)body);
		((Control)this).Controls.Add((Control)(object)titleLabel);
		((Control)this).Controls.Add((Control)(object)buttons);
		((Form)this).AcceptButton = (IButtonControl)(object)val;
		((Form)this).CancelButton = (IButtonControl)(object)val2;
		((Form)this).ClientSize = new Size(width, 160);
		((Control)this).Paint += (PaintEventHandler)delegate(object sender, PaintEventArgs e)
		{
			if (PopupPanel.UseBlueBorder)
			{
				int radius = ((cornerRadiusApplied > 0) ? cornerRadiusApplied : UiTools.CornerRadiusFor(((Control)this).Handle));
				UiTools.DrawPanelRing(e.Graphics, ((Control)this).Width, ((Control)this).Height, radius, PopupPanel.PanelBlueBorder);
			}
		};
	}

	private void ApplyCornerRegion()
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Expected O, but got Unknown
		if (!((Control)this).IsHandleCreated)
		{
			return;
		}
		try
		{
			GraphicsPath val = UiTools.RoundedRect(radius: cornerRadiusApplied = UiTools.CornerRadiusFor(((Control)this).Handle), bounds: new RectangleF(0f, 0f, ((Control)this).Width, ((Control)this).Height));
			try
			{
				((Control)this).Region = new Region(val);
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

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		ApplyCornerRegion();
	}

	private void FitContent(int width)
	{
		//IL_0004: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		Padding padding = ((Control)this).Padding;
		int width2 = Math.Max(80, width - padding.Horizontal);
		try
		{
			((Control)this).CreateControl();
		}
		catch
		{
		}
		((Control)body).Width = width2;
		int val;
		try
		{
			val = ((Control)body).GetPreferredSize(new Size(width2, 0)).Height;
		}
		catch
		{
			val = 60;
		}
		val = Math.Max(60, val);
		padding = ((Control)this).Padding;
		int num = padding.Vertical + ((Control)titleLabel).Height + val + ((Control)buttons).Height;
		if (((Form)this).ClientSize.Width != width || ((Form)this).ClientSize.Height < num)
		{
			((Form)this).ClientSize = new Size(width, Math.Max(((Form)this).ClientSize.Height, num));
		}
		if (((Control)body).Height + 2 < val)
		{
			((Form)this).ClientSize = new Size(width, ((Form)this).ClientSize.Height + (val - ((Control)body).Height));
		}
	}

	public DialogResult ShowNear(Form owner)
	{
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		FitContent(((Form)this).ClientSize.Width);
		Rectangle rectangle = ((owner == null) ? new Rectangle(0, 0, 0, 0) : ((Control)owner).Bounds);
		if (rectangle.Width <= 0 || rectangle.Height <= 0)
		{
			rectangle = Screen.PrimaryScreen.WorkingArea;
		}
		Rectangle workingArea = Screen.FromRectangle(rectangle).WorkingArea;
		int num = rectangle.Left;
		if (num + ((Control)this).Width > workingArea.Right)
		{
			num = workingArea.Right - ((Control)this).Width;
		}
		if (num < workingArea.Left)
		{
			num = workingArea.Left;
		}
		int num2 = rectangle.Bottom + 10;
		if (num2 + ((Control)this).Height > workingArea.Bottom)
		{
			num2 = rectangle.Top - ((Control)this).Height - 10;
		}
		if (num2 < workingArea.Top)
		{
			num2 = workingArea.Top;
		}
		((Form)this).Location = new Point(num, num2);
		return ((Form)this).ShowDialog((IWin32Window)(object)owner);
	}
}
