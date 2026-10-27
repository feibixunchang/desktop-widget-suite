using System;
using System.Drawing;
using System.Windows.Forms;

namespace Shared;

internal sealed class HintForm : Form
{
	private static HintForm showing;

	private readonly Timer life;

	private readonly Timer fade;

	private readonly string message;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ExStyle |= 0x8000080;
			return createParams;
		}
	}

	public HintForm(string message)
	{
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Expected O, but got Unknown
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Expected O, but got Unknown
		//IL_012c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0131: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Expected O, but got Unknown
		this.message = message ?? string.Empty;
		((Form)this).FormBorderStyle = (FormBorderStyle)0;
		((Form)this).ShowInTaskbar = false;
		((Form)this).StartPosition = (FormStartPosition)0;
		((Form)this).TopMost = true;
		((Control)this).BackColor = Color.FromArgb(30, 32, 36);
		((Control)this).ForeColor = Color.White;
		((Control)this).Font = Fonts.Ui(11f);
		((Control)this).Padding = new Padding(16, 9, 16, 9);
		((Form)this).Opacity = 0.92;
		Bitmap val = new Bitmap(1, 1);
		try
		{
			Graphics val2 = Graphics.FromImage((Image)(object)val);
			try
			{
				Size size = TextRenderer.MeasureText((IDeviceContext)(object)val2, message, ((Control)this).Font, new Size(2400, 300), (TextFormatFlags)268437536);
				int width = size.Width;
				Padding padding = ((Control)this).Padding;
				int width2 = width + padding.Horizontal;
				int height = size.Height;
				padding = ((Control)this).Padding;
				((Form)this).ClientSize = new Size(width2, height + padding.Vertical);
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
		life = new Timer
		{
			Interval = 1400
		};
		life.Tick += delegate
		{
			life.Stop();
			fade.Start();
		};
		fade = new Timer
		{
			Interval = 24
		};
		fade.Tick += delegate
		{
			((Form)this).Opacity = ((Form)this).Opacity - 0.07;
			if (((Form)this).Opacity <= 0.08)
			{
				CloseMe();
			}
		};
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		life.Start();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		Padding padding = ((Control)this).Padding;
		int left = padding.Left;
		padding = ((Control)this).Padding;
		int top = padding.Top;
		int width = ((Form)this).ClientSize.Width;
		padding = ((Control)this).Padding;
		int width2 = width - padding.Horizontal;
		int height = ((Form)this).ClientSize.Height;
		padding = ((Control)this).Padding;
		Rectangle rectangle = new Rectangle(left, top, width2, height - padding.Vertical);
		TextRenderer.DrawText((IDeviceContext)(object)e.Graphics, message, ((Control)this).Font, rectangle, Color.White, (TextFormatFlags)2085);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		CloseMe();
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		CloseMe();
	}

	public void ShowNear(Rectangle anchor, Rectangle workArea)
	{
		int val = anchor.Left + (anchor.Width - ((Control)this).Width) / 2;
		int num = anchor.Top - ((Control)this).Height - 10;
		if (num < workArea.Top)
		{
			num = anchor.Bottom + 10;
		}
		val = Math.Max(workArea.Left, Math.Min(val, Math.Max(workArea.Left, workArea.Right - ((Control)this).Width)));
		num = Math.Max(workArea.Top, Math.Min(num, Math.Max(workArea.Top, workArea.Bottom - ((Control)this).Height)));
		((Form)this).Location = new Point(val, num);
		DismissCurrent();
		showing = this;
		((Control)this).Show();
	}

	private void CloseMe()
	{
		try
		{
			((Form)this).Close();
		}
		catch
		{
		}
	}

	public static void DismissCurrent()
	{
		try
		{
			if (showing != null && !((Control)showing).IsDisposed)
			{
				((Form)showing).Close();
			}
		}
		catch
		{
		}
		showing = null;
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		if (showing == this)
		{
			showing = null;
		}
		base.OnFormClosed(e);
	}
}
