using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Shared;

namespace DesktopLunar
{
    /// <summary>
    /// 桌面阴历挂件：一行字显示「2026年10月23日13:06:33丙午马年九月十四」——
    /// 公历日期 + 时分秒（每秒走字）+ 干支生肖年 + 农历月日。
    ///
    /// 无背景、完全透明（逐像素 alpha 分层窗口，不是颜色键）；
    /// 可自由拖动，可锁定位置（锁定/解锁都有淡出提示）；
    /// 右键菜单可调日期颜色与时间颜色（赤橙黄绿青蓝紫白 8 色，时间默认跟随日期），
    /// 字号 18~36pt 共十档；自动网络对时（SNTP，30 分钟一次，可手动"立即对时"）；
    /// "只在桌面显示"是基类默认行为（被任何程序窗口盖住，只看空桌面时可见）。
    /// </summary>
    internal sealed class LunarClockForm : WidgetForm
    {
        // 八种颜色：赤橙黄绿青蓝紫 + 白
        private static readonly string[] ColorKeys = { "red", "orange", "yellow", "green", "cyan", "blue", "purple", "white" };
        private static readonly string[] ColorLabels = { "赤", "橙", "黄", "绿", "青", "蓝", "紫", "白" };

        /// <summary>
        /// 八个色的具体值。不能直接用纯红纯黄：浅色壁纸上 255,255,0 这种字看不见，
        /// 这里在"还是这个色相"的前提下稍微压深一点，配合 1px 深色投影保证可读。
        /// 白也压成 250 而非 255，道理相同（纯白配投影容易糊边）。
        /// </summary>
        private static readonly Color[] Palette =
        {
            Color.FromArgb(224, 48, 40),    // 赤
            Color.FromArgb(240, 128, 24),   // 橙
            Color.FromArgb(232, 196, 32),   // 黄
            Color.FromArgb(52, 176, 84),    // 绿
            Color.FromArgb(0, 176, 180),    // 青
            Color.FromArgb(56, 112, 224),   // 蓝
            Color.FromArgb(152, 84, 204),   // 紫
            Color.FromArgb(250, 250, 252),  // 白
        };

        private readonly System.Windows.Forms.Timer clock;
        private ToolStripMenuItem colorMenu;
        private ToolStripMenuItem timeColorMenu;
        private ToolStripMenuItem syncMenu;
        private int syncing;                 // 0/1：正在对时（手动连点只提示不排队）

        private string text = string.Empty;
        private string dateText = string.Empty;
        private string timeText = string.Empty;
        private string lunarText = string.Empty;
        private string second = string.Empty;
        private string measuredKey = string.Empty;
        private float dpi = 96f;

        public LunarClockForm()
        {
            // 窗口标题：无边框窗口看不见标题，但截图工具/排查时要按标题找窗口
            Text = "桌面阴历";

            // 右键菜单必须在这里建（基类只提供 BuildMenu 钩子，不会自动建）
            BuildBaseMenu();

            // 纯文字挂件没有背景卡片，"透明度"菜单项对它没有任何效果，摘掉
            RemoveMenuItem("透明度");

            // 200ms 一醒，秒数变了才重画（每秒最多重画 5 次中的 1 次）
            clock = new System.Windows.Forms.Timer { Interval = 200 };
            clock.Tick += delegate { Tick(); };
            clock.Start();
        }

        // ---------------- 基类重载 ----------------

        /// <summary>走真正的逐像素透明（UpdateLayeredWindow）：文字以外的地方 alpha=0，直接透出桌面。</summary>
        protected override bool UseLayeredSurface
        {
            get { return true; }
        }

        protected override string DisplayName
        {
            get { return "桌面阴历"; }
        }

        public override int DefaultFontSize
        {
            get { return 24; }
        }

        public override int MinFontSize
        {
            get { return 18; }
        }

        public override int MaxFontSize
        {
            get { return 36; }
        }

        /// <summary>用户要求的十档：18-20-22-24-26-28-30-32-34-36（pt）。</summary>
        public override int[] FontSizes()
        {
            return new[] { 18, 20, 22, 24, 26, 28, 30, 32, 34, 36 };
        }

