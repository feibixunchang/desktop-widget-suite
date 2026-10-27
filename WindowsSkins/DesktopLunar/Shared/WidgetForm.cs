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
using System.Windows.Forms.Layout;
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

	protected ContextMenuStrip Menu = new ContextMenuStrip();

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
			if (UseLayeredSurface)
			{
				return (TextRenderingHint)3;
			}
			return (TextRenderingHint)5;
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
			createParams.ExStyle |= 0x8000080;
			if (UseLayeredSurface)
			{
				createParams.ExStyle |= 0x80000;
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
				return ((Control)Canvas).Capture;
			}
			return ((Control)this).Capture;
		}
	}

	public virtual Rectangle DefaultBounds
	{
		get
		{
			Rectangle placementArea = PlacementArea;
			int num = Math.Max(((Control)this).Width, 300);
			int height = Math.Max(((Control)this).Height, 200);
			return new Rectangle(placementArea.Right - num - 30, placementArea.Top + 60, num, height);
		}
	}

	protected virtual int DragClampHeight => ((Control)this).Height;

	public bool IsClosing => closing;

	protected virtual Rectangle PlacementArea
	{
		get
		{
			try
			{
				IntPtr intPtr = MonitorFromWindow(((Control)this).Handle, 2);
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
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Expected O, but got Unknown
		//IL_015c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Expected O, but got Unknown
		//IL_0174: Unknown result type (might be due to invalid IL or missing references)
		//IL_017e: Expected O, but got Unknown
		//IL_018c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0196: Expected O, but got Unknown
		//IL_01a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ae: Expected O, but got Unknown
		//IL_01d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01de: Expected O, but got Unknown
		//IL_0228: Unknown result type (might be due to invalid IL or missing references)
		//IL_0232: Expected O, but got Unknown
		//IL_01ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f8: Expected O, but got Unknown
		((Form)this).FormBorderStyle = (FormBorderStyle)0;
		((Form)this).ShowInTaskbar = false;
		((Form)this).StartPosition = (FormStartPosition)0;
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)0;
		((Control)this).AutoSize = false;
		((Form)this).KeyPreview = true;
		if (UseLayeredSurface)
		{
			((Control)this).BackColor = Color.Black;
			DoubleBuffered = false;
		}
		else
		{
			((Control)this).BackColor = Theme.Key;
			((Form)this).TransparencyKey = Theme.Key;
			DoubleBuffered = true;
			SetStyle((ControlStyles)139266, true);
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
		((Form)this).TopMost = Store.Settings.TopMost;
		ClearTopMostStyle();
		DoubleBufferedPanel doubleBufferedPanel = new DoubleBufferedPanel();
		((Control)doubleBufferedPanel).Dock = (DockStyle)5;
		((Control)doubleBufferedPanel).BackColor = Color.Transparent;
		Canvas = doubleBufferedPanel;
		((Control)Canvas).Paint += new PaintEventHandler(OnCanvasPaint);
		((Control)Canvas).MouseDown += new MouseEventHandler(OnCanvasMouseDown);
		((Control)Canvas).MouseMove += new MouseEventHandler(OnCanvasMouseMove);
		((Control)Canvas).MouseUp += new MouseEventHandler(OnCanvasMouseUp);
		if (!UseLayeredSurface)
		{
			((Control)this).Controls.Add((Control)(object)Canvas);
		}
		((Control)Canvas).MouseWheel += (MouseEventHandler)delegate(object sender, MouseEventArgs e)
		{
			OnSurfaceMouseWheel(e);
		};
		if (UseLayeredSurface)
		{
			((Control)this).MouseWheel += (MouseEventHandler)delegate(object sender, MouseEventArgs e)
			{
				OnSurfaceMouseWheel(e);
			};
		}
		((Control)this).ContextMenuStrip = Menu;
		((ToolStripDropDown)Menu).Opening += delegate
		{
			menuOpen = true;
			RefreshMenuState();
		};
		((ToolStripDropDown)Menu).Closed += (ToolStripDropDownClosedEventHandler)delegate
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
			((Form)this).Opacity = 1.0;
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
				((Control)Canvas).Invalidate(dirty);
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
		if (!hasRendered || !((Control)this).IsHandleCreated || !((Control)this).Visible)
		{
			return false;
		}
		if (!GetWindowRect(((Control)this).Handle, out var rect))
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
		if (((Control)this).IsHandleCreated && GetWindowRect(((Control)this).Handle, out var rect))
		{
			Rectangle result = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
			if (result.Width > 0 && result.Height > 0)
			{
				return result;
			}
		}
		return ((Control)this).RectangleToScreen(new Rectangle(0, 0, ((Control)this).Width, ((Control)this).Height));
	}

	private void RenderSurface()
	{
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected O, but got Unknown
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Invalid comparison between Unknown and I4
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Expected O, but got Unknown
		if (rendering || ((Control)this).IsDisposed || !((Control)this).IsHandleCreated)
		{
			return;
		}
		int width = ((Form)this).ClientSize.Width;
		int height = ((Form)this).ClientSize.Height;
		if (width <= 0 || height <= 0)
		{
			return;
		}
		rendering = true;
		try
		{
			if (surface == null || ((Image)surface).Width != width || ((Image)surface).Height != height || (int)((Image)surface).PixelFormat != 2498570)
			{
				if (surface != null)
				{
					((Image)surface).Dispose();
				}
				surface = new Bitmap(width, height, (PixelFormat)2498570);
			}
			Graphics val = Graphics.FromImage((Image)(object)surface);
			try
			{
				val.CompositingMode = (CompositingMode)0;
				val.Clear(Color.Transparent);
				val.SmoothingMode = (SmoothingMode)4;
				val.TextRenderingHint = SurfaceTextHint;
				Rectangle rectangle = new Rectangle(0, 0, width, height);
				OnCanvasPaint(this, new PaintEventArgs(val, rectangle));
				val.Flush();
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			if (GetWindowRect(((Control)this).Handle, out var rect))
			{
				lastRenderedAt = new Point(rect.Left, rect.Top);
				hasRendered = true;
				LayeredSurface.Update(((Control)this).Handle, surface, lastRenderedAt);
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
			((Control)this).Capture = value;
		}
		else
		{
			((Control)Canvas).Capture = value;
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
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Expected O, but got Unknown
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Expected O, but got Unknown
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Expected O, but got Unknown
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Expected O, but got Unknown
		//IL_0140: Unknown result type (might be due to invalid IL or missing references)
		//IL_014a: Expected O, but got Unknown
		//IL_0214: Unknown result type (might be due to invalid IL or missing references)
		//IL_021a: Expected O, but got Unknown
		//IL_0243: Unknown result type (might be due to invalid IL or missing references)
		//IL_0249: Expected O, but got Unknown
		//IL_0278: Unknown result type (might be due to invalid IL or missing references)
		//IL_0282: Expected O, but got Unknown
		//IL_0294: Unknown result type (might be due to invalid IL or missing references)
		//IL_029e: Expected O, but got Unknown
		//IL_02a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02aa: Expected O, but got Unknown
		//IL_02d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d9: Expected O, but got Unknown
		//IL_01a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a8: Expected O, but got Unknown
		((ToolStrip)Menu).Items.Clear();
		lockItem = new ToolStripMenuItem("锁定位置");
		((ToolStripItem)lockItem).Click += delegate
		{
			ToggleLock();
		};
		((ToolStrip)Menu).Items.Add((ToolStripItem)(object)lockItem);
		desktopLayerItem = new ToolStripMenuItem("只在桌面显示（不压在程序上面）");
		((ToolStripItem)desktopLayerItem).Click += delegate
		{
			Store.Settings.DesktopLayer = !Store.Settings.DesktopLayer.GetValueOrDefault(true);
			Store.Save();
			if (Store.Settings.DesktopLayer.GetValueOrDefault())
			{
				if (((Form)this).TopMost)
				{
					((Form)this).TopMost = false;
				}
				EnterDesktopLayer();
			}
			else
			{
				LeaveDesktopLayer();
			}
			RefreshMenuState();
		};
		((ToolStrip)Menu).Items.Add((ToolStripItem)(object)desktopLayerItem);
		fontMenu = new ToolStripMenuItem("字体大小");
		int[] array = FontSizes();
		for (int i = 0; i < array.Length; i++)
		{
			int num = array[i];
			int captured2 = num;
			ToolStripMenuItem val = new ToolStripMenuItem(num.ToString());
			((ToolStripItem)val).Tag = "font:" + num;
			((ToolStripItem)val).Click += delegate
			{
				OnMenuClick("font:" + captured2);
			};
			((ToolStripDropDownItem)fontMenu).DropDownItems.Add((ToolStripItem)(object)val);
		}
		((ToolStrip)Menu).Items.Add((ToolStripItem)(object)fontMenu);
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
			ToolStripMenuItem val2 = new ToolStripMenuItem((num2 == 0) ? "无背景（完全透明）" : (num2 + "%"));
			((ToolStripItem)val2).Tag = "opacity:" + num2;
			((ToolStripItem)val2).Click += delegate
			{
				OnMenuClick("opacity:" + captured);
			};
			((ToolStripDropDownItem)opacityMenu).DropDownItems.Add((ToolStripItem)(object)val2);
		}
		((ToolStrip)Menu).Items.Add((ToolStripItem)(object)opacityMenu);
		ToolStripMenuItem val3 = new ToolStripMenuItem("回到默认位置");
		((ToolStripItem)val3).Click += delegate
		{
			ResetPosition();
		};
		((ToolStrip)Menu).Items.Add((ToolStripItem)(object)val3);
		ToolStripMenuItem val4 = new ToolStripMenuItem("开机启动");
		((ToolStripItem)val4).Click += delegate
		{
			ToggleAutoStart();
		};
		((ToolStrip)Menu).Items.Add((ToolStripItem)(object)val4);
		((ToolStrip)Menu).Items.Add((ToolStripItem)new ToolStripSeparator());
		BuildMenu();
		((ToolStrip)Menu).Items.Add((ToolStripItem)new ToolStripSeparator());
		ToolStripMenuItem val5 = new ToolStripMenuItem("关于");
		((ToolStripItem)val5).Click += delegate
		{
			ShowAbout();
		};
		((ToolStrip)Menu).Items.Add((ToolStripItem)(object)val5);
		ToolStripMenuItem val6 = new ToolStripMenuItem("退出");
		((ToolStripItem)val6).Click += delegate
		{
			((Form)this).Close();
		};
		((ToolStrip)Menu).Items.Add((ToolStripItem)(object)val6);
	}

	private void ClearTopMostStyle()
	{
		if (!((Form)this).TopMost && ((Control)this).IsHandleCreated)
		{
			SetWindowPos(((Control)this).Handle, HwndNotTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
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
		((Form)this).TopMost = false;
		SendToBack();
	}

	private void LeaveDesktopLayer()
	{
		SetParent(((Control)this).Handle, IntPtr.Zero);
		SetWindowLongW(((Control)this).Handle, -16, new IntPtr(GetWindowLongW(((Control)this).Handle, -16).ToInt32() & -1073741825));
		SetWindowLongW(((Control)this).Handle, -20, new IntPtr(GetWindowLongW(((Control)this).Handle, -20).ToInt32() & -1073741825));
		SetWindowPos(((Control)this).Handle, new IntPtr(-2), 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | 0x20u);
		ShowWindow(((Control)this).Handle, 5);
		inDesktopLayer = false;
		RestorePosition();
	}

	private void PollFullscreen()
	{
		if (inDesktopLayer || closing || ((Control)this).IsDisposed || !((Control)this).IsHandleCreated || WantsTopMostWhileActive)
		{
			return;
		}
		if (Store.Settings.DesktopLayer != false)
		{
			if (((Control)this).Visible && !menuOpen)
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
			IntPtr intPtr = GetTopWindow(IntPtr.Zero);
			uint currentProcessId = GetCurrentProcessId();
			Rectangle bounds = Screen.PrimaryScreen.Bounds;
			double num = (double)bounds.Width * (double)bounds.Height;
			while (intPtr != IntPtr.Zero && !flag)
			{
				GetWindowThreadProcessId(intPtr, out var processId);
				if (processId != currentProcessId && IsWindowVisible(intPtr) && !IsShellOverlay(intPtr))
				{
					int num2 = GetWindowLongW(intPtr, -20).ToInt32();
					if ((num2 & 0x80) == 0 && (((uint)num2 & 8u) != 0 || intPtr == GetForegroundWindow()) && GetWindowRect(intPtr, out var rect) && (double)(rect.Right - rect.Left) * (double)(rect.Bottom - rect.Top) >= num * 0.92)
					{
						flag = true;
					}
				}
				intPtr = GetWindow(intPtr, 2u);
			}
			if (flag && !hiddenForFullscreen && ((Control)this).Visible)
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

	private void SendToBack()
	{
		if (((Control)this).IsDisposed || !((Control)this).IsHandleCreated)
		{
			return;
		}
		try
		{
			if (NeedsSendToBack())
			{
				if (!WantsTopMostWhileActive)
				{
					zOrderWasTopMost = ((Form)this).TopMost;
				}
				if (((Form)this).TopMost)
				{
					((Form)this).TopMost = false;
				}
				SetWindowPos(((Control)this).Handle, HwndNotTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
				SetWindowPos(((Control)this).Handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
			}
		}
		catch
		{
		}
	}

	private bool NeedsSendToBack()
	{
		if (((Form)this).TopMost)
		{
			return true;
		}
		IntPtr intPtr = GetTopWindow(IntPtr.Zero);
		uint currentProcessId = GetCurrentProcessId();
		int num = 0;
		while (intPtr != IntPtr.Zero && num++ < 256)
		{
			if (intPtr == ((Control)this).Handle)
			{
				return false;
			}
			if (IsWindowVisible(intPtr))
			{
				GetWindowThreadProcessId(intPtr, out var processId);
				if (processId != currentProcessId && !IsShellOverlay(intPtr) && (GetWindowLongW(intPtr, -20).ToInt32() & 0x80) == 0)
				{
					return true;
				}
			}
			intPtr = GetWindow(intPtr, 2u);
		}
		return true;
	}

	private void RestoreZOrder()
	{
		if (((Control)this).IsDisposed || !((Control)this).IsHandleCreated)
		{
			return;
		}
		try
		{
			if (zOrderWasTopMost || Store.Settings.TopMost)
			{
				((Form)this).TopMost = true;
				return;
			}
			if (((Form)this).TopMost)
			{
				((Form)this).TopMost = false;
			}
			ClearTopMostStyle();
			SetWindowPos(((Control)this).Handle, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
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
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		Version version = typeof(WidgetForm).Assembly.GetName().Version;
		string text = ((version == null) ? "2.0.1" : (version.Major + "." + version.Minor + "." + version.Build));
		MessageBox.Show((IWin32Window)(object)this, DisplayName + "  版本 " + text + "（当前最新）\n\n数据目录：" + AppPaths.DataDirectory + "\n\n右键菜单可锁定位置、调整字号与背景透明度、设置开机启动。", DisplayName, (MessageBoxButtons)0, (MessageBoxIcon)64);
	}

	private void RefreshMenuState()
	{
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Expected O, but got Unknown
		//IL_0119: Unknown result type (might be due to invalid IL or missing references)
		//IL_011f: Expected O, but got Unknown
		OnMenuOpening();
		if (lockItem != null)
		{
			((ToolStripItem)lockItem).Text = (Store.Settings.Locked ? "解锁位置" : "锁定位置");
		}
		if (desktopLayerItem != null)
		{
			desktopLayerItem.Checked = Store.Settings.DesktopLayer.GetValueOrDefault(true);
		}
		if (fontMenu != null)
		{
			foreach (ToolStripItem item in (ArrangedElementCollection)((ToolStripDropDownItem)fontMenu).DropDownItems)
			{
				object obj = (object)item;
				ToolStripMenuItem val = (ToolStripMenuItem)((obj is ToolStripMenuItem) ? obj : null);
				if (val != null && ((ToolStripItem)val).Tag != null)
				{
					val.Checked = string.Equals(((ToolStripItem)val).Tag.ToString(), "font:" + Store.Settings.FontSize, StringComparison.Ordinal);
				}
			}
		}
		if (opacityMenu != null)
		{
			foreach (ToolStripItem item2 in (ArrangedElementCollection)((ToolStripDropDownItem)opacityMenu).DropDownItems)
			{
				object obj2 = (object)item2;
				ToolStripMenuItem val2 = (ToolStripMenuItem)((obj2 is ToolStripMenuItem) ? obj2 : null);
				if (val2 != null && ((ToolStripItem)val2).Tag != null)
				{
					val2.Checked = string.Equals(((ToolStripItem)val2).Tag.ToString(), "opacity:" + Store.Settings.Opacity, StringComparison.Ordinal);
				}
			}
		}
		ToolStripMenuItem val3 = FindMenuItem("开机启动");
		if (val3 != null)
		{
			val3.Checked = IsAutoStartEnabled();
		}
	}

	protected ToolStripMenuItem FindMenuItem(string text)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Expected O, but got Unknown
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Expected O, but got Unknown
		foreach (ToolStripItem item in (ArrangedElementCollection)((ToolStrip)Menu).Items)
		{
			ToolStripItem val = item;
			if (val.Text == text)
			{
				return (ToolStripMenuItem)(object)((val is ToolStripMenuItem) ? val : null);
			}
			ToolStripMenuItem val2 = (ToolStripMenuItem)(object)((val is ToolStripMenuItem) ? val : null);
			if (val2 == null)
			{
				continue;
			}
			foreach (ToolStripItem item2 in (ArrangedElementCollection)((ToolStripDropDownItem)val2).DropDownItems)
			{
				ToolStripItem val3 = item2;
				if (val3.Text == text)
				{
					return (ToolStripMenuItem)(object)((val3 is ToolStripMenuItem) ? val3 : null);
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
		ShowHint(Store.Settings.Locked ? "已锁定位置：拖动无效，右键菜单可解锁" : "已解锁位置：按住左键即可自由拖动");
	}

	protected virtual void ShowHint(string message)
	{
		try
		{
			new HintForm(message).ShowNear(AnchorBoundsOnScreen(), PlacementArea);
		}
		catch
		{
		}
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
			else if (Canvas != null && ((Control)Canvas).IsHandleCreated)
			{
				((Control)Canvas).Invalidate();
				((Control)Canvas).Update();
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
			else if (Canvas != null && ((Control)Canvas).IsHandleCreated)
			{
				((Control)Canvas).Invalidate();
				((Control)Canvas).Update();
			}
			else
			{
				((Control)this).Invalidate();
				((Control)this).Update();
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
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected O, but got Unknown
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
		((Control)this).Bounds = defaultBounds;
	}

	protected void RestorePosition()
	{
		Rectangle placementArea = PlacementArea;
		int num = Math.Max(((Control)this).Width, 300);
		int num2 = Math.Max(((Control)this).Height, 200);
		int x = Store.Settings.X;
		int y = Store.Settings.Y;
		if (Store.Settings.FirstRun || (x <= 0 && y <= 0))
		{
			Rectangle defaultBounds = DefaultBounds;
			x = defaultBounds.Left;
			y = defaultBounds.Top;
			Store.Settings.X = x;
			Store.Settings.Y = y;
			Store.Settings.FirstRun = false;
			Store.Save();
		}
		else
		{
			int num3 = placementArea.Right - num;
			int num4 = placementArea.Bottom - num2;
			x = ((num3 < placementArea.Left) ? placementArea.Left : Math.Max(placementArea.Left, Math.Min(x, num3)));
			y = ((num4 < placementArea.Top) ? placementArea.Top : Math.Max(placementArea.Top, Math.Min(y, num4)));
		}
		((Control)this).Bounds = new Rectangle(x, y, num, num2);
	}

	private void SavePosition()
	{
		if (!Store.Settings.Locked && !suppressSave)
		{
			Store.Settings.X = ((Control)this).Left;
			Store.Settings.Y = ((Control)this).Top;
			Store.Save();
		}
	}

	protected virtual void OnCanvasMouseDown(object sender, MouseEventArgs e)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Invalid comparison between Unknown and I4
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Invalid comparison between Unknown and I4
		if ((int)e.Button == 1048576)
		{
			try
			{
				PopupPanel.CloseAll();
			}
			catch
			{
			}
		}
		if ((int)e.Button == 1048576 && !Store.Settings.Locked)
		{
			dragStart = new Point(e.X, e.Y);
		}
	}

	protected virtual void OnCanvasMouseMove(object sender, MouseEventArgs e)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Invalid comparison between Unknown and I4
		if ((int)e.Button == 1048576 && !(dragStart == Point.Empty) && !Store.Settings.Locked)
		{
			int val = ((Control)this).Left + e.X - dragStart.X;
			int val2 = ((Control)this).Top + e.Y - dragStart.Y;
			Rectangle placementArea = PlacementArea;
			int num = Math.Max(60, Math.Min(((Control)this).Height, DragClampHeight));
			val = Math.Max(placementArea.Left, Math.Min(val, Math.Max(placementArea.Left, placementArea.Right - ((Control)this).Width)));
			val2 = Math.Max(placementArea.Top, Math.Min(val2, Math.Max(placementArea.Top, placementArea.Bottom - num)));
			((Control)this).Bounds = new Rectangle(val, val2, ((Control)this).Width, ((Control)this).Height);
		}
	}

	protected virtual void OnCanvasMouseUp(object sender, MouseEventArgs e)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Invalid comparison between Unknown and I4
		dragStart = Point.Empty;
		if ((int)e.Button == 1048576)
		{
			SavePosition();
		}
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Expected O, but got Unknown
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
		HintForm.DismissCurrent();
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
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected O, but got Unknown
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
			if (!((Control)this).Visible)
			{
				((Control)this).Show();
			}
			((Control)this).BringToFront();
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
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
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
			MessageBox.Show((IWin32Window)(object)this, "设置开机启动失败：" + ex.Message, DisplayName, (MessageBoxButtons)0, (MessageBoxIcon)48);
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
			((Image)surface).Dispose();
			surface = null;
		}
		base.Dispose(disposing);
	}
}
