using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.Layout;
using Shared;
using Timer = System.Windows.Forms.Timer;

namespace DesktopLunar;

internal sealed class LunarClockForm : WidgetForm
{
	private static readonly string[] ColorKeys = new string[8] { "red", "orange", "yellow", "green", "cyan", "blue", "purple", "white" };

	private static readonly string[] ColorLabels = new string[8] { "赤", "橙", "黄", "绿", "青", "蓝", "紫", "白" };

	private static readonly Color[] Palette = new Color[8]
	{
		Color.FromArgb(224, 48, 40),
		Color.FromArgb(240, 128, 24),
		Color.FromArgb(232, 196, 32),
		Color.FromArgb(52, 176, 84),
		Color.FromArgb(0, 176, 180),
		Color.FromArgb(56, 112, 224),
		Color.FromArgb(152, 84, 204),
		Color.FromArgb(250, 250, 252)
	};

	private readonly Timer clock;

	private ToolStripMenuItem colorMenu;

	private ToolStripMenuItem timeColorMenu;

	private ToolStripMenuItem syncMenu;

	private int syncing;

	private string text = string.Empty;

	private string dateText = string.Empty;

	private string weekText = string.Empty;

	private string timeText = string.Empty;

	private string lunarText = string.Empty;

	private string second = string.Empty;

	private string measuredKey = string.Empty;

	private float dpi = 96f;

	protected override bool UseLayeredSurface => true;

	protected override string DisplayName => "桌面阴历";

	public override int DefaultFontSize => 24;

	public override int MinFontSize => 18;

	public override int MaxFontSize => 36;

	public override Rectangle DefaultBounds
	{
		get
		{
			Rectangle placementArea = PlacementArea;
			Size size = MeasureWidgetSize();
			// 顶部居中：水平取工作区中线，字号过大导致长条比屏幕还宽时贴左边界，
			// 否则算出的负 X 会被存进设置，下次启动就跑到屏幕左边外面。
			int left = placementArea.Left + Math.Max(0, (placementArea.Width - size.Width) / 2);
			return new Rectangle(left, placementArea.Top, size.Width, size.Height);
		}
	}

	private int PadX => (int)Math.Round(PtToPx(Store.Settings.FontSize) * 0.22f);

	private int PadY => (int)Math.Round(PtToPx(Store.Settings.FontSize) * 0.14f);

	private string CurrentColorKey
	{
		get
		{
			string fontColor = Store.Settings.FontColor;
			if (string.IsNullOrEmpty(fontColor))
			{
				return "yellow";
			}
			for (int i = 0; i < ColorKeys.Length; i++)
			{
				if (string.Equals(ColorKeys[i], fontColor, StringComparison.OrdinalIgnoreCase))
				{
					return ColorKeys[i];
				}
			}
			return "yellow";
		}
	}

	private string CurrentTimeColorKey
	{
		get
		{
			string timeColor = Store.Settings.TimeColor;
			if (string.IsNullOrEmpty(timeColor))
			{
				return string.Empty;
			}
			for (int i = 0; i < ColorKeys.Length; i++)
			{
				if (string.Equals(ColorKeys[i], timeColor, StringComparison.OrdinalIgnoreCase))
				{
					return ColorKeys[i];
				}
			}
			return string.Empty;
		}
	}

	private Color TextColor => Palette[Array.IndexOf(ColorKeys, CurrentColorKey)];

	private Color TimeTextColor
	{
		get
		{
			string currentTimeColorKey = CurrentTimeColorKey;
			if (string.IsNullOrEmpty(currentTimeColorKey))
			{
				return TextColor;
			}
			return Palette[Array.IndexOf(ColorKeys, currentTimeColorKey)];
		}
	}

	public LunarClockForm()
	{
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Expected O, but got Unknown
		((Control)this).Text = "桌面阴历";
		BuildBaseMenu();
		RemoveMenuItem("透明度");
		clock = new Timer
		{
			Interval = 200
		};
		clock.Tick += delegate
		{
			Tick();
		};
		clock.Start();
	}