        /// <summary>在基类菜单（锁定位置/只在桌面显示/字体大小/回到默认位置/开机启动/关于/退出）后面加颜色子菜单与对时项。</summary>
        protected override void BuildMenu()
        {
            colorMenu = new ToolStripMenuItem("日期颜色");
            AddColorItems(colorMenu, "color:");
            Menu.Items.Add(colorMenu);

            timeColorMenu = new ToolStripMenuItem("时间颜色");
            AddColorItems(timeColorMenu, "timecolor:");
            Menu.Items.Add(timeColorMenu);

            syncMenu = new ToolStripMenuItem("立即对时");
            syncMenu.Tag = "sync";
            syncMenu.ToolTipText = "从网络时间服务器（NTP）校准本机时钟偏差，本机慢/快多少秒都会按网络时间显示。";
            syncMenu.Click += delegate { OnMenuClick("sync"); };
            Menu.Items.Add(syncMenu);
        }

        private void AddColorItems(ToolStripMenuItem menu, string tagPrefix)
        {
            // 时间颜色子菜单第一项：跟随日期颜色（不单独设色，时间跟着日期一起变）
            if (string.Equals(tagPrefix, "timecolor:", StringComparison.Ordinal))
            {
                var follow = new ToolStripMenuItem("跟随日期颜色");
                follow.Tag = "timecolor:";          // 键为空 = 跟随
                follow.Click += delegate { OnMenuClick("timecolor:"); };
                menu.DropDownItems.Add(follow);
            }

            for (int i = 0; i < ColorKeys.Length; i++)
            {
                int index = i;
                var item = new ToolStripMenuItem(ColorLabels[i]);
                item.ForeColor = Palette[i];          // 菜单文字直接染成对应颜色，不用猜
                item.Tag = tagPrefix + ColorKeys[i];
                item.Click += delegate { OnMenuClick(tagPrefix + ColorKeys[index]); };
                // 白字配白底菜单等于隐形，这一项给个深色底
                if (string.Equals(ColorKeys[i], "white", StringComparison.Ordinal))
                {
                    item.BackColor = Color.FromArgb(32, 32, 36);
                }

                menu.DropDownItems.Add(item);
            }
        }

        protected override void OnMenuClick(string tag)
        {
            if (tag.StartsWith("font:", StringComparison.Ordinal))
            {
                int size;
                if (int.TryParse(tag.Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out size))
                {
                    ApplyFontSize(size);
                    ApplyTextLayout();      // 字号变了，窗口要跟着变大变小
                    Log("字号 " + size + "pt");
                }

                return;
            }

            if (tag.StartsWith("timecolor:", StringComparison.Ordinal))
            {
                // 空键 = 跟随日期颜色；存 null，下次启动还是跟随
                string key = tag.Substring(10);
                Store.Settings.TimeColor = string.IsNullOrEmpty(key) ? null : key;
                Store.Save();
                InvalidateSurface();
                Log(string.IsNullOrEmpty(key) ? "时间颜色 跟随日期" : "时间颜色 " + key);
                return;
            }

            if (tag.StartsWith("color:", StringComparison.Ordinal))
            {
                Store.Settings.FontColor = tag.Substring(6);
                Store.Save();
                InvalidateSurface();
                Log("日期颜色 " + tag.Substring(6));
                return;
            }

            if (string.Equals(tag, "sync", StringComparison.Ordinal))
            {
                SyncTime(true);
                return;
            }

            if (tag.StartsWith("opacity:", StringComparison.Ordinal))
            {
                ApplyOpacity(int.Parse(tag.Substring(8), CultureInfo.InvariantCulture));
            }
        }

        /// <summary>菜单每次弹出前刷新两个颜色子菜单的勾选态（基类 RefreshMenuState 会调这里）。</summary>
        protected override void OnMenuOpening()
        {
            RefreshColorChecks(colorMenu, CurrentColorKey);
            RefreshColorChecks(timeColorMenu, CurrentTimeColorKey);
        }

        private void RefreshColorChecks(ToolStripMenuItem menu, string currentKey)
        {
            if (menu == null) { return; }
            foreach (ToolStripItem item in menu.DropDownItems)
            {
                var entry = item as ToolStripMenuItem;
                if (entry == null || entry.Tag == null) { continue; }
                string tag = entry.Tag.ToString();
                int colon = tag.IndexOf(':');
                entry.Checked = colon > 0
                    && string.Equals(tag.Substring(colon + 1), currentKey, StringComparison.Ordinal);
            }
        }

