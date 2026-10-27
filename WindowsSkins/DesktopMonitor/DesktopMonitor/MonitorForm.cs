using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.Layout;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace DesktopMonitor;

internal sealed class MonitorForm : Form
{
	private static int refreshMilliseconds = 2000;

	private static readonly StringFormat MeasureFormat = StringFormat.GenericTypographic;

	private const uint SwpNoZOrder = 4u;

	private const uint SwpNoSize = 1u;

	private const uint SwpNoActivate = 16u;

	private const string StartupRegistryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string StartupRegistryName = "DesktopMonitor";

	private const string DiskLabel = "硬盘 ";

	private const string CpuWidthSample = "CPU 100%";

	private const string MemoryWidthSample = "内存 100.0% (99.9/99.9G)";

	private const string NetworkWidthSample = "网速 ↓999MB/s ↑999MB/s";

	// 天气列的"占位宽度"模板，必须是真实文本的上界：温度 PadLeft(5)、湿度 PadLeft(3)、
	// 风速 PadLeft(3) 都用数字补位，所以这里全用 9 占满，城市名运行时再换成当前城市。
	// 只要真实文本永远不超过它，天气异步到达就不会让整行重新排版、窗口重新变宽。
	private const string WeatherWidthSample = "北京市 雷雨 999.9℃ 湿度100% 西北风999km/h 更新23:59";

	private const int WidthQuantum = 40;

	private static readonly Color CpuColor = Color.FromArgb(80, 180, 255);

	private static readonly Color MemoryColor = Color.FromArgb(255, 220, 50);

	private static readonly Color WeatherColor = Color.FromArgb(200, 180, 255);

	private static readonly Color NetworkColor = Color.FromArgb(100, 255, 100);

	private static readonly Color[] DiskPalette = new Color[3]
	{
		Color.White,
		Color.FromArgb(255, 220, 50),
		Color.FromArgb(80, 180, 255)
	};

	private static readonly Color DiskLabelColor = Color.White;

	private const int NetworkValuePadding = 5;

	private const float SegmentGapRatio = 0.5f;

	private const float CpuGapRatio = 0.5f;

	private const float MemoryGapRatio = 0.4f;

	private const float WeatherGapRatio = 0.4f;

	private const float WidthSafetyMargin = 24f;

	private const float HorizontalPadding = 20f;

	private const float InkProbeTop = 3f;

	private readonly object dataLock = new object();

	private AppSettings settings;

	private WeatherService weatherService;

	private MonitorPanel panel;

	private const bool layeredMode = true;

	private ContextMenuStrip menu = new ContextMenuStrip();

	private NotifyIcon trayIcon;

	private Font mainFont;

	private Font menuFont;

	private readonly Brush cpuBrush = (Brush)new SolidBrush(CpuColor);

	private readonly Brush memoryBrush = (Brush)new SolidBrush(MemoryColor);

	private readonly Brush weatherBrush = (Brush)new SolidBrush(WeatherColor);

	private readonly Brush networkBrush = (Brush)new SolidBrush(NetworkColor);

	private readonly Brush diskLabelBrush = (Brush)new SolidBrush(DiskLabelColor);

	private readonly Brush[] diskBrushes;

	private readonly ToolStripMenuItem lockItem = new ToolStripMenuItem();

	private readonly ToolStripMenuItem sizeMenu = new ToolStripMenuItem();

	private readonly ToolStripMenuItem cityMenuItem = new ToolStripMenuItem();

	private readonly ToolStripMenuItem quickCityMenu = new ToolStripMenuItem();

	private readonly ToolStripMenuItem showWeatherItem = new ToolStripMenuItem();

	private readonly ToolStripMenuItem refreshMenu = new ToolStripMenuItem();

	private readonly ToolStripMenuItem autoStartItem = new ToolStripMenuItem();

	private readonly ToolStripMenuItem desktopLayerItem = new ToolStripMenuItem();

	private readonly ToolStripMenuItem darkBackgroundItem = new ToolStripMenuItem();

	private readonly ToolStripMenuItem opacityMenu = new ToolStripMenuItem("背景透明度");

	private readonly Timer dataTimer = new Timer();

	private readonly Timer reassertTimer = new Timer();

	private readonly Timer centerTimer = new Timer();

	private PerformanceCounter cpuCounter;

	private string[] segments = new string[5]
	{
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty
	};

	private string[] diskPieces = new string[1] { "硬盘 " };

	private float[] diskPieceWidths = new float[0];

	private float[] diskPieceFloors = new float[0];

	private int diskDriveCount;

	private int layoutDriveCount;

	private long previousReceivedBytes = -1L;

	private long previousSentBytes = -1L;

	private Point dragStart = Point.Empty;

	private int targetWidth = 1000;

	private IntPtr desktopHandle = IntPtr.Zero;

	private bool desktopAttached;

	private bool layoutReady;

	private bool weatherWarmupStarted;

	private bool fontJustChanged;

	private bool isClosing;

	private string layoutSignature = string.Empty;

	private float fixedCpuWidth;

	private float fixedMemoryWidth;

	private float fixedWeatherWidth;

	private float fixedDiskWidth;

	private float fixedNetworkWidth;

	private float segmentGap;

	private string cpuLabel = "CPU";

	private string cpuValue = " 0%";

	private float gapAfterCpu;

	private float gapAfterMemory;

	private float gapAfterWeather;

	private float drawOriginX = 20f;

	private float sampleInkTop;

	private float sampleInkHeight;

	private Icon appIcon;

	private static readonly Color KeyColor = Color.FromArgb(1, 2, 3);

	private Bitmap surface;

	private bool rendering;

	private Point lastRenderedAt;

	private bool hasRendered;

	private readonly Rectangle[] segmentRects = new Rectangle[5];

	private readonly string[] paintedSegments = new string[5];

	private bool segmentRectsReady;

