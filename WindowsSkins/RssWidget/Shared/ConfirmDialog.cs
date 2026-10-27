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
		Text = title;
		base.FormBorderStyle = FormBorderStyle.None;
		base.StartPosition = FormStartPosition.Manual;
		base.ShowInTaskbar = false;
		base.MinimizeBox = false;
		base.MaximizeBox = false;
		BackColor = PopupPanel.PanelBlack;
		ForeColor = Theme.Text;
		Font = Fonts.Ui(12f);
		base.Padding = new Padding(18, 16, 18, 14);
		base.AutoScaleMode = AutoScaleMode.None;
		DoubleBuffered = true;
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, value: true);
		int num = 360;
		titleLabel = UiTools.Label(title, 14f, Theme.Text);
		titleLabel.Font = Fonts.UiBold(14f);
		body = UiTools.Label(message, 12f, Theme.TextDim);
		body.AutoSize = false;
		body.AutoEllipsis = false;
		Button button = UiTools.Button("删除", primary: true);
		button.DialogResult = DialogResult.Yes;
		Button button2 = UiTools.Button("取消", primary: false);
		button2.DialogResult = DialogResult.No;
		buttons = new FlowLayoutPanel
		{
			FlowDirection = FlowDirection.RightToLeft,
			WrapContents = false,
			AutoSize = true,
			Dock = DockStyle.Bottom,
			Height = UiTools.ControlHeight(1f)
		};
		buttons.Controls.Add(button);
		buttons.Controls.Add(button2);
		body.Dock = DockStyle.Fill;
		titleLabel.Dock = DockStyle.Top;
		base.Controls.Add(body);
		base.Controls.Add(titleLabel);
		base.Controls.Add(buttons);
		base.AcceptButton = button;
		base.CancelButton = button2;
		base.ClientSize = new Size(num, 160);
		base.Paint += delegate(object sender, PaintEventArgs e)
		{
			if (PopupPanel.UseBlueBorder)
			{
				int radius = ((cornerRadiusApplied > 0) ? cornerRadiusApplied : UiTools.CornerRadiusFor(base.Handle));
				UiTools.DrawPanelRing(e.Graphics, base.Width, base.Height, radius, PopupPanel.PanelBlueBorder);
			}
		};
	}

	private void ApplyCornerRegion()
	{
		if (!base.IsHandleCreated)
		{
			return;
		}
		try
		{
			using GraphicsPath path = UiTools.RoundedRect(radius: cornerRadiusApplied = UiTools.CornerRadiusFor(base.Handle), bounds: new RectangleF(0f, 0f, base.Width, base.Height));
			base.Region = new Region(path);
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
		int num = Math.Max(80, width - base.Padding.Horizontal);
		try
		{
			CreateControl();
		}
		catch
		{
		}
		body.Width = num;
		int val;
		try
		{
			val = body.GetPreferredSize(new Size(num, 0)).Height;
		}
		catch
		{
			val = 60;
		}
		val = Math.Max(60, val);
		int num2 = base.Padding.Vertical + titleLabel.Height + val + buttons.Height;
		if (base.ClientSize.Width != width || base.ClientSize.Height < num2)
		{
			base.ClientSize = new Size(width, Math.Max(base.ClientSize.Height, num2));
		}
		if (body.Height + 2 < val)
		{
			base.ClientSize = new Size(width, base.ClientSize.Height + (val - body.Height));
		}
	}

	public DialogResult ShowNear(Form owner)
	{
		FitContent(base.ClientSize.Width);
		Rectangle rect = owner?.Bounds ?? new Rectangle(0, 0, 0, 0);
		if (rect.Width <= 0 || rect.Height <= 0)
		{
			rect = Screen.PrimaryScreen.WorkingArea;
		}
		Rectangle workingArea = Screen.FromRectangle(rect).WorkingArea;
		int num = rect.Left;
		if (num + base.Width > workingArea.Right)
		{
			num = workingArea.Right - base.Width;
		}
		if (num < workingArea.Left)
		{
			num = workingArea.Left;
		}
		int num2 = rect.Bottom + 10;
		if (num2 + base.Height > workingArea.Bottom)
		{
			num2 = rect.Top - base.Height - 10;
		}
		if (num2 < workingArea.Top)
		{
			num2 = workingArea.Top;
		}
		base.Location = new Point(num, num2);
		return ShowDialog(owner);
	}
}