	public override int[] FontSizes()
	{
		return new int[10] { 18, 20, 22, 24, 26, 28, 30, 32, 34, 36 };
	}

	protected override void BuildMenu()
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Expected O, but got Unknown
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Expected O, but got Unknown
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		colorMenu = new ToolStripMenuItem("日期颜色");
		AddColorItems(colorMenu, "color:");
		((ToolStrip)Menu).Items.Add((ToolStripItem)(object)colorMenu);
		timeColorMenu = new ToolStripMenuItem("时间颜色");
		AddColorItems(timeColorMenu, "timecolor:");
		((ToolStrip)Menu).Items.Add((ToolStripItem)(object)timeColorMenu);
		syncMenu = new ToolStripMenuItem("立即对时");
		((ToolStripItem)syncMenu).Tag = "sync";
		((ToolStripItem)syncMenu).ToolTipText = "从网络时间服务器（NTP）校准本机时钟偏差，本机慢/快多少秒都会按网络时间显示。";
		((ToolStripItem)syncMenu).Click += delegate
		{
			OnMenuClick("sync");
		};
		((ToolStrip)Menu).Items.Add((ToolStripItem)(object)syncMenu);
	}

	private void AddColorItems(ToolStripMenuItem menu, string tagPrefix)
	{
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Expected O, but got Unknown
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Expected O, but got Unknown
		if (string.Equals(tagPrefix, "timecolor:", StringComparison.Ordinal))
		{
			ToolStripMenuItem val = new ToolStripMenuItem("跟随日期颜色");
			((ToolStripItem)val).Tag = "timecolor:";
			((ToolStripItem)val).Click += delegate
			{
				OnMenuClick("timecolor:");
			};
			((ToolStripDropDownItem)menu).DropDownItems.Add((ToolStripItem)(object)val);
		}
		for (int i = 0; i < ColorKeys.Length; i++)
		{
			int index = i;
			ToolStripMenuItem val2 = new ToolStripMenuItem(ColorLabels[i]);
			((ToolStripItem)val2).ForeColor = Palette[i];
			((ToolStripItem)val2).Tag = tagPrefix + ColorKeys[i];
			((ToolStripItem)val2).Click += delegate
			{
				OnMenuClick(tagPrefix + ColorKeys[index]);
			};
			if (string.Equals(ColorKeys[i], "white", StringComparison.Ordinal))
			{
				((ToolStripItem)val2).BackColor = Color.FromArgb(32, 32, 36);
			}
			((ToolStripDropDownItem)menu).DropDownItems.Add((ToolStripItem)(object)val2);
		}
	}

	protected override void OnMenuClick(string tag)
	{
		if (tag.StartsWith("font:", StringComparison.Ordinal))
		{
			if (int.TryParse(tag.Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			{
				ApplyFontSize(result);
				ApplyTextLayout();
				Log("字号 " + result + "pt");
			}
		}
		else if (tag.StartsWith("timecolor:", StringComparison.Ordinal))
		{
			string text = tag.Substring(10);
			Store.Settings.TimeColor = (string.IsNullOrEmpty(text) ? null : text);
			Store.Save();
			InvalidateSurface();
			Log(string.IsNullOrEmpty(text) ? "时间颜色 跟随日期" : ("时间颜色 " + text));
		}
		else if (tag.StartsWith("color:", StringComparison.Ordinal))
		{
			Store.Settings.FontColor = tag.Substring(6);
			Store.Save();
			InvalidateSurface();
			Log("日期颜色 " + tag.Substring(6));
		}
		else if (string.Equals(tag, "sync", StringComparison.Ordinal))
		{
			SyncTime(manual: true);
		}
		else if (tag.StartsWith("opacity:", StringComparison.Ordinal))
		{
			ApplyOpacity(int.Parse(tag.Substring(8), CultureInfo.InvariantCulture));
		}
	}

	protected override void OnMenuOpening()
	{
		RefreshColorChecks(colorMenu, CurrentColorKey);
		RefreshColorChecks(timeColorMenu, CurrentTimeColorKey);
	}

	private void RefreshColorChecks(ToolStripMenuItem menu, string currentKey)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected O, but got Unknown
		if (menu == null)
		{
			return;
		}
		foreach (ToolStripItem item in (ArrangedElementCollection)((ToolStripDropDownItem)menu).DropDownItems)
		{
			object obj = (object)item;
			ToolStripMenuItem val = (ToolStripMenuItem)((obj is ToolStripMenuItem) ? obj : null);
			if (val != null && ((ToolStripItem)val).Tag != null)
			{
				string text = ((ToolStripItem)val).Tag.ToString();
				int num = text.IndexOf(':');
				val.Checked = num > 0 && string.Equals(text.Substring(num + 1), currentKey, StringComparison.Ordinal);
			}
		}
	}

	protected override void OnCanvasMouseDown(object sender, MouseEventArgs e)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Invalid comparison between Unknown and I4
		base.OnCanvasMouseDown(sender, e);
		if ((int)e.Button == 1048576 && Store.Settings.Locked)
		{
			ShowHint("位置已锁定：右键菜单里点\"解锁位置\"可恢复拖动");
		}
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		Graphics val = ((Control)this).CreateGraphics();
		try
		{
			dpi = val.DpiX;
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		if (dpi <= 0f)
		{
			dpi = 96f;
		}
		ApplyTextLayout();
		RestoreMyPosition();
		LogStartup();
		TimeSync.Start(TimeSpan.FromMinutes(30.0), OnSyncFinished);
	}

	private void OnSyncFinished(SyncResult r)
	{
		try
		{
			if (((Control)this).IsHandleCreated && !((Control)this).IsDisposed)
			{
				((Control)this).BeginInvoke((Delegate)new Action<SyncResult>(OnSyncFinishedUi), new object[1] { r });
			}
		}
		catch
		{
		}
	}

	private void SyncTime(bool manual)
	{
		if (Interlocked.Exchange(ref syncing, 1) == 1)
		{
			if (manual)
			{
				ShowHint("正在对时，请稍候…");
			}
			return;
		}
		if (manual)
		{
			ShowHint("正在对时…");
		}
		Thread thread = new Thread((ThreadStart)delegate
		{
			try
			{
				SyncResult r = TimeSync.SyncOnce();
				try
				{
					if (((Control)this).IsHandleCreated && !((Control)this).IsDisposed)
					{
						((Control)this).BeginInvoke((Delegate)new Action<SyncResult>(OnSyncFinishedUi), new object[1] { r });
						if (manual)
						{
							((Control)this).BeginInvoke((Delegate)(Action)delegate
							{
								ShowHint(r.Ok ? ("对时成功：" + TimeSync.StatusText) : "对时失败：连不上时间服务器，暂时用本机时间");
							});
						}
					}
				}
				catch
				{
				}
			}
			finally
			{
				Interlocked.Exchange(ref syncing, 0);
			}
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private void OnSyncFinishedUi(SyncResult r)
	{
		try
		{
			if (syncMenu != null)
			{
				((ToolStripItem)syncMenu).Text = "立即对时（" + TimeSync.StatusText + "）";
			}
			if (r.Ok)
			{
				Log("网络对时：" + r.Server + " 偏差 " + r.Offset.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " 秒（往返 " + r.DelayMs.ToString("0", CultureInfo.InvariantCulture) + "ms）");
			}
			else
			{
				Log("网络对时失败：时间服务器都连不上，继续用本机时间");
			}
		}
		catch
		{
		}
	}

	private void RestoreMyPosition()
	{
		Rectangle placementArea = PlacementArea;
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
			int val = Math.Max(placementArea.Left, placementArea.Right - ((Control)this).Width);
			int val2 = Math.Max(placementArea.Top, placementArea.Bottom - ((Control)this).Height);
			x = Math.Max(placementArea.Left, Math.Min(x, val));
			y = Math.Max(placementArea.Top, Math.Min(y, val2));
		}
		((Form)this).Location = new Point(x, y);
	}

	private void ClampIntoWorkArea()
	{
		Rectangle placementArea = PlacementArea;
		int num = Math.Max(placementArea.Left, Math.Min(((Control)this).Left, Math.Max(placementArea.Left, placementArea.Right - ((Control)this).Width)));
		int num2 = Math.Max(placementArea.Top, Math.Min(((Control)this).Top, Math.Max(placementArea.Top, placementArea.Bottom - ((Control)this).Height)));
		if (num != ((Control)this).Left || num2 != ((Control)this).Top)
		{
			((Form)this).Location = new Point(num, num2);
		}
	}

	private float PtToPx(float pt)
	{
		return pt * dpi / 72f;
	}

	private static StringFormat TextFormat()
	{
		// 测量与绘制共用这一个格式：默认格式的 MeasureString 会在每段两侧附带留白，
		// 四段分别累加出来的排版宽度会大于整串测量值，末段就被挤出窗口右边界。
		StringFormat stringFormat = (StringFormat)StringFormat.GenericTypographic.Clone();
		stringFormat.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
		stringFormat.Alignment = (StringAlignment)0;
		stringFormat.LineAlignment = (StringAlignment)0;
		return stringFormat;
	}

	private string[] MeasureSegments()
	{
		DateTime now = TimeSync.Now;
		LunarDate lunarDate = Lunar.FromSolar(now);
		return new string[4]
		{
			now.Year.ToString(CultureInfo.InvariantCulture) + "年" + now.Month.ToString(CultureInfo.InvariantCulture) + "月" + now.Day.ToString(CultureInfo.InvariantCulture) + "日",
			"周" + Lunar.WeekNameShort(now),
			"88:88:88",
			lunarDate.ShortYearText + lunarDate.ToString()
		};
	}

	private Size MeasureWidgetSize()
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Expected O, but got Unknown
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Expected O, but got Unknown
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected O, but got Unknown
		try
		{
			float num = PtToPx(Store.Settings.FontSize);
			Bitmap val = new Bitmap(2, 2);
			try
			{
				Graphics val2 = Graphics.FromImage((Image)(object)val);
				try
				{
					Font val3 = new Font(Fonts.FamilyName, num, (FontStyle)0, (GraphicsUnit)2);
					try
					{
						StringFormat val4 = TextFormat();
						try
						{
							string[] array = MeasureSegments();
							float num2 = 0f;
							float num3 = 0f;
							foreach (string text in array)
							{
								SizeF sizeF = val2.MeasureString(text, val3, PointF.Empty, val4);
								num2 += sizeF.Width;
								num3 = Math.Max(num3, sizeF.Height);
							}
							return new Size((int)Math.Ceiling(num2) + PadX * 2 + 2, (int)Math.Ceiling(num3) + PadY * 2);
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
		catch
		{
			return new Size(640, 60);
		}
	}

	private void ApplyTextLayout()
	{
		DateTime now = TimeSync.Now;
		string text = Store.Settings.FontSize + "|" + now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + dpi.ToString(CultureInfo.InvariantCulture);
		if (text != measuredKey)
		{
			measuredKey = text;
			Size size = MeasureWidgetSize();
			if (size.Width > 0 && size.Height > 0 && size != ((Form)this).Size)
			{
				((Form)this).Size = size;
				ClampIntoWorkArea();
			}
		}
		ComposeText(now);
		second = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
		InvalidateSurface();
	}

	private void ComposeText(DateTime now)
	{
		LunarDate lunarDate = Lunar.FromSolar(now);
		dateText = now.Year.ToString(CultureInfo.InvariantCulture) + "年" + now.Month.ToString(CultureInfo.InvariantCulture) + "月" + now.Day.ToString(CultureInfo.InvariantCulture) + "日";
		// 星期：格式 "周二"（用户要求：2026年10月27日周二14:30:16丙午马年九月十八）
		weekText = "周" + Lunar.WeekNameShort(now);
		timeText = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
		lunarText = lunarDate.ShortYearText + lunarDate.ToString();
		text = dateText + weekText + timeText + lunarText;
	}

	private void Tick()
	{
		if (!(TimeSync.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) == second))
		{
			ApplyTextLayout();
		}
	}

	protected override void OnCanvasPaint(object sender, PaintEventArgs e)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Expected O, but got Unknown
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Expected O, but got Unknown
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Expected O, but got Unknown
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Expected O, but got Unknown
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Expected O, but got Unknown
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Expected O, but got Unknown
		Graphics graphics = e.Graphics;
		SolidBrush val = new SolidBrush(SurfaceColor(0, 0, 0, 0));
		try
		{
			graphics.FillRectangle((Brush)(object)val, ((Control)this).ClientRectangle);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		float num = PtToPx(Store.Settings.FontSize);
		Font val2 = new Font(Fonts.FamilyName, num, (FontStyle)0, (GraphicsUnit)2);
		try
		{
			StringFormat val3 = TextFormat();
			try
			{
				int num2 = Math.Max(1, (int)Math.Round(num / 28f));
				SolidBrush val4 = new SolidBrush(Color.FromArgb(120, 16, 16, 16));
				try
				{
					graphics.DrawString(text, val2, (Brush)(object)val4, (float)(PadX + num2), (float)(PadY + num2), val3);
				}
				finally
				{
					((IDisposable)val4)?.Dispose();
				}
				float num3 = PadX;
				SolidBrush val5 = new SolidBrush(TextColor);
				try
				{
					SolidBrush val6 = new SolidBrush(TimeTextColor);
					try
					{
						SolidBrush val7 = new SolidBrush(TextColor);
						try
						{
							graphics.DrawString(dateText, val2, (Brush)(object)val5, num3, (float)PadY, val3);
							num3 += graphics.MeasureString(dateText, val2, PointF.Empty, val3).Width;
							graphics.DrawString(weekText, val2, (Brush)(object)val7, num3, (float)PadY, val3);
							num3 += graphics.MeasureString(weekText, val2, PointF.Empty, val3).Width;
							graphics.DrawString(timeText, val2, (Brush)(object)val6, num3, (float)PadY, val3);
							num3 += graphics.MeasureString(timeText, val2, PointF.Empty, val3).Width;
							graphics.DrawString(lunarText, val2, (Brush)(object)val7, num3, (float)PadY, val3);
						}
						finally
						{
							((IDisposable)val7)?.Dispose();
						}
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

	private void RemoveMenuItem(string text)
	{
		for (int num = ((ArrangedElementCollection)((ToolStrip)Menu).Items).Count - 1; num >= 0; num--)
		{
			ToolStripItem obj = ((ToolStrip)Menu).Items[num];
			ToolStripMenuItem val = (ToolStripMenuItem)(object)((obj is ToolStripMenuItem) ? obj : null);
			if (val != null && string.Equals(((ToolStripItem)val).Text, text, StringComparison.Ordinal))
			{
				((ToolStrip)Menu).Items.RemoveAt(num);
			}
		}
	}

	private void LogStartup()
	{
		Log("启动 字体=" + Fonts.FamilyName + " dpi=" + dpi.ToString(CultureInfo.InvariantCulture) + " 字号=" + Store.Settings.FontSize + "pt 日期颜色=" + CurrentColorKey + " 时间颜色=" + (string.IsNullOrEmpty(CurrentTimeColorKey) ? "跟随日期" : CurrentTimeColorKey) + " 对时=" + TimeSync.StatusText + " 尺寸=" + ((Control)this).Width + "x" + ((Control)this).Height + " 位置=" + ((Control)this).Left + "," + ((Control)this).Top);
	}

	private void Log(string message)
	{
		try
		{
			File.AppendAllText(Path.Combine(AppPaths.DataDirectory, "log.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
		}
		catch
		{
		}
	}
}
