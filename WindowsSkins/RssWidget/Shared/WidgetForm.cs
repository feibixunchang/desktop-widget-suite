using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Shared;

internal class WidgetForm : Form
{
	private struct NativeRect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private struct MonitorInfoEx
	{
		public int Size;

		public NativeRect Monitor;

		public NativeRect Work;

		public int Flags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string Device;
	}

	private static readonly uint SwpNoZOrder = 4u;

	private static readonly uint SwpNoSize = 1u;

	private static readonly uint SwpNoMove = 2u;

	private static readonly uint SwpNoActivate = 16u;

	private static readonly IntPtr HwndTopMost = new IntPtr(-1);

	private static readonly IntPtr HwndNotTopMost = new IntPtr(-2);

	private static readonly IntPtr HwndBottom = new IntPtr(1);

	private static readonly IntPtr HwndTop = new IntPtr(0);

	private const string StartupRegistryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	protected NotifyIcon TrayIcon;

	protected new ContextMenuStrip Menu = new ContextMenuStrip();

	protected WidgetSettingsStore Store;

	protected DoubleBufferedPanel Canvas;

	protected Point dragStart;

	private bool suppressSave;

	private ToolStripMenuItem lockItem;

	private ToolStripMenuItem desktopLayerItem;

	private ToolStripMenuItem fontMenu;

	private ToolStripMenuItem opacityMenu;

	private bool closing;

	private Timer saveTimer;

	private Timer fullscreenTimer;

	private bool hiddenForFullscreen;

	private bool zOrderWasTopMost;

	private bool menuOpen;

	private bool inDesktopLayer;

	private Bitmap surface;

	private bool rendering;

	private Point lastRenderedAt;

	private bool hasRendered;

	private static IntPtr cachedDesktopHost = IntPtr.Zero;

	protected virtual bool WantsTopMostWhileActive => false;

	private static bool DebugLogging => Environment.GetEnvironmentVariable("FSWATCH_LOG") == "1";

	protected virtual bool UseLayeredSurface => false;

	protected TextRenderingHint SurfaceTextHint
	{
		get
		{
			if (!UseLayeredSurface)
			{
				return TextRenderingHint.ClearTypeGridFit;
			}
			return TextRenderingHint.AntiAliasGridFit;
		}
	}

	protected virtual string RegistryName => AppPaths.AppId;

	public bool NoBackground => Store.Settings.Opacity <= 0;

	public virtual int DefaultFontSize => 14;

	public virtual int MinFontSize => 11;

	public virtual int MaxFontSize => 18;