        /// <summary>锁定了还去拖：给条提示，别让用户以为挂件卡死了。</summary>
        protected override void OnCanvasMouseDown(object sender, MouseEventArgs e)
        {
            base.OnCanvasMouseDown(sender, e);
            if (e.Button == MouseButtons.Left && Store.Settings.Locked)
            {
                ShowHint("位置已锁定：右键菜单里点\"解锁位置\"可恢复拖动");
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // 窗口建好之后才拿得到真实 DPI（WinForms 的 DeviceDpi 是陈旧值，
            // CreateGraphics().DpiX 才是当前显示器上的真实值）
            using (var probe = CreateGraphics())
            {
                dpi = probe.DpiX;
            }

            if (dpi <= 0f) { dpi = 96f; }

            ApplyTextLayout();
            RestoreMyPosition();
            LogStartup();

            // 自动网络对时：进来先对一次，之后每 30 分钟一次。
            // 对时在后台线程跑，显示时间 = 本机时间 + 网络偏差（不动系统时钟）。
            TimeSync.Start(TimeSpan.FromMinutes(30), OnSyncFinished);
        }

        /// <summary>对时完成回调。注意：定时对时是在线程池线程上回调的，UI 操作必须跳回 UI 线程。</summary>
        private void OnSyncFinished(SyncResult r)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(new Action<SyncResult>(OnSyncFinishedUi), r);
                }
            }
            catch { }
        }

        /// <summary>手动点"立即对时"。后台线程查服务器，回来后给提示；连点不排队。</summary>
        private void SyncTime(bool manual)
        {
            if (Interlocked.Exchange(ref syncing, 1) == 1)
            {
                if (manual) { ShowHint("正在对时，请稍候…"); }
                return;
            }

            if (manual) { ShowHint("正在对时…"); }

            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var r = TimeSync.SyncOnce();
                    try
                    {
                        if (IsHandleCreated && !IsDisposed)
                        {
                            BeginInvoke(new Action<SyncResult>(OnSyncFinishedUi), r);
                            if (manual)
                            {
                                BeginInvoke(new Action(() => ShowHint(r.Ok
                                    ? "对时成功：" + TimeSync.StatusText
                                    : "对时失败：连不上时间服务器，暂时用本机时间")));
                            }
                        }
                    }
                    catch { }
                }
                finally
                {
                    Interlocked.Exchange(ref syncing, 0);
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        /// <summary>对时结果的 UI 侧处理（菜单文字、日志）。</summary>
        private void OnSyncFinishedUi(SyncResult r)
        {
            try
            {
                if (syncMenu != null)
                {
                    syncMenu.Text = "立即对时（" + TimeSync.StatusText + "）";
                }

                if (r.Ok)
                {
                    Log("网络对时：" + r.Server + " 偏差 "
                        + r.Offset.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)
                        + " 秒（往返 " + r.DelayMs.ToString("0", CultureInfo.InvariantCulture) + "ms）");
                }
                else
                {
                    Log("网络对时失败：时间服务器都连不上，继续用本机时间");
                }
            }
            catch { }
        }

        // ---------------- 位置 ----------------

        /// <summary>
        /// 自己的位置恢复逻辑。基类的 RestorePosition 会把窗口高度强设成至少 200px，
        /// 这个挂件只有一行字，用它会被拉出一大片空白，所以不用它，只借它的判定思路。
        /// </summary>
        private void RestoreMyPosition()
        {
            Rectangle area = PlacementArea;
            int x = Store.Settings.X;
            int y = Store.Settings.Y;
            if (Store.Settings.FirstRun || (x <= 0 && y <= 0))
            {
                Rectangle fallback = DefaultBounds;
                x = fallback.Left;
                y = fallback.Top;
                Store.Settings.X = x;
                Store.Settings.Y = y;
                Store.Settings.FirstRun = false;
                Store.Save();
            }
            else
            {
                int maxX = Math.Max(area.Left, area.Right - Width);
                int maxY = Math.Max(area.Top, area.Bottom - Height);
                x = Math.Max(area.Left, Math.Min(x, maxX));
                y = Math.Max(area.Top, Math.Min(y, maxY));
            }

            Location = new Point(x, y);
        }

        /// <summary>默认位置：屏幕顶部正中间（用户指定）。</summary>
        public override Rectangle DefaultBounds
        {
            get
            {
                Rectangle area = PlacementArea;
                Size size = MeasureWidgetSize();
                return new Rectangle(area.Left + (area.Width - size.Width) / 2, area.Top, size.Width, size.Height);
            }
        }

        /// <summary>窗口变大变小之后夹回屏幕工作区，别有一截拖到屏幕外点不着。</summary>
        private void ClampIntoWorkArea()
        {
            Rectangle area = PlacementArea;
            int x = Math.Max(area.Left, Math.Min(Left, Math.Max(area.Left, area.Right - Width)));
            int y = Math.Max(area.Top, Math.Min(Top, Math.Max(area.Top, area.Bottom - Height)));
            if (x != Left || y != Top) { Location = new Point(x, y); }
        }

        // ---------------- 尺寸与文本 ----------------

        /// <summary>pt → 物理像素。1pt = 1/72 英寸，150% 缩放的屏上 24pt 就是 48px。</summary>
        private float PtToPx(float pt)
        {
            return pt * dpi / 72f;
        }

        private int PadX
        {
            get { return (int)Math.Round(PtToPx(Store.Settings.FontSize) * 0.22f); }
        }

        private int PadY
        {
            get { return (int)Math.Round(PtToPx(Store.Settings.FontSize) * 0.14f); }
        }

        /// <summary>
        /// 量尺寸用的"模板文本"：时间部分换成 88:88:88。
        /// 比例字体下 "1" 比 "8" 窄，若按真实秒数量尺寸，窗口会逐秒跳动；
        /// 用最坏的数字量一次，窗口宽度就固定了。
        /// </summary>
        private string TemplateText()
        {
            DateTime now = TimeSync.Now;
            var lunar = Lunar.FromSolar(now);
            return string.Concat(
                now.Year.ToString(CultureInfo.InvariantCulture), "年",
                now.Month.ToString(CultureInfo.InvariantCulture), "月",
                now.Day.ToString(CultureInfo.InvariantCulture), "日",
                "88:88:88",
                lunar.ShortYearText,
                lunar.ToString());
        }

        private Size MeasureWidgetSize()
        {
            try
            {
                float px = PtToPx(Store.Settings.FontSize);
                using (var probe = new Bitmap(2, 2))
                using (var g = Graphics.FromImage(probe))
                using (var font = new Font(Fonts.FamilyName, px, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var format = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.FitBlackBox))
                {
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Near;
                    SizeF measured = g.MeasureString(TemplateText(), font, PointF.Empty, format);
                    return new Size(
                        (int)Math.Ceiling(measured.Width) + PadX * 2,
                        (int)Math.Ceiling(measured.Height) + PadY * 2);
                }
            }
            catch
            {
                // 字体出问题也不能没有尺寸，给个能看的默认值
                return new Size(640, 60);
            }
        }

        /// <summary>
        /// 字号/DPI/日期变了才重新量尺寸（measuredKey 没变就只换文字），
        /// 然后按最新一秒重画。每秒走一次也只会走到"文字变了"那一支。
        /// </summary>
        private void ApplyTextLayout()
        {
            DateTime now = TimeSync.Now;
            string key = Store.Settings.FontSize + "|" + now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + "|" + dpi.ToString(CultureInfo.InvariantCulture);
            if (key != measuredKey)
            {
                measuredKey = key;
                Size want = MeasureWidgetSize();
                if (want.Width > 0 && want.Height > 0 && want != Size)
                {
                    Size = want;
                    ClampIntoWorkArea();
                }
            }

            ComposeText(now);
            second = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            InvalidateSurface();
        }

        /// <summary>
        /// 用户要的格式：2026年10月23日13:06:33丙午马年九月十四（无分隔符，原样拼）。
        /// 拆成三段存起来：日期 | 时间 | 农历，绘制时段与段可以分别上色。
        /// </summary>
        private void ComposeText(DateTime now)
        {
            var lunar = Lunar.FromSolar(now);
            dateText = now.Year.ToString(CultureInfo.InvariantCulture) + "年"
                     + now.Month.ToString(CultureInfo.InvariantCulture) + "月"
                     + now.Day.ToString(CultureInfo.InvariantCulture) + "日";
            timeText = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            lunarText = lunar.ShortYearText + lunar.ToString();
            text = dateText + timeText + lunarText;
        }

        private void Tick()
        {
            string nowSecond = TimeSync.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            if (nowSecond == second) { return; }
            ApplyTextLayout();   // 跨零点时 measuredKey 会变，顺带重新量尺寸
        }

        // ---------------- 绘制 ----------------

        protected override void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            // 分层窗口按 alpha 做命中测试：alpha=0 的地方鼠标点不到，挂件就拖不动。
            // 铺一层 alpha=1 的底（肉眼不可见，合成后和桌面只差 0.4%），整块都能点。
            using (var hit = new SolidBrush(SurfaceColor(0, 0, 0, 0)))
            {
                g.FillRectangle(hit, ClientRectangle);
            }

            float px = PtToPx(Store.Settings.FontSize);
            using (var font = new Font(Fonts.FamilyName, px, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var format = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.FitBlackBox))
            {
                // 先画整行的 1px 深色投影：浅色壁纸上黄字绿字也看得清（是字的影子，不是背景）
                int shadowOffset = Math.Max(1, (int)Math.Round(px / 28f));
                using (var shadow = new SolidBrush(Color.FromArgb(120, 16, 16, 16)))
                {
                    g.DrawString(text, font, shadow, PadX + shadowOffset, PadY + shadowOffset, format);
                }

                // 三段分别上色：日期（黄） | 时间（青，可单独调） | 农历（黄）
                float x = PadX;
                using (var dateBrush = new SolidBrush(TextColor))
                using (var timeBrush = new SolidBrush(TimeTextColor))
                using (var lunarBrush = new SolidBrush(TextColor))
                {
                    g.DrawString(dateText, font, dateBrush, x, PadY, format);
                    x += g.MeasureString(dateText, font, PointF.Empty, format).Width;
                    g.DrawString(timeText, font, timeBrush, x, PadY, format);
                    x += g.MeasureString(timeText, font, PointF.Empty, format).Width;
                    g.DrawString(lunarText, font, lunarBrush, x, PadY, format);
                }
            }
        }

        // ---------------- 颜色 ----------------

        /// <summary>日期与农历部分的颜色键，缺省黄（用户指定默认黄色）。</summary>
        private string CurrentColorKey
        {
            get
            {
                string key = Store.Settings.FontColor;
                if (string.IsNullOrEmpty(key)) { return "yellow"; }
                for (int i = 0; i < ColorKeys.Length; i++)
                {
                    if (string.Equals(ColorKeys[i], key, StringComparison.OrdinalIgnoreCase)) { return ColorKeys[i]; }
                }

                return "yellow";
            }
        }

        /// <summary>
        /// 时间部分（HH:mm:ss）的颜色键；空串 = 跟随日期颜色。
        /// 用户没单独设过色时，时间跟着日期一起变色，菜单里对应项也保持勾选一致。
        /// </summary>
        private string CurrentTimeColorKey
        {
            get
            {
                string key = Store.Settings.TimeColor;
                if (string.IsNullOrEmpty(key)) { return string.Empty; }
                for (int i = 0; i < ColorKeys.Length; i++)
                {
                    if (string.Equals(ColorKeys[i], key, StringComparison.OrdinalIgnoreCase)) { return ColorKeys[i]; }
                }

                return string.Empty;
            }
        }

        private Color TextColor
        {
            get { return Palette[Array.IndexOf(ColorKeys, CurrentColorKey)]; }
        }

        private Color TimeTextColor
        {
            get
            {
                string key = CurrentTimeColorKey;
                if (string.IsNullOrEmpty(key)) { return TextColor; }   // 跟随日期颜色
                return Palette[Array.IndexOf(ColorKeys, key)];
            }
        }

        // ---------------- 杂项 ----------------

        private void RemoveMenuItem(string text)
        {
            for (int i = Menu.Items.Count - 1; i >= 0; i--)
            {
                var item = Menu.Items[i] as ToolStripMenuItem;
                if (item != null && string.Equals(item.Text, text, StringComparison.Ordinal))
                {
                    Menu.Items.RemoveAt(i);
                }
            }
        }

        private void LogStartup()
        {
            Log("启动 字体=" + Fonts.FamilyName
                + " dpi=" + dpi.ToString(CultureInfo.InvariantCulture)
                + " 字号=" + Store.Settings.FontSize + "pt"
                + " 日期颜色=" + CurrentColorKey
                + " 时间颜色=" + (string.IsNullOrEmpty(CurrentTimeColorKey) ? "跟随日期" : CurrentTimeColorKey)
                + " 对时=" + TimeSync.StatusText
                + " 尺寸=" + Width + "x" + Height
                + " 位置=" + Left + "," + Top);
        }

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppPaths.DataDirectory, "log.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
