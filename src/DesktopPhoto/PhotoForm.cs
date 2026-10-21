using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Windows.Forms;
using Shared;

namespace DesktopPhoto
{
    [DataContract]
    internal sealed class FrameSettings
    {
        /// <summary>放照片的文件夹。空 = 还没选过。</summary>
        [DataMember] public string Folder { get; set; }

        /// <summary>轮播间隔（秒）。</summary>
        [DataMember] public int Interval { get; set; }

        /// <summary>用可空：老设置里没有这一项时按默认值走，而不是被当成 false。</summary>
        [DataMember] public bool? AutoPlay { get; set; }

        /// <summary>true = 铺满（裁掉多余部分），false = 适应（整张都看得见，留边）。</summary>
        [DataMember] public bool? Fill { get; set; }

        /// <summary>画不画那圈白色相框边。</summary>
        [DataMember] public bool? ShowFrame { get; set; }

        /// <summary>上次看到的那张，按文件名记，换了文件夹也不会指错。</summary>
        [DataMember] public string Current { get; set; }
    }

    /// <summary>
    /// 桌面相框挂件：把某个文件夹里的照片贴在桌面上，自动轮播。
    ///
    /// 卡片本身是分层窗口，只负责画；换图、选文件夹这些操作全在右键菜单里。
    /// 图片不预先加载到内存里常驻 —— 只留当前这一张，切走时立刻释放，
    /// 否则一个几百张的文件夹会把内存吃掉一大截。
    /// </summary>
    internal sealed class PhotoForm : WidgetForm
    {
        private static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

        private const float BaseWidth = 460f;
        private const float BaseHeight = 400f;
        private const float CaptionHeight = 26f;
        private const float Pad = 10f;

        private readonly FrameSettings frame;
        private readonly string framePath;
        private readonly List<string> files = new List<string>();
        private readonly Timer rotate = new Timer();

        private Image picture;          // 当前这张的原图
        private string picturePath;     // 它对应的文件
        private Bitmap scaled;          // 按当前显示区域缩好的缓存
        private bool cropThis;          // 当前这张比例太极端，窗口高度被夹住了，得裁掉才不出黑边
        private string scaledKey;       // 缓存对应的（文件 + 区域尺寸）

        private ToolStripMenuItem autoItem;
        private ToolStripMenuItem fillItem;
        private ToolStripMenuItem borderItem;
        private readonly List<ToolStripMenuItem> intervals = new List<ToolStripMenuItem>();

        public PhotoForm()
        {
            framePath = Path.Combine(AppPaths.DataDirectory, "frame.json");
            frame = JsonStore.Load<FrameSettings>(framePath);
            if (frame == null) { frame = new FrameSettings(); }
            if (frame.Interval < 2 || frame.Interval > 3600) { frame.Interval = 10; }
            if (string.IsNullOrEmpty(frame.Folder)) { frame.Folder = DefaultFolder(); }

            BuildBaseMenu();
            SetupTray();
            ApplySize();
            RestorePosition();

            ScanFolder();

            rotate.Interval = frame.Interval * 1000;
            rotate.Tick += delegate { Next(); };
            if (AutoPlay && files.Count > 1) { rotate.Start(); }
        }

        // ------------------------------------------------------------ 基类契约

        protected override bool UseLayeredSurface
        {
            get { return true; }
        }

        protected override string DisplayName
        {
            get { return "桌面相框挂件"; }
        }

        protected override string RegistryName
        {
            get { return "DesktopPhoto"; }
        }

        public override int DefaultFontSize
        {
            get { return 14; }
        }

        public override int MinFontSize
        {
            get { return 12; }
        }

        public override int MaxFontSize
        {
            get { return 26; }
        }

        public override int[] FontSizes()
        {
            return new[] { 12, 14, 16, 18, 22, 26 };
        }

        public override Rectangle DefaultBounds
        {
            get
            {
                Rectangle area = PlacementArea;
                var size = PreferredSize();
                return new Rectangle(area.Left + 200, area.Top + 80, size.Width, size.Height);
            }
        }