	protected int CardAlpha
	{
		get
		{
			int opacity = Store.Settings.Opacity;
			if (opacity <= 0)
			{
				return 0;
			}
			if (opacity >= 100)
			{
				return 255;
			}
			return (int)Math.Round(255.0 * (double)opacity / 100.0);
		}
	}

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ExStyle |= 134217856;
			if (UseLayeredSurface)
			{
				createParams.ExStyle |= 524288;
			}
			return createParams;
		}
	}

	protected virtual string DisplayName => "桌面挂件";

	protected bool SurfaceHasCapture
	{
		get
		{
			if (!UseLayeredSurface)
			{
				return Canvas.Capture;
			}
			return base.Capture;
		}
	}

	public virtual Rectangle DefaultBounds
	{
		get
		{
			Rectangle placementArea = PlacementArea;
			int num = Math.Max(base.Width, 300);
			int num2 = Math.Max(base.Height, 200);
			return new Rectangle(placementArea.Right - num - 30, placementArea.Top + 60, num, num2);
		}
	}

	protected virtual int DragClampHeight => base.Height;

	public bool IsClosing => closing;

	protected virtual Rectangle PlacementArea
	{
		get
		{
			try
			{
				IntPtr intPtr = MonitorFromWindow(base.Handle, 2);
				if (intPtr != IntPtr.Zero)
				{
					MonitorInfoEx info = default(MonitorInfoEx);
					info.Size = Marshal.SizeOf(typeof(MonitorInfoEx));
					if (GetMonitorInfo(intPtr, ref info))
					{
						return new Rectangle(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
					}
				}
			}
			catch
			{
			}
			return Screen.PrimaryScreen.WorkingArea;
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr FindWindowW(string className, string windowName);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string className, string windowName);

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

	[DllImport("user32.dll")]
	private static extern IntPtr GetTopWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr hWnd, int command);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentProcessId();

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

	[DllImport("user32.dll")]
	private static extern bool SetParent(IntPtr child, IntPtr newParent);

	[DllImport("user32.dll")]
	private static extern IntPtr SetWindowLongW(IntPtr hWnd, int index, IntPtr value);

	[DllImport("user32.dll")]
	private static extern IntPtr GetWindowLongW(IntPtr hWnd, int index);

	protected Color SurfaceColor(int alpha, int red, int green, int blue)
	{
		if (UseLayeredSurface && alpha <= 0)
		{
			return Color.FromArgb(1, red, green, blue);
		}
		return Color.FromArgb(alpha, red, green, blue);
	}

	protected Color SurfaceColor(int alpha, Color color)
	{
		return SurfaceColor(alpha, color.R, color.G, color.B);
	}

	public WidgetForm()
	{
		base.FormBorderStyle = FormBorderStyle.None;
		base.ShowInTaskbar = false;
		base.StartPosition = FormStartPosition.Manual;
		base.AutoScaleMode = AutoScaleMode.None;
		AutoSize = false;
		base.KeyPreview = true;
		if (UseLayeredSurface)
		{
			BackColor = Color.Black;
			DoubleBuffered = false;
		}
		else
		{
			BackColor = Theme.Key;
			base.TransparencyKey = Theme.Key;
			DoubleBuffered = true;
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		}
		Store = new WidgetSettingsStore(AppPaths.SettingsFile);
		if (Store.Settings.Opacity < 0 || Store.Settings.Opacity > 100)
		{
			Store.Settings.Opacity = 92;
		}
		if (Store.Settings.FontSize < MinFontSize || Store.Settings.FontSize > MaxFontSize)
		{
			Store.Settings.FontSize = DefaultFontSize;
		}
		ApplyBackground();
		base.TopMost = Store.Settings.TopMost;
		ClearTopMostStyle();
		Canvas = new DoubleBufferedPanel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.Transparent
		};
		Canvas.Paint += OnCanvasPaint;
		Canvas.MouseDown += OnCanvasMouseDown;
		Canvas.MouseMove += OnCanvasMouseMove;
		Canvas.MouseUp += OnCanvasMouseUp;
		if (!UseLayeredSurface)
		{
			base.Controls.Add(Canvas);
		}
		Canvas.MouseWheel += delegate(object sender, MouseEventArgs e)
		{
			OnSurfaceMouseWheel(e);
		};
		if (UseLayeredSurface)
		{
			base.MouseWheel += delegate(object sender, MouseEventArgs e)
			{
				OnSurfaceMouseWheel(e);
			};
		}
		ContextMenuStrip = Menu;
		Menu.Opening += delegate
		{
			menuOpen = true;
			RefreshMenuState();
		};
		Menu.Closed += delegate
		{
			menuOpen = false;
			if (Store.Settings.DesktopLayer != false)
			{
				SendToBack();
			}
		};
	}

	public virtual int[] FontSizes()
	{
		return new int[6] { 11, 12, 13, 14, 16, 18 };
	}

	private void ApplyBackground()
	{
		if (!UseLayeredSurface)
		{
			base.Opacity = 1.0;
		}
	}

	protected void InvalidateSurface()
	{
		InvalidateSurface(Rectangle.Empty);
	}

	protected void InvalidateSurface(Rectangle dirty)
	{
		if (!UseLayeredSurface)
		{
			if (Canvas != null)
			{
				Canvas.Invalidate(dirty);
			}
		}
		else
		{
			RenderSurface();
		}
	}

	protected override void OnMove(EventArgs e)
	{
		base.OnMove(e);
		if (UseLayeredSurface)
		{
			InvalidateSurface();
		}
	}

	private bool SurfaceOutOfPlace()
	{
		if (!hasRendered || !base.IsHandleCreated || !base.Visible)
		{
			return false;
		}
		if (!GetWindowRect(base.Handle, out var rect))
		{
			return false;
		}
		if (rect.Left == lastRenderedAt.X)
		{
			return rect.Top != lastRenderedAt.Y;
		}
		return true;
	}

	protected Rectangle AnchorBoundsOnScreen()
	{
		if (base.IsHandleCreated && GetWindowRect(base.Handle, out var rect))
		{
			Rectangle result = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
			if (result.Width > 0 && result.Height > 0)
			{
				return result;
			}
		}
		return RectangleToScreen(new Rectangle(0, 0, base.Width, base.Height));
	}

	private void RenderSurface()
	{
		if (rendering || base.IsDisposed || !base.IsHandleCreated)
		{
			return;
		}
		int num = base.ClientSize.Width;
		int num2 = base.ClientSize.Height;
		if (num <= 0 || num2 <= 0)
		{
			return;
		}
		rendering = true;
		try
		{
			if (surface == null || surface.Width != num || surface.Height != num2 || surface.PixelFormat != PixelFormat.Format32bppArgb)
			{
				if (surface != null)
				{
					surface.Dispose();
				}
				surface = new Bitmap(num, num2, PixelFormat.Format32bppArgb);
			}
			using (Graphics graphics = Graphics.FromImage(surface))
			{
				graphics.CompositingMode = CompositingMode.SourceOver;
				graphics.Clear(Color.Transparent);
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.TextRenderingHint = SurfaceTextHint;
				Rectangle clipRect = new Rectangle(0, 0, num, num2);
				OnCanvasPaint(this, new PaintEventArgs(graphics, clipRect));
				graphics.Flush();
			}
			if (GetWindowRect(base.Handle, out var rect))
			{
				lastRenderedAt = new Point(rect.Left, rect.Top);
				hasRendered = true;
				LayeredSurface.Update(base.Handle, surface, lastRenderedAt);
			}
		}
		catch
		{
		}
		finally
		{
			rendering = false;
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		if (!UseLayeredSurface)
		{
			base.OnPaint(e);
		}
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		if (!UseLayeredSurface)
		{
			base.OnPaintBackground(e);
		}
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		if (UseLayeredSurface)
		{
			OnCanvasMouseDown(this, e);
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		if (UseLayeredSurface)
		{
			OnCanvasMouseMove(this, e);
		}
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);
		if (UseLayeredSurface)
		{
			OnCanvasMouseUp(this, e);
		}
	}

	protected override void OnMouseCaptureChanged(EventArgs e)
	{
		base.OnMouseCaptureChanged(e);
		if (UseLayeredSurface)
		{
			OnMouseCaptureChangedForCanvas(e);
		}
	}

	protected virtual void OnMouseCaptureChangedForCanvas(EventArgs e)
	{
	}

	protected virtual void OnCanvasPaint(object sender, PaintEventArgs e)
	{
	}

	protected virtual void OnSurfaceMouseWheel(MouseEventArgs e)
	{
	}

	protected void SetSurfaceCapture(bool value)
	{
		if (UseLayeredSurface)
		{
			base.Capture = value;
		}
		else
		{
			Canvas.Capture = value;
		}
	}

	protected virtual void BuildMenu()
	{
	}

	protected virtual void OnMenuOpening()
	{
	}

	protected virtual void OnMenuClick(string tag)
	{
	}

	protected void BuildBaseMenu()
	{
		Menu.Items.Clear();
		lockItem = new ToolStripMenuItem("锁定位置");
		lockItem.Click += delegate
		{
			ToggleLock();
		};
		Menu.Items.Add(lockItem);
		desktopLayerItem = new ToolStripMenuItem("只在桌面显示（不压在程序上面）");
		desktopLayerItem.Click += delegate
		{
			Store.Settings.DesktopLayer = !Store.Settings.DesktopLayer.GetValueOrDefault(true);
			Store.Save();
			if (Store.Settings.DesktopLayer.GetValueOrDefault())
			{
				if (base.TopMost)
				{
					base.TopMost = false;
				}
				EnterDesktopLayer();
			}
			else
			{
				LeaveDesktopLayer();
			}
			RefreshMenuState();
		};
		Menu.Items.Add(desktopLayerItem);
		fontMenu = new ToolStripMenuItem("字体大小");
		int[] array = FontSizes();
		for (int i = 0; i < array.Length; i++)
		{
			int num = array[i];
			int captured2 = num;
			ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem(num.ToString());
			toolStripMenuItem.Tag = "font:" + num;
			toolStripMenuItem.Click += delegate
			{
				OnMenuClick("font:" + captured2);
			};
			fontMenu.DropDownItems.Add(toolStripMenuItem);
		}
		Menu.Items.Add(fontMenu);
		opacityMenu = new ToolStripMenuItem("透明度");
		array = new int[11]
		{
			0, 10, 20, 30, 40, 50, 60, 70, 80, 90,
			100
		};
		for (int i = 0; i < array.Length; i++)
		{
			int num2 = array[i];
			int captured = num2;
			ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem((num2 == 0) ? "无背景（完全透明）" : (num2 + "%"));
			toolStripMenuItem2.Tag = "opacity:" + num2;
			toolStripMenuItem2.Click += delegate
			{
				OnMenuClick("opacity:" + captured);
			};
			opacityMenu.DropDownItems.Add(toolStripMenuItem2);
		}
		Menu.Items.Add(opacityMenu);
		ToolStripMenuItem toolStripMenuItem3 = new ToolStripMenuItem("回到默认位置");
		toolStripMenuItem3.Click += delegate
		{
			ResetPosition();
		};
		Menu.Items.Add(toolStripMenuItem3);
		ToolStripMenuItem toolStripMenuItem4 = new ToolStripMenuItem("开机启动");
		toolStripMenuItem4.Click += delegate
		{
			ToggleAutoStart();
		};
		Menu.Items.Add(toolStripMenuItem4);
		Menu.Items.Add(new ToolStripSeparator());
		BuildMenu();
		Menu.Items.Add(new ToolStripSeparator());
		ToolStripMenuItem toolStripMenuItem5 = new ToolStripMenuItem("关于");
		toolStripMenuItem5.Click += delegate
		{
			ShowAbout();
		};
		Menu.Items.Add(toolStripMenuItem5);
		ToolStripMenuItem toolStripMenuItem6 = new ToolStripMenuItem("退出");
		toolStripMenuItem6.Click += delegate
		{
			Close();
		};
		Menu.Items.Add(toolStripMenuItem6);
	}

	private void ClearTopMostStyle()
	{
		if (!base.TopMost && base.IsHandleCreated)
		{
			SetWindowPos(base.Handle, HwndNotTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
		}
	}

	private static IntPtr DesktopHostWindow()
	{
		if (cachedDesktopHost != IntPtr.Zero && IsWindowVisible(cachedDesktopHost))
		{
			return cachedDesktopHost;
		}
		IntPtr intPtr = FindWindowW("Progman", null);
		if (intPtr == IntPtr.Zero)
		{
			return IntPtr.Zero;
		}
		SendMessageTimeoutW(intPtr, 1324u, IntPtr.Zero, IntPtr.Zero, 0u, 1000u, out var _);
		cachedDesktopHost = IntPtr.Zero;
		return IntPtr.Zero;
	}

	private void EnterDesktopLayer()
	{
		base.TopMost = false;
		SendToBack();
	}

	private void LeaveDesktopLayer()
	{
		SetParent(base.Handle, IntPtr.Zero);
		SetWindowLongW(base.Handle, -16, new IntPtr(GetWindowLongW(base.Handle, -16).ToInt32() & -1073741825));
		SetWindowLongW(base.Handle, -20, new IntPtr(GetWindowLongW(base.Handle, -20).ToInt32() & -1073741825));
		SetWindowPos(base.Handle, new IntPtr(-2), 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | 0x20u);
		ShowWindow(base.Handle, 5);
		inDesktopLayer = false;
		RestorePosition();
	}

	private void PollFullscreen()
	{
		if (inDesktopLayer || closing || base.IsDisposed || !base.IsHandleCreated || WantsTopMostWhileActive)
		{
			return;
		}
		if (Store.Settings.DesktopLayer != false)
		{
			if (base.Visible && !menuOpen)
			{
				hiddenForFullscreen = true;
				SendToBack();
			}
		}
		else
		{
			if (!Store.Settings.HideOnFullscreen.GetValueOrDefault(true))
			{
				return;
			}
			bool flag = false;
			IntPtr topWindow = GetTopWindow(IntPtr.Zero);
			uint currentProcessId = GetCurrentProcessId();
			Rectangle bounds = Screen.PrimaryScreen.Bounds;
			double num = (double)bounds.Width * (double)bounds.Height;
			while (topWindow != IntPtr.Zero && !flag)
			{
				GetWindowThreadProcessId(topWindow, out var processId);
				if (processId != currentProcessId && IsWindowVisible(topWindow) && !IsShellOverlay(topWindow))
				{
					int num2 = GetWindowLongW(topWindow, -20).ToInt32();
					if ((num2 & 0x80) == 0 && (((uint)num2 & 8u) != 0 || topWindow == GetForegroundWindow()) && GetWindowRect(topWindow, out var rect) && (double)(rect.Right - rect.Left) * (double)(rect.Bottom - rect.Top) >= num * 0.92)
					{
						flag = true;
					}
				}
				topWindow = GetWindow(topWindow, 2u);
			}
			if (flag && !hiddenForFullscreen && base.Visible)
			{
				hiddenForFullscreen = true;
				SendToBack();
				DebugLog("有全屏窗口在前台，已压到 z 序底层");
			}
			else if (!flag && hiddenForFullscreen)
			{
				hiddenForFullscreen = false;
				RestoreZOrder();
				DebugLog("全屏窗口没了，已恢复 z 序");
			}
		}
	}

	private new void SendToBack()
	{
		if (base.IsDisposed || !base.IsHandleCreated)
		{
			return;
		}
		try
		{
			if (NeedsSendToBack())
			{
				if (!WantsTopMostWhileActive)
				{
					zOrderWasTopMost = base.TopMost;
				}
				if (base.TopMost)
				{
					base.TopMost = false;
				}
				SetWindowPos(base.Handle, HwndNotTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
				SetWindowPos(base.Handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
			}
		}
		catch
		{
		}
	}

	private bool NeedsSendToBack()
	{
		if (base.TopMost)
		{
			return true;
		}
		IntPtr topWindow = GetTopWindow(IntPtr.Zero);
		uint currentProcessId = GetCurrentProcessId();
		int num = 0;
		while (topWindow != IntPtr.Zero && num++ < 256)
		{
			if (topWindow == base.Handle)
			{
				return false;
			}
			if (IsWindowVisible(topWindow))
			{
				GetWindowThreadProcessId(topWindow, out var processId);
				if (processId != currentProcessId && !IsShellOverlay(topWindow) && (GetWindowLongW(topWindow, -20).ToInt32() & 0x80) == 0)
				{
					return true;
				}
			}
			topWindow = GetWindow(topWindow, 2u);
		}
		return true;
	}

	private void RestoreZOrder()
	{
		if (base.IsDisposed || !base.IsHandleCreated)
		{
			return;
		}
		try
		{
			if (zOrderWasTopMost || Store.Settings.TopMost)
			{
				base.TopMost = true;
				return;
			}
			if (base.TopMost)
			{
				base.TopMost = false;
			}
			ClearTopMostStyle();
			SetWindowPos(base.Handle, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
		}
		catch
		{
		}
	}

	private static void DebugLog(string message)
	{
		if (!DebugLogging)
		{
			return;
		}
		try
		{
			File.AppendAllText(Path.Combine(Path.GetTempPath(), "fswatch.log"), DateTime.Now.ToString("HH:mm:ss") + " " + Process.GetCurrentProcess().ProcessName + " " + message + Environment.NewLine);
		}
		catch
		{
		}
	}

	private static bool IsShellOverlay(IntPtr handle)
	{
		StringBuilder stringBuilder = new StringBuilder(64);
		GetClassNameW(handle, stringBuilder, 64);
		string text = stringBuilder.ToString();
		switch (text)
		{
		case "Progman":
		case "WorkerW":
			return true;
		case "Windows.UI.Core.CoreWindow":
		case "ShellHandwritingCanvas":
			return true;
		default:
			if (text.StartsWith("Wnd_StartFeed", StringComparison.Ordinal))
			{
				return true;
			}
			if (!(handle == FindWindowW("Progman", null)))
			{
				return handle == FindWindowW("WorkerW", null);
			}
			return true;
		}
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassNameW(IntPtr hWnd, StringBuilder text, int count);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
	private static extern int NativeGetWindowTextW(IntPtr hWnd, StringBuilder text, int count);

	private void ShowAbout()
	{
		Version version = typeof(WidgetForm).Assembly.GetName().Version;
		string text = ((version == null) ? "2.0.1" : (version.Major + "." + version.Minor + "." + version.Build));
		MessageBox.Show(this, DisplayName + "  版本 " + text + "（当前最新）\n\n数据目录：" + AppPaths.DataDirectory + "\n\n右键菜单可锁定位置、调整字号与背景透明度、设置开机启动。", DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void RefreshMenuState()
	{
		OnMenuOpening();
		if (lockItem != null)
		{
			lockItem.Text = (Store.Settings.Locked ? "解锁位置" : "锁定位置");
		}
		if (desktopLayerItem != null)
		{
			desktopLayerItem.Checked = Store.Settings.DesktopLayer.GetValueOrDefault(true);
		}
		if (fontMenu != null)
		{
			foreach (ToolStripItem dropDownItem in fontMenu.DropDownItems)
			{
				if (dropDownItem is ToolStripMenuItem { Tag: not null } toolStripMenuItem)
				{
					toolStripMenuItem.Checked = string.Equals(toolStripMenuItem.Tag.ToString(), "font:" + Store.Settings.FontSize, StringComparison.Ordinal);
				}
			}
		}
		if (opacityMenu != null)
		{
			foreach (ToolStripItem dropDownItem2 in opacityMenu.DropDownItems)
			{
				if (dropDownItem2 is ToolStripMenuItem { Tag: not null } toolStripMenuItem2)
				{
					toolStripMenuItem2.Checked = string.Equals(toolStripMenuItem2.Tag.ToString(), "opacity:" + Store.Settings.Opacity, StringComparison.Ordinal);
				}
			}
		}
		ToolStripMenuItem toolStripMenuItem3 = FindMenuItem("开机启动");
		if (toolStripMenuItem3 != null)
		{
			toolStripMenuItem3.Checked = IsAutoStartEnabled();
		}
	}

	protected ToolStripMenuItem FindMenuItem(string text)
	{
		foreach (ToolStripItem item in Menu.Items)
		{
			if (item.Text == text)
			{
				return item as ToolStripMenuItem;
			}
			if (!(item is ToolStripMenuItem toolStripMenuItem))
			{
				continue;
			}
			foreach (ToolStripItem dropDownItem in toolStripMenuItem.DropDownItems)
			{
				if (dropDownItem.Text == text)
				{
					return dropDownItem as ToolStripMenuItem;
				}
			}
		}
		return null;
	}

	protected void ToggleLock()
	{
		Store.Settings.Locked = !Store.Settings.Locked;
		if (!Store.Settings.Locked)
		{
			SavePosition();
		}
		Store.Save();
		RefreshMenuState();
	}

	public virtual void ApplyFontSize(int size)
	{
		Store.Settings.FontSize = size;
		Store.Save();
		RefreshMenuState();
		try
		{
			if (UseLayeredSurface)
			{
				InvalidateSurface();
			}
			else if (Canvas != null && Canvas.IsHandleCreated)
			{
				Canvas.Invalidate();
				Canvas.Update();
			}
		}
		catch
		{
		}
	}

	public void ApplyOpacity(int level)
	{
		Store.Settings.Opacity = level;
		ApplyBackground();
		try
		{
			if (UseLayeredSurface)
			{
				InvalidateSurface();
			}
			else if (Canvas != null && Canvas.IsHandleCreated)
			{
				Canvas.Invalidate();
				Canvas.Update();
			}
			else
			{
				Invalidate();
				Update();
			}
		}
		catch
		{
		}
		RefreshMenuState();
		ScheduleSave();
	}

	private void ScheduleSave()
	{
		if (saveTimer == null)
		{
			saveTimer = new Timer
			{
				Interval = 400
			};
			saveTimer.Tick += delegate
			{
				saveTimer.Stop();
				Store.Save();
			};
		}
		saveTimer.Stop();
		saveTimer.Start();
	}

	public void ResetPosition()
	{
		Rectangle defaultBounds = DefaultBounds;
		Store.Settings.X = defaultBounds.Left;
		Store.Settings.Y = defaultBounds.Top;
		Store.Settings.FirstRun = false;
		Store.Save();
		base.Bounds = defaultBounds;
	}

	protected void RestorePosition()
	{
		Rectangle placementArea = PlacementArea;
		int num = Math.Max(base.Width, 300);
		int num2 = Math.Max(base.Height, 200);
		int num3 = Store.Settings.X;
		int num4 = Store.Settings.Y;
		if (Store.Settings.FirstRun || (num3 <= 0 && num4 <= 0))
		{
			Rectangle defaultBounds = DefaultBounds;
			num3 = defaultBounds.Left;
			num4 = defaultBounds.Top;
			Store.Settings.X = num3;
			Store.Settings.Y = num4;
			Store.Settings.FirstRun = false;
			Store.Save();
		}
		else
		{
			int num5 = placementArea.Right - num;
			int num6 = placementArea.Bottom - num2;
			num3 = ((num5 < placementArea.Left) ? placementArea.Left : Math.Max(placementArea.Left, Math.Min(num3, num5)));
			num4 = ((num6 < placementArea.Top) ? placementArea.Top : Math.Max(placementArea.Top, Math.Min(num4, num6)));
		}
		base.Bounds = new Rectangle(num3, num4, num, num2);
	}

	private void SavePosition()
	{
		if (!Store.Settings.Locked && !suppressSave)
		{
			Store.Settings.X = base.Left;
			Store.Settings.Y = base.Top;
			Store.Save();
		}
	}

	protected virtual void OnCanvasMouseDown(object sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			try
			{
				PopupPanel.CloseAll();
			}
			catch
			{
			}
		}
		if (e.Button == MouseButtons.Left && !Store.Settings.Locked)
		{
			dragStart = new Point(e.X, e.Y);
		}
	}

	protected virtual void OnCanvasMouseMove(object sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left && !(dragStart == Point.Empty) && !Store.Settings.Locked)
		{
			int val = base.Left + e.X - dragStart.X;
			int val2 = base.Top + e.Y - dragStart.Y;
			Rectangle placementArea = PlacementArea;
			int num = Math.Max(60, Math.Min(base.Height, DragClampHeight));
			val = Math.Max(placementArea.Left, Math.Min(val, Math.Max(placementArea.Left, placementArea.Right - base.Width)));
			val2 = Math.Max(placementArea.Top, Math.Min(val2, Math.Max(placementArea.Top, placementArea.Bottom - num)));
			base.Bounds = new Rectangle(val, val2, base.Width, base.Height);
		}
	}

	protected virtual void OnCanvasMouseUp(object sender, MouseEventArgs e)
	{
		dragStart = Point.Empty;
		if (e.Button == MouseButtons.Left)
		{
			SavePosition();
		}
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		if (Store.Settings.DesktopLayer != false)
		{
			EnterDesktopLayer();
		}
		if (fullscreenTimer == null)
		{
			fullscreenTimer = new Timer
			{
				Interval = 1000
			};
			fullscreenTimer.Tick += delegate
			{
				PollFullscreen();
				if (UseLayeredSurface && SurfaceOutOfPlace())
				{
					InvalidateSurface();
				}
			};
		}
		fullscreenTimer.Start();
		if (UseLayeredSurface)
		{
			InvalidateSurface();
		}
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		if (UseLayeredSurface)
		{
			InvalidateSurface();
		}
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		closing = true;
		if (fullscreenTimer != null)
		{
			fullscreenTimer.Stop();
		}
		if (inDesktopLayer)
		{
			LeaveDesktopLayer();
		}
		if (saveTimer != null && saveTimer.Enabled)
		{
			saveTimer.Stop();
			Store.Save();
		}
		base.OnFormClosing(e);
	}

	protected void SetupTray()
	{
		if (!TrayAllowed())
		{
			return;
		}
		TrayIcon = new NotifyIcon
		{
			Icon = AppIcon.Load(16),
			Text = DisplayName,
			ContextMenuStrip = Menu,
			Visible = true
		};
		TrayIcon.DoubleClick += delegate
		{
			if (!base.Visible)
			{
				Show();
			}
			BringToFront();
		};
	}

	private static bool TrayAllowed()
	{
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		for (int i = 0; i < commandLineArgs.Length; i++)
		{
			if (string.Equals(commandLineArgs[i], "--tray", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private bool IsAutoStartEnabled()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			if (registryKey == null)
			{
				return false;
			}
			object value = registryKey.GetValue(RegistryName);
			if (value == null)
			{
				return false;
			}
			return value.ToString().IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
		}
		catch
		{
			return false;
		}
	}

	private void ToggleAutoStart()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			if (registryKey == null)
			{
				return;
			}
			if (IsAutoStartEnabled())
			{
				registryKey.DeleteValue(RegistryName, throwOnMissingValue: false);
			}
			else
			{
				registryKey.SetValue(RegistryName, "\"" + Application.ExecutablePath + "\"");
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, "设置开机启动失败：" + ex.Message, DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
		RefreshMenuState();
	}

	protected override void SetVisibleCore(bool value)
	{
		base.SetVisibleCore(value);
		if (!value && TrayIcon != null)
		{
			TrayIcon.Visible = true;
		}
	}

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);

	[DllImport("user32.dll")]
	private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

	protected override void Dispose(bool disposing)
	{
		if (disposing && surface != null)
		{
			surface.Dispose();
			surface = null;
		}
		base.Dispose(disposing);
	}
}
