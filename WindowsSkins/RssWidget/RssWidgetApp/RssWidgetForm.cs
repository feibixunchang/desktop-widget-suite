using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Shared;

namespace RssWidgetApp;

internal sealed class RssWidgetForm : WidgetForm
{
	private sealed class RowInfo
	{
		public List<string> Lines;

		public int LineHeight;

		public int TextHeight;

		public Color Color;
	}

	private const string Title = "桌面RSS挂件";

	private const int HeaderLine = 32;

	private const int HeaderExtra = 22;

	private const int ChipRow = 26;

	private const int StatusHeight = 30;

	private const int Pad = 4;

	private const int RowGap = 1;

	private const int FadeHeight = 40;

	private const int ListGap = 14;

	private const int GripSize = 18;

	private const int TextLeft = 2;

	private const int TextRight = 0;

	private const float DateGap = 12f;

	private const float ClockGap = 14f;

	private static readonly Color CardColor = Color.FromArgb(0, 0, 0);

	private static readonly Color InvisibleCard = Color.FromArgb(1, 2, 3, 4);

	private static readonly Color HeadDate = Color.FromArgb(235, 240, 245);

	private static readonly Color HeadClock = Color.FromArgb(255, 205, 90);

	private static readonly Color HeadIp = Color.FromArgb(120, 190, 255);

	private static readonly Color[] ChipColors = new Color[3]
	{
		Color.FromArgb(238, 240, 243),
		Color.FromArgb(255, 205, 90),
		Color.FromArgb(120, 190, 255)
	};

	private static readonly Color[] RowColors = new Color[3]
	{
		Color.FromArgb(238, 240, 243),
		Color.FromArgb(255, 205, 90),
		Color.FromArgb(120, 190, 255)
	};

	private static readonly Color ChipBase = Color.FromArgb(22, 22, 22);

	private static readonly Color ChipLine = Color.FromArgb(60, 60, 60);

	private static readonly Color[] Palette = new Color[9]
	{
		Color.FromArgb(120, 190, 255),
		Color.FromArgb(255, 205, 90),
		Color.FromArgb(120, 220, 170),
		Color.FromArgb(255, 150, 130),
		Color.FromArgb(200, 160, 255),
		Color.FromArgb(120, 220, 220),
		Color.FromArgb(240, 200, 120),
		Color.FromArgb(180, 200, 255),
		Color.FromArgb(255, 180, 210)
	};

	private readonly List<FeedSource> sources = new List<FeedSource>();

	private readonly List<FeedItem> items = new List<FeedItem>();

	private readonly List<FeedItem> view = new List<FeedItem>();

	private readonly List<string> chipLabels = new List<string>();

	private readonly List<string> chipTargets = new List<string>();

	private readonly List<Rectangle> chipRects = new List<Rectangle>();

	private readonly List<Rectangle> rowRects = new List<Rectangle>();

	private readonly List<RowInfo> rowInfos = new List<RowInfo>();

	private readonly Dictionary<string, Color> sourceColors = new Dictionary<string, Color>(StringComparer.Ordinal);

	private readonly Dictionary<float, Font> fontCache = new Dictionary<float, Font>();

	private RssOptions options;

	private string dateText = "";

	private string clockText = "";

	private string ipText = "";

	private string status = "正在加载订阅…";

	private int headerHeight;

	private int chipsHeight;

	private int listTop;

	private int totalHeight;

	private int scroll;

	private float scrollShown;

	private float scrollTarget;

	private float wheelRemainder;

	private int hoverRow = -1;

	private float ipX;

	private float clockX;

	private float ipY;

	private bool layoutDirty = true;

	private int layoutWidth = -1;

	private bool viewDirty = true;

	private bool fetching;

	private bool resizing;

	private Point resizeOrigin;

	private Size resizeStart;

	private System.Windows.Forms.Timer clockTimer;

	private System.Windows.Forms.Timer refreshTimer;

	private System.Windows.Forms.Timer scrollTimer;

	private bool listDirty;

	private Rectangle lastClip = Rectangle.Empty;

	private Font chipFont;

	private Font statusFont;

	private float fontScale = 1f;

	private Font headFont;

	private Font bodyFont;

	private Font smallFont;

	private const int DefaultPanelWidth = 700;

	private const int DefaultBottomGapCm = 1;

	protected override bool UseLayeredSurface => true;

	protected override string DisplayName => "桌面RSS挂件";

	public override int DefaultFontSize => 13;

	public override int MinFontSize => 13;

	public override int MaxFontSize => 18;

	private float ScaleFactor
	{
		get
		{
			float num = (float)base.Width / 700f;
			if (num < 0.55f)
			{
				num = 0.55f;
			}
			if (num > 2f)
			{
				num = 2f;
			}
			return (float)Math.Round(num * 20f) / 20f;
		}
	}

	public override Rectangle DefaultBounds
	{
		get
		{
			Rectangle placementArea = PlacementArea;
			int num = Math.Max(base.Width, MinimumWidth());
			int num2 = Math.Max(base.Height, placementArea.Height - (int)Math.Round(CmToPx(1f)));
			return new Rectangle(placementArea.Right - num, placementArea.Top, num, num2);
		}
	}

	private static string AppDir
	{
		get
		{
			string directoryName = Path.GetDirectoryName(Application.ExecutablePath);
			if (!string.IsNullOrEmpty(directoryName))
			{
				return directoryName;
			}
			return AppPaths.DataDirectory;
		}
	}

	private string SourceFile => Path.Combine(AppDir, "订阅源.txt");

	private int HeadLineHeight
	{
		get
		{
			if (headFont != null)
			{
				return headFont.Height + 10;
			}
			return 32;
		}
	}

	private int HeadExtraHeight
	{
		get
		{
			if (smallFont != null)
			{
				return smallFont.Height + 10;
			}
			return 22;
		}
	}

