using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Shared
{
    /// <summary>
    /// 弹出面板：默认在挂件上方弹出，上方不够才往下弹，任何情况下都不超出工作区；
    /// 内容超出时内部滚动；点击面板外或按 Esc 关闭。
    /// </summary>
    internal class PopupPanel : Form
    {
        /// <summary>
        /// 弹出面板统一配色：**整个面板纯黑**（不透明）。
        ///
        /// 之前用的是 Theme.Panel(30,34,40) / Theme.PanelSoft(40,45,52) 那套深灰蓝，
        /// 底色偏灰、层次不干净。现在按用户要求整块纯黑：
        ///   * 背景、标题栏、内容区、按钮、输入框、下拉框、列表 —— 全部同一种黑；
        ///   * 保留一条略浅的分隔线，不然标题栏和内容区糊在一起看不出层次；
        ///   * 文字颜色一律不改（Theme.Text 等本来就接近纯白），所以在纯黑上依旧清晰。
        /// 全部**不透明**，不涉及任何透明度调节。
        /// </summary>
        public static readonly Color PanelBlack = Color.FromArgb(0, 0, 0);

        /// <summary>纯黑面板上的分隔线/描边（略亮一点的黑，用来区分层次）。</summary>
        public static readonly Color PanelBlackLine = Color.FromArgb(42, 42, 42);

        /// <summary>纯黑面板上按钮的悬停底色（深灰，仍是黑基调）。</summary>
        public static readonly Color PanelBlackHover = Color.FromArgb(38, 38, 38);

        /// <summary>
        /// 弹出面板最外圈那根**细蓝边框**（1px）。
        /// 纯黑面板在深色壁纸上很容易"融"进背景，靠这根线把边界划出来。
        /// 颜色和 <c>Theme.Accent</c> 一致（80,170,255），这里写成字面量是为了避免
        /// 静态初始化顺序上的耦合。
        /// </summary>
        public static readonly Color PanelBlueBorder = Color.FromArgb(80, 170, 255);

        /// <summary>
        /// 要不要给弹出面板加那根 1px 蓝边框。
        ///
        /// **默认关**。因为 <see cref="PopupPanel"/> 是共用代码，桌面视频、RSS 也在用，
        /// 不加这个开关的话，改基类会把它们的弹出面板一起染色。
        /// 只有万年历 / 记账 / 倒计时在 <c>Program.Main</c> 里显式打开。
        /// </summary>
        internal static bool UseBlueBorder;

        /// <summary>ApplyCornerRegion 实际用过的圆角半径；Paint 必须复用它，否则弧会被 Region 裁掉。</summary>
        private int cornerRadiusApplied;

        private static readonly List<PopupPanel> Open = new List<PopupPanel>();
        private static readonly System.Windows.Forms.Timer Watcher = new System.Windows.Forms.Timer();
        private static bool watcherRunning;
        private static bool buttonWasDown;

        private readonly Panel body = new Panel();
        private readonly Panel header = new Panel();
        private readonly Label titleLabel = new Label();
        private int preferredWidth = 340;
        private int preferredHeight = 430;
        private string title = string.Empty;

        public event EventHandler Closed;

        public PopupPanel()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = PanelBlack;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            KeyPreview = true;
            MinimumSize = new Size(240, 160);

            header.Dock = DockStyle.Top;
            header.Height = 46;

            // ⚠️ 内缩必须走 **Form 自己的 Padding**，不能靠子控件的 Margin。
            // WinForms 绘制顺序是"父窗体先画、子控件后画"，而 header/body 都是
            // Dock 铺满整个客户区的 ⇒ 父窗体 Paint 里画在最外圈的那根线会被盖死，
            // 这就是原来那圈 PanelBlackLine 描边几乎看不见的原因。
            //
            // 实测：给 header/body 设 Margin=1 **完全不生效**（Bounds 依然 X=0、宽度满格），
            // 试过 PerformLayout/Refresh 也没用。Form.Padding 才真正参与
            // DefaultLayout 的"显示矩形"计算，能真正把子控件推开 1px。
            //
            // ⚠️ 这个值**必须保持很小（2px）**。曾试过 Padding = radius + 2，
            // 思路是"把整块内容往里推，让所有子控件都碰不到边框"——
            // 但内容区会从 356x370 被压到 314x328（窄 42px），按钮重排、
            // 文字被截断，倒计时的面板甚至直接出不来。
            // 教训：为了让边框好看而牺牲内容布局是本末倒置。
            Padding = UseBlueBorder ? new Padding(2) : new Padding(0);
            header.BackColor = PanelBlack;   // 整块纯黑
            header.Paint += delegate(object sender, PaintEventArgs e)
            {
                // 分隔线用略浅的黑，纯黑底上才看得出标题栏和内容区的分界
                using (var pen = new Pen(PanelBlackLine))
                {
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
                }
            };

            titleLabel.Dock = DockStyle.Fill;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.ForeColor = Theme.Text;
            titleLabel.Font = Fonts.UiBold(Fonts.PopupTitle);
            titleLabel.Padding = new Padding(12, 0, 0, 0);
            titleLabel.Text = title;
            header.Controls.Add(titleLabel);

            body.Dock = DockStyle.Fill;
            // 内容永远按实际需要撑开弹窗，不靠 AutoScroll 滚动：
            // 否则点输入框时 WinForms 会自动滚动容器，把左侧内容顶出可视区。
            body.AutoScroll = false;

            body.BackColor = PanelBlack;
            body.Padding = new Padding(12, 10, 12, 10);

            Controls.Add(body);
            Controls.Add(header);

            // ⚠️ 边框必须画在**子控件之上**，这是"圆角连不上"的真正原因。
            //
            // 直边在第 0~1 行/列，`Padding=2` 让子控件正好盖不到，所以看得见；
            // 但**圆弧是向内弯的**，它经过的坐标几乎都 > 2，全落在 header/body 的
            // 黑色背景里 ⇒ 被子控件整片覆盖 ⇒ 表现为"四条边有线、四个角全空"。
            // 在父窗体 Paint 里画多少次都没用，WinForms 一定是先画父、后画子。
            //
            // 做法：加一个**最上层**的 overlay，只画那一圈边框、其余像素不碰，
            // 既盖住子控件、又不影响中间内容。最后添加 => z 序最上。

            Paint += delegate(object sender, PaintEventArgs e)
            {
// 边框用**填充圆环**，不用画笔描边。
                //
                // 为什么不用 Pen：圆角是 45° 斜线，任何粗细的画笔都会被抗锯齿摊开
                // ——1f 时斜角颜色落到 (20,42,63)，肉眼几乎看不见（用户报"圆角没有边框"）；
                // 1.6f 能看见但直边又变粗。调画笔宽度是在和 AA 讨价还价。
                //
                // 填充圆环是几何保证：外圈用**和窗口 Region 完全一致**的路径
                // （RoundedRect((0,0,W,H), CornerRadius())），内圈往里缩 1px，
                // FillMode.Alternate 把中间挖空 ⇒ 得到一圈**处处 1px 实心**的环，
                // 直边和圆角粗细完全一致，且不会被 Region 裁掉。
                RectangleF outer = new RectangleF(0f, 0f, Width, Height);
                RectangleF inner = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
                float radius = cornerRadiusApplied > 0 ? cornerRadiusApplied : CornerRadius();
                Color edgeColor = UseBlueBorder ? PanelBlueBorder : PanelBlackLine;

                using (GraphicsPath path = UiTools.RoundedRect(outer, radius))
                using (var fill = new SolidBrush(PanelBlack))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.FillPath(fill, path);
                }

                if (UseBlueBorder)
                {
                    // 只画这一圈 1px 圆环就够了。
                    //
                    // 之前额外叠了一道 2px 的弧，那是在"圆角被 Region / 子控件盖掉、
                    // 只剩 46% 断续像素"的前提下用来补救的。现在 header/body 已经通过
                    // Region 让开了最外圈，圆环在角上能完整连续地画出来 ⇒ 再叠 2px 弧
                    // 只会让角比直边粗一圈（实测角上 3px、直边 1px），不统一。
                    using (GraphicsPath ring = new GraphicsPath())
                    using (var brush = new SolidBrush(PanelBlueBorder))
                    {
                        ring.AddPath(UiTools.RoundedRect(outer, radius), false);
                        ring.AddPath(UiTools.RoundedRect(inner, Math.Max(1f, radius - 1f)), false);
                        ring.FillMode = FillMode.Alternate;   // 内圈变成洞
                        e.Graphics.FillPath(brush, ring);
                    }
                }
                else
                {
                    using (GraphicsPath path = UiTools.RoundedRect(outer, radius))
                    using (var pen = new Pen(PanelBlackLine, 1f))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            };
        }

        /// <summary>
        /// 把 <paramref name="root"/> 及其**所有后代控件**的绘制区域都裁掉边框那一圈。
        ///
        /// 必须递归：父控件的 Region 不会自动裁剪子控件（WinForms 的行为），
        /// 只裁 root 的话，root 里面那些按钮/输入区仍会盖住底部两个圆角。
        /// 给每个后代控件各设一个"避开边框"的 Region 即可，**不改变任何控件的布局和尺寸**。
        /// </summary>
        private void ClipOffBorder(Control root, Region inner)
        {
            List<Control> all = new List<Control>();
            Collect(root, all);
            foreach (Control c in all)
            {
                // 控件自己的 Region 用它自己的坐标系：把 form 坐标换算成"相对 root 链"的偏移
                int offX = 0, offY = 0;
                Control p = c;
                while (p != null && p != this)
                {
                    offX += p.Left;
                    offY += p.Top;
                    p = p.Parent;
                }

                using (GraphicsPath path = UiTools.RoundedRect(
                    new RectangleF(1 - offX, 1 - offY, Width - 2, Height - 2),
                    Math.Max(1, cornerRadiusApplied - 1)))
                {
                    c.Region = new Region(path);
                }
            }
        }

        private static void Collect(Control c, List<Control> acc)
        {
            acc.Add(c);
            foreach (Control k in c.Controls) { Collect(k, acc); }
        }

        /// <summary>把 <paramref name="inner"/> 裁到控件自己的范围内。</summary>
        private static Region ClippedTo(Region inner, Rectangle bounds)
        {
            Region result = inner.Clone() as Region;
            if (result != null)
            {
                result.Intersect(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            }
            return result;
        }

        /// <summary>圆角半径，随 DPI 缩放。</summary>
        private int CornerRadius()
        {
            float dpi = 96f;
            if (IsHandleCreated)
            {
                try
                {
                    using (Graphics graphics = Graphics.FromHwnd(Handle))
                    {
                        dpi = graphics.DpiX;
                    }
                }
                catch
                {
                }
            }

            return (int)Math.Round(14f * dpi / 96f);
        }

        /// <summary>
        /// 用 Region 把四个角真正挖掉，桌面才能从圆角外透出来；
        /// 只在 Paint 里画圆角矩形是不够的，子面板的方形背景会把角盖住。
        /// </summary>
        private void ApplyCornerRegion()
        {
            if (!IsHandleCreated) { return; }
            try
            {
                int radius = CornerRadius();

                // ⚠️ 半径必须**记下来给 Paint 复用**。
                // 之前描边在 Paint 里自己调 CornerRadius()，Region 在这里调 ——
                // 两处各算一次，DPI/句柄状态稍有差异就算出不同的半径。
                // 半径一变小，直线段还在 Region 内（看得见），但圆角弧落到 Region 外
                // ⇒ 被裁掉 ⇒ 表现为"上下左右都有线、唯独四个角是空的、连不上一条线"。
                // 复用同一个值，从构造上杜绝这种不一致。
                cornerRadiusApplied = radius;

                using (GraphicsPath path = UiTools.RoundedRect(new RectangleF(0, 0, Width, Height), radius))
                {
                    Region = new Region(path);
                }

// ⚠️ 边框所在的最外圈必须空出来。header / body 各自让开（内缩 1px、圆角同步减 1）。
                // ⚠️ 但**只裁 header/body 是不够的** —— WinForms 不会用父控件的 Region
                // 去裁剪它的**子控件**，body 里的按钮/输入区照样会画到最外圈，
                // 把底边两个角盖掉（上面两角被 header 挡着、已处理，所以只坏下面两个）。
                if (UseBlueBorder)
                {
                    using (GraphicsPath innerPath = UiTools.RoundedRect(
                        new RectangleF(1f, 1f, Width - 2f, Height - 2f), Math.Max(1, radius - 1)))
                    using (Region inner = new Region(innerPath))
                    {
                        ClipOffBorder(header, inner);
                        ClipOffBorder(body, inner);
                    }
                }
                else
                {
                    header.Region = null;
                    body.Region = null;
                }
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

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x80;
                return parameters;
            }
        }

        public Panel Body
        {
            get { return body; }
        }

        public Panel Header
        {
            get { return header; }
        }

        public string PanelTitle
        {
            get { return title; }
            set
            {
                title = value ?? string.Empty;
                titleLabel.Text = title;
            }
        }

        public int PreferredWidth
        {
            get { return preferredWidth; }
            set { preferredWidth = value; }
        }

        public int PreferredHeight
        {
            get { return preferredHeight; }
            set { preferredHeight = value; }
        }

        public void SetContentSize(int width, int height)
        {
            preferredWidth = width;
            preferredHeight = height;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (msg.Msg == 0x100 && keyData == Keys.Escape)
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
            Rectangle area = Screen.FromRectangle(anchorScreenRect).WorkingArea;
            if (area.Width <= 0 || area.Height <= 0)
            {
                area = Screen.PrimaryScreen.WorkingArea;
            }

            const int margin = 10;
            int panelWidth = Math.Max(240, Math.Min(width, area.Width - margin * 2));
            int spaceAbove = anchorScreenRect.Top - margin - area.Top;
            int spaceBelow = area.Bottom - margin - anchorScreenRect.Bottom;
            int usable = Math.Max(spaceAbove, spaceBelow);
            if (usable < MinimumUsableHeight)
            {
                usable = Math.Max(MinimumUsableHeight, area.Height - margin * 2);
            }

            int panelHeight = Math.Max(140, Math.Min(height, usable));
            int x = anchorScreenRect.Left + (anchorScreenRect.Width - panelWidth) / 2;
            if (x + panelWidth > area.Right - margin) { x = area.Right - margin - panelWidth; }
            if (x < area.Left + margin) { x = area.Left + margin; }

            int y;
            openedAbove = spaceAbove >= MinimumUsableHeight && spaceAbove >= panelHeight;
            if (openedAbove)
            {
                y = anchorScreenRect.Top - panelHeight - margin;   // 优先向上
            }
            else if (spaceBelow >= panelHeight)
            {
                y = anchorScreenRect.Bottom + margin;              // 下方放得下才往下
            }
            else if (spaceAbove >= spaceBelow)
            {
                panelHeight = Math.Max(140, Math.Min(height, spaceAbove));
                y = Math.Max(area.Top + margin, anchorScreenRect.Top - panelHeight - margin);
            }
            else
            {
                panelHeight = Math.Max(140, Math.Min(height, spaceBelow));
                y = anchorScreenRect.Bottom + margin;
            }

            if (y + panelHeight > area.Bottom - margin) { y = area.Bottom - margin - panelHeight; }
            if (y < area.Top + margin) { y = area.Top + margin; }
            if (panelHeight > area.Height - margin * 2) { panelHeight = area.Height - margin * 2; }

            SetBounds(x, y, panelWidth, panelHeight);
            LayoutContent(body.ClientSize.Width);

            // 内容比弹窗高就扩；开在挂件**上方**的时候必须往上扩，
            // 否则弹窗底边会压到挂件自己的标题上（用户报过："自定"弹窗挡住"倒计时"三个字）。
            if (ContentHeight > body.ClientSize.Height)
            {
                int wanted = Math.Min(area.Height - margin * 2, panelHeight + (ContentHeight - body.ClientSize.Height));
                if (wanted > panelHeight)
                {
                    int newY = openedAbove ? y - (wanted - panelHeight) : y;
                    if (newY + wanted > area.Bottom - margin) { newY = area.Bottom - margin - wanted; }
                    if (newY < area.Top + margin) { newY = area.Top + margin; }
                    SetBounds(x, newY, panelWidth, wanted);
                    LayoutContent(body.ClientSize.Width);
                }
            }

            Show();
            ApplyCornerRegion();
            try
            {
                // 让面板拿到键盘焦点，Esc 才能直接关掉
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
                Watcher.Tick += delegate { WatchMouse(); };
                Watcher.Start();
            }

            Open.Add(this);
        }

        private const int MinimumUsableHeight = 260;

        /// <summary>子类排版后写入自己需要的总高度，弹窗会据此自动加高。</summary>
        protected int ContentHeight { get; set; }

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

            bool down = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0;
            if (!down || buttonWasDown)
            {
                buttonWasDown = down;
                return;
            }

            buttonWasDown = true;
            Point cursor = Cursor.Position;
            var copy = new List<PopupPanel>(Open);
            foreach (PopupPanel panel in copy)
            {
                if (panel.IsDisposed) { continue; }
                if (new Rectangle(panel.Left, panel.Top, panel.Width, panel.Height).Contains(cursor)) { continue; }
                if (panel.AnchorBounds.Contains(cursor)) { continue; }
                // 面板弹出的模态对话框（例如"删除选中"的确认框）是独立窗口，落在面板矩形之外。
                // 以前这里会把它当成"点了外面"把面板关掉，对话框的 owner 一没，ShowDialog 直接返回
                // Cancel，删除就永远不执行 —— 表现是"看着删了，其实没删"。
                if (panel.HasOpenChildDialog()) { continue; }
                panel.ClosePopup();
            }
        }

        /// <summary>
        /// 面板自己正在弹一个对话框（"浏览…"文件夹选择器之类）时置成 true。
        ///
        /// 以前只能靠 <see cref="HasOpenChildDialog"/> 里的 OwnedForms 去找，
        /// 但 CommonDialog（FolderBrowserDialog / OpenFileDialog）走的是
        /// Win32 的 owner 关系，WinForms 的 OwnedForms 里不一定有，
        /// 于是"点浏览"的那一下会被当成"点了面板外面"，面板当场消失 ——
        /// 用户的原话就是"一点浏览，菜单就没了"。置了这个标志就不会再被点掉。
        /// </summary>
        public bool ExternalDialogOpen { get; set; }

        /// <summary>面板弹出的模态对话框是否还开着（开着就不能当"点了外面"处理）。</summary>
        private bool HasOpenChildDialog()
        {
            if (ExternalDialogOpen) { return true; }
            try
            {
                foreach (Form owned in OwnedForms)
                {
                    if (owned != null && !owned.IsDisposed && owned.Visible) { return true; }
                }
            }
            catch
            {
            }

            return false;
        }

        private Rectangle AnchorBounds { get; set; }

        /// <summary>标题栏高度。算弹窗总高时要加上它，否则 ShowAboveOrBelow 会发现不够高、事后再扩。</summary>
        protected int HeaderHeight { get { return header.Height; } }

        /// <summary>弹窗开在挂件的上方还是下方。收紧高度时要知道往哪一侧贴。</summary>
        private bool openedAbove;

        /// <summary>
        /// 把弹窗收到内容刚好放下的高度。
        /// ShowAboveOrBelow 用的高度是各面板 EstimateHeight 估出来的，而 ContentScale = 宽度/336
        /// 这个公式算出来的控件高度和 UiTools 实际做出来的不一致（面板越宽差得越多），
        /// 底部就会空一大块（用户反馈过）。这里直接量 Body 里控件的真实底边，不猜。
        /// </summary>
        public void FitToContent()
        {
            try
            {
                if (body.Controls.Count == 0 || !IsHandleCreated) { return; }
                int bottom = 0;
                foreach (Control child in body.Controls)
                {
                    if (!child.Visible) { continue; }
                    if (child.Bottom > bottom) { bottom = child.Bottom; }
                }

                int wanted = header.Height + bottom + 8;
                if (wanted >= Height || wanted < 140) { return; }
                int y = openedAbove ? Bottom - wanted : Top;   // 朝挂件那一侧贴住
                SetBounds(Left, y, Width, wanted);
            }
            catch
            {
            }
        }

        protected virtual void LayoutContent(int contentWidth)
        {
        }

        /// <summary>按面板实际宽度换算的缩放系数（传入的宽度是按 336 逻辑像素设计的）。</summary>
        protected float ContentScale(int contentWidth)
        {
            if (contentWidth <= 0) { return 1f; }
            float value = contentWidth / 336f;
            return value < 0.6f ? 0.6f : (value > 3f ? 3f : value);
        }

        /// <summary>内容区里统一的"关闭"按钮，点它或按 Esc 都能关掉面板。</summary>
        protected Button MakeCloseButton()
        {
            Button button = UiTools.Button("关闭", false);
            button.Click += delegate { ClosePopup(); };
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

            if (!IsDisposed)
            {
                Hide();
                Close();
            }


            if (Closed != null)
            {
                Closed(this, EventArgs.Empty);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Open.Remove(this);
            base.OnFormClosed(e);
        }

        public static void CloseAll()
        {
            var copy = new List<PopupPanel>(Open);
            foreach (PopupPanel panel in copy)
            {
                panel.ClosePopup();
            }
        }
    }

    internal sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
        }
    }

    internal static class UiTools
    {
        /// <summary>
        /// 画弹出面板最外圈那根 **1px 细线边框**（圆环）。
        /// 抽成共用方法，让 <see cref="PopupPanel"/> 和 <see cref="ConfirmDialog"/> 用同一套画法。
        /// </summary>
        /// <remarks>
        /// 用**填充圆环**（outer 减 inner + <c>FillMode.Alternate</c>）而不是画笔描边：
        /// 圆角是 45° 斜线，任何粗细的 Pen 都会被抗锯齿摊开（1px 时斜角落到 (20,42,63)，
        /// 看起来是一串断续的点）。填充是几何保证，处处 1px 实心。
        /// </remarks>
        public static void DrawPanelRing(Graphics g, int width, int height, int radius, Color color)
        {
            if (width <= 2 || height <= 2) { return; }
            RectangleF outer = new RectangleF(0f, 0f, width, height);
            RectangleF inner = new RectangleF(1f, 1f, width - 2f, height - 2f);
            using (GraphicsPath ring = new GraphicsPath())
            using (var brush = new SolidBrush(color))
            {
                ring.AddPath(RoundedRect(outer, radius), false);
                ring.AddPath(RoundedRect(inner, Math.Max(1f, radius - 1f)), false);
                ring.FillMode = FillMode.Alternate;   // 内圈变成洞
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(brush, ring);
            }
        }

        /// <summary>按 DPI 算圆角半径（面板和确认框都要用，保证圆角一致）。</summary>
        public static int CornerRadiusFor(IntPtr handle)
        {
            float dpi = 96f;
            if (handle != IntPtr.Zero)
            {
                try
                {
                    using (Graphics graphics = Graphics.FromHwnd(handle))
                    {
                        dpi = graphics.DpiX;
                    }
                }
                catch
                {
                }
            }
            return (int)Math.Round(14f * dpi / 96f);
        }

        public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            float diameter = radius * 2f;
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            if (diameter > bounds.Width) { diameter = bounds.Width; }
            if (diameter > bounds.Height) { diameter = bounds.Height; }
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            return RoundedRect(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radius);
        }

        /// <summary>
        /// 在一块矩形里把一行字水平垂直都居中。
        /// 分层窗口（真 alpha）上必须用这个，不能用 <c>TextRenderer.DrawText</c>：
        /// 那是 GDI 画的，只写 RGB、不碰 alpha 通道，落在 alpha=0 的像素上字就整个不见了。
        /// </summary>
        public static void DrawCentered(Graphics graphics, string text, Font font, RectangleF bounds, Color color)
        {
            using (var brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.Trimming = StringTrimming.EllipsisCharacter;
                graphics.DrawString(text, font, brush, bounds, format);
            }
        }

        /// <summary>
        /// 挂件底部按钮的统一样式（跟桌面视频控制块一个样子）：
        /// 深色半透明底 + 彩色描边 + 彩色文字。
        /// 关键：底色和描边**跟着卡片透明度走**（<paramref name="cardAlpha"/>）——
        /// 卡片调到 0% 时按钮就该只剩文字。以前万年历/记账是写死
        /// <c>Color.FromArgb(150, PanelSoft)</c>，卡片全透明了按钮还一块深色底
        /// （用户反馈"0% 无背景，但底部三个按钮还有背景"）。
        /// 文字**不受**透明度影响：0% 时也要看得见字。
        /// </summary>
        public static void DrawChipButton(Graphics graphics, RectangleF bounds, string text,
            Font font, Color tint, int cardAlpha, bool hover)
        {
            int alpha = Math.Max(0, Math.Min(255, cardAlpha));
            using (GraphicsPath path = RoundedRect(bounds, Math.Max(3f, bounds.Height * 0.28f)))
            {
                // 常态底色 60/255 ≈ 0.24，跟桌面视频一致
                using (var fill = new SolidBrush(hover
                    ? Color.FromArgb(Math.Min(160, alpha + 40), tint)
                    : Color.FromArgb((int)(alpha * 0.24f), 0, 0, 0)))
                {
                    graphics.FillPath(fill, path);
                }

                using (var border = new Pen(hover ? tint : Color.FromArgb((int)(alpha * 0.6f), tint)))
                {
                    graphics.DrawPath(border, path);
                }
            }

            DrawCentered(graphics, text, font, bounds, hover ? Color.White : tint);
        }

        /// <summary>同 <see cref="DrawCentered"/>，但左对齐（只做垂直居中）。</summary>
        public static void DrawLeftCentered(Graphics graphics, string text, Font font, RectangleF bounds, Color color)
        {
            using (var brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
                graphics.DrawString(text, font, brush, bounds, format);
            }
        }

        /// <summary>量一行字有多宽（像素）。和 <see cref="DrawCentered"/> 用的是同一套度量，量宽画宽才对得上。</summary>
        public static SizeF MeasureLine(Graphics graphics, string text, Font font)
        {
            return graphics.MeasureString(text, font);
        }

        public static Label Label(string text, float size, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = color,
                Font = Fonts.Ui(Math.Max(Fonts.PopupSize, size)),
                BackColor = Color.Transparent
            };
        }

        /// <summary>一行 12pt 文字实际占多高（像素）。</summary>
        public static int LineHeight()
        {
            using (Font font = Fonts.Ui(Fonts.PopupSize))
            {
                return TextRenderer.MeasureText("测Ag中", font).Height;
            }
        }

        /// <summary>按钮/输入框的最小高度：既要放得下 12pt 文字，也要留出内边距。</summary>
        public static int ControlHeight(float scale)
        {
            int padding = (int)Math.Round(10 * scale);
            return Math.Max((int)Math.Round(28 * scale), LineHeight() + padding);
        }

        /// <summary>按文字实际宽度算按钮宽度，避免 12pt 下长分类名被截断。</summary>
        public static int ButtonWidth(Graphics graphics, string text, float scale, float minimum)
        {
            using (Font font = Fonts.Ui(Fonts.PopupSize))
            {
                float textWidth = graphics.MeasureString(text ?? string.Empty, font).Width;
                return Math.Max((int)Math.Round(minimum * scale), (int)Math.Round(textWidth + 16 * scale));
            }
        }

        /// <summary>
        /// 把一行按钮排好：放不下就自动换行，绝不会互相压住。返回这一行实际需要的高度。
        /// </summary>
        public static int FlowRow(FlowLayoutPanel row, int available, int gap, int itemHeight)
        {
            row.FlowDirection = FlowDirection.LeftToRight;
            row.WrapContents = true;
            row.Width = Math.Max(40, available);
            foreach (Control control in row.Controls)
            {
                control.Margin = new Padding(0, 0, gap, gap);
            }

            Size preferred = row.GetPreferredSize(new Size(row.Width, 0));
            row.Height = Math.Max(itemHeight + gap, preferred.Height);
            return row.Height;
        }

        public static Button Button(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = PopupPanel.PanelBlack,   // 按钮底色也是黑的（主按钮保留蓝色强调，普通按钮纯黑）
                ForeColor = Theme.Text,
                Font = Fonts.Ui(Fonts.PopupSize),
                Cursor = Cursors.Hand,
                AutoSize = false,
                Height = ControlHeight(1f),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(52, 104, 150) : PopupPanel.PanelBlackHover;
            return button;
        }

        public static TextBox TextBox(bool multiLine)
        {
            return new TextBox
            {
                BackColor = PopupPanel.PanelBlack,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Fonts.Ui(Fonts.PopupSize),
                Multiline = multiLine
            };
        }

        public static ComboBox ComboBox()
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = PopupPanel.PanelBlack,
                ForeColor = Theme.Text,
                Font = Fonts.Ui(Fonts.PopupSize)
            };
            return combo;
        }

        public static ListBox ListBox()
        {
            return new ListBox
            {
                BackColor = PopupPanel.PanelBlack,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None,
                Font = Fonts.Ui(Fonts.PopupSize),
                IntegralHeight = false
            };
        }

        public static CheckBox Check(string text, bool value)
        {
            return new CheckBox
            {
                Text = text,
                Checked = value,
                ForeColor = Theme.TextDim,
                Font = Fonts.Ui(Fonts.PopupSize),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat
            };
        }

        /// <summary>
        /// 贴着 owner 弹出的确认框。
        /// 不用 MessageBox：它固定出现在屏幕正中，4K 屏上离挂件太远，
        /// 用户根本看不到，会以为"点了没反应"（2026-09-28 用户反馈删除无效就是被这个坑的）。
        /// </summary>
        public static DialogResult Confirm(Form owner, string title, string message)
        {
            using (var dialog = new ConfirmDialog(title, message))
            {
                return dialog.ShowNear(owner);
            }
        }
    }

    /// <summary>
    /// 深色（纯黑底）的 <see cref="DateTimePicker"/>。
    ///
    /// ⚠️ 为什么不能直接设 <c>BackColor</c>：应用开了 <c>Application.EnableVisualStyles()</c>，
    /// **带可视样式的 DateTimePicker 会忽略 BackColor**，实测整个控件仍是白底黑字
    /// （用户报"输入框应该是黑色的"）。必须用 <c>SetWindowTheme(handle, "", "")</c>
    /// 把主题摘掉，BackColor / ForeColor 才会真正生效。
    /// </summary>
    internal sealed class DarkDateTimePicker : DateTimePicker
    {
        [DllImport("uxtheme.dll")]
        private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

        public DarkDateTimePicker()
        {
            ApplyColors();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { SetWindowTheme(Handle, "", ""); } catch { }
            ApplyColors();   // 摘掉主题后必须重设一次才生效
        }

        private void ApplyColors()
        {
            BackColor = PopupPanel.PanelBlack;
            ForeColor = Theme.Text;
        }
    }

    /// <summary>深色小确认框，位置由 <see cref="ShowNear"/> 贴着调用方窗口摆放。</summary>
    internal sealed class ConfirmDialog : Form
    {
        private readonly Label body;
        private readonly Label titleLabel;
        private readonly FlowLayoutPanel buttons;

        /// <summary>实际用过的圆角半径；绘制时必须复用它，否则弧会被 Region 裁掉。</summary>
        private int cornerRadiusApplied;

        public ConfirmDialog(string title, string message)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = PopupPanel.PanelBlack;
            ForeColor = Theme.Text;
            Font = Fonts.Ui(Fonts.PopupSize);
            Padding = new Padding(18, 16, 18, 14);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            int width = 360;
            titleLabel = UiTools.Label(title, Fonts.PopupSize + 2f, Theme.Text);
            titleLabel.Font = Fonts.UiBold(Fonts.PopupSize + 2f);
            body = UiTools.Label(message, Fonts.PopupSize, Theme.TextDim);
            body.AutoSize = false;
            body.AutoEllipsis = false;

            var yes = UiTools.Button("删除", true);
            yes.DialogResult = DialogResult.Yes;
            var no = UiTools.Button("取消", false);
            no.DialogResult = DialogResult.No;

            buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                Dock = DockStyle.Bottom,
                Height = UiTools.ControlHeight(1f)
            };
            buttons.Controls.Add(yes);
            buttons.Controls.Add(no);

            body.Dock = DockStyle.Fill;
            titleLabel.Dock = DockStyle.Top;
            Controls.Add(body);
            Controls.Add(titleLabel);
            Controls.Add(buttons);
            AcceptButton = yes;
            CancelButton = no;

            // 构造阶段句柄/字体还没就绪，这里量不准；真正的高度在 ShowNear 里定
            ClientSize = new Size(width, 160);

            // 边框：只在最外圈画 1px 细蓝线。
            // ⚠️ **不改 Padding、不改 ClientSize** —— 这个框的 Padding(18,16,18,14)
            // 是内容视觉内边距，内容离边缘最少 14px，本来就碰不到最外圈那 1px，
            // 所以**不需要**像 PopupPanel 那样递归裁剪后代控件，布局一点不动、字体不会被截断。
            Paint += delegate(object sender, PaintEventArgs e)
            {
                if (!PopupPanel.UseBlueBorder) { return; }
                int radius = cornerRadiusApplied > 0 ? cornerRadiusApplied : UiTools.CornerRadiusFor(Handle);
                UiTools.DrawPanelRing(e.Graphics, Width, Height, radius, PopupPanel.PanelBlueBorder);
            };
        }

        /// <summary>切圆角：Region 和边框必须用**同一个**半径，否则弧会被裁掉。</summary>
        private void ApplyCornerRegion()
        {
            if (!IsHandleCreated) { return; }
            try
            {
                int radius = UiTools.CornerRadiusFor(Handle);
                cornerRadiusApplied = radius;
                using (GraphicsPath path = UiTools.RoundedRect(new RectangleF(0, 0, Width, Height), radius))
                {
                    Region = new Region(path);
                }
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

        /// <summary>
        /// 按**真实字体**重新量正文需要多高，再定最终窗体高度。
        ///
        /// ⚠️ 两个坑：
        /// 1. 必须在**句柄就绪后**量（<see cref="ShowNear"/> 里）。构造函数里
        ///    <c>GetPreferredSize</c> 量出来偏小（实测需要 225px 却只量到 ~46px），
        ///    长文本下半截直接被切掉（用户报"字体显示不全"）。
        /// 2. <c>body.Dock = Fill</c>，**显式设的 <c>body.Height</c> 会被停靠布局覆盖**
        ///    （实测设 227 实际只剩 48）。所以只能改 <c>ClientSize</c>，
        ///    并在最后回补一次被 Dock 压掉的那部分高度。
        /// </summary>
        private void FitContent(int width)
        {
            int inner = Math.Max(80, width - Padding.Horizontal);
            try { CreateControl(); } catch { }

            body.Width = inner;
            int textHeight;
            try { textHeight = body.GetPreferredSize(new Size(inner, 0)).Height; }
            catch { textHeight = 60; }
            textHeight = Math.Max(60, textHeight);

            int wanted = Padding.Vertical + titleLabel.Height + textHeight + buttons.Height;
            if (ClientSize.Width != width || ClientSize.Height < wanted)
            {
                ClientSize = new Size(width, Math.Max(ClientSize.Height, wanted));
            }

            // Dock=Fill 可能又把 body 压矮了，再回补一次
            if (body.Height + 2 < textHeight)
            {
                ClientSize = new Size(width, ClientSize.Height + (textHeight - body.Height));
            }
        }

        /// <summary>把对话框摆在 owner 下方（放不下就摆上方），并水平对齐 owner 的左边缘。</summary>
        public DialogResult ShowNear(Form owner)
        {
            // 显示前再按真实字体量一次：构造时量的高度可能偏小，长文本会被截断
            FitContent(ClientSize.Width);

            Rectangle anchor = owner == null ? new Rectangle(0, 0, 0, 0) : owner.Bounds;
            if (anchor.Width <= 0 || anchor.Height <= 0) { anchor = Screen.PrimaryScreen.WorkingArea; }

            Rectangle area = Screen.FromRectangle(anchor).WorkingArea;
            const int gap = 10;
            int x = anchor.Left;
            if (x + Width > area.Right) { x = area.Right - Width; }
            if (x < area.Left) { x = area.Left; }

            int y = anchor.Bottom + gap;
            if (y + Height > area.Bottom) { y = anchor.Top - Height - gap; }
            if (y < area.Top) { y = area.Top; }

            Location = new Point(x, y);
            return ShowDialog(owner);
        }
    }
}
