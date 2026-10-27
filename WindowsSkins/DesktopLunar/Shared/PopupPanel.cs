using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Layout;

namespace Shared;

internal class PopupPanel : Form
{
	public static readonly Color PanelBlack = Color.FromArgb(0, 0, 0);

	public static readonly Color PanelBlackLine = Color.FromArgb(42, 42, 42);

	public static readonly Color PanelBlackHover = Color.FromArgb(38, 38, 38);

	public static readonly Color PanelBlueBorder = Color.FromArgb(80, 170, 255);

	internal static bool UseBlueBorder;

	private int cornerRadiusApplied;

	private static readonly List<PopupPanel> Open = new List<PopupPanel>();

	private static readonly Timer Watcher = new Timer();

	private static bool watcherRunning;

	private static bool buttonWasDown;

	private readonly Panel body = new Panel();

	private readonly Panel header = new Panel();

	private readonly Label titleLabel = new Label();

	private int preferredWidth = 340;

	private int preferredHeight = 430;

	private string title = string.Empty;

	private const int MinimumUsableHeight = 260;

	private bool openedAbove;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ExStyle |= 0x80;
			return createParams;
		}
	}

	public Panel Body => body;

	public Panel Header => header;

	public string PanelTitle
	{
		get
		{
			return title;
		}
		set
		{
			title = value ?? string.Empty;
			((Control)titleLabel).Text = title;
		}
	}

	public int PreferredWidth
	{
		get
		{
			return preferredWidth;
		}
		set
		{
			preferredWidth = value;
		}
	}

	public int PreferredHeight
	{
		get
		{
			return preferredHeight;
		}
		set
		{
			preferredHeight = value;
		}
	}

	protected int ContentHeight { get; set; }

	public bool ExternalDialogOpen { get; set; }

	private Rectangle AnchorBounds { get; set; }

	protected int HeaderHeight => ((Control)header).Height;

	public event EventHandler Closed;

	public PopupPanel()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Expected O, but got Unknown
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Expected O, but got Unknown
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Expected O, but got Unknown
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Expected O, but got Unknown
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e0: Expected O, but got Unknown
		((Form)this).FormBorderStyle = (FormBorderStyle)0;
		((Form)this).ShowInTaskbar = false;
		((Form)this).StartPosition = (FormStartPosition)0;
		((Control)this).BackColor = PanelBlack;
		DoubleBuffered = true;
		SetStyle((ControlStyles)139266, true);
		((Form)this).KeyPreview = true;
		((Control)this).MinimumSize = new Size(240, 160);
		((Control)header).Dock = (DockStyle)1;
		((Control)header).Height = 46;
		((Control)this).Padding = (UseBlueBorder ? new Padding(2) : new Padding(0));
		((Control)header).BackColor = PanelBlack;
		((Control)header).Paint += (PaintEventHandler)delegate(object sender, PaintEventArgs e)
		{
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Expected O, but got Unknown
			Pen val7 = new Pen(PanelBlackLine);
			try
			{
				e.Graphics.DrawLine(val7, 0, ((Control)header).Height - 1, ((Control)header).Width, ((Control)header).Height - 1);
			}
			finally
			{
				((IDisposable)val7)?.Dispose();
			}
		};
		((Control)titleLabel).Dock = (DockStyle)5;
		titleLabel.TextAlign = (ContentAlignment)16;
		((Control)titleLabel).ForeColor = Theme.Text;
		((Control)titleLabel).Font = Fonts.UiBold(13.5f);
		((Control)titleLabel).Padding = new Padding(12, 0, 0, 0);
		((Control)titleLabel).Text = title;
		((Control)header).Controls.Add((Control)(object)titleLabel);
		((Control)body).Dock = (DockStyle)5;
		((ScrollableControl)body).AutoScroll = false;
		((Control)body).BackColor = PanelBlack;
		((Control)body).Padding = new Padding(12, 10, 12, 10);
		((Control)this).Controls.Add((Control)(object)body);
		((Control)this).Controls.Add((Control)(object)header);
		((Control)this).Paint += (PaintEventHandler)delegate(object sender, PaintEventArgs e)
		{
			//IL_0085: Unknown result type (might be due to invalid IL or missing references)
			//IL_008c: Expected O, but got Unknown
			//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cc: Expected O, but got Unknown
			//IL_014a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0151: Expected O, but got Unknown
			//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d8: Expected O, but got Unknown
			RectangleF bounds = new RectangleF(0f, 0f, ((Control)this).Width, ((Control)this).Height);
			RectangleF bounds2 = new RectangleF(1f, 1f, (float)((Control)this).Width - 2f, (float)((Control)this).Height - 2f);
			float num = ((cornerRadiusApplied > 0) ? cornerRadiusApplied : CornerRadius());
			if (!UseBlueBorder)
			{
				_ = PanelBlackLine;
			}
			else
			{
				_ = PanelBlueBorder;
			}
			GraphicsPath val = UiTools.RoundedRect(bounds, num);
			try
			{
				SolidBrush val2 = new SolidBrush(PanelBlack);
				try
				{
					e.Graphics.SmoothingMode = (SmoothingMode)4;
					e.Graphics.FillPath((Brush)(object)val2, val);
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
			if (UseBlueBorder)
			{
				GraphicsPath val3 = new GraphicsPath();
				try
				{
					SolidBrush val4 = new SolidBrush(PanelBlueBorder);
					try
					{
						val3.AddPath(UiTools.RoundedRect(bounds, num), false);
						val3.AddPath(UiTools.RoundedRect(bounds2, Math.Max(1f, num - 1f)), false);
						val3.FillMode = (FillMode)0;
						e.Graphics.FillPath((Brush)(object)val4, val3);
						return;
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
			GraphicsPath val5 = UiTools.RoundedRect(bounds, num);
			try
			{
				Pen val6 = new Pen(PanelBlackLine, 1f);
				try
				{
					e.Graphics.SmoothingMode = (SmoothingMode)4;
					e.Graphics.DrawPath(val6, val5);
				}
				finally
				{
					((IDisposable)val6)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)val5)?.Dispose();
			}
		};
	}

	private void ClipOffBorder(Control root, Region inner)
	{
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Expected O, but got Unknown
		List<Control> list = new List<Control>();
		Collect(root, list);
		foreach (Control item in list)
		{
			int num = 0;
			int num2 = 0;
			Control val = item;
			while (val != null && val != this)
			{
				num += val.Left;
				num2 += val.Top;
				val = val.Parent;
			}
			GraphicsPath val2 = UiTools.RoundedRect(new RectangleF(1 - num, 1 - num2, ((Control)this).Width - 2, ((Control)this).Height - 2), Math.Max(1, cornerRadiusApplied - 1));
			try
			{
				item.Region = new Region(val2);
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
	}

	private static void Collect(Control c, List<Control> acc)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Expected O, but got Unknown
		acc.Add(c);
		foreach (Control item in (ArrangedElementCollection)c.Controls)
		{
			Collect(item, acc);
		}
	}

	private static Region ClippedTo(Region inner, Rectangle bounds)
	{
		Region val = inner.Clone();
		if (val != null)
		{
			val.Intersect(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height));
		}
		return val;
	}

	private int CornerRadius()
	{
		float num = 96f;
		if (((Control)this).IsHandleCreated)
		{
			try
			{
				Graphics val = Graphics.FromHwnd(((Control)this).Handle);
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

	private void ApplyCornerRegion()
	{
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Expected O, but got Unknown
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Expected O, but got Unknown
		if (!((Control)this).IsHandleCreated)
		{
			return;
		}
		try
		{
			int num = (cornerRadiusApplied = CornerRadius());
			GraphicsPath val = UiTools.RoundedRect(new RectangleF(0f, 0f, ((Control)this).Width, ((Control)this).Height), num);
			try
			{
				((Control)this).Region = new Region(val);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			if (UseBlueBorder)
			{
				GraphicsPath val2 = UiTools.RoundedRect(new RectangleF(1f, 1f, (float)((Control)this).Width - 2f, (float)((Control)this).Height - 2f), Math.Max(1, num - 1));
				try
				{
					Region val3 = new Region(val2);
					try
					{
						ClipOffBorder((Control)(object)header, val3);
						ClipOffBorder((Control)(object)body, val3);
						return;
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
			((Control)header).Region = null;
			((Control)body).Region = null;
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

	public void SetContentSize(int width, int height)
	{
		preferredWidth = width;
		preferredHeight = height;
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Invalid comparison between Unknown and I4
		if (msg.Msg == 256 && (int)keyData == 27)
		{
			ClosePopup();
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	public void ShowAboveOrBelow(Rectangle anchorScreenRect, int width, int height)
	{
		preferredWidth = width;
		preferredHeight = height;
		AnchorBounds = anchorScreenRect;
		Rectangle workingArea = Screen.FromRectangle(anchorScreenRect).WorkingArea;
		if (workingArea.Width <= 0 || workingArea.Height <= 0)
		{
			workingArea = Screen.PrimaryScreen.WorkingArea;
		}
		int num = Math.Max(240, Math.Min(width, workingArea.Width - 20));
		int num2 = anchorScreenRect.Top - 10 - workingArea.Top;
		int num3 = workingArea.Bottom - 10 - anchorScreenRect.Bottom;
		int num4 = Math.Max(num2, num3);
		if (num4 < 260)
		{
			num4 = Math.Max(260, workingArea.Height - 20);
		}
		int num5 = Math.Max(140, Math.Min(height, num4));
		int num6 = anchorScreenRect.Left + (anchorScreenRect.Width - num) / 2;
		if (num6 + num > workingArea.Right - 10)
		{
			num6 = workingArea.Right - 10 - num;
		}
		if (num6 < workingArea.Left + 10)
		{
			num6 = workingArea.Left + 10;
		}
		openedAbove = num2 >= 260 && num2 >= num5;
		int num7;
		if (openedAbove)
		{
			num7 = anchorScreenRect.Top - num5 - 10;
		}
		else if (num3 >= num5)
		{
			num7 = anchorScreenRect.Bottom + 10;
		}
		else if (num2 >= num3)
		{
			num5 = Math.Max(140, Math.Min(height, num2));
			num7 = Math.Max(workingArea.Top + 10, anchorScreenRect.Top - num5 - 10);
		}
		else
		{
			num5 = Math.Max(140, Math.Min(height, num3));
			num7 = anchorScreenRect.Bottom + 10;
		}
		if (num7 + num5 > workingArea.Bottom - 10)
		{
			num7 = workingArea.Bottom - 10 - num5;
		}
		if (num7 < workingArea.Top + 10)
		{
			num7 = workingArea.Top + 10;
		}
		if (num5 > workingArea.Height - 20)
		{
			num5 = workingArea.Height - 20;
		}
		((Control)this).SetBounds(num6, num7, num, num5);
		LayoutContent(((Control)body).ClientSize.Width);
		if (ContentHeight > ((Control)body).ClientSize.Height)
		{
			int num8 = Math.Min(workingArea.Height - 20, num5 + (ContentHeight - ((Control)body).ClientSize.Height));
			if (num8 > num5)
			{
				int num9 = (openedAbove ? (num7 - (num8 - num5)) : num7);
				if (num9 + num8 > workingArea.Bottom - 10)
				{
					num9 = workingArea.Bottom - 10 - num8;
				}
				if (num9 < workingArea.Top + 10)
				{
					num9 = workingArea.Top + 10;
				}
				((Control)this).SetBounds(num6, num9, num, num8);
				LayoutContent(((Control)body).ClientSize.Width);
			}
		}
		((Control)this).Show();
		ApplyCornerRegion();
		try
		{
			((Form)this).Activate();
			((Control)this).Focus();
		}
		catch
		{
		}
		if (!watcherRunning)
		{
			watcherRunning = true;
			Watcher.Interval = 120;
			Watcher.Tick += delegate
			{
				WatchMouse();
			};
			Watcher.Start();
		}
		Open.Add(this);
	}

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int virtualKey);

	private static void WatchMouse()
	{
		if (Open.Count == 0)
		{
			Watcher.Stop();
			watcherRunning = false;
			return;
		}
		bool flag = ((uint)GetAsyncKeyState(1) & 0x8000u) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0;
		if (!flag || buttonWasDown)
		{
			buttonWasDown = flag;
			return;
		}
		buttonWasDown = true;
		Point position = Cursor.Position;
		foreach (PopupPanel item in new List<PopupPanel>(Open))
		{
			if (!((Control)item).IsDisposed && !new Rectangle(((Control)item).Left, ((Control)item).Top, ((Control)item).Width, ((Control)item).Height).Contains(position) && !item.AnchorBounds.Contains(position) && !item.HasOpenChildDialog())
			{
				item.ClosePopup();
			}
		}
	}

	private bool HasOpenChildDialog()
	{
		if (ExternalDialogOpen)
		{
			return true;
		}
		try
		{
			Form[] ownedForms = ((Form)this).OwnedForms;
			foreach (Form val in ownedForms)
			{
				if (val != null && !((Control)val).IsDisposed && ((Control)val).Visible)
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	public void FitToContent()
	{
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected O, but got Unknown
		try
		{
			if (((ArrangedElementCollection)((Control)body).Controls).Count == 0 || !((Control)this).IsHandleCreated)
			{
				return;
			}
			int num = 0;
			foreach (Control item in (ArrangedElementCollection)((Control)body).Controls)
			{
				Control val = item;
				if (val.Visible && val.Bottom > num)
				{
					num = val.Bottom;
				}
			}
			int num2 = ((Control)header).Height + num + 8;
			if (num2 < ((Control)this).Height && num2 >= 140)
			{
				int num3 = (openedAbove ? (((Control)this).Bottom - num2) : ((Control)this).Top);
				((Control)this).SetBounds(((Control)this).Left, num3, ((Control)this).Width, num2);
			}
		}
		catch
		{
		}
	}

	protected virtual void LayoutContent(int contentWidth)
	{
	}

	protected float ContentScale(int contentWidth)
	{
		if (contentWidth <= 0)
		{
			return 1f;
		}
		float num = (float)contentWidth / 336f;
		if (!(num < 0.6f))
		{
			if (!(num > 3f))
			{
				return num;
			}
			return 3f;
		}
		return 0.6f;
	}

	protected Button MakeCloseButton()
	{
		Button obj = UiTools.Button("关闭", primary: false);
		((Control)obj).Click += delegate
		{
			ClosePopup();
		};
		return obj;
	}

	public void ClosePopup()
	{
		try
		{
			Open.Remove(this);
		}
		catch
		{
		}
		if (!((Control)this).IsDisposed)
		{
			((Control)this).Hide();
			((Form)this).Close();
		}
		if (this.Closed != null)
		{
			this.Closed(this, EventArgs.Empty);
		}
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		Open.Remove(this);
		base.OnFormClosed(e);
	}

	public static void CloseAll()
	{
		foreach (PopupPanel item in new List<PopupPanel>(Open))
		{
			item.ClosePopup();
		}
	}
}