	private int ViewportHeight => Math.Max(20, base.Height - listTop - 30 - 8);

	public RssWidgetForm()
	{
		bool flag = !File.Exists(AppPaths.SettingsFile);
		if (Store.Settings.Opacity < 0 || Store.Settings.Opacity > 100 || (flag && Store.Settings.Opacity == 0))
		{
			Store.Settings.Opacity = 88;
			Store.Save();
			base.Opacity = 0.8799999952316284;
		}
		options = RssOptionsStore.Load();
		options.Normalize();
		LoadSources();
		Text = "桌面RSS挂件 " + VersionNumber();
		BuildFonts();
		DateTime now = DateTime.Now;
		dateText = now.ToString("yyyy年M月d日", CultureInfo.InvariantCulture) + " 星期" + WeekText(now.DayOfWeek);
		clockText = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
		ipText = (options.ShowIp ? ("本机IP：" + LocalIp()) : string.Empty);
		int num = PreferredWidth();
		int val = ((options.SavedWidth > 0) ? Math.Max(options.SavedWidth, num) : num);
		int num2 = ((options.SavedHeight >= 600) ? options.SavedHeight : DefaultHeight());
		base.ClientSize = new Size(Math.Min(val, MaxPreferredWidth()), num2);
		BuildBaseMenu();
		RestorePosition();
		SetupTray();
		clockTimer = new System.Windows.Forms.Timer
		{
			Interval = 1000
		};
		clockTimer.Tick += delegate
		{
			UpdateHeader();
		};
		refreshTimer = new System.Windows.Forms.Timer
		{
			Interval = options.RefreshMinutes * 60 * 1000
		};
		refreshTimer.Tick += delegate
		{
			StartFetch(showStatus: false);
		};
	}

	private void ResetScroll()
	{
		scrollShown = 0f;
		scrollTarget = 0f;
		wheelRemainder = 0f;
		scroll = 0;
	}

	private static string VersionNumber()
	{
		Version version = typeof(RssWidgetForm).Assembly.GetName().Version;
		if (!(version == null))
		{
			return version.Major + "." + version.Minor + "." + version.Build;
		}
		return "1.0.0";
	}

	public override int[] FontSizes()
	{
		return new int[4] { 13, 14, 16, 18 };
	}

	private void BuildFonts()
	{
		foreach (Font value in fontCache.Values)
		{
			value.Dispose();
		}
		fontCache.Clear();
		Dispose(ref headFont);
		Dispose(ref bodyFont);
		Dispose(ref smallFont);
		Dispose(ref chipFont);
		Dispose(ref statusFont);
		float num = (float)Store.Settings.FontSize * ScaleFactor;
		if (num < 8f)
		{
			num = 8f;
		}
		if (num > 40f)
		{
			num = 40f;
		}
		headFont = Fonts.Widget(num);
		bodyFont = Fonts.Widget(num);
		smallFont = Fonts.Widget(Math.Max(7f, num - 3f));
		chipFont = Fonts.Widget(Math.Max(7f, num - 2f));
		statusFont = Fonts.Widget(Math.Max(8f, num - 1f));
		fontScale = ScaleFactor;
	}

	private static void Dispose(ref Font font)
	{
		if (font != null)
		{
			font.Dispose();
			font = null;
		}
	}

	public override void ApplyFontSize(int size)
	{
		base.ApplyFontSize(size);
		BuildFonts();
		LayoutHeader();
		layoutDirty = true;
		viewDirty = true;
		InvalidateSurface();
	}

	private static float CmToPx(float cm)
	{
		try
		{
			using Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
			return cm * graphics.DpiX / 2.54f;
		}
		catch
		{
			return cm * 96f / 2.54f;
		}
	}

	private int DefaultHeight()
	{
		int num = (int)Math.Round(CmToPx(1f));
		return Math.Max(600, PlacementArea.Height - num);
	}

	private int PreferredWidth()
	{
		int num = 700;
		try
		{
			using Bitmap image = new Bitmap(1, 1);
			using Graphics graphics = Graphics.FromImage(image);
			if (headFont != null)
			{
				int num2 = (int)Math.Ceiling(4f + graphics.MeasureString(dateText, headFont).Width + 12f + graphics.MeasureString(clockText, headFont).Width + ((ipText.Length > 0) ? (14f + graphics.MeasureString(ipText, headFont).Width) : 0f) + 4f);
				if (num2 > num)
				{
					num = num2;
				}
			}
			if (chipLabels.Count > 0)
			{
				using Font font = Fonts.Widget(Math.Max(8f, (float)Store.Settings.FontSize * ScaleFactor));
				int num3 = 0;
				foreach (string chipLabel in chipLabels)
				{
					num3 += (int)Math.Ceiling(UiTools.MeasureLine(graphics, chipLabel, font).Width);
				}
				int num4 = 8 + num3 + 14 * chipLabels.Count + 4 * (chipLabels.Count - 1);
				if (num4 > num)
				{
					num = num4;
				}
			}
		}
		catch
		{
		}
		return num + 48;
	}

	private int MinimumWidth()
	{
		return Math.Min(MaxPreferredWidth(), PreferredWidth());
	}

	private int MaxPreferredWidth()
	{
		return (int)Math.Round((float)PlacementArea.Width * 0.75f);
	}

	private static string WeekText(DayOfWeek day)
	{
		return (new string[7] { "日", "一", "二", "三", "四", "五", "六" })[(int)day];
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		int num = MinimumWidth();
		if (base.Width < num && resizing)
		{
			base.Size = new Size(num, base.Height);
			return;
		}
		layoutDirty = true;
		if (base.IsHandleCreated && fontScale != ScaleFactor)
		{
			BuildFonts();
		}
		UpdateHeader(force: true);
		if (base.IsHandleCreated && base.Width > 0 && base.Height >= 600 && (options.SavedWidth != base.Width || options.SavedHeight != base.Height))
		{
			options.SavedWidth = base.Width;
			options.SavedHeight = base.Height;
			RssOptionsStore.Save(options);
		}
	}