        // ------------------------------------------------------------ 设置

        private bool AutoPlay
        {
            get { return frame.AutoPlay ?? true; }
        }

        private bool Fill
        {
            get { return frame.Fill ?? false; }
        }

        private bool ShowFrame
        {
            get { return frame.ShowFrame ?? true; }
        }

        private static string DefaultFolder()
        {
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(pictures)) { pictures = AppPaths.DataDirectory; }
            return Path.Combine(pictures, "桌面相框");
        }

        private void Save()
        {
            JsonStore.Save(framePath, frame);
        }

        // ------------------------------------------------------------ 图库

        /// <summary>重新扫一遍文件夹。文件夹不存在就直接当成空，不去创建它 ——
        /// 用户选错路径时不该在磁盘上凭空多出一个目录。</summary>
        private void ScanFolder()
        {
            files.Clear();
            try
            {
                if (Directory.Exists(frame.Folder))
                {
                    var found = new List<string>();
                    foreach (string path in Directory.GetFiles(frame.Folder))
                    {
                        string ext = Path.GetExtension(path).ToLowerInvariant();
                        if (Array.IndexOf(Extensions, ext) >= 0) { found.Add(path); }
                    }

                    found.Sort(StringComparer.CurrentCultureIgnoreCase);
                    files.AddRange(found);
                }
            }
            catch
            {
            }

            if (files.Count == 0)
            {
                ReleasePicture();
                return;
            }

            int index = string.IsNullOrEmpty(frame.Current) ? 0 : files.IndexOf(frame.Current);
            if (index < 0) { index = 0; }
            Load(index);
        }

        private int CurrentIndex
        {
            get
            {
                if (files.Count == 0 || string.IsNullOrEmpty(picturePath)) { return -1; }
                return files.IndexOf(picturePath);
            }
        }

        private void Step(int delta)
        {
            if (files.Count == 0) { return; }
            int index = CurrentIndex;
            if (index < 0) { index = 0; }
            else { index = (index + delta + files.Count) % files.Count; }
            Load(index);
        }

        private void Next()
        {
            Step(1);
        }

