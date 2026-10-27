using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
			CreateParams obj = base.CreateParams;
			obj.ExStyle |= 128;
			return obj;
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
			titleLabel.Text = title;
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

	protected int HeaderHeight => header.Height;

	public new event EventHandler Closed;

	public PopupPanel()
	{
		base.FormBorderStyle = FormBorderStyle.None;
		base.ShowInTaskbar = false;
		base.StartPosition = FormStartPosition.Manual;
		BackColor = PanelBlack;
		DoubleBuffered = true;
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		base.KeyPreview = true;
		MinimumSize = new Size(240, 160);
		header.Dock = DockStyle.Top;
		header.Height = 46;
		base.Padding = (UseBlueBorder ? new Padding(2) : new Padding(0));
		header.BackColor = PanelBlack;
		header.Paint += delegate(object sender, PaintEventArgs e)
		{
			using Pen pen2 = new Pen(PanelBlackLine);
			e.Graphics.DrawLine(pen2, 0, header.Height - 1, header.Width, header.Height - 1);
		};
		titleLabel.Dock = DockStyle.Fill;
		titleLabel.TextAlign = ContentAlignment.MiddleLeft;
		titleLabel.ForeColor = Theme.Text;
		titleLabel.Font = Fonts.UiBold(13.5f);
		titleLabel.Padding = new Padding(12, 0, 0, 0);
		titleLabel.Text = title;
		header.Controls.Add(titleLabel);
		body.Dock = DockStyle.Fill;
		body.AutoScroll = false;
		body.BackColor = PanelBlack;
		body.Padding = new Padding(12, 10, 12, 10);
		base.Controls.Add(body);
		base.Controls.Add(header);
		base.Paint += delegate(object sender, PaintEventArgs e)
		{
			RectangleF bounds = new RectangleF(0f, 0f, base.Width, base.Height);
			RectangleF bounds2 = new RectangleF(1f, 1f, (float)base.Width - 2f, (float)base.Height - 2f);
			float num = ((cornerRadiusApplied > 0) ? cornerRadiusApplied : CornerRadius());
			if (!UseBlueBorder)
			{
				_ = PanelBlackLine;
			}
			else
			{
				_ = PanelBlueBorder;
			}
			using (GraphicsPath path = UiTools.RoundedRect(bounds, num))
			{
				using SolidBrush brush = new SolidBrush(PanelBlack);
				e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				e.Graphics.FillPath(brush, path);
			}
			if (UseBlueBorder)
			{
				using (GraphicsPath graphicsPath = new GraphicsPath())
				{
					using SolidBrush brush2 = new SolidBrush(PanelBlueBorder);
					graphicsPath.AddPath(UiTools.RoundedRect(bounds, num), connect: false);
					graphicsPath.AddPath(UiTools.RoundedRect(bounds2, Math.Max(1f, num - 1f)), connect: false);
					graphicsPath.FillMode = FillMode.Alternate;
					e.Graphics.FillPath(brush2, graphicsPath);
					return;
				}
			}
			using GraphicsPath path2 = UiTools.RoundedRect(bounds, num);
			using Pen pen = new Pen(PanelBlackLine, 1f);
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			e.Graphics.DrawPath(pen, path2);
		};
	}

	private void ClipOffBorder(Control root, Region inner)
	{
		List<Control> list = new List<Control>();
		Collect(root, list);
		foreach (Control item in list)
		{
			int num = 0;
			int num2 = 0;
			Control control = item;
			while (control != null && control != this)
			{
				num += control.Left;
				num2 += control.Top;
				control = control.Parent;
			}
			using GraphicsPath path = UiTools.RoundedRect(new RectangleF(1 - num, 1 - num2, base.Width - 2, base.Height - 2), Math.Max(1, cornerRadiusApplied - 1));
			item.Region = new Region(path);
		}
	}

	private static void Collect(Control c, List<Control> acc)
	{
		acc.Add(c);
		foreach (Control control in c.Controls)
		{
			Collect(control, acc);
		}
	}

	private static Region ClippedTo(Region inner, Rectangle bounds)
	{
		Region region = inner.Clone();
		region?.Intersect(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height));
		return region;
	}

	private int CornerRadius()
	{
		float num = 96f;
		if (base.IsHandleCreated)
		{
			try
			{
				using Graphics graphics = Graphics.FromHwnd(base.Handle);
				num = graphics.DpiX;
			}
			catch
			{
			}
		}
		return (int)Math.Round(14f * num / 96f);
	}

	private void ApplyCornerRegion()
	{
		if (!base.IsHandleCreated)
		{
			return;
		}
		try
		{
			int num = (cornerRadiusApplied = CornerRadius());
			using (GraphicsPath path = UiTools.RoundedRect(new RectangleF(0f, 0f, base.Width, base.Height), num))
			{
				base.Region = new Region(path);
			}
			if (UseBlueBorder)
			{
				using (GraphicsPath path2 = UiTools.RoundedRect(new RectangleF(1f, 1f, (float)base.Width - 2f, (float)base.Height - 2f), Math.Max(1, num - 1)))
				{
					using Region inner = new Region(path2);
					ClipOffBorder(header, inner);
					ClipOffBorder(body, inner);
					return;
				}
			}
			header.Region = null;
			body.Region = null;
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
		if (msg.Msg == 256 && keyData == Keys.Escape)
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
		SetBounds(num6, num7, num, num5);
		LayoutContent(body.ClientSize.Width);
		if (ContentHeight > body.ClientSize.Height)
		{
			int num8 = Math.Min(workingArea.Height - 20, num5 + (ContentHeight - body.ClientSize.Height));
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
				SetBounds(num6, num9, num, num8);
				LayoutContent(body.ClientSize.Width);
			}
		}
		Show();
		ApplyCornerRegion();
		try
		{
			Activate();
			Focus();
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
			if (!item.IsDisposed && !new Rectangle(item.Left, item.Top, item.Width, item.Height).Contains(position) && !item.AnchorBounds.Contains(position) && !item.HasOpenChildDialog())
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
			Form[] ownedForms = base.OwnedForms;
			foreach (Form form in ownedForms)
			{
				if (form != null && !form.IsDisposed && form.Visible)
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
		try
		{
			if (body.Controls.Count == 0 || !base.IsHandleCreated)
			{
				return;
			}
			int num = 0;
			foreach (Control control in body.Controls)
			{
				if (control.Visible && control.Bottom > num)
				{
					num = control.Bottom;
				}
			}
			int num2 = header.Height + num + 8;
			if (num2 < base.Height && num2 >= 140)
			{
				int num3 = (openedAbove ? (base.Bottom - num2) : base.Top);
				SetBounds(base.Left, num3, base.Width, num2);
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
		Button button = UiTools.Button("关闭", primary: false);
		button.Click += delegate
		{
			ClosePopup();
		};
		return button;
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
		if (!base.IsDisposed)
		{
			Hide();
			Close();
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