	protected override void WndProc(ref Message m)
	{
		_ = m.Msg;
		_ = 328;
		base.WndProc(ref m);
	}

	private bool InResizeCorner(int x, int y)
	{
		if (x >= base.Width - 18)
		{
			return y >= base.Height - 18;
		}
		return false;
	}

	private void LoadSources()
	{
		sources.Clear();
		sources.AddRange(Feeds.Default());
		try
		{
			if (File.Exists(SourceFile))
			{
				string[] array = File.ReadAllLines(SourceFile, Encoding.UTF8);
				foreach (string obj in array)
				{
					string text = obj.Trim();
					if (text.Length == 0 || text.StartsWith("#"))
					{
						continue;
					}
					string[] array2 = text.Split('|');
					if (array2.Length >= 2)
					{
						string url = array2[1].Trim();
						if (url.Length != 0 && !sources.Exists((FeedSource s) => s.Url == url))
						{
							string category = ((array2.Length > 2 && array2[2].Trim().Length > 0) ? array2[2].Trim() : "自定义");
							sources.Add(new FeedSource
							{
								Name = array2[0].Trim(),
								Url = url,
								Category = category
							});
						}
					}
				}
			}
		}
		catch
		{
		}
		chipLabels.Clear();
		chipTargets.Clear();
		chipLabels.Add("全部");
		chipTargets.Add(null);
		foreach (FeedSource source in sources)
		{
			string item = (string.IsNullOrEmpty(source.Short) ? source.Name : source.Short);
			if (!chipLabels.Contains(item))
			{
				chipLabels.Add(item);
				chipTargets.Add(source.Name);
			}
		}
		if (!chipLabels.Contains(options.Category))
		{
			options.Category = "全部";
		}
		for (int j = 0; j < sources.Count; j++)
		{
			if (!sourceColors.ContainsKey(sources[j].Name))
			{
				sourceColors[sources[j].Name] = Palette[j % Palette.Length];
			}
		}
		viewDirty = true;
		layoutDirty = true;
	}

	private void WriteSourceTemplate()
	{
		try
		{
			if (File.Exists(SourceFile))
			{
				return;
			}
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("# 桌面RSS挂件 订阅源文件");
			stringBuilder.AppendLine("# 一行一个：名称|订阅地址|分类   （分类自己起，菜单会自动多一个按钮）");
			stringBuilder.AppendLine("# 改完保存，再点右键菜单「重新加载订阅源」生效");
			stringBuilder.AppendLine();
			foreach (FeedSource item in Feeds.Default())
			{
				stringBuilder.AppendLine(item.Name + "|" + item.Url + "|" + item.Category);
			}
			File.WriteAllText(SourceFile, stringBuilder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		}
		catch
		{
		}
	}

	private void StartFetch(bool showStatus)
	{
		if (fetching)
		{
			return;
		}
		fetching = true;
		if (showStatus)
		{
			status = "正在刷新…";
			InvalidateSurface();
		}
		List<FeedSource> snapshot = new List<FeedSource>(sources);
		int max = options.MaxItemsPerFeed;
		ThreadPool.QueueUserWorkItem(delegate
		{
			List<FeedItem> collected = new List<FeedItem>();
			int failed = 0;
			foreach (FeedSource item in snapshot)
			{
				try
				{
					collected.AddRange(Feeds.Fetch(item, max, 12000));
				}
				catch
				{
					failed++;
				}
			}
			collected.Sort(delegate(FeedItem a, FeedItem b)
			{
				int num = b.PublishedTime.CompareTo(a.PublishedTime);
				return (num != 0) ? num : string.Compare(a.Title, b.Title, StringComparison.Ordinal);
			});
			if (collected.Count > 300)
			{
				collected.RemoveRange(300, collected.Count - 300);
			}
			GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
			RunOnUi(delegate
			{
				fetching = false;
				items.Clear();
				items.AddRange(collected);
				viewDirty = true;
				layoutDirty = true;
				ResetScroll();
				options.LastRefreshTicks = DateTime.Now.Ticks;
				RssOptionsStore.Save(options);
				status = string.Format("共 {0} 条 · 刷新于 {1}{2}", items.Count, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), (failed > 0) ? (" · " + failed + " 个源失败") : string.Empty);
				InvalidateSurface();
			});
		});
	}

	private void RunOnUi(MethodInvoker action)
	{
		try
		{
			if (base.IsHandleCreated && !base.IsDisposed)
			{
				BeginInvoke(action);
			}
		}
		catch
		{
		}
	}

	private void UpdateHeader()
	{
		UpdateHeader(force: false);
	}

	private void UpdateHeader(bool force)
	{
		DateTime now = DateTime.Now;
		string text = "星期" + WeekText(now.DayOfWeek);
		string text2 = now.ToString("yyyy年M月d日", CultureInfo.InvariantCulture) + " " + text;
		string text3 = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
		if (ipText.Length == 0)
		{
			ipText = (options.ShowIp ? ("本机IP：" + LocalIp()) : string.Empty);
		}
		if (force || !(text2 == dateText) || !(text3 == clockText))
		{
			dateText = text2;
			clockText = text3;
			int num = headerHeight;
			LayoutHeader();
			if (headerHeight != num)
			{
				layoutDirty = true;
				InvalidateSurface();
			}
			else
			{
				InvalidateSurface(new Rectangle(0, 0, base.Width, 4 + headerHeight + ChipsRoom()));
			}
		}
	}

	private int ChipsRoom()
	{
		if (chipRects.Count <= 0)
		{
			return 26;
		}
		return chipsHeight;
	}