	private static int RefreshMilliseconds => refreshMilliseconds;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ExStyle |= 0x80;
			createParams.ExStyle |= 0x80000;
			return createParams;
		}
	}

	public MonitorForm()
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Expected O, but got Unknown
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Expected O, but got Unknown
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Expected O, but got Unknown
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Expected O, but got Unknown
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Expected O, but got Unknown
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Expected O, but got Unknown
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Expected O, but got Unknown
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Expected O, but got Unknown
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Expected O, but got Unknown
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Expected O, but got Unknown
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Expected O, but got Unknown
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Expected O, but got Unknown
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Expected O, but got Unknown
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Expected O, but got Unknown
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Expected O, but got Unknown
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Expected O, but got Unknown
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Expected O, but got Unknown
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Expected O, but got Unknown
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Expected O, but got Unknown
		//IL_025b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0261: Expected O, but got Unknown
		//IL_02da: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e4: Expected O, but got Unknown
		//IL_02f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fb: Expected O, but got Unknown
		//IL_0308: Unknown result type (might be due to invalid IL or missing references)
		//IL_0312: Expected O, but got Unknown
		//IL_031f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0329: Expected O, but got Unknown
		//IL_0331: Unknown result type (might be due to invalid IL or missing references)
		//IL_033b: Expected O, but got Unknown
		//IL_0343: Unknown result type (might be due to invalid IL or missing references)
		//IL_034d: Expected O, but got Unknown
		//IL_0355: Unknown result type (might be due to invalid IL or missing references)
		//IL_035f: Expected O, but got Unknown
		//IL_0367: Unknown result type (might be due to invalid IL or missing references)
		//IL_0371: Expected O, but got Unknown
		//IL_03ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_03bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03df: Expected O, but got Unknown
		settings = AppSettings.Load();
		settings.Save();
		weatherService = new WeatherService(settings);
		cpuCounter = SystemMetrics.CreateCpuCounter();
		mainFont = Fonts.Widget(settings.FontSize);
		menuFont = Fonts.Menu(12f);
		diskBrushes = (Brush[])(object)new Brush[DiskPalette.Length];
		for (int i = 0; i < DiskPalette.Length; i++)
		{
			diskBrushes[i] = (Brush)new SolidBrush(DiskPalette[i]);
		}
		((Form)this).FormBorderStyle = (FormBorderStyle)0;
		((Form)this).TopMost = false;
		((Form)this).StartPosition = (FormStartPosition)0;
		((Form)this).ShowInTaskbar = false;
		ApplyBackgroundStyle();
		appIcon = AppIcon.Load(32);
		((Form)this).Icon = appIcon;
		UpdateSize();
		MonitorPanel monitorPanel = new MonitorPanel();
		((Control)monitorPanel).Dock = (DockStyle)5;
		((Control)monitorPanel).BackColor = Color.Transparent;
		panel = monitorPanel;
		((Control)panel).Paint += new PaintEventHandler(PanelPaint);
		((Control)panel).MouseDown += new MouseEventHandler(PanelMouseDown);
		((Control)panel).MouseMove += new MouseEventHandler(PanelMouseMove);
		((Control)panel).MouseUp += new MouseEventHandler(PanelMouseUp);
		((Control)this).MouseDown += new MouseEventHandler(FormMouseDown);
		((Control)this).MouseMove += new MouseEventHandler(FormMouseMove);
		((Control)this).MouseUp += new MouseEventHandler(FormMouseUp);
		((Form)this).FormClosed += (FormClosedEventHandler)delegate
		{
			Cleanup();
		};
		BuildMenu();
		((Control)this).ContextMenuStrip = menu;
		((Control)panel).ContextMenuStrip = menu;
		((ToolStripDropDown)menu).Opening += delegate
		{
			RefreshAutoStartItem();
			MarkDesktopLayerItem();
		};
		trayIcon = new NotifyIcon
		{
			Icon = CreateTrayIcon(),
			Text = "桌面天气CPU网速挂件",
			ContextMenuStrip = menu,
			Visible = false
		};
		trayIcon.DoubleClick += delegate
		{
			((Form)this).WindowState = (FormWindowState)0;
			((Form)this).Activate();
		};
		dataTimer.Interval = RefreshMilliseconds;
		dataTimer.Tick += delegate
		{
			TimerTick();
		};
		dataTimer.Start();
		MonitorForm self = this;
		centerTimer.Interval = 100;
		centerTimer.Tick += delegate(object sender, EventArgs args)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Expected O, but got Unknown
			StopTimer((Timer)sender);
			// 这个 100ms 的"稳定后再居中"跑在 OnShown 之后：用户拖动过、已经按记住的坐标
			// 摆好位的话，再居中一次就把位置抢回去了，所以只在没有记住位置时才居中。
			if (!self.isClosing && !self.settings.UserMoved)
			{
				self.CenterOnTaskbar();
			}
		};
		centerTimer.Start();
		reassertTimer.Interval = 2500;
		reassertTimer.Tick += delegate
		{
			if (!isClosing)
			{
				if (desktopAttached && !settings.WindowX.HasValue)
				{
					CenterOnTaskbar();
				}
				RememberPosition();
				EnsurePosition();
				if (SurfaceOutOfPlace())
				{
					RenderSurface();
				}
			}
		};
		reassertTimer.Start();
		// 窗口显示之前先把本地数据读好（硬盘有几块盘只有读了才知道）。以前第一帧是空数据，
		// 硬盘那一列宽度 0，窗口按窄的算，盘读到了再变宽重画 —— 就是"先残缺一点点、过两秒才齐"。
		// 天气在这里不读（要联网），见 StartWeatherWarmup。
		try
		{
			LoadData();
		}
		catch
		{
		}
	}

	private void EnsurePosition()
	{
		if (!isClosing && ((Control)this).IsHandleCreated && ((Control)this).Visible && settings.PositionLocked && settings.UserMoved && !(dragStart != Point.Empty) && settings.WindowX.HasValue && settings.WindowY.HasValue && NativeMethods.GetWindowRect(((Control)this).Handle, out var rect) && (rect.Left != settings.WindowX.Value || rect.Top != settings.WindowY.Value))
		{
			if (desktopAttached && desktopHandle != IntPtr.Zero)
			{
				NativeMethods.SetWindowPos(((Control)this).Handle, IntPtr.Zero, settings.WindowX.Value, settings.WindowY.Value, 0, 0, 21u);
			}
			else
			{
				((Form)this).Location = new Point(settings.WindowX.Value, settings.WindowY.Value);
			}
		}
	}

	private static void StopTimer(Timer timer)
	{
		timer.Stop();
		((Component)(object)timer).Dispose();
	}

	private static string BuildVersionNumber()
	{
		Version version = typeof(MonitorForm).Assembly.GetName().Version;
		if (!(version == null))
		{
			return version.Major + "." + version.Minor + "." + version.Build;
		}
		return "1.2.4";
	}

	private static string BuildVersionText()
	{
		return "版本 " + BuildVersionNumber() + "（当前最新）";
	}

	private void ApplyBackgroundStyle()
	{
		((Control)this).BackColor = Color.Black;
		((Form)this).Opacity = 1.0;
	}

	private int BackgroundAlphaValue()
	{
		int backgroundOpacity = settings.BackgroundOpacity;
		if (backgroundOpacity <= 0)
		{
			return 0;
		}
		if (backgroundOpacity >= 100)
		{
			return 255;
		}
		return (int)Math.Round(255.0 * (double)backgroundOpacity / 100.0);
	}

	private double BackgroundOpacity()
	{
		int num = settings.BackgroundOpacity;
		if (num < 0)
		{
			num = 0;
		}
		if (num > 100)
		{
			num = 100;
		}
		return (double)num / 100.0;
	}

	private void ToggleDarkBackground()
	{
		settings.DarkBackground = !settings.DarkBackground;
		settings.Save();
		ApplyBackgroundStyle();
		UpdateSize();
		CenterOnTaskbar();
		RequestRepaint();
		MarkDarkBackgroundItem();
	}

	private void BuildOpacityMenu()
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Expected O, but got Unknown
		foreach (ToolStripItem item in (ArrangedElementCollection)((ToolStripDropDownItem)opacityMenu).DropDownItems)
		{
			object obj = (object)item;
			ToolStripMenuItem val = (ToolStripMenuItem)((obj is ToolStripMenuItem) ? obj : null);
			if (val != null && ((ToolStripItem)val).Tag != null)
			{
				val.Checked = string.Equals(((ToolStripItem)val).Tag.ToString(), "opacity:" + settings.BackgroundOpacity, StringComparison.Ordinal);
			}
		}
	}

	private void MarkDarkBackgroundItem()
	{
		if (darkBackgroundItem != null)
		{
			darkBackgroundItem.Checked = settings.DarkBackground;
			((ToolStripItem)darkBackgroundItem).Text = (settings.DarkBackground ? "半透明黑底（已开启）" : "半透明黑底");
		}
	}

	private void RenderSurface()
	{
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected O, but got Unknown
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Invalid comparison between Unknown and I4
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Expected O, but got Unknown
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
				PanelPaint(this, new PaintEventArgs(val, new Rectangle(0, 0, width, height)));
				val.Flush();
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			if (NativeMethods.GetWindowRect(((Control)this).Handle, out var rect))
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

	private bool SurfaceOutOfPlace()
	{
		if (!hasRendered || !((Control)this).IsHandleCreated || !((Control)this).Visible)
		{
			return false;
		}
		if (!NativeMethods.GetWindowRect(((Control)this).Handle, out var rect))
		{
			return false;
		}
		if (rect.Left == lastRenderedAt.X)
		{
			return rect.Top != lastRenderedAt.Y;
		}
		return true;
	}

	private void PaintNow()
	{
		RenderSurface();
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		RenderSurface();
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		RenderSurface();
	}

	protected override void OnMove(EventArgs e)
	{
		base.OnMove(e);
		RenderSurface();
	}

	protected override void OnShown(EventArgs e)
	{
		//IL_0191: Unknown result type (might be due to invalid IL or missing references)
		//IL_0196: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c5: Unknown result type (might be due to invalid IL or missing references)
		base.OnShown(e);
		AttachToDesktop();
		CenterOnTaskbar();
		// 默认位置就是"紧贴任务栏上方居中"（上面 CenterOnTaskbar 刚摆好的）。
		// 只有用户真的拖动过挂件，才用记住的坐标；否则窗口一变宽就还回居中，
		// 不会被上一次窄窗口留下的老坐标钉在屏幕中间。
		if (settings.UserMoved && settings.WindowX.HasValue && settings.WindowY.HasValue)
		{
			MoveWidgetTo(settings.WindowX.Value, settings.WindowY.Value);
			WriteDiag("OnShown: 使用记住的位置 " + settings.WindowX + "," + settings.WindowY);
		}
		WriteDiag("OnShown: handle=" + ((Control)this).Handle + " IsHandleCreated=" + ((Control)this).IsHandleCreated + " Visible=" + ((Control)this).Visible + " Bounds=" + ((Control)this).Bounds.ToString() + " desktopHandle=" + desktopHandle + " desktopAttached=" + desktopAttached + " Screen=" + Screen.PrimaryScreen.WorkingArea);
		MonitorForm self = this;
		Timer val = new Timer
		{
			Interval = 2000
		};
		val.Tick += delegate(object sender, EventArgs args)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Expected O, but got Unknown
			//IL_005e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0063: Unknown result type (might be due to invalid IL or missing references)
			StopTimer((Timer)sender);
			string[] obj = new string[10]
			{
				"2秒后: handle=",
				((Control)self).Handle.ToString(),
				" Visible=",
				((Control)self).Visible.ToString(),
				" WindowState=",
				null,
				null,
				null,
				null,
				null
			};
			FormWindowState windowState = ((Form)self).WindowState;
			obj[5] = windowState.ToString();
			obj[6] = " Bounds=";
			obj[7] = ((Control)self).Bounds.ToString();
			obj[8] = " desktopAttached=";
			bool flag = self.desktopAttached;
			obj[9] = flag.ToString();
			WriteDiag(string.Concat(obj));
		};
		val.Start();
		Timer val2 = new Timer
		{
			Interval = 50
		};
		val2.Tick += delegate(object sender, EventArgs args)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Expected O, but got Unknown
			StopTimer((Timer)sender);
			if (!self.isClosing)
			{
				self.TimerTick();
			}
		};
		val2.Start();
	}

	// 天气要联网（超时 4 秒）。以前第一帧也在这儿同步等，结果 CPU/内存/硬盘/网速
	// 全被拖住：窗口还停在初始 1000 宽、五列宽度都是 0，文字全叠在 x=0 上，
	// 看起来就是"先残缺蹦出来一点点，过两秒才完整"。改成后台拉、拉到后刷新一次。
	private void StartWeatherWarmup()
	{
		if (weatherWarmupStarted || isClosing)
		{
			return;
		}
		weatherWarmupStarted = true;
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				weatherService.GetWeatherText();
			}
			catch
			{
			}
			if (isClosing)
			{
				return;
			}
			if (weatherService.PeekCachedText().Length == 0)
			{
				weatherWarmupStarted = false;
			}
			TimerTick();
		});
	}

	private static void WriteDiag(string message)
	{
		try
		{
			File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopMonitor", "startup.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine);
		}
		catch
		{
		}
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		base.OnFormClosed(e);
		Cleanup();
	}

	private void Cleanup()
	{
		if (!isClosing)
		{
			isClosing = true;
			settings.Save();
			StopTimer(dataTimer);
			StopTimer(reassertTimer);
			StopTimer(centerTimer);
			TryCleanup((IDisposable)cpuCounter);
			TryCleanup((IDisposable)trayIcon);
			TryCleanup((IDisposable)appIcon);
			TryCleanup((IDisposable)mainFont);
			TryCleanup((IDisposable)menuFont);
		}
	}

	private static void TryCleanup(IDisposable resource)
	{
		try
		{
			resource?.Dispose();
		}
		catch
		{
		}
	}

	private static Icon CreateTrayIcon()
	{
		return AppIcon.Load(16);
	}

	private void UpdateSize()
	{
		((Control)this).Height = CalcPanelHeight();
		layoutReady = false;
		fontJustChanged = true;
		segmentGap = settings.FontSize * 0.5f;
		gapAfterCpu = settings.FontSize * 0.5f;
		gapAfterMemory = settings.FontSize * 0.4f;
		gapAfterWeather = settings.FontSize * 0.4f;
		RequestRepaint();
	}

	private int CalcPanelHeight()
	{
		int num = (int)Math.Ceiling((double)mainFont.Height);
		return ((num < 14) ? 14 : num) + 14;
	}

	private void AttachToDesktop()
	{
		desktopAttached = false;
		if (!settings.DesktopLayer)
		{
			return;
		}
		try
		{
			if (desktopHandle == IntPtr.Zero)
			{
				desktopHandle = NativeMethods.FindWindow("Progman", null);
			}
			if (desktopHandle != IntPtr.Zero && !NativeMethods.IsWindowVisible(desktopHandle))
			{
				desktopHandle = NativeMethods.FindWindow("Progman", null);
			}
			if (desktopHandle != IntPtr.Zero && ((Control)this).Handle != IntPtr.Zero && NativeMethods.IsWindowVisible(desktopHandle))
			{
				NativeMethods.SetParent(((Control)this).Handle, desktopHandle);
				int windowLongStyle = NativeMethods.GetWindowLongStyle(((Control)this).Handle);
				windowLongStyle |= 0x40000000;
				NativeMethods.SetWindowLongStyle(((Control)this).Handle, windowLongStyle);
				NativeMethods.SetWindowPos(((Control)this).Handle, IntPtr.Zero, 0, 0, 0, 0, 51u);
				NativeMethods.ShowWindow(((Control)this).Handle, 5);
				if (!NativeMethods.IsWindowVisible(((Control)this).Handle))
				{
					NativeMethods.SetParent(((Control)this).Handle, IntPtr.Zero);
					windowLongStyle &= -1073741825;
					windowLongStyle |= -2080374784;
					NativeMethods.SetWindowLongStyle(((Control)this).Handle, windowLongStyle);
					NativeMethods.ShowWindow(((Control)this).Handle, 5);
					settings.DesktopLayer = false;
					settings.Save();
				}
				else
				{
					desktopAttached = true;
				}
			}
			else
			{
				settings.DesktopLayer = false;
				settings.Save();
			}
		}
		catch
		{
			desktopAttached = false;
		}
	}

	private void SetDesktopLayer(bool on)
	{
		settings.DesktopLayer = on;
		settings.Save();
		try
		{
			if (on)
			{
				if (desktopHandle == IntPtr.Zero)
				{
					desktopHandle = NativeMethods.FindWindow("Progman", null);
				}
				if (desktopHandle != IntPtr.Zero && !NativeMethods.IsWindowVisible(desktopHandle))
				{
					desktopHandle = NativeMethods.FindWindow("Progman", null);
				}
				if (desktopHandle != IntPtr.Zero && NativeMethods.IsWindowVisible(desktopHandle))
				{
					NativeMethods.SetParent(((Control)this).Handle, desktopHandle);
					int windowLongStyle = NativeMethods.GetWindowLongStyle(((Control)this).Handle);
					windowLongStyle |= 0x40000000;
					NativeMethods.SetWindowLongStyle(((Control)this).Handle, windowLongStyle);
					NativeMethods.SetWindowPos(((Control)this).Handle, IntPtr.Zero, 0, 0, 0, 0, 51u);
					NativeMethods.ShowWindow(((Control)this).Handle, 5);
					if (NativeMethods.IsWindowVisible(((Control)this).Handle))
					{
						desktopAttached = true;
					}
					else
					{
						NativeMethods.SetParent(((Control)this).Handle, IntPtr.Zero);
						windowLongStyle &= -1073741825;
						windowLongStyle |= -2080374784;
						NativeMethods.SetWindowLongStyle(((Control)this).Handle, windowLongStyle);
						NativeMethods.ShowWindow(((Control)this).Handle, 5);
						settings.DesktopLayer = false;
						settings.Save();
					}
				}
				else
				{
					settings.DesktopLayer = false;
					settings.Save();
				}
			}
			else if (desktopAttached)
			{
				NativeMethods.SetParent(((Control)this).Handle, IntPtr.Zero);
				int windowLongStyle2 = NativeMethods.GetWindowLongStyle(((Control)this).Handle);
				windowLongStyle2 &= -1073741825;
				windowLongStyle2 |= -2080374784;
				NativeMethods.SetWindowLongStyle(((Control)this).Handle, windowLongStyle2);
				desktopAttached = false;
			}
		}
		catch
		{
			desktopAttached = false;
		}
		UpdateSize();
		CenterOnTaskbar();
		RequestRepaint();
		MarkDesktopLayerItem();
	}

	private void CenterOnTaskbar(int forceWidth = -1)
	{
		Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
		if (forceWidth > 0)
		{
			targetWidth = forceWidth;
		}
		int num = CalcPanelHeight();
		int num2 = targetWidth;
		int num3 = (desktopAttached ? num : Math.Max(((Control)this).Height, num));
		int x = workingArea.Left + (workingArea.Width - num2) / 2;
		int y = workingArea.Bottom - num3;
		if (desktopAttached && ((Control)this).Handle != IntPtr.Zero && desktopHandle != IntPtr.Zero)
		{
			NativeMethods.SetWindowPos(((Control)this).Handle, IntPtr.Zero, x, y, num2, num3, 20u);
			return;
		}
		((Form)this).Location = new Point(x, y);
		if (((Control)this).Width != num2 || ((Control)this).Height != num3)
		{
			((Form)this).Size = new Size(num2, num3);
		}
		if (forceWidth > 0)
		{
			((Form)this).Size = new Size(num2, num3);
		}
	}

	private void ResetToTaskbarCenter()
	{
		settings.UserMoved = false;
		CenterOnTaskbar();
		settings.Save();
	}

	private void MoveWidgetTo(int x, int y)
	{
		if (desktopAttached && ((Control)this).Handle != IntPtr.Zero)
		{
			NativeMethods.SetWindowPos(((Control)this).Handle, IntPtr.Zero, x, y, 0, 0, 21u);
		}
		else
		{
			((Form)this).Location = new Point(x, y);
		}
	}

	private void BuildMenu()
	{
		//IL_0123: Unknown result type (might be due to invalid IL or missing references)
		//IL_0128: Unknown result type (might be due to invalid IL or missing references)
		//IL_013c: Expected O, but got Unknown
		//IL_01b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bb: Expected O, but got Unknown
		//IL_01ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f4: Expected O, but got Unknown
		//IL_034c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0356: Expected O, but got Unknown
		//IL_03b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_03bf: Expected O, but got Unknown
		//IL_0293: Unknown result type (might be due to invalid IL or missing references)
		//IL_029a: Expected O, but got Unknown
		//IL_04a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ad: Expected O, but got Unknown
		//IL_0426: Unknown result type (might be due to invalid IL or missing references)
		//IL_042b: Unknown result type (might be due to invalid IL or missing references)
		//IL_043f: Expected O, but got Unknown
		//IL_05d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_05e0: Expected O, but got Unknown
		//IL_0509: Unknown result type (might be due to invalid IL or missing references)
		//IL_050e: Unknown result type (might be due to invalid IL or missing references)
		//IL_052a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0546: Expected O, but got Unknown
		//IL_0785: Unknown result type (might be due to invalid IL or missing references)
		//IL_078a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0793: Expected O, but got Unknown
		//IL_079e: Unknown result type (might be due to invalid IL or missing references)
		//IL_07a8: Expected O, but got Unknown
		//IL_07c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_07c8: Expected O, but got Unknown
		//IL_0707: Unknown result type (might be due to invalid IL or missing references)
		//IL_070c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0723: Unknown result type (might be due to invalid IL or missing references)
		//IL_072c: Expected O, but got Unknown
		((Control)menu).Font = menuFont;
		((ToolStrip)menu).BackColor = Color.White;
		((ToolStrip)menu).ForeColor = Color.Black;
		((ToolStripDropDownMenu)menu).ShowImageMargin = false;
		((ToolStrip)menu).Items.Clear();
		((ToolStripItem)lockItem).Text = (settings.PositionLocked ? "解锁位置" : "锁定位置");
		((ToolStripItem)lockItem).Click += delegate
		{
			settings.PositionLocked = !settings.PositionLocked;
			((ToolStripItem)lockItem).Text = (settings.PositionLocked ? "解锁位置" : "锁定位置");
			settings.Save();
		};
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)lockItem);
		((ToolStripItem)sizeMenu).Text = "字体大小";
		((ToolStripDropDownItem)sizeMenu).DropDownItems.Clear();
		float[] array = new float[3] { 14f, 16f, 18f };
		string[] array2 = new string[3] { "小 (14号)", "中 (16号)", "大 (18号)" };
		for (int i = 0; i < array.Length; i++)
		{
			float size = array[i];
			ToolStripMenuItem val = new ToolStripMenuItem(array2[i])
			{
				Tag = size
			};
			val.Checked = Math.Abs(settings.FontSize - size) < 0.01f;
			((ToolStripItem)val).Click += delegate
			{
				settings.FontSize = size;
				settings.Save();
				ReplaceMainFont();
				MarkFontSizeItems();
			};
			((ToolStripDropDownItem)sizeMenu).DropDownItems.Add((ToolStripItem)(object)val);
		}
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)sizeMenu);
		ToolStripMenuItem val2 = new ToolStripMenuItem("回到任务栏上方居中");
		((ToolStripItem)val2).Click += delegate
		{
			ResetToTaskbarCenter();
		};
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)val2);
		((ToolStrip)menu).Items.Add((ToolStripItem)new ToolStripSeparator());
		((ToolStripItem)cityMenuItem).Text = "更换城市…";
		((ToolStripItem)cityMenuItem).Click += delegate
		{
			ChangeCity();
		};
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)cityMenuItem);
		((ToolStripItem)quickCityMenu).Text = "常用城市";
		((ToolStripDropDownItem)quickCityMenu).DropDownItems.Clear();
		string[] quickCityNames = CityCatalog.QuickCityNames;
		foreach (string name in quickCityNames)
		{
			CityInfo cityInfo = CityCatalog.Resolve(name);
			if (cityInfo != null)
			{
				CityInfo target = cityInfo;
				ToolStripMenuItem val3 = new ToolStripMenuItem(target.Name);
				((ToolStripItem)val3).Click += delegate
				{
					ApplyCity(target);
				};
				((ToolStripDropDownItem)quickCityMenu).DropDownItems.Add((ToolStripItem)(object)val3);
			}
		}
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)quickCityMenu);
		((ToolStripItem)showWeatherItem).Text = "显示天气";
		showWeatherItem.CheckOnClick = false;
		showWeatherItem.Checked = settings.ShowWeather;
		((ToolStripItem)showWeatherItem).Click += delegate
		{
			settings.ShowWeather = !settings.ShowWeather;
			showWeatherItem.Checked = settings.ShowWeather;
			settings.Save();
			weatherService.InvalidateCache();
			layoutReady = false;
			LoadData();
			RequestRepaint();
		};
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)showWeatherItem);
		ToolStripMenuItem compactItem = new ToolStripMenuItem("精简模式（只显示CPU/内存/网速）");
		compactItem.Checked = settings.CompactMode;
		((ToolStripItem)compactItem).Click += delegate
		{
			settings.CompactMode = !settings.CompactMode;
			compactItem.Checked = settings.CompactMode;
			((ToolStripItem)showWeatherItem).Enabled = !settings.CompactMode;
			settings.Save();
			weatherService.InvalidateCache();
			layoutReady = false;
			LoadData();
			RequestRepaint();
		};
		((ToolStripItem)showWeatherItem).Enabled = !settings.CompactMode;
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)compactItem);
		ToolStripMenuItem val4 = new ToolStripMenuItem("天气刷新间隔");
		int[] array3 = new int[4] { 5, 10, 15, 30 };
		foreach (int num in array3)
		{
			int value = num;
			ToolStripMenuItem val5 = new ToolStripMenuItem(value + " 分钟" + ((value == 15) ? "（默认）" : string.Empty))
			{
				Tag = value
			};
			val5.Checked = settings.WeatherRefreshMinutes == value;
			((ToolStripItem)val5).Click += delegate
			{
				settings.WeatherRefreshMinutes = value;
				settings.Save();
				weatherService.InvalidateCache();
				MarkIntervalItems();
			};
			((ToolStripDropDownItem)val4).DropDownItems.Add((ToolStripItem)(object)val5);
		}
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)val4);
		ToolStripMenuItem val6 = new ToolStripMenuItem("数据刷新间隔");
		array3 = new int[5] { 1, 2, 3, 5, 10 };
		for (int j = 0; j < array3.Length; j++)
		{
			int num2 = array3[j];
			int captured2 = num2;
			ToolStripMenuItem val7 = new ToolStripMenuItem(num2 + " 秒" + ((num2 == 2) ? "（默认，最省）" : string.Empty))
			{
				Tag = "speed:" + captured2,
				Checked = (RefreshMilliseconds == captured2 * 1000)
			};
			((ToolStripItem)val7).Click += delegate
			{
				refreshMilliseconds = captured2 * 1000;
				dataTimer.Stop();
				dataTimer.Interval = refreshMilliseconds;
				dataTimer.Start();
				MarkSpeedItems();
				LoadData();
				RequestRepaint();
			};
			((ToolStripDropDownItem)val6).DropDownItems.Add((ToolStripItem)(object)val7);
		}
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)val6);
		((ToolStripItem)refreshMenu).Text = "立即刷新天气";
		((ToolStripItem)refreshMenu).Click += delegate
		{
			weatherService.InvalidateCache();
			LoadData();
			RequestRepaint();
		};
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)refreshMenu);
		((ToolStrip)menu).Items.Add((ToolStripItem)new ToolStripSeparator());
		((ToolStripItem)autoStartItem).Text = "开机启动";
		autoStartItem.CheckOnClick = false;
		((ToolStripItem)autoStartItem).Click += delegate
		{
			ToggleAutoStart();
		};
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)autoStartItem);
		((ToolStripItem)desktopLayerItem).Text = "贴在桌面底层（图标后面）";
		desktopLayerItem.CheckOnClick = false;
		((ToolStripItem)desktopLayerItem).Click += delegate
		{
			SetDesktopLayer(!settings.DesktopLayer);
		};
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)desktopLayerItem);
		MarkDesktopLayerItem();
		((ToolStripItem)darkBackgroundItem).Text = "半透明黑底";
		darkBackgroundItem.CheckOnClick = false;
		((ToolStripItem)darkBackgroundItem).Click += delegate
		{
			ToggleDarkBackground();
		};
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)darkBackgroundItem);
		MarkDarkBackgroundItem();
		for (int k = 0; k <= 100; k += 10)
		{
			int captured = k;
			ToolStripMenuItem val8 = new ToolStripMenuItem((k == 0) ? "0%（全透明）" : (k + "%"))
			{
				Tag = "opacity:" + k,
				CheckOnClick = false
			};
			((ToolStripItem)val8).Click += delegate
			{
				settings.BackgroundOpacity = captured;
				settings.DarkBackground = captured > 0;
				settings.Save();
				ApplyBackgroundStyle();
				((ToolStrip)menu).Items.Remove((ToolStripItem)(object)opacityMenu);
				MarkDarkBackgroundItem();
				BuildOpacityMenu();
				((Control)this).Invalidate();
			};
			((ToolStripDropDownItem)opacityMenu).DropDownItems.Add((ToolStripItem)(object)val8);
		}
		BuildOpacityMenu();
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)opacityMenu);
		ToolStripMenuItem val9 = new ToolStripMenuItem(BuildVersionText())
		{
			Enabled = false
		};
		((ToolStrip)menu).Items.Add((ToolStripItem)new ToolStripSeparator());
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)val9);
		ToolStripMenuItem val10 = new ToolStripMenuItem("退出");
		((ToolStripItem)val10).Click += delegate
		{
			((Form)this).Close();
		};
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)val10);
	}

	private void MarkFontSizeItems()
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Expected O, but got Unknown
		foreach (ToolStripItem item in (ArrangedElementCollection)((ToolStripDropDownItem)sizeMenu).DropDownItems)
		{
			object obj = (object)item;
			ToolStripMenuItem val = (ToolStripMenuItem)((obj is ToolStripMenuItem) ? obj : null);
			if (val != null && ((ToolStripItem)val).Tag is float)
			{
				val.Checked = Math.Abs(settings.FontSize - (float)((ToolStripItem)val).Tag) < 0.01f;
			}
		}
	}

	private void MarkIntervalItems()
	{
		MarkSpeedItems();
	}

	private void MarkSpeedItems()
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Expected O, but got Unknown
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Expected O, but got Unknown
		ToolStripMenuItem val = null;
		foreach (ToolStripItem item in (ArrangedElementCollection)((ToolStrip)menu).Items)
		{
			object obj = (object)item;
			ToolStripMenuItem val2 = (ToolStripMenuItem)((obj is ToolStripMenuItem) ? obj : null);
			if (val2 != null && ((ToolStripItem)val2).Text == "数据刷新间隔")
			{
				val = val2;
				break;
			}
		}
		if (val == null)
		{
			return;
		}
		foreach (ToolStripItem item2 in (ArrangedElementCollection)((ToolStripDropDownItem)val).DropDownItems)
		{
			object obj2 = (object)item2;
			ToolStripMenuItem val3 = (ToolStripMenuItem)((obj2 is ToolStripMenuItem) ? obj2 : null);
			if (val3 != null && ((ToolStripItem)val3).Tag is string)
			{
				val3.Checked = string.Equals(((ToolStripItem)val3).Tag.ToString(), "speed:" + RefreshMilliseconds, StringComparison.Ordinal);
			}
		}
	}

	private void MarkWeatherIntervalItems()
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Expected O, but got Unknown
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Expected O, but got Unknown
		foreach (ToolStripItem item in (ArrangedElementCollection)((ToolStrip)menu).Items)
		{
			object obj = (object)item;
			ToolStripMenuItem val = (ToolStripMenuItem)((obj is ToolStripMenuItem) ? obj : null);
			if (val == null || ((ToolStripItem)val).Text != "天气刷新间隔")
			{
				continue;
			}
			foreach (ToolStripItem item2 in (ArrangedElementCollection)((ToolStripDropDownItem)val).DropDownItems)
			{
				object obj2 = (object)item2;
				ToolStripMenuItem val2 = (ToolStripMenuItem)((obj2 is ToolStripMenuItem) ? obj2 : null);
				if (val2 != null && ((ToolStripItem)val2).Tag is int)
				{
					val2.Checked = settings.WeatherRefreshMinutes == (int)((ToolStripItem)val2).Tag;
				}
			}
		}
	}

	private void ReplaceMainFont()
	{
		Font obj = mainFont;
		mainFont = Fonts.Widget(settings.FontSize);
		obj.Dispose();
		segmentGap = settings.FontSize * 0.5f;
		gapAfterCpu = settings.FontSize * 0.5f;
		gapAfterMemory = settings.FontSize * 0.4f;
		gapAfterWeather = settings.FontSize * 0.4f;
		((Control)this).Height = CalcPanelHeight();
		layoutReady = false;
		fontJustChanged = true;
		RequestRepaint();
	}

	private void ChangeCity()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Invalid comparison between Unknown and I4
		CitySearchDialog citySearchDialog = new CitySearchDialog(settings.CityName);
		try
		{
			if ((int)((Form)citySearchDialog).ShowDialog((IWin32Window)(object)this) == 1 && citySearchDialog.SelectedCity != null)
			{
				ApplyCity(citySearchDialog.SelectedCity);
			}
		}
		finally
		{
			((IDisposable)(object)citySearchDialog)?.Dispose();
		}
	}

	private void ApplyCity(CityInfo city)
	{
		if (city != null)
		{
			weatherService.ApplyCity(city);
			settings.Save();
			layoutReady = false;
			LoadData();
			RequestRepaint();
		}
	}

	private static bool IsAutoStartEnabled()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: false);
			if (registryKey == null)
			{
				return false;
			}
			object value = registryKey.GetValue("DesktopMonitor");
			if (value == null)
			{
				return false;
			}
			string a = value.ToString().Trim().Trim(new char[1] { '"' });
			string b = Application.ExecutablePath.Trim(new char[1] { '"' });
			return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private void ToggleAutoStart()
	{
		bool flag = IsAutoStartEnabled();
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey == null)
			{
				return;
			}
			if (flag)
			{
				registryKey.DeleteValue("DesktopMonitor", throwOnMissingValue: false);
			}
			else
			{
				registryKey.SetValue("DesktopMonitor", "\"" + Application.ExecutablePath + "\"");
			}
		}
		catch
		{
		}
		RefreshAutoStartItem();
	}

	private void RefreshAutoStartItem()
	{
		if (autoStartItem != null)
		{
			bool flag = IsAutoStartEnabled();
			((ToolStripItem)autoStartItem).Text = (flag ? "开机启动（已开启）" : "开机启动");
			autoStartItem.Checked = flag;
		}
	}

	private void MarkDesktopLayerItem()
	{
		if (desktopLayerItem != null)
		{
			desktopLayerItem.Checked = settings.DesktopLayer && desktopAttached;
			((ToolStripItem)desktopLayerItem).Text = ((settings.DesktopLayer && desktopAttached) ? "贴在桌面底层（已开启）" : "贴在桌面底层（图标后面）");
		}
	}

	private void LoadData()
	{
		lock (dataLock)
		{
			int cpuUsagePct = SystemMetrics.GetCpuUsagePct(cpuCounter);
			MemoryUsage memory = SystemMetrics.GetMemory();
			bool compactMode = settings.CompactMode;
			List<DiskEntry> list = (compactMode ? new List<DiskEntry>() : SystemMetrics.GetDisks());
			NetworkUsage networkSpeed = SystemMetrics.GetNetworkSpeed(ref previousReceivedBytes, ref previousSentBytes, RefreshMilliseconds);
			string text = ((settings.ShowWeather && !compactMode) ? weatherService.PeekCachedText() : string.Empty);
			if (text.Length == 0 && settings.ShowWeather && !compactMode)
			{
				StartWeatherWarmup();
			}
			List<string> list2 = new List<string>();
			if (!compactMode)
			{
				list2.Add("硬盘 ");
				foreach (DiskEntry item in list)
				{
					list2.Add(item.Text);
				}
			}
			diskPieces = list2.ToArray();
			diskDriveCount = list.Count;
			string text2 = (settings.PositionLocked ? " [锁]" : string.Empty);
			cpuLabel = "CPU";
			cpuValue = ((cpuUsagePct < 0) ? " ?%" : (" " + cpuUsagePct.ToString(CultureInfo.InvariantCulture).PadLeft(2) + "%"));
			segments = new string[5]
			{
				cpuLabel + cpuValue,
				"内存 " + memory.UsedPercent.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5) + "% (" + memory.UsedGb.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(4) + "/" + memory.TotalGb.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(4) + "G)",
				text,
				(list2.Count > 0) ? string.Join(" ", list2) : string.Empty,
				"网速 ↓" + networkSpeed.Receive.PadLeft(5) + " ↑" + networkSpeed.Send.PadLeft(5) + text2
			};
		}
	}

	private void TimerTick()
	{
		if (isClosing)
		{
			return;
		}
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				LoadData();
			}
			catch
			{
			}
			RequestRepaint();
		});
	}

	private void RequestRepaint()
	{
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Expected O, but got Unknown
		try
		{
			if (panel == null || ((Control)this).IsDisposed || ((Control)this).Disposing || !((Control)this).IsHandleCreated)
			{
				return;
			}
			((Control)this).BeginInvoke((Delegate)(MethodInvoker)delegate
			{
				if (panel != null && !((Control)this).IsDisposed && !((Control)this).Disposing)
				{
					PaintNow();
				}
			});
		}
		catch
		{
		}
	}

	private void PanelPaint(object sender, PaintEventArgs e)
	{
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Expected O, but got Unknown
		Graphics graphics = e.Graphics;
		try
		{
			graphics.SmoothingMode = (SmoothingMode)2;
			graphics.TextRenderingHint = (TextRenderingHint)3;
			int num = BackgroundAlphaValue();
			if (num <= 0)
			{
				num = 1;
			}
			SolidBrush val = new SolidBrush(Color.FromArgb(num, 0, 0, 0));
			try
			{
				graphics.FillRectangle((Brush)(object)val, new RectangleF(0f, 0f, ((Control)this).Width, ((Control)this).Height));
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			if (layoutReady && BuildLayoutSignature() != layoutSignature)
			{
				layoutReady = false;
			}
			if (!layoutReady)
			{
				// 不再等天气/硬盘的数据到位才排版：列宽本来就按"实际文本与采样取大"算出来的，
				// 空文本时按采样撑底，第一帧就能算出正确的窗口宽度。
				RecalculateLayout(graphics);
			}
			float top = ((float)((Control)this).Height - sampleInkHeight) / 2f - sampleInkTop;
			DrawLine(graphics, drawOriginX, top, null);
			RecordSegmentRects(top);
		}
		catch
		{
		}
	}

	private void RecordSegmentRects(float top)
	{
		if (layoutReady)
		{
			float[] array = new float[5] { fixedCpuWidth, fixedMemoryWidth, fixedWeatherWidth, fixedDiskWidth, fixedNetworkWidth };
			float num = drawOriginX;
			for (int i = 0; i < segmentRects.Length; i++)
			{
				int width = (int)Math.Ceiling(array[i]);
				segmentRects[i] = new Rectangle((int)Math.Round(num), (int)Math.Round(top) - 2, width, (int)Math.Ceiling(sampleInkHeight) + 4);
				paintedSegments[i] = ((i < segments.Length) ? (segments[i] ?? string.Empty) : string.Empty);
				num += array[i];
			}
			segmentRectsReady = true;
		}
	}

	private float MeasureInkWidth(Graphics graphics, string text)
	{
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Expected O, but got Unknown
		if (string.IsNullOrEmpty(text))
		{
			return 0f;
		}
		float width = graphics.MeasureString(text, mainFont, PointF.Empty, MeasureFormat).Width;
		int num = Math.Max(16, (int)Math.Ceiling(width * 1.25f) + 32);
		int num2 = Math.Max(8, (int)Math.Ceiling((float)mainFont.Height * 2f) + 8);
		Bitmap val = new Bitmap(num, num2, (PixelFormat)2498570);
		try
		{
			if (graphics.DpiX > 0f)
			{
				val.SetResolution(graphics.DpiX, graphics.DpiY);
			}
			Graphics val2 = Graphics.FromImage((Image)(object)val);
			try
			{
				val2.Clear(Color.Transparent);
				val2.TextRenderingHint = (TextRenderingHint)3;
				val2.DrawString(text, mainFont, Brushes.Black, 4f, 2f);
				int num3 = -1;
				int num4 = -1;
				for (int i = 0; i < num; i++)
				{
					for (int j = 0; j < num2; j++)
					{
						if (val.GetPixel(i, j).A > 24)
						{
							if (num3 < 0)
							{
								num3 = i;
							}
							num4 = i;
							break;
						}
					}
				}
				return (num4 >= num3) ? ((float)(num4 - num3 + 1)) : width;
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

	private RectangleF MeasureSampleLineInk(Graphics graphics)
	{
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Expected O, but got Unknown
		float num = fixedCpuWidth + fixedMemoryWidth + fixedWeatherWidth + fixedDiskWidth + fixedNetworkWidth;
		int num2 = Math.Max(64, (int)Math.Ceiling(num * 1.25f) + 32);
		int num3 = Math.Max(8, (int)Math.Ceiling((float)mainFont.Height * 2f) + 8);
		Bitmap val = new Bitmap(num2, num3, (PixelFormat)2498570);
		try
		{
			if (graphics.DpiX > 0f)
			{
				val.SetResolution(graphics.DpiX, graphics.DpiY);
			}
			Graphics val2 = Graphics.FromImage((Image)(object)val);
			try
			{
				val2.Clear(Color.Transparent);
				val2.TextRenderingHint = (TextRenderingHint)3;
				DrawSampleLine(val2, 0f, 3f, Brushes.Black);
				int num4 = -1;
				int num5 = -1;
				for (int i = 0; i < num2; i++)
				{
					for (int j = 0; j < num3; j++)
					{
						if (val.GetPixel(i, j).A > 24)
						{
							if (num4 < 0)
							{
								num4 = j;
							}
							num5 = j;
							break;
						}
					}
				}
				if (num5 < num4)
				{
					return new RectangleF(0f, 0f, 0f, mainFont.Height);
				}
				return new RectangleF(0f, (float)num4 - 3f, 0f, num5 - num4 + 1);
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

	private void DrawCpu(Graphics graphics, float x, float top, Brush brush)
	{
		if (cpuLabel.Length != 0 || cpuValue.Length != 0)
		{
			graphics.DrawString(cpuLabel, mainFont, brush, x, top);
			if (cpuValue.Length != 0)
			{
				float width = graphics.MeasureString(cpuValue, mainFont, PointF.Empty, MeasureFormat).Width;
				float num = x + fixedCpuWidth - gapAfterCpu;
				graphics.DrawString(cpuValue, mainFont, brush, num - width, top);
			}
		}
	}

	private void DrawSampleLine(Graphics graphics, float x, float top, Brush brush)
	{
		graphics.DrawString("CPU 100%", mainFont, brush, x, top);
		x += fixedCpuWidth;
		graphics.DrawString("内存 100.0% (99.9/99.9G)", mainFont, brush, x, top);
		x += fixedMemoryWidth;
		if (fixedWeatherWidth > 0f)
		{
			graphics.DrawString("北京市 雷雨  25.9℃ 湿度 39% 西南风 13km/h 更新17:15", mainFont, brush, x, top);
		}
		x += fixedWeatherWidth;
		if (fixedDiskWidth > 0f)
		{
			DrawDisk(graphics, x, top, brush);
		}
		x += fixedDiskWidth;
		graphics.DrawString("网速 ↓999MB/s ↑999MB/s", mainFont, brush, x, top);
	}

	private void DrawLine(Graphics graphics, float x, float top, Brush singleBrush)
	{
		Brush brush = singleBrush ?? cpuBrush;
		Brush val = singleBrush ?? memoryBrush;
		Brush val2 = singleBrush ?? weatherBrush;
		Brush val3 = singleBrush ?? networkBrush;
		string text = segments[1];
		string text2 = segments[2];
		string obj = segments[3];
		string text3 = segments[4];
		DrawCpu(graphics, x, top, brush);
		x += fixedCpuWidth;
		graphics.DrawString(text, mainFont, val, x, top);
		x += fixedMemoryWidth;
		if (text2.Length > 0)
		{
			graphics.DrawString(text2, mainFont, val2, x, top);
		}
		x += fixedWeatherWidth;
		if (obj.Length > 0)
		{
			DrawDisk(graphics, x, top, singleBrush);
		}
		x += fixedDiskWidth;
		graphics.DrawString(text3, mainFont, val3, x, top);
	}

	private string BuildLayoutSignature()
	{
		StringBuilder stringBuilder = new StringBuilder(64);
		stringBuilder.Append(settings.CompactMode ? 'C' : '-');
		stringBuilder.Append(settings.ShowWeather ? 'W' : '-');
		stringBuilder.Append(settings.PositionLocked ? 'L' : '-');
		stringBuilder.Append('@').Append(settings.CityName.Length);
		for (int i = 0; i < diskPieces.Length; i++)
		{
			stringBuilder.Append('|').Append(diskPieces[i].Length);
		}
		// ⚠️ 天气文本的长度必须进签名。
		// 天气是异步拉取的：刚启动时 segments[2] 还是空的，MeasureWeatherWidth() 会返回 0，
		// 于是"天气"这一列宽度为 0，等天气回来时布局签名没变 ⇒ 不重新计算列宽
		// ⇒ 天气文字直接画在"硬盘"那一列上，两段字叠在一起（用户实机截图就是这个现象）。
		stringBuilder.Append('#').Append(segments[2].Length);
		return stringBuilder.ToString();
	}

	private void RecalculateLayout(Graphics graphics)
	{
		int width = Screen.PrimaryScreen.WorkingArea.Width;
		fixedCpuWidth = ColumnWidth(graphics, segments[0], "CPU 100%") + gapAfterCpu;
		fixedMemoryWidth = ColumnWidth(graphics, segments[1], "内存 100.0% (99.9/99.9G)") + gapAfterMemory;
		fixedWeatherWidth = MeasureWeatherWidth(graphics);
		fixedDiskWidth = MeasureDiskWidth(graphics) + segmentGap;
		fixedNetworkWidth = ColumnWidth(graphics, segments[4], "网速 ↓999MB/s ↑999MB/s") + segmentGap;
		layoutDriveCount = diskDriveCount;
		layoutSignature = BuildLayoutSignature();
		float num = fixedCpuWidth + fixedMemoryWidth + fixedWeatherWidth + fixedDiskWidth + fixedNetworkWidth - segmentGap;
		RectangleF rectangleF = MeasureSampleLineInk(graphics);
		sampleInkTop = rectangleF.Top;
		sampleInkHeight = rectangleF.Height;
		int num2 = (int)Math.Ceiling((num + 40f + 24f) / 40f) * 40;
		if (num2 < 800)
		{
			num2 = 800;
		}
		if (num2 > width)
		{
			num2 = width;
		}
		drawOriginX = ((float)num2 - num) / 2f;
		fontJustChanged = false;
		// 只在宽度真的变了（≥40，正好是一个取整步长）时才改窗口尺寸并重新居中。
		// 以前"数据还没齐"的每一次重算都会往屏幕底部居中挪一次，于是记住的位置先被甩到
		// 中间、再被 EnsurePosition 拉回来 —— 启动时看到的"跳"有一半是它。
		if (Math.Abs(num2 - targetWidth) >= 40)
		{
			targetWidth = num2;
			CenterOnTaskbar(num2);
		}
		layoutReady = true;
	}

	private float ColumnWidth(Graphics graphics, string text, string sample)
	{
		float num = MeasureInkWidth(graphics, text);
		if (!string.IsNullOrEmpty(sample))
		{
			num = Math.Max(num, MeasureInkWidth(graphics, sample));
		}
		return num;
	}

	private float MeasureWeatherWidth(Graphics graphics)
	{
		if (!settings.ShowWeather || settings.CompactMode)
		{
			return 0f;
		}
		// ⚠️ 天气还没拉到时（segments[2] 为空）也必须按"模板宽度"留位置，
		// 否则这一列宽度为 0，等天气回来文字就直接压在下一列（硬盘）上面。
		// 模板固定 ⇒ 列宽恒定 ⇒ 天气异步到达也不会让整行错位。
		return ColumnWidth(graphics, segments[2], WeatherWidthSampleForCity()) + gapAfterWeather;
	}

	// 模板里的城市名要换成当前设置的城市：城市名比"北京市"长时，真实天气文本会比模板宽，
	// 天气一到位整行就得变宽重排，看起来又是"先残缺、后跳齐"。
	private string WeatherWidthSampleForCity()
	{
		string city = settings.CityName ?? string.Empty;
		if (city.Length == 0)
		{
			return WeatherWidthSample;
		}
		return city + WeatherWidthSample.Substring("北京市".Length);
	}

	private float MeasureDiskWidth(Graphics graphics)
	{
		if (diskPieceFloors.Length != diskPieces.Length)
		{
			diskPieceFloors = new float[diskPieces.Length];
		}
		float num = 0f;
		float[] array = new float[diskPieces.Length];
		for (int i = 0; i < diskPieces.Length; i++)
		{
			float val = MeasureInkWidth(graphics, diskPieces[i]);
			float val2 = MeasureInkWidth(graphics, DiskPieceSample(diskPieces[i]));
			diskPieceFloors[i] = Math.Max(diskPieceFloors[i], Math.Max(val, val2));
			array[i] = diskPieceFloors[i];
			num += array[i];
			if (i < diskPieces.Length - 1)
			{
				num += segmentGap;
			}
		}
		diskPieceWidths = array;
		return num;
	}

	private static string DiskPieceSample(string piece)
	{
		if (string.IsNullOrEmpty(piece))
		{
			return string.Empty;
		}
		if (piece.Length <= 3 && piece.IndexOf(':') == 1)
		{
			return "C:";
		}
		if (piece.EndsWith("%", StringComparison.Ordinal))
		{
			return "100%";
		}
		if (piece.IndexOf('/') >= 0 || piece.IndexOfAny(new char[10] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' }) >= 0)
		{
			return "9999/9999GB";
		}
		return piece;
	}

	private void DrawDisk(Graphics graphics, float x, float top, Brush singleBrush)
	{
		float num = x;
		for (int i = 0; i < diskPieces.Length; i++)
		{
			string text = diskPieces[i];
			if (text.Length > 0)
			{
				Brush val = singleBrush ?? ((i == 0) ? diskLabelBrush : diskBrushes[(i - 1) % diskBrushes.Length]);
				graphics.DrawString(text, mainFont, val, num, top);
				num += ((i < diskPieceWidths.Length) ? diskPieceWidths[i] : 0f);
			}
			if (i < diskPieces.Length - 1)
			{
				num += segmentGap;
			}
		}
	}

	private void PanelMouseDown(object sender, MouseEventArgs e)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Invalid comparison between Unknown and I4
		if ((int)e.Button == 1048576 && !settings.PositionLocked)
		{
			dragStart = e.Location;
		}
	}

	private void PanelMouseMove(object sender, MouseEventArgs e)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Invalid comparison between Unknown and I4
		if (dragStart != Point.Empty && !settings.PositionLocked && (int)e.Button == 1048576)
		{
			settings.UserMoved = true;
			MoveWidgetTo(((Form)this).Location.X + e.X - dragStart.X, ((Form)this).Location.Y + e.Y - dragStart.Y);
		}
		Cursor val = (settings.PositionLocked ? Cursors.Arrow : Cursors.SizeAll);
		if (((Control)this).Cursor != val)
		{
			((Control)this).Cursor = val;
		}
	}

	private void PanelMouseUp(object sender, MouseEventArgs e)
	{
		dragStart = Point.Empty;
		RememberPosition();
	}

	private void FormMouseDown(object sender, MouseEventArgs e)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Invalid comparison between Unknown and I4
		if ((int)e.Button == 1048576 && !settings.PositionLocked)
		{
			dragStart = e.Location;
		}
	}

	private void FormMouseMove(object sender, MouseEventArgs e)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Invalid comparison between Unknown and I4
		if (dragStart != Point.Empty && !settings.PositionLocked && (int)e.Button == 1048576)
		{
			settings.UserMoved = true;
			MoveWidgetTo(((Form)this).Location.X + e.X - dragStart.X, ((Form)this).Location.Y + e.Y - dragStart.Y);
		}
	}

	private void FormMouseUp(object sender, MouseEventArgs e)
	{
		dragStart = Point.Empty;
		RememberPosition();
	}

	private void RememberPosition()
	{
		if (((Control)this).IsHandleCreated && layoutReady && NativeMethods.GetWindowRect(((Control)this).Handle, out var rect) && rect.Right - rect.Left == targetWidth && (settings.WindowX != rect.Left || settings.WindowY != rect.Top))
		{
			settings.WindowX = rect.Left;
			settings.WindowY = rect.Top;
			settings.Save();
		}
	}
}