        /// <summary>
        /// 载入第 index 张。必须从 FileStream 读进内存再解码 —— 直接
        /// Image.FromFile 会把文件句柄一直占着，用户在资源管理器里就删不掉、改不了名。
        /// </summary>
        private void Load(int index)
        {
            if (index < 0 || index >= files.Count) { return; }
            string path = files[index];
            ReleasePicture();
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var temp = Image.FromStream(stream))
                {
                    picture = new Bitmap(temp);
                }
            }
            catch
            {
                picture = null;
            }

            picturePath = path;
            frame.Current = path;
            Save();
            // 相框跟着这张图的比例变形：16:9 的图就是矮胖相框，不再上下留黑边
            ApplySize();
            InvalidateSurface();
        }

        private void ReleasePicture()
        {
            if (picture != null) { picture.Dispose(); }
            picture = null;
            if (scaled != null) { scaled.Dispose(); }
            scaled = null;
            scaledKey = null;
        }

        private void ChooseFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择放照片的文件夹";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(frame.Folder)) { dialog.SelectedPath = frame.Folder; }
                if (dialog.ShowDialog() != DialogResult.OK) { return; }
                frame.Folder = dialog.SelectedPath;
                frame.Current = null;
                Save();
                ScanFolder();
                RestartTimer();
                InvalidateSurface();
            }
        }

        private void OpenFolder()
        {
            try
            {
                if (Directory.Exists(frame.Folder))
                {
                    Process.Start("explorer.exe", "\"" + frame.Folder + "\"");
                }
            }
            catch
            {
            }
        }

        private void RestartTimer()
        {
            rotate.Stop();
            rotate.Interval = Math.Max(2, frame.Interval) * 1000;
            if (AutoPlay && files.Count > 1) { rotate.Start(); }
        }

        // ------------------------------------------------------------ 菜单

        protected override void BuildMenu()
        {
            var choose = new ToolStripMenuItem("选择照片文件夹…");
            choose.Click += delegate { ChooseFolder(); };
            Menu.Items.Add(choose);

            var open = new ToolStripMenuItem("打开这个文件夹");
            open.Click += delegate { OpenFolder(); };
            Menu.Items.Add(open);

            var rescan = new ToolStripMenuItem("重新扫描（加了新照片点这里）");
            rescan.Click += delegate
            {
                ScanFolder();
                RestartTimer();
                InvalidateSurface();
            };
            Menu.Items.Add(rescan);

            Menu.Items.Add(new ToolStripSeparator());

            var prev = new ToolStripMenuItem("上一张");
            prev.Click += delegate { Step(-1); };
            Menu.Items.Add(prev);

            var next = new ToolStripMenuItem("下一张");
            next.Click += delegate { Next(); };
            Menu.Items.Add(next);

            autoItem = new ToolStripMenuItem("自动轮播");
            autoItem.Click += delegate
            {
                frame.AutoPlay = !AutoPlay;
                Save();
                RestartTimer();
            };
            Menu.Items.Add(autoItem);

            var intervalMenu = new ToolStripMenuItem("切换间隔");
            foreach (int seconds in new[] { 5, 10, 20, 30, 60, 300 })
            {
                int captured = seconds;
                var item = new ToolStripMenuItem(seconds >= 60 ? (seconds / 60) + " 分钟" : seconds + " 秒");
                item.Tag = "interval:" + seconds;
                item.Click += delegate
                {
                    frame.Interval = captured;
                    Save();
                    RestartTimer();
                };
                intervals.Add(item);
                intervalMenu.DropDownItems.Add(item);
            }
            Menu.Items.Add(intervalMenu);

            Menu.Items.Add(new ToolStripSeparator());

            // 相框默认就跟着图片的比例走（不出黑边）；这一项只在图片比例太极端、
            // 窗口高度被夹住时才有区别：勾上=裁掉多余部分，不勾=整张都看得见（留边）。
            fillItem = new ToolStripMenuItem("极端比例时铺满（裁掉多余部分）");
            fillItem.Click += delegate
            {
                frame.Fill = !Fill;
                Save();
                ApplySize();
                InvalidateSurface();
            };
            Menu.Items.Add(fillItem);

            borderItem = new ToolStripMenuItem("显示相框边框");
            borderItem.Click += delegate
            {
                frame.ShowFrame = !ShowFrame;
                Save();
                InvalidateSurface();
            };
            Menu.Items.Add(borderItem);
        }

        protected override void OnMenuOpening()
        {
            base.OnMenuOpening();
            if (autoItem != null) { autoItem.Checked = AutoPlay; }
            if (fillItem != null) { fillItem.Checked = Fill; }
            if (borderItem != null) { borderItem.Checked = ShowFrame; }
            foreach (ToolStripMenuItem item in intervals)
            {
                string tag = Convert.ToString(item.Tag, CultureInfo.InvariantCulture);
                item.Checked = tag == "interval:" + frame.Interval;
            }
        }

        protected override void OnMenuClick(string tag)
        {
            if (tag.StartsWith("font:", StringComparison.Ordinal))
            {
                ApplyFontSize(int.Parse(tag.Substring(5), CultureInfo.InvariantCulture));
                ApplySize();
                return;
            }

            if (tag.StartsWith("opacity:", StringComparison.Ordinal))
            {
                ApplyOpacity(int.Parse(tag.Substring(8), CultureInfo.InvariantCulture));
            }
        }

        // ------------------------------------------------------------ 尺寸

        private float Scale
        {
            get
            {
                try
                {
                    using (Graphics graphics = Graphics.FromHwnd(Handle))
                    {
                        float value = graphics.DpiX / 96f;
                        return value < 1f ? 1f : value;
                    }
                }
                catch
                {
                    return 1f;
                }
            }
        }

        private float S(float logical)
        {
            return logical * Scale;
        }

        /// <summary>
        /// 当前图片的宽高比；没有图返回 0。
        /// </summary>
        private float PictureAspect()
        {
            if (picture == null || picture.Width <= 0 || picture.Height <= 0) { return 0f; }
            return (float)picture.Width / picture.Height;
        }

        /// <summary>
        /// 相框的边框跟着图片走：宽度照旧（跟着字号设置），高度按图片的长宽比算。
        /// 这样 16:9 的照片就是一个 16:9 的相框，图片铺满、上下不再出现两道大黑边；
        /// 竖图和全景图则把高度夹在合理范围内，夹不住时按"铺满"裁掉一点，同样不出黑边。
        /// </summary>
        private Size PreferredSize()
        {
            float scale = Scale;
            int fontDelta = Store.Settings.FontSize - DefaultFontSize;
            int width = (int)Math.Round((BaseWidth + fontDelta * 6f) * scale);
            width = Math.Max(220, width);

            float aspect = PictureAspect();
            if (aspect <= 0.01f)
            {
                // 没有图（空状态）：用设计时的方块尺寸
                int baseHeight = (int)Math.Round((BaseHeight + fontDelta * 5f) * scale);
                cropThis = false;
                return new Size(width, Math.Max(200, baseHeight));
            }

            // 图片区宽度 = 窗口宽 - 左右内边距；高度由比例倒推
            float inner = width - 2 * S(Pad);
            float photo = inner / aspect;

            // 夹住：太矮压成了横幅、太高变成一根柱子，都按裁切处理
            float minPhoto = Math.Max(90f, S(130f));
            float maxPhoto = Math.Max(minPhoto + 1f, width * 1.7f);
            bool fits = photo >= minPhoto && photo <= maxPhoto;
            cropThis = !fits && Fill;
            photo = Math.Min(Math.Max(photo, minPhoto), maxPhoto);

            int height = (int)Math.Round(photo + S(CaptionHeight) + 2 * S(Pad));
            return new Size(width, Math.Max(180, height));
        }

        private void ApplySize()
        {
            Size = PreferredSize();
            scaled = null;
            scaledKey = null;
            InvalidateSurface();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplySize();
            RestorePosition();
        }

        // ------------------------------------------------------------ 绘制

        protected override void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = SurfaceTextHint;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);

            var bounds = new Rectangle(0, 0, Width, Height);
            using (GraphicsPath path = UiTools.RoundedRect(bounds, (int)S(14)))
            {
                using (var fill = new LinearGradientBrush(bounds,
                           SurfaceColor(CardAlpha, 30, 34, 40),
                           SurfaceColor(CardAlpha, 18, 20, 24), LinearGradientMode.Vertical))
                {
                    graphics.FillPath(fill, path);
                }

                if (CardAlpha > 0)
                {
                    using (var border = new Pen(Color.FromArgb(Math.Min(255, CardAlpha + 20), Theme.Line)))
                    {
                        graphics.DrawPath(border, path);
                    }
                }
            }

            float pad = S(Pad);
            var area = new RectangleF(pad, pad,
                Math.Max(20f, Width - pad * 2f),
                Math.Max(20f, Height - pad * 2f - S(CaptionHeight)));

            if (picture == null)
            {
                DrawEmpty(graphics, area);
            }
            else
            {
                DrawPicture(graphics, area);
            }

            DrawCaption(graphics, new RectangleF(pad, Height - pad - S(CaptionHeight) + S(2), 
                Math.Max(20f, Width - pad * 2f), S(CaptionHeight)));
        }

        /// <summary>
        /// 空状态：没选文件夹 / 文件夹里没照片。
        ///
        /// 早先的写法是三行定死字号往上堆，路径一长、DPI 一高（150% 下 11pt 的字
        /// 一个汉字约 22 像素），"右键 →「选择照片文件夹…」"这一行就冲出窗口右边缘，
        /// 截图里直接少半个字。现在每一行都按可用宽度缩字号，路径还会按 "\" 折行，
        /// 怎么都不会出界。
        /// </summary>
        private void DrawEmpty(Graphics graphics, RectangleF area)
        {
            float pad = S(18f);
            float maxW = Math.Max(80f, area.Width - pad * 2f);
            float baseSize = Math.Max(12f, Store.Settings.FontSize - 3f);

            string head = Directory.Exists(frame.Folder) ? "这个文件夹里还没有照片" : "还没选照片文件夹";
            string tail = "右键 →「选择照片文件夹…」";
            string folder = (frame.Folder ?? string.Empty).Replace('/', '\\');

            // 路径按 "\" 折行，避免一行拉太宽
            var lines = new List<string>();
            lines.Add(head);
            if (folder.Length > 0)
            {
                using (var probe = Fonts.Widget(baseSize))
                {
                    string current = string.Empty;
                    foreach (string part in folder.Split('\\'))
                    {
                        string next = current.Length == 0 ? part : current + "\\" + part;
                        if (current.Length == 0 || graphics.MeasureString(next, probe).Width <= maxW)
                        {
                            current = next;
                            continue;
                        }

                        lines.Add(current);
                        current = part;
                    }

                    if (current.Length > 0) { lines.Add(current); }
                }
            }

            lines.Add(tail);

            // 统一缩字号：先保证每行不超宽，再保证总高不超出绘画区
            float size = baseSize;
            while (size > 8f)
            {
                bool fits = true;
                float total = 0f;
                using (var probe = Fonts.Widget(size))
                {
                    foreach (string text in lines)
                    {
                        if (graphics.MeasureString(text, probe).Width > maxW) { fits = false; break; }

                        total += probe.Height;
                    }

                    total += S(6f) * (lines.Count - 1);
                    if (total > area.Height) { fits = false; }
                }

                if (fits) { break; }

                size -= 0.5f;
            }

            using (var font = Fonts.Widget(size))
            {
                float total = 0f;
                foreach (string text in lines) { total += font.Height; }

                total += S(6f) * (lines.Count - 1);
                float y = area.Y + Math.Max(0f, (area.Height - total) / 2f);

                for (int i = 0; i < lines.Count; i++)
                {
                    Color color = i == 0 ? Theme.TextDim
                               : i == lines.Count - 1 ? Theme.SkyBlue
                               : Theme.TextFaint;
                    SizeF measure = graphics.MeasureString(lines[i], font);
                    Center(graphics, lines[i], font, area, ref y, measure, color);
                    if (i < lines.Count - 1) { y += S(6f); }
                }
            }
        }

        private static void Center(Graphics graphics, string text, Font font, RectangleF area,
            ref float y, SizeF size, Color color)
        {
            if (text.Length > 0)
            {
                using (var brush = new SolidBrush(color))
                using (var format = new StringFormat(StringFormat.GenericTypographic))
                {
                    format.Alignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    graphics.DrawString(text, font, brush, new RectangleF(area.X, y, area.Width, size.Height), format);
                }
            }

            y += size.Height;
        }

        private void DrawPicture(Graphics graphics, RectangleF area)
        {
            var box = new Rectangle((int)Math.Round(area.X), (int)Math.Round(area.Y),
                (int)Math.Round(area.Width), (int)Math.Round(area.Height));
            if (box.Width < 8 || box.Height < 8) { return; }

            Bitmap art = ScaledFor(picture, picturePath, box.Size);
            if (art == null) { return; }

            int radius = (int)S(8);
            using (GraphicsPath path = UiTools.RoundedRect(box, radius))
            {
                Region old = null;
                try
                {
                    old = graphics.Clip;
                    graphics.SetClip(path, CombineMode.Intersect);
                    graphics.DrawImageUnscaled(art, box.X, box.Y);
                }
                catch
                {
                }
                finally
                {
                    if (old != null)
                    {
                        graphics.Clip = old;
                        old.Dispose();
                    }
                }

                if (ShowFrame)
                {
                    using (var pen = new Pen(Color.FromArgb(Math.Min(255, Math.Max(60, CardAlpha)), 236, 241, 247),
                               Math.Max(1f, S(2))))
                    {
                        graphics.DrawPath(pen, path);
                    }
                }
            }
        }

        /// <summary>
        /// 按显示区域把当前这张缩好并缓存。
        /// 每帧现缩一张 4000px 的原图很贵（分层窗口每次重绘都要走一遍），
        /// 而显示区域在一轮里是不变的，所以按（文件 + 尺寸）缓存一份就够了。
        /// </summary>
        private Bitmap ScaledFor(Image source, string path, Size box)
        {
            string key = path + "@" + box.Width + "x" + box.Height + (Fill ? "#fill" : "#fit");
            if (scaled != null && scaledKey == key && scaled.Width == box.Width && scaled.Height == box.Height)
            {
                return scaled;
            }

            if (scaled != null) { scaled.Dispose(); }
            scaled = null;

            try
            {
                var canvas = new Bitmap(box.Width, box.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(canvas))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;

                    float sx = (float)box.Width / source.Width;
                    float sy = (float)box.Height / source.Height;
                    // 窗口高度已经按这张图的比例算过了，fit 和 fill 在这里等价；
                    // 只有比例太极端被夹住（cropThis）或用户选了铺满时才真的裁。
                    float scale = (Fill || cropThis) ? Math.Max(sx, sy) : Math.Min(sx, sy);
                    int w = Math.Max(1, (int)Math.Round(source.Width * scale));
                    int h = Math.Max(1, (int)Math.Round(source.Height * scale));

                    // 用 DrawImage 的目标矩形做缩放，而不是先缩再贴：
                    // 一次高质量重采样比拼两次清楚。
                    graphics.DrawImage(source,
                        new Rectangle((box.Width - w) / 2, (box.Height - h) / 2, w, h));
                }

                scaled = canvas;
                scaledKey = key;
                return scaled;
            }
            catch
            {
                return null;
            }
        }

        private void DrawCaption(Graphics graphics, RectangleF row)
        {
            if (row.Height < 8f) { return; }
            float fontSize = Math.Max(11f, Store.Settings.FontSize - 4f);

            using (var font = Fonts.Widget(fontSize))
            {
                string left;
                if (files.Count == 0)
                {
                    left = "空";
                }
                else
                {
                    int index = Math.Max(0, CurrentIndex);
                    left = (index + 1) + " / " + files.Count + "  ·  " +
                           Path.GetFileNameWithoutExtension(picturePath ?? string.Empty);
                }

                using (var brush = new SolidBrush(Theme.TextFaint))
                using (var format = new StringFormat(StringFormat.GenericTypographic))
                {
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    format.LineAlignment = StringAlignment.Center;
                    graphics.DrawString(left, font, brush, row, format);
                }

                if (AutoPlay && files.Count > 1)
                {
                    using (var brush = new SolidBrush(Theme.TextFaint))
                    {
                        const string right = "轮播";
                        SizeF size = graphics.MeasureString(right, font);
                        graphics.DrawString(right, font, brush,
                            row.Right - size.Width, row.Y + (row.Height - size.Height) / 2f);
                    }
                }
            }
        }

        // ------------------------------------------------------------ 交互

        protected override void OnSurfaceMouseWheel(MouseEventArgs e)
        {
            base.OnSurfaceMouseWheel(e);
            Step(e.Delta > 0 ? -1 : 1);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            Next();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) { Step(-1); e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { Next(); e.Handled = true; }
            else if (e.KeyCode == Keys.Space)
            {
                frame.AutoPlay = !AutoPlay;
                Save();
                RestartTimer();
                InvalidateSurface();
                e.Handled = true;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            rotate.Stop();
            ReleasePicture();
            base.OnFormClosing(e);
        }
    }
}