	private float OneLineHeaderWidth(Graphics g)
	{
		if (!options.ShowHeader || headFont == null)
		{
			return 8f;
		}
		float num = g.MeasureString(dateText, headFont).Width;
		float num2 = g.MeasureString(clockText, headFont).Width;
		float num3 = ((ipText.Length == 0) ? 0f : g.MeasureString(ipText, headFont).Width);
		return 4f + num + 12f + num2 + ((num3 > 0f) ? (14f + num3) : 0f) + 4f;
	}

	private void LayoutHeader()
	{
		ipX = 0f;
		ipY = 4f;
		clockX = 4f;
		headerHeight = (options.ShowHeader ? HeadLineHeight : 0);
		if (!options.ShowHeader || headFont == null)
		{
			return;
		}
		try
		{
			using Bitmap image = new Bitmap(1, 1);
			using Graphics graphics = Graphics.FromImage(image);
			float num = graphics.MeasureString(dateText, headFont).Width;
			float num2 = graphics.MeasureString(clockText, headFont).Width;
			float num3 = ((ipText.Length > 0) ? graphics.MeasureString(ipText, headFont).Width : 0f);
			float num4 = ((float)base.Width - num2) / 2f;
			float num5 = ((num3 > 0f) ? ((float)base.Width - num3 - 4f) : ((float)(base.Width - 4)));
			if (num4 >= 4f + num + 8f && num4 + num2 <= num5 - 8f)
			{
				clockX = num4;
			}
			else
			{
				clockX = 4f + num + 12f;
			}
			if (ipText.Length != 0)
			{
				if (OneLineHeaderWidth(graphics) + 2f <= (float)base.Width)
				{
					ipX = (float)base.Width - num3 - 4f;
					return;
				}
				headerHeight = HeadLineHeight + HeadExtraHeight;
				ipX = 4f;
				ipY = 4 + HeadLineHeight - 4;
			}
		}
		catch
		{
		}
	}

	private static string LocalIp()
	{
		try
		{
			using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			socket.Connect("1.1.1.1", 53);
			if (socket.LocalEndPoint is IPEndPoint iPEndPoint)
			{
				return iPEndPoint.Address.ToString();
			}
		}
		catch
		{
		}
		return "—";
	}

	private void EnsureView()
	{
		if (!viewDirty)
		{
			return;
		}
		view.Clear();
		if (options.Category == "全部")
		{
			view.AddRange(items);
		}
		else
		{
			int num = chipLabels.IndexOf(options.Category);
			string text = ((num >= 0 && num < chipTargets.Count) ? chipTargets[num] : options.Category);
			foreach (FeedItem item in items)
			{
				if (item.Source == text)
				{
					view.Add(item);
				}
			}
		}
		DropDuplicates();
		viewDirty = false;
		layoutDirty = true;
	}

