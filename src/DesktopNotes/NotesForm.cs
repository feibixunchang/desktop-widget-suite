using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Windows.Forms;
using Shared;

namespace DesktopNotes
{
    /// <summary>一条便签。Id 用时间戳生成，够唯一，也不用额外维护计数器。</summary>
    [DataContract]
    internal sealed class NoteItem
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Text { get; set; }
        [DataMember] public string Updated { get; set; }
    }

    [DataContract]
    internal sealed class NoteBook
    {
        [DataMember] public List<NoteItem> Items { get; set; }
        [DataMember] public string Selected { get; set; }

        public NoteBook()
        {
            Items = new List<NoteItem>();
        }
    }

    /// <summary>
    /// 桌面记事挂件：一张卡片显示一条便签，左右翻页，点「编辑」弹面板改字。
    ///
    /// 为什么编辑要弹面板而不是直接在卡片上打字：卡片是分层窗口
    /// （UpdateLayeredWindow），整块画面由我们自己合成到一张 32 位 ARGB 位图上，
    /// 这种窗口挂不了子控件 —— 一旦加 TextBox，那个框不会出现在合成结果里。
    /// 所以卡片只负责显示，输入交给 PopupPanel，那里是普通窗口，真控件。
    /// </summary>
    internal sealed class NotesForm : WidgetForm
    {
        private const float BaseWidth = 320f;
        private const float BaseHeight = 286f;
        private const float HeaderHeight = 34f;
        private const float FooterHeight = 34f;
        private const float PadX = 16f;

        private static readonly string[] PlaceholderNote =
        {
            "双击这里开始写。",
            "",
            "右键菜单：新建 / 编辑 / 删除，",
            "也可以用卡片底部的按钮翻页。",
            "",
            "内容存在 %AppData%\\DesktopNotes\\notes.json，",
            "换电脑时把它拷过去就行。"
        };

        private readonly NoteBook book;
        private readonly string bookPath;
        private NoteEditPanel panel;

        private RectangleF prevHit = RectangleF.Empty;
        private RectangleF editHit = RectangleF.Empty;
        private RectangleF nextHit = RectangleF.Empty;
        private RectangleF newHit = RectangleF.Empty;
        private Point hoverPoint = Point.Empty;

        public NotesForm()
        {
            bookPath = Path.Combine(AppPaths.DataDirectory, "notes.json");
            book = JsonStore.Load<NoteBook>(bookPath);
            if (book == null) { book = new NoteBook(); }
            if (book.Items == null) { book.Items = new List<NoteItem>(); }
            if (book.Items.Count == 0)
            {
                book.Items.Add(new NoteItem
                {
                    Id = NewId(),
                    Text = string.Join("\r\n", PlaceholderNote),
                    Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                });
                book.Selected = book.Items[0].Id;
                Save();
            }

            if (IndexOf(book.Selected) < 0) { book.Selected = book.Items[0].Id; }

            BuildBaseMenu();
            SetupTray();
            ApplySize();
            RestorePosition();
        }

        // ------------------------------------------------------------ 基类契约

        protected override bool UseLayeredSurface
        {
            get { return true; }
        }

        protected override string DisplayName
        {
            get { return "桌面记事挂件"; }
        }

        protected override string RegistryName
        {
            get { return "DesktopNotes"; }
        }

        public override int DefaultFontSize
        {
            get { return 17; }
        }

        public override int MinFontSize
        {
            get { return 12; }
        }

        public override int MaxFontSize
        {
            get { return 34; }
        }

        public override int[] FontSizes()
        {
            return new[] { 12, 14, 17, 20, 24, 28, 34 };
        }

        public override Rectangle DefaultBounds
        {
            get
            {
                Rectangle area = PlacementArea;
                var size = PreferredSize();
                return new Rectangle(area.Left + 40, area.Top + 120, size.Width, size.Height);
            }
        }

        // ------------------------------------------------------------ 数据

        private NoteItem Current
        {
            get
            {
                int index = IndexOf(book.Selected);
                return index < 0 ? null : book.Items[index];
            }
        }

        private int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id) || book == null) { return -1; }
            for (int i = 0; i < book.Items.Count; i++)
            {
                if (book.Items[i] != null && book.Items[i].Id == id) { return i; }
            }

            return -1;
        }

        private static string NewId()
        {
            return DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        }

        private void Save()
        {
            JsonStore.Save(bookPath, book);
        }

        private void Step(int delta)
        {
            if (book.Items.Count < 2) { return; }
            int index = Math.Max(0, IndexOf(book.Selected));
            index = (index + delta + book.Items.Count) % book.Items.Count;
            book.Selected = book.Items[index].Id;
            Save();
            InvalidateSurface();
        }

        private void AddNote()
        {
            var item = new NoteItem
            {
                Id = NewId(),
                Text = string.Empty,
                Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            };
            book.Items.Add(item);
            book.Selected = item.Id;
            Save();
            InvalidateSurface();
            OpenEditor(true);
        }

        private void RemoveNote()
        {
            if (book.Items.Count <= 1)
            {
                // 最后一条不删，只清空：删光了卡片上什么都没有，看起来像挂了
                NoteItem only = Current;
                if (only == null) { return; }
                only.Text = string.Empty;
                Touch(only);
                Save();
                InvalidateSurface();
                return;
            }

            int index = Math.Max(0, IndexOf(book.Selected));
            book.Items.RemoveAt(index);
            if (index >= book.Items.Count) { index = book.Items.Count - 1; }
            book.Selected = book.Items[index].Id;
            Save();
            InvalidateSurface();
        }

        private static void Touch(NoteItem item)
        {
            if (item == null) { return; }
            item.Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        private void OpenEditor(bool isNew)
        {
            NoteItem item = Current;
            if (item == null) { return; }
            if (panel != null && !panel.IsDisposed) { panel.ClosePopup(); }

            panel = new NoteEditPanel(item.Text, isNew);
            panel.Committed += delegate(object sender, NoteEditEventArgs e)
            {
                item.Text = e.Text ?? string.Empty;
                Touch(item);
                Save();
                InvalidateSurface();
            };
            panel.Closed += delegate
            {
                panel = null;
                InvalidateSurface();
            };

            Size size = PreferredSize();
            panel.ShowAboveOrBelow(RectangleToScreen(new Rectangle(0, 0, size.Width, size.Height)),
                Math.Max(size.Width, (int)S(360)), (int)S(300));
            panel.TopMost = TopMost;
            panel.FocusText();
        }

        // ------------------------------------------------------------ 菜单

        protected override void BuildMenu()
        {
            var add = new ToolStripMenuItem("新建一条");
            add.Click += delegate { AddNote(); };
            Menu.Items.Add(add);

            var edit = new ToolStripMenuItem("编辑当前…");
            edit.Click += delegate { OpenEditor(false); };
            Menu.Items.Add(edit);

            var remove = new ToolStripMenuItem("删除当前");
            remove.Click += delegate { RemoveNote(); };
            Menu.Items.Add(remove);

            Menu.Items.Add(new ToolStripSeparator());

            var prev = new ToolStripMenuItem("上一条");
            prev.Click += delegate { Step(-1); };
            Menu.Items.Add(prev);

            var next = new ToolStripMenuItem("下一条");
            next.Click += delegate { Step(1); };
            Menu.Items.Add(next);
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

        /// <summary>当前屏幕的缩放比（96 DPI 为 1）。尺寸、圆角、内边距都乘这个值。</summary>
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

        private Size PreferredSize()
        {
            float scale = Scale;
            int fontDelta = Store.Settings.FontSize - DefaultFontSize;
            int width = (int)Math.Round((BaseWidth + fontDelta * 7f) * scale);
            int height = (int)Math.Round((BaseHeight + fontDelta * 11f) * scale);
            return new Size(Math.Max(240, width), Math.Max(180, height));
        }

        private void ApplySize()
        {
            Size = PreferredSize();
            InvalidateSurface();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 句柄建好之后才拿得到真实 DPI，这时按屏幕缩放重算一次尺寸
            ApplySize();
            RestorePosition();
        }

        // ------------------------------------------------------------ 绘制

        protected override void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = SurfaceTextHint;
            graphics.Clear(Color.Transparent);

            var bounds = new Rectangle(0, 0, Width, Height);
            using (GraphicsPath path = UiTools.RoundedRect(bounds, (int)S(14)))
            {
                using (var fill = new LinearGradientBrush(bounds,
                           SurfaceColor(CardAlpha, 44, 40, 28),
                           SurfaceColor(CardAlpha, 24, 22, 16), LinearGradientMode.Vertical))
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

            int fontSize = Store.Settings.FontSize;
            float left = S(PadX);
            float width = Width - left * 2f;

            DrawHeader(graphics, new RectangleF(left, S(9), width, S(HeaderHeight - 9)), fontSize);
            DrawBody(graphics, new RectangleF(left, S(HeaderHeight) + S(4), width,
                Height - S(HeaderHeight) - S(FooterHeight) - S(10)), fontSize);
            DrawFooter(graphics, new RectangleF(left, Height - S(FooterHeight) + S(2), width, S(26)), fontSize);
        }

        private void DrawHeader(Graphics graphics, RectangleF row, int fontSize)
        {
            using (var font = Fonts.WidgetBold(Math.Max(12f, fontSize - 4f)))
            using (var brush = new SolidBrush(Theme.SkyBlue))
            {
                UiTools.DrawLeftCentered(graphics, "桌面记事", font, row, Theme.SkyBlue);
            }

            int index = Math.Max(0, IndexOf(book.Selected));
            string counter = (index + 1) + " / " + book.Items.Count;
            NoteItem item = Current;
            string stamp = item != null && !string.IsNullOrEmpty(item.Updated) ? item.Updated : string.Empty;

            using (var font = Fonts.Widget(Math.Max(12f, fontSize - 6f)))
            {
                float right = row.Right;
                using (var brush = new SolidBrush(Theme.TextFaint))
                {
                    SizeF size = graphics.MeasureString(counter, font);
                    graphics.DrawString(counter, font, brush, right - size.Width, row.Y + (row.Height - size.Height) / 2f);
                    right -= size.Width + S(10);
                }

                if (stamp.Length > 0)
                {
                    using (var brush = new SolidBrush(Theme.TextFaint))
                    {
                        SizeF size = graphics.MeasureString(stamp, font);
                        graphics.DrawString(stamp, font, brush, right - size.Width, row.Y + (row.Height - size.Height) / 2f);
                    }
                }
            }

            // 标题和正文之间一根细线，和万年历卡片的分隔一个样子
            using (var pen = new Pen(Color.FromArgb(Math.Min(255, Math.Max(24, CardAlpha)), Theme.Line)))
            {
                float y = row.Bottom + S(3);
                graphics.DrawLine(pen, S(PadX), y, Width - S(PadX), y);
            }
        }

        private void DrawBody(Graphics graphics, RectangleF body, int fontSize)
        {
            NoteItem item = Current;
            string text = item == null ? string.Empty : (item.Text ?? string.Empty);
            if (text.Trim().Length == 0)
            {
                text = "（空便签）双击这里或点「编辑」开始写";
                using (var font = Fonts.Widget(Math.Max(12f, fontSize - 2f)))
                using (var brush = new SolidBrush(Theme.TextFaint))
                using (var format = WordWrap())
                {
                    graphics.DrawString(text, font, brush, body, format);
                }

                return;
            }

            using (var font = Fonts.Widget(fontSize))
            using (var brush = new SolidBrush(Theme.Text))
            using (StringFormat format = WordWrap())
            {
                // LineLimit + EllipsisWord：放得下几行画几行，最后一行以"…"收尾。
                // 分层窗口上不能用 TextRenderer（GDI 只写 RGB 不写 alpha），
                // 所以必须走 GDI+ DrawString。
                format.FormatFlags |= StringFormatFlags.LineLimit;
                format.Trimming = StringTrimming.EllipsisWord;
                graphics.DrawString(text, font, brush, body, format);
            }
        }

        private static StringFormat WordWrap()
        {
            var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near
            };
            return format;
        }

        private void DrawFooter(Graphics graphics, RectangleF row, int fontSize)
        {
            float gap = S(6);
            float newWidth = S(34);
            float sideWidth = S(38);
            float editWidth = Math.Max(S(70), row.Width - newWidth - sideWidth * 2 - gap * 3);

            var newRect = new RectangleF(row.Right - newWidth, row.Y, newWidth, row.Height);
            var nextRect = new RectangleF(newRect.Left - gap - sideWidth, row.Y, sideWidth, row.Height);
            var editRect = new RectangleF(nextRect.Left - gap - editWidth, row.Y, editWidth, row.Height);
            var prevRect = new RectangleF(editRect.Left - gap - sideWidth, row.Y, sideWidth, row.Height);

            prevHit = prevRect;
            editHit = editRect;
            nextHit = nextRect;
            newHit = newRect;

            bool many = book.Items.Count > 1;
            Chip(graphics, prevRect, "‹", fontSize, many);
            Chip(graphics, editRect, "编辑这条", fontSize, true);
            Chip(graphics, nextRect, "›", fontSize, many);
            Chip(graphics, newRect, "＋", fontSize, true);
        }

        /// <summary>底部小按钮：底色和描边跟着卡片透明度走，0% 时只剩文字。</summary>
        private void Chip(Graphics graphics, RectangleF bounds, string text, int fontSize, bool enabled)
        {
            if (bounds.Width <= 2 || bounds.Height <= 2) { return; }
            bool hot = enabled && bounds.Contains(hoverPoint);

            using (GraphicsPath path = UiTools.RoundedRect(bounds, (int)S(7)))
            {
                if (CardAlpha > 0)
                {
                    int alpha = hot ? Math.Min(255, CardAlpha + 26) : Math.Max(18, CardAlpha / 2);
                    using (var fill = new SolidBrush(Color.FromArgb(alpha, 62, 70, 84)))
                    {
                        graphics.FillPath(fill, path);
                    }

                    using (var border = new Pen(Color.FromArgb(Math.Min(255, CardAlpha + 10),
                               hot ? Theme.SkyBlue : Theme.Line)))
                    {
                        graphics.DrawPath(border, path);
                    }
                }
            }

            Color color = !enabled ? Theme.TextFaint : (hot ? Theme.SkyBlue : Theme.Text);
            using (var font = Fonts.Widget(Math.Max(12f, fontSize - 4f)))
            {
                UiTools.DrawCentered(graphics, text, font, bounds, color);
            }
        }

        // ------------------------------------------------------------ 交互

        protected override void OnCanvasMouseDown(object sender, MouseEventArgs e)
        {
            base.OnCanvasMouseDown(sender, e);
            if (e.Button != MouseButtons.Left) { return; }

            var point = new PointF(e.X, e.Y);
            if (newHit.Contains(point)) { AddNote(); return; }
            if (nextHit.Contains(point)) { Step(1); return; }
            if (prevHit.Contains(point)) { Step(-1); return; }
            if (editHit.Contains(point)) { OpenEditor(false); return; }
        }

        protected override void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            base.OnCanvasMouseMove(sender, e);
            var point = new Point(e.X, e.Y);
            if (point == hoverPoint) { return; }
            hoverPoint = point;
            InvalidateSurface();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!UseLayeredSurface) { return; }
            var point = new Point(e.X, e.Y);
            if (point == hoverPoint) { return; }
            hoverPoint = point;
            InvalidateSurface();
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            OpenEditor(false);
        }

        protected override void OnSurfaceMouseWheel(MouseEventArgs e)
        {
            base.OnSurfaceMouseWheel(e);
            Step(e.Delta > 0 ? -1 : 1);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.N && e.Control) { AddNote(); e.Handled = true; }
            else if (e.KeyCode == Keys.E && e.Control) { OpenEditor(false); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete && e.Control) { RemoveNote(); e.Handled = true; }
            else if (e.KeyCode == Keys.Left) { Step(-1); e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { Step(1); e.Handled = true; }
        }
    }
}