	private void DropDuplicates()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		List<FeedItem> list = new List<FeedItem>(view.Count);
		for (int i = 0; i < view.Count; i++)
		{
			string text = NormalizeTitle(view[i].Title);
			if (text.Length == 0 || hashSet.Add(text))
			{
				list.Add(view[i]);
			}
		}
		if (list.Count != view.Count)
		{
			view.Clear();
			view.AddRange(list);
		}
	}

	private static string NormalizeTitle(string title)
	{
		if (string.IsNullOrEmpty(title))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(title.Length);
		char c = '\0';
		foreach (char c2 in title)
		{
			if (!char.IsWhiteSpace(c2) && "，。！？、：；“”‘’（）《》【】…—－,.!?;:\"'()[]{}<>-_~".IndexOf(c2) < 0 && c2 != c)
			{
				stringBuilder.Append(char.ToLowerInvariant(c2));
				c = c2;
			}
		}
		return stringBuilder.ToString();
	}

	private static int LabelWidth(Graphics g, string text, Font font)
	{
		return (int)Math.Round(UiTools.MeasureLine(g, text, font).Width);
	}

	private void LayoutChips(Graphics g)
	{
		chipRects.Clear();
		int num = base.Width - 8;
		int num2 = 4 + (options.ShowHeader ? headerHeight : 0);
		float num3 = Math.Max(8f, (float)Store.Settings.FontSize * ScaleFactor);
		int num4 = 14;
		int num5 = 4;
		int num6 = 0;
		for (int i = 0; i < 14; i++)
		{
			num6 = 0;
			using (Font font = Fonts.Widget(num3))
			{
				foreach (string chipLabel in chipLabels)
				{
					num6 += LabelWidth(g, chipLabel, font);
				}
			}
			if (num6 + num4 * chipLabels.Count + num5 * Math.Max(0, chipLabels.Count - 1) <= num)
			{
				break;
			}
			if (num4 > 7)
			{
				num4--;
				continue;
			}
			if (num5 > 1)
			{
				num5--;
				continue;
			}
			if (!(num3 > 8f))
			{
				break;
			}
			num3 -= 0.5f;
		}
		if (chipFont == null || Math.Abs(chipFont.SizeInPoints - num3) > 0.01f)
		{
			Dispose(ref chipFont);
			chipFont = Fonts.Widget(num3);
		}
		int count = chipLabels.Count;
		int[] array = new int[count];
		num6 = 0;
		for (int j = 0; j < count; j++)
		{
			array[j] = LabelWidth(g, chipLabels[j], chipFont);
			num6 += array[j];
		}
		int num7 = num - num6 - num4 * count - num5 * (count - 1);
		int num8 = ((count > 1) ? (num7 / (count - 1)) : 0);
		if (num8 > 16)
		{
			num8 = 16;
		}
		int num9 = num5 + ((num8 > 0) ? num8 : 0);
		int num10 = 4;
		for (int k = 0; k < count; k++)
		{
			chipRects.Add(new Rectangle(num10, num2, array[k] + num4, 26));
			num10 += array[k] + num4 + num9;
		}
		chipsHeight = 26;
		listTop = num2 + chipsHeight + 14;
	}

	private static StringFormat TightFormat()
	{
		return new StringFormat(StringFormat.GenericTypographic)
		{
			FormatFlags = (StringFormatFlags.NoWrap | StringFormatFlags.NoClip),
			Trimming = StringTrimming.None
		};
	}

	private static float LineWidth(Graphics g, string text, Font font)
	{
		if (text.Length == 0)
		{
			return 0f;
		}
		using StringFormat format = TightFormat();
		return g.MeasureString(text, font, int.MaxValue, format).Width;
	}

	private static List<string> WrapText(Graphics g, string text, Font font, int width, out int lineHeight)
	{
		List<string> list = new List<string>();
		lineHeight = (int)Math.Round(g.MeasureString("国Ag", font).Height);
		if (string.IsNullOrEmpty(text))
		{
			list.Add(string.Empty);
			return list;
		}
		int i = 0;
		while (i < text.Length)
		{
			string text2 = text.Substring(i);
			if (LineWidth(g, text2, font) <= (float)width)
			{
				list.Add(text2.TrimEnd());
				break;
			}
			int num = 1;
			int num2 = text2.Length - 1;
			int num3 = 1;
			while (num <= num2)
			{
				int num4 = (num + num2) / 2;
				if (LineWidth(g, text2.Substring(0, num4), font) <= (float)width)
				{
					num3 = num4;
					num = num4 + 1;
				}
				else
				{
					num2 = num4 - 1;
				}
			}
			int num5 = 0;
			while (num3 > 1 && num5++ < 80)
			{
				char c = text2[num3 - 1];
				char c2 = ((num3 < text2.Length) ? text2[num3] : '\0');
				bool flag = IsLatinDigit(c) && IsLatinDigit(c2);
				bool flag2 = IsLatinDigit(c) && IsCjk(c2);
				bool flag3 = num3 < text2.Length && IsNoLineStart(c2);
				if (!flag && !flag2 && !flag3)
				{
					break;
				}
				num3--;
			}
			string text3 = text2.Substring(0, num3);
			if (text3.Length == 0)
			{
				text3 = text2.Substring(0, 1);
			}
			list.Add(text3.TrimEnd());
			for (i += text3.Length; i < text.Length && text[i] == ' '; i++)
			{
			}
		}
		if (list.Count == 0)
		{
			list.Add(string.Empty);
		}
		return list;
	}

	private static bool IsLatinDigit(char c)
	{
		if ((c < '0' || c > '9') && (c < 'a' || c > 'z'))
		{
			if (c >= 'A')
			{
				return c <= 'Z';
			}
			return false;
		}
		return true;
	}

	private static bool IsCjk(char c)
	{
		if (c >= '⺀')
		{
			return c <= '\u9fff';
		}
		return false;
	}

	private static bool IsNoLineStart(char c)
	{
		return "，。、；：！？）】》」』〉〕｝…—·～%‰,.;:!?)]}".IndexOf(c) >= 0;
	}

	private RowInfo BuildRow(Graphics g, FeedItem item, int textWidth, int index)
	{
		int lineHeight;
		List<string> list = WrapText(g, item.Title, bodyFont, textWidth, out lineHeight);
		int textHeight = list.Count * lineHeight + 1;
		return new RowInfo
		{
			Lines = list,
			LineHeight = lineHeight,
			TextHeight = textHeight,
			Color = RowColors[index % RowColors.Length]
		};
	}

	private void LayoutRows(Graphics g)
	{
		rowRects.Clear();
		rowInfos.Clear();
		int num = base.Width - 8 - 2;
		if (num < 60)
		{
			num = 60;
		}
		int num2 = listTop;
		for (int i = 0; i < view.Count; i++)
		{
			RowInfo rowInfo = BuildRow(g, view[i], num, i);
			int num3 = rowInfo.TextHeight + 4;
			int num4 = bodyFont.Height + 4;
			if (num3 < num4)
			{
				num3 = num4;
			}
			rowRects.Add(new Rectangle(4, num2, base.Width - 8, num3));
			rowInfos.Add(rowInfo);
			num2 += num3 + 1;
		}
		totalHeight = num2 - listTop;
	}

	private void EnsureLayout(Graphics g)
	{
		EnsureView();
		if (layoutDirty || layoutWidth != base.Width)
		{
			LayoutChips(g);
			LayoutRows(g);
			layoutDirty = false;
			layoutWidth = base.Width;
		}
	}

	protected override void OnCanvasPaint(object sender, PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Rectangle rect = (lastClip = e.ClipRectangle);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.TextRenderingHint = base.SurfaceTextHint;
		if (layoutDirty)
		{
			EnsureLayout(graphics);
		}
		else
		{
			EnsureView();
		}
		int num = 4 + (options.ShowHeader ? headerHeight : 0);
		Rectangle rectangle = new Rectangle(0, 0, base.Width, num);
		Rectangle rectangle2 = new Rectangle(0, num, base.Width, chipsHeight + 2);
		Rectangle rectangle3 = new Rectangle(0, listTop, base.Width, Math.Max(0, base.Height - listTop - 30));
		Rectangle rectangle4 = new Rectangle(0, base.Height - 30, base.Width, 30);
		DrawCard(graphics);
		if (rectangle.IntersectsWith(rect) && options.ShowHeader)
		{
			DrawHeader(graphics);
		}
		if (rectangle2.IntersectsWith(rect))
		{
			DrawChips(graphics);
		}
		if (rectangle3.IntersectsWith(rect))
		{
			DrawRows(graphics);
		}
		if (rectangle4.IntersectsWith(rect))
		{
			DrawStatus(graphics);
		}
		if (rect.IntersectsWith(new Rectangle(base.Width - 18, base.Height - 18, 18, 18)))
		{
			DrawGrip(graphics);
		}
	}

	private Color Shade(int alpha, Color color)
	{
		return SurfaceColor((int)Math.Round((float)alpha * ((float)base.CardAlpha / 255f)), color);
	}

	private void DrawCard(Graphics g)
	{
		Rectangle rectangle = new Rectangle(2, 2, base.Width - 4, base.Height - 4);
		if (rectangle.Width <= 0 || rectangle.Height <= 0)
		{
			return;
		}
		Color color = SurfaceColor(base.CardAlpha, CardColor);
		if (lastClip.Left < rectangle.Left + 12 || lastClip.Top < rectangle.Top + 12 || lastClip.Right > rectangle.Right - 12 || lastClip.Bottom > rectangle.Bottom - 12)
		{
			using GraphicsPath path = UiTools.RoundedRect(rectangle, 12);
			using SolidBrush brush = new SolidBrush(color);
			g.FillPath(brush, path);
		}
		else
		{
			using SolidBrush brush2 = new SolidBrush(color);
			g.FillRectangle(brush2, rectangle);
		}
		if (base.Width <= 18 || base.Height <= 18)
		{
			return;
		}
		using SolidBrush brush3 = new SolidBrush(color);
		g.FillRectangle(brush3, base.Width - 18 - 2, base.Height - 18 - 2, 20, 20);
	}

	private void DrawHeader(Graphics g)
	{
		g.DrawString(dateText, headFont, new SolidBrush(HeadDate), 4f, 4f);
		g.DrawString(clockText, headFont, new SolidBrush(HeadClock), clockX, 4f);
		if (ipText.Length > 0)
		{
			g.DrawString(ipText, headFont, new SolidBrush(HeadIp), ipX, ipY);
		}
	}

	private void DrawChips(Graphics g)
	{
		for (int i = 0; i < chipRects.Count && i < chipLabels.Count; i++)
		{
			Rectangle rectangle = chipRects[i];
			bool flag = string.Equals(chipLabels[i], options.Category, StringComparison.Ordinal);
			Color color = ChipColors[i % ChipColors.Length];
			using (GraphicsPath path = UiTools.RoundedRect(rectangle, 5))
			{
				using (SolidBrush brush = new SolidBrush(flag ? Shade(70, color) : Shade(255, ChipBase)))
				{
					g.FillPath(brush, path);
				}
				using Pen pen = new Pen(flag ? Shade(200, color) : Shade(60, ChipLine));
				g.DrawPath(pen, path);
			}
			UiTools.DrawCentered(g, chipLabels[i], chipFont, rectangle, color);
		}
	}

	private void DrawRows(Graphics g)
	{
		int viewportHeight = ViewportHeight;
		int num = Math.Max(0, totalHeight - viewportHeight);
		if (scroll > num)
		{
			scroll = num;
		}
		if (scroll < 0)
		{
			scroll = 0;
		}
		scrollShown = Clamp(scrollShown, 0f, num);
		scrollTarget = Clamp(scrollTarget, 0f, num);
		g.SetClip(new Rectangle(4, listTop, base.Width - 8, viewportHeight));
		for (int i = 0; i < rowRects.Count; i++)
		{
			Rectangle rectangle = rowRects[i];
			int num2 = rectangle.Top - scroll;
			if (num2 > listTop + viewportHeight)
			{
				break;
			}
			if (num2 + rectangle.Height < listTop)
			{
				continue;
			}
			Rectangle bounds = new Rectangle(rectangle.X, num2, rectangle.Width, rectangle.Height);
			using (GraphicsPath path = UiTools.RoundedRect(bounds, 6))
			{
				using SolidBrush brush = new SolidBrush((i == hoverRow) ? Shade(130, Color.Black) : Shade(70, Color.Black));
				g.FillPath(brush, path);
			}
			using (Pen pen = new Pen((i == hoverRow) ? Shade(60, Color.White) : Shade(26, Color.White)))
			{
				g.DrawLine(pen, bounds.Left + 6, bounds.Bottom - 1, bounds.Right - 6, bounds.Bottom - 1);
			}
			RowInfo rowInfo = rowInfos[i];
			int num3 = bounds.Top + Math.Max(0, (bounds.Height - rowInfo.TextHeight) / 2);
			using SolidBrush brush2 = new SolidBrush(rowInfo.Color);
			using StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic);
			stringFormat.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
			stringFormat.Trimming = StringTrimming.None;
			for (int j = 0; j < rowInfo.Lines.Count; j++)
			{
				if (rowInfo.Lines[j].Length != 0)
				{
					g.DrawString(rowInfo.Lines[j], bodyFont, brush2, bounds.Left + 2, num3 + j * rowInfo.LineHeight, stringFormat);
				}
			}
		}
		g.ResetClip();
		Color color = SurfaceColor(base.CardAlpha, CardColor);
		int num4 = Math.Min(40, viewportHeight);
		if (num4 <= 0)
		{
			return;
		}
		if (scroll > 0)
		{
			Rectangle rect = new Rectangle(4, listTop, base.Width - 8, num4);
			using LinearGradientBrush brush3 = new LinearGradientBrush(new Rectangle(rect.X, rect.Y, rect.Width, 1), Color.FromArgb(0, color), color, LinearGradientMode.Vertical);
			g.FillRectangle(brush3, rect);
		}
	}

	private void DrawStatus(Graphics g)
	{
		using (SolidBrush brush = new SolidBrush(SurfaceColor(base.CardAlpha, CardColor)))
		{
			g.FillRectangle(brush, 0, base.Height - 30, base.Width, 30);
		}
		string s = ((options.Category == "全部") ? ("共 " + items.Count + " 条") : ("共 " + view.Count + " 条（" + options.Category + "）"));
		float num = base.Height - 30 + 4;
		using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(175, 180, 185)))
		{
			g.DrawString(s, statusFont, brush2, 4f, num, StringFormat.GenericTypographic);
		}
		string s2;
		if (fetching)
		{
			s2 = "正在刷新…";
		}
		else
		{
			string text = ((options.LastRefreshTicks == 0L) ? "—" : new DateTime(options.LastRefreshTicks).ToString("HH:mm:ss", CultureInfo.InvariantCulture));
			s2 = "已刷新 · 最后刷新 " + text;
		}
		float num2 = UiTools.MeasureLine(g, s2, statusFont).Width;
		using SolidBrush brush3 = new SolidBrush(fetching ? HeadIp : Color.FromArgb(120, 220, 170));
		g.DrawString(s2, statusFont, brush3, (float)base.Width - num2 - 4f, num, StringFormat.GenericTypographic);
	}

	private void DrawGrip(Graphics g)
	{
		using Pen pen = new Pen(Color.FromArgb(120, 120, 120));
		for (int i = 0; i < 3; i++)
		{
			g.DrawLine(pen, base.Width - 7 - i * 5, base.Height - 7, base.Width - 7, base.Height - 7 - i * 5);
		}
	}

	protected override void OnCanvasMouseDown(object sender, MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Left)
		{
			return;
		}
		if (InResizeCorner(e.X, e.Y))
		{
			resizing = true;
			resizeOrigin = new Point(e.X, e.Y);
			resizeStart = new Size(base.Width, base.Height);
			SetSurfaceCapture(value: true);
			Cursor.Current = Cursors.SizeNWSE;
			return;
		}
		base.OnCanvasMouseDown(sender, e);
		int num = ChipAt(e.X, e.Y);
		if (num >= 0)
		{
			options.Category = chipLabels[num];
			RssOptionsStore.Save(options);
			ResetScroll();
			viewDirty = true;
			layoutDirty = true;
			InvalidateSurface();
			return;
		}
		int num2 = RowAt(e.X, e.Y);
		if (num2 < 0 || num2 >= view.Count || string.IsNullOrEmpty(view[num2].Link))
		{
			return;
		}
		try
		{
			Process.Start(view[num2].Link);
		}
		catch
		{
		}
	}

	protected override void OnCanvasMouseMove(object sender, MouseEventArgs e)
	{
		if (resizing)
		{
			if ((Control.MouseButtons & MouseButtons.Left) != 0)
			{
				int num = Math.Max(320, resizeStart.Width + (e.X - resizeOrigin.X));
				int num2 = Math.Max(420, resizeStart.Height + (e.Y - resizeOrigin.Y));
				if (num != base.Width || num2 != base.Height)
				{
					base.Size = new Size(num, num2);
				}
				return;
			}
			resizing = false;
			SetSurfaceCapture(value: false);
		}
		Cursor.Current = (InResizeCorner(e.X, e.Y) ? Cursors.SizeNWSE : Cursors.Default);
		int num3 = RowAt(e.X, e.Y);
		if (num3 != hoverRow)
		{
			InvalidateRow(hoverRow);
			hoverRow = num3;
			InvalidateRow(hoverRow);
		}
		base.OnCanvasMouseMove(sender, e);
	}

	protected override void OnCanvasMouseUp(object sender, MouseEventArgs e)
	{
		if (resizing)
		{
			resizing = false;
			SetSurfaceCapture(value: false);
			layoutDirty = true;
			InvalidateSurface();
		}
		else
		{
			base.OnCanvasMouseUp(sender, e);
		}
	}

	protected override void OnMouseCaptureChanged(EventArgs e)
	{
		if (resizing && !base.SurfaceHasCapture)
		{
			resizing = false;
		}
		base.OnMouseCaptureChanged(e);
	}

	private int ChipAt(int x, int y)
	{
		for (int i = 0; i < chipRects.Count; i++)
		{
			if (chipRects[i].Contains(x, y))
			{
				return i;
			}
		}
		return -1;
	}

	private int RowAt(int x, int y)
	{
		if (y < listTop || y >= listTop + ViewportHeight)
		{
			return -1;
		}
		int result = -1;
		for (int i = 0; i < rowRects.Count; i++)
		{
			Rectangle rectangle = rowRects[i];
			int num = rectangle.Top - scroll;
			if (y >= num && y < num + rectangle.Height)
			{
				result = i;
				break;
			}
			if (num > y)
			{
				break;
			}
		}
		return result;
	}

	protected override void OnSurfaceMouseWheel(MouseEventArgs e)
	{
		int num = Math.Max(0, totalHeight - ViewportHeight);
		if (num <= 0)
		{
			scrollShown = (scrollTarget = 0f);
			scroll = 0;
			InvalidateList();
			return;
		}
		wheelRemainder -= (float)e.Delta * 0.75f;
		if (wheelRemainder > 480f)
		{
			wheelRemainder = 480f;
		}
		if (wheelRemainder < -480f)
		{
			wheelRemainder = -480f;
		}
		float num2 = Clamp((float)scroll + wheelRemainder, 0f, num);
		wheelRemainder = num2 - (float)scroll;
		scroll = (int)Math.Round(num2);
		scrollShown = (scrollTarget = scroll);
		ScheduleListRepaint();
	}

	private void InvalidateList()
	{
		try
		{
			int viewportHeight = ViewportHeight;
			if (viewportHeight <= 0 || listTop <= 0)
			{
				InvalidateSurface();
			}
			else
			{
				InvalidateSurface(new Rectangle(4, listTop, Math.Max(1, base.Width - 8), viewportHeight));
			}
		}
		catch
		{
			InvalidateSurface();
		}
	}

	private void ScheduleListRepaint()
	{
		listDirty = true;
		if (scrollTimer == null)
		{
			scrollTimer = new System.Windows.Forms.Timer
			{
				Interval = 16
			};
			scrollTimer.Tick += delegate
			{
				scrollTimer.Stop();
				if (listDirty)
				{
					listDirty = false;
					InvalidateList();
				}
			};
		}
		if (!scrollTimer.Enabled)
		{
			scrollTimer.Start();
		}
	}

	private void InvalidateRow(int index)
	{
		if (index < 0 || index >= rowRects.Count)
		{
			return;
		}
		try
		{
			Rectangle rectangle = rowRects[index];
			Rectangle a = new Rectangle(rectangle.X, rectangle.Top - scroll, rectangle.Width, rectangle.Height);
			Rectangle b = new Rectangle(4, listTop, base.Width - 8, ViewportHeight);
			Rectangle dirty = Rectangle.Intersect(a, b);
			if (dirty.Width > 0 && dirty.Height > 0)
			{
				InvalidateSurface(dirty);
			}
		}
		catch
		{
		}
	}

	private static float Clamp(float value, float low, float high)
	{
		if (!(value < low))
		{
			if (!(value > high))
			{
				return value;
			}
			return high;
		}
		return low;
	}

	protected override void BuildMenu()
	{
		ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem("立即刷新");
		toolStripMenuItem.Click += delegate
		{
			StartFetch(showStatus: true);
		};
		Menu.Items.Add(toolStripMenuItem);
		ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem("重新加载订阅源");
		toolStripMenuItem2.Click += delegate
		{
			LoadSources();
			StartFetch(showStatus: true);
		};
		Menu.Items.Add(toolStripMenuItem2);
		ToolStripMenuItem toolStripMenuItem3 = new ToolStripMenuItem("编辑订阅源…");
		toolStripMenuItem3.Click += delegate
		{
			WriteSourceTemplate();
			try
			{
				Process.Start("notepad.exe", "\"" + SourceFile + "\"");
			}
			catch
			{
			}
		};
		Menu.Items.Add(toolStripMenuItem3);
		ToolStripMenuItem toolStripMenuItem4 = new ToolStripMenuItem("自动刷新间隔");
		int[] array = new int[5] { 5, 10, 15, 30, 60 };
		for (int i = 0; i < array.Length; i++)
		{
			int num = array[i];
			int captured = num;
			ToolStripMenuItem toolStripMenuItem5 = new ToolStripMenuItem(num + " 分钟")
			{
				Tag = "refresh:" + captured
			};
			toolStripMenuItem5.Click += delegate
			{
				options.RefreshMinutes = captured;
				RssOptionsStore.Save(options);
				refreshTimer.Interval = captured * 60 * 1000;
				refreshTimer.Stop();
				refreshTimer.Start();
			};
			toolStripMenuItem4.DropDownItems.Add(toolStripMenuItem5);
		}
		Menu.Items.Add(toolStripMenuItem4);
		ToolStripMenuItem toolStripMenuItem6 = new ToolStripMenuItem("显示日期时钟")
		{
			Checked = options.ShowHeader
		};
		toolStripMenuItem6.Click += delegate
		{
			options.ShowHeader = !options.ShowHeader;
			RssOptionsStore.Save(options);
			headerHeight = 0;
			layoutDirty = true;
			UpdateHeader(force: true);
			InvalidateSurface();
		};
		Menu.Items.Add(toolStripMenuItem6);
		ToolStripMenuItem toolStripMenuItem7 = new ToolStripMenuItem("显示本机IP")
		{
			Checked = options.ShowIp
		};
		toolStripMenuItem7.Click += delegate
		{
			options.ShowIp = !options.ShowIp;
			RssOptionsStore.Save(options);
			ipText = (options.ShowIp ? ("本机IP：" + LocalIp()) : string.Empty);
			layoutDirty = true;
			UpdateHeader(force: true);
			InvalidateSurface();
		};
		Menu.Items.Add(toolStripMenuItem7);
	}

	protected override void OnMenuClick(string tag)
	{
		if (!string.IsNullOrEmpty(tag))
		{
			int result2;
			if (tag.StartsWith("font:", StringComparison.Ordinal) && int.TryParse(tag.Substring(5), out var result))
			{
				ApplyFontSize(result);
			}
			else if (tag.StartsWith("opacity:", StringComparison.Ordinal) && int.TryParse(tag.Substring(8), out result2))
			{
				ApplyOpacity(result2);
			}
		}
	}

	protected override void OnMenuOpening()
	{
		base.OnMenuOpening();
		ToolStripMenuItem toolStripMenuItem = FindMenuItem("自动刷新间隔");
		if (toolStripMenuItem == null)
		{
			return;
		}
		foreach (ToolStripItem dropDownItem in toolStripMenuItem.DropDownItems)
		{
			if (dropDownItem is ToolStripMenuItem { Tag: not null } toolStripMenuItem2)
			{
				toolStripMenuItem2.Checked = string.Equals(toolStripMenuItem2.Tag.ToString(), "refresh:" + options.RefreshMinutes, StringComparison.Ordinal);
			}
		}
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		ipText = (options.ShowIp ? ("本机IP：" + LocalIp()) : string.Empty);
		clockTimer.Start();
		refreshTimer.Start();
		UpdateHeader(force: true);
		StartFetch(showStatus: true);
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		clockTimer.Stop();
		refreshTimer.Stop();
		if (scrollTimer != null)
		{
			scrollTimer.Stop();
		}
		base.OnFormClosing(e);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			clockTimer.Stop();
			refreshTimer.Stop();
			if (scrollTimer != null)
			{
				scrollTimer.Stop();
				scrollTimer.Dispose();
				scrollTimer = null;
			}
			foreach (Font value in fontCache.Values)
			{
				value.Dispose();
			}
			fontCache.Clear();
			Dispose(ref headFont);
			Dispose(ref bodyFont);
			Dispose(ref smallFont);
			Dispose(ref chipFont);
			Dispose(ref statusFont);
		}
		base.Dispose(disposing);
	}
}
