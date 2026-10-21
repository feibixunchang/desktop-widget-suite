using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Shared
{
    /// <summary>
    /// 桌面挂件基类：透明无边框、可拖动、位置记忆、托盘、右键菜单、字体大小与透明度设置。
    /// 位置记忆与"锁定位置"互斥：锁定后仍可用菜单里的"回到默认位置"。
    /// </summary>
    internal class WidgetForm : Form
    {
        private static readonly uint SwpNoZOrder = 0x0004;
        private static readonly uint SwpNoSize = 0x0001;
        private static readonly uint SwpNoMove = 0x0002;
        private static readonly uint SwpNoActivate = 0x0010;
        private static readonly IntPtr HwndTopMost = new IntPtr(-1);
        private static readonly IntPtr HwndNotTopMost = new IntPtr(-2);

        /// <summary>z 序最底：压在所有普通窗口之下（HWND_BOTTOM = 1）。</summary>
        private static readonly IntPtr HwndBottom = new IntPtr(1);

        /// <summary>z 序正常顶部（HWND_TOP = 0）。</summary>
        private static readonly IntPtr HwndTop = new IntPtr(0);

        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr FindWindowW(string className, string windowName);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);


        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int command);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetParent(IntPtr child, IntPtr newParent);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongW(IntPtr hWnd, int index, IntPtr value);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetWindowLongW(IntPtr hWnd, int index);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        protected NotifyIcon TrayIcon;
        protected ContextMenuStrip Menu = new ContextMenuStrip();
        protected WidgetSettingsStore Store;
        protected DoubleBufferedPanel Canvas;
        protected Point dragStart;
        private bool suppressSave;
        private ToolStripMenuItem lockItem;
        private ToolStripMenuItem desktopLayerItem;
        private ToolStripMenuItem fontMenu;
        private ToolStripMenuItem opacityMenu;        private bool closing;
        private System.Windows.Forms.Timer saveTimer;
        private System.Windows.Forms.Timer fullscreenTimer;
        private bool hiddenForFullscreen;

        /// <summary>被压到底层之前是不是置顶的，恢复 z 序时要用（见 RestoreZOrder）。</summary>
        private bool zOrderWasTopMost;

        /// <summary>
        /// 子类全屏播放时返回 true —— 这期间窗口必须留在最前，
        /// 不能被"只在桌面显示"的压底逻辑按下去（否则全屏画面会闪、任务栏会冒出来）。
        /// </summary>
        protected virtual bool WantsTopMostWhileActive
        {
            get { return false; }
        }

        /// <summary>右键菜单是否正开着。开着的时候不能压 z 序，否则会来回抢、窗口乱闪。</summary>
        private bool menuOpen;

        private bool inDesktopLayer;

        /// <summary>排查用：设 FSWATCH_LOG=1 时把每轮判定写进 %TEMP%\fswatch.log。</summary>
        private static bool DebugLogging
        {
            get
            {
                string flag = System.Environment.GetEnvironmentVariable("FSWATCH_LOG");
                return flag == "1";
            }
        }

        /// <summary>
        /// 真正的逐像素透明模式（<c>UpdateLayeredWindow</c>）。
        /// 关闭时（默认）沿用老的颜色键透明：只有"无背景"那一档有效果。
        /// 万年历/记账/RSS 还没转过来，所以默认关；倒计时闹钟已经转成 true。
        /// </summary>
        protected virtual bool UseLayeredSurface
        {
            get { return false; }
        }

        /// <summary>逐像素透明模式下画文字的渲染提示。ClearType 依赖不透明底色，这里必须用灰度抗锯齿。</summary>
        protected System.Drawing.Text.TextRenderingHint SurfaceTextHint
        {
            get
            {
                return UseLayeredSurface
                    ? System.Drawing.Text.TextRenderingHint.AntiAliasGridFit
                    : System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            }
        }

        /// <summary>
        /// 背景色。逐像素透明模式下，"无背景"那一档不能一个字都不画：
        /// 分层窗口按 alpha 做命中测试，alpha=0 的地方鼠标点不到，挂件就整个点不动。
        /// 所以这时用 alpha=1 铺底——肉眼看不出来（合成后和桌面只差 0.4%），但整块都能点。
        /// </summary>
        protected Color SurfaceColor(int alpha, int red, int green, int blue)
        {
            if (UseLayeredSurface && alpha <= 0) { return Color.FromArgb(1, red, green, blue); }
            return Color.FromArgb(alpha, red, green, blue);
        }

        /// <summary>和 <see cref="SurfaceColor(int,int,int,int)"/> 一样，只是直接给一个颜色。</summary>
        protected Color SurfaceColor(int alpha, Color color)
        {
            return SurfaceColor(alpha, color.R, color.G, color.B);
        }

        public WidgetForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            AutoSize = false;
            KeyPreview = true;

            if (UseLayeredSurface)
            {
                // 分层窗口：画面由我们自己画进 32 位 ARGB 位图再交给系统合成，
                // 不能用颜色键，也不能走 WinForms 那套 WM_PAINT 缓冲绘制。
                BackColor = Color.Black;
                DoubleBuffered = false;
            }
            else
            {
                BackColor = Theme.Key;
                TransparencyKey = Theme.Key;
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            }

            Store = new WidgetSettingsStore(AppPaths.SettingsFile);
            if (Store.Settings.Opacity < 0 || Store.Settings.Opacity > 100) { Store.Settings.Opacity = 92; }            if (Store.Settings.FontSize < MinFontSize || Store.Settings.FontSize > MaxFontSize)
            {
                Store.Settings.FontSize = DefaultFontSize;
            }

            ApplyBackground();
            // 置顶**默认关闭**：用户要的是挂件只待在桌面层、像桌面图标那样被别的窗口盖住，
            // 而不是压在所有程序上面。FirstRun 也不再强制置顶了。
            // 桌面层模式（默认）下 OnHandleCreated 里会再确保一次 TopMost=false。
            TopMost = Store.Settings.TopMost;
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
            // 逐像素透明模式：窗体没有子控件，Canvas 只当作"事件收件箱"，
            // 画面直接画在窗体自己的分层表面上。鼠标事件由窗体转发进来。
            if (!UseLayeredSurface) { Controls.Add(Canvas); }

            // 滚轮：两种模式都要能收到。颜色键模式下由 Canvas 收（它是真的子控件），
            // 分层模式下没有子控件，滚轮会落到窗体上——不接的话滚轮就是完全死的。
            Canvas.MouseWheel += delegate(object sender, MouseEventArgs e) { OnSurfaceMouseWheel(e); };
            if (UseLayeredSurface)
            {
                MouseWheel += delegate(object sender, MouseEventArgs e) { OnSurfaceMouseWheel(e); };
            }

            ContextMenuStrip = Menu;
            Menu.Opening += delegate
            {
                // 菜单弹出时**必须暂停压 z 序**。
                // 菜单是独立的顶层窗口，弹出时会被系统插到挂件前面；
                // 而 PollFullscreen 每秒都在压底，两者来回抢 z 序，
                // 挂件就一闪一闪（用户实机反馈"右键点一下播放窗口就乱闪"）。
                menuOpen = true;
                RefreshMenuState();
            };
            Menu.Closed += delegate
            {
                // 菜单关掉后再压一次，把挂件沉回普通窗口之下
                menuOpen = false;
                if (Store.Settings.DesktopLayer != false) { SendToBack(); }
            };
        }

        protected virtual string RegistryName
        {
            get { return AppPaths.AppId; }
        }

        /// <summary>0 = 完全不画背景（无背景，完全透明）。</summary>
        public bool NoBackground
        {
            get { return Store.Settings.Opacity <= 0; }
        }

        public virtual int DefaultFontSize
        {
            get { return 14; }
        }

        public virtual int MinFontSize
        {
            get { return 11; }
        }

        public virtual int MaxFontSize
        {
            get { return 18; }
        }

        public virtual int[] FontSizes()
        {
            return new[] { 11, 12, 13, 14, 16, 18 };
        }

        private void ApplyBackground()
        {
            // 关键：透明度只作用在"背景卡片"上，不能去改整个窗体的 Opacity。
            // 以前这里设 Opacity = 级别/100，结果卡片和文字一起被淡化，调到 10% 时
            // 字几乎看不清（小普河是分层窗口、文字单独绘制，所以它没这个问题）。
            // 现在窗体永远 100% 不透明，卡片浓淡由各挂件画背景时用 CardAlpha 决定。
            // 逐像素透明模式更不能碰 Opacity：Form.Opacity 会顺手调
            // SetLayeredWindowAttributes，把我们自己做的 alpha 合成整个顶掉。
            if (!UseLayeredSurface) { Opacity = 1f; }
        }

        /// <summary>
        /// 背景卡片的不透明度（0~255）。0 = 完全不画卡片，只剩文字。
        /// 菜单里的 0%~100% 换算成这个值给画背景用。
        /// </summary>
        protected int CardAlpha
        {
            get
            {
                int level = Store.Settings.Opacity;
                if (level <= 0) { return 0; }
                if (level >= 100) { return 255; }
                return (int)Math.Round(255d * level / 100d);
            }
        }
        /// <summary>
        /// 桌面挂件一律按"工具窗口 + 不激活"创建。
        /// WS_EX_TOOLWINDOW 这一条很关键：只有带这个样式的窗口才能正常挂到桌面层
        /// （Progman）上显示。天气CPU挂件早就是这个样式，所以它的"贴在桌面底层"
        /// 一直正常；而 RSS / 万年历 / 记账以前是普通顶层窗口，一挂到桌面层就整个消失。
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= 0x00000080 /* WS_EX_TOOLWINDOW */ | 0x08000000 /* WS_EX_NOACTIVATE */;
                if (UseLayeredSurface) { parameters.ExStyle |= LayeredSurface.WsExLayered; }
                return parameters;
            }
        }

        // ---------------- 逐像素透明模式 ----------------

        private Bitmap surface;
        private bool rendering;
        private Point lastRenderedAt;
        private bool hasRendered;

        /// <summary>重画挂件。颜色键模式转成普通重绘，分层模式则重画整张 ARGB 位图再合成。</summary>
        protected void InvalidateSurface()
        {
            InvalidateSurface(Rectangle.Empty);
        }

        /// <summary>和 <see cref="InvalidateSurface()"/> 一样，参数是"只重画这一块"（目前分层模式整张一起画）。</summary>
        protected void InvalidateSurface(Rectangle dirty)
        {
            if (!UseLayeredSurface)
            {
                if (Canvas != null) { Canvas.Invalidate(dirty); }
                return;
            }

            RenderSurface();
        }

        /// <summary>
        /// 分层窗口的画面必须和窗口位置一起交给系统，挪过之后要重新交一次。
        /// 这里的坑：<c>Form.Location</c> 是控件自己缓存的值，窗口被系统挪动（DPI 变化、
        /// 贴边、拖动）之后它可能还是旧的，于是画面就会停在老地方 ——
        /// 现象是"看得见挂件，但点它没反应"（鼠标点在窗口矩形里，画面却在上方）。
        /// 所以这里一律用 <c>GetWindowRect</c> 拿实际位置，并且每秒核对一次，错了就重画。
        /// </summary>
        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (UseLayeredSurface) { InvalidateSurface(); }
        }

        private bool SurfaceOutOfPlace()
        {
            if (!hasRendered || !IsHandleCreated || !Visible) { return false; }
            NativeRect actual;
            if (!GetWindowRect(Handle, out actual)) { return false; }
            return actual.Left != lastRenderedAt.X || actual.Top != lastRenderedAt.Y;
        }

        /// <summary>
        /// 挂件在屏幕上的真实矩形（弹窗定位用）。
        /// 弹窗必须贴着挂件开，位置算错一点就会压到挂件自己的标题上
        /// （用户报过："自定"弹窗挡住"倒计时"三个字）。
        /// <c>Form.Bounds</c>/<c>RectangleToScreen</c> 都是缓存值，窗口被系统挪动后会过期，
        /// 所以一律用 <c>GetWindowRect</c>。
        /// </summary>
        protected Rectangle AnchorBoundsOnScreen()
        {
            NativeRect actual;
            if (IsHandleCreated && GetWindowRect(Handle, out actual))
            {
                var real = new Rectangle(actual.Left, actual.Top,
                    actual.Right - actual.Left, actual.Bottom - actual.Top);
                if (real.Width > 0 && real.Height > 0) { return real; }
            }

            return RectangleToScreen(new Rectangle(0, 0, Width, Height));
        }

        /// <summary>把画面画进 32 位 ARGB 位图，然后交给系统做 alpha 合成。</summary>
        private void RenderSurface()
        {
            if (rendering || IsDisposed || !IsHandleCreated) { return; }
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            if (width <= 0 || height <= 0) { return; }

            rendering = true;
            try
            {
                if (surface == null || surface.Width != width || surface.Height != height ||
                    surface.PixelFormat != System.Drawing.Imaging.PixelFormat.Format32bppArgb)
                {
                    if (surface != null) { surface.Dispose(); }
                    surface = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                }

                using (Graphics g = Graphics.FromImage(surface))
                {
                    // 每帧都要清成"完全透明"，卡片之外的像素 alpha=0 ⇒ 那里能看见桌面
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = SurfaceTextHint;

                    var bounds = new Rectangle(0, 0, width, height);
                    OnCanvasPaint(this, new PaintEventArgs(g, bounds));
                    g.Flush();
                }

                NativeRect actual;
                if (!GetWindowRect(Handle, out actual)) { return; }
                lastRenderedAt = new Point(actual.Left, actual.Top);
                hasRendered = true;
                LayeredSurface.Update(Handle, surface, lastRenderedAt);
            }
            catch
            {
                // 画不出来就保持上一次的画面，绝不能让挂件凭空消失
            }
            finally
            {
                rendering = false;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 分层模式：画面不走 WM_PAINT，只在 OnHandleCreated / 状态变化时合成一次
            if (!UseLayeredSurface) { base.OnPaint(e); }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (!UseLayeredSurface) { base.OnPaintBackground(e); }
        }

        // 分层模式没有子控件，窗体自己收鼠标事件，再直接转给各挂件的处理方法。
        // 注意不能靠"触发 Canvas 的 MouseDown 事件"来转发：Control.OnMouseDown 里
        // 有一句 if (!IsHandleCreated) return，而分层模式下 Canvas 从来没被加进
        // Controls、也就永远没有句柄，事件会被静默丢掉（挂件就变成点不动）。
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (UseLayeredSurface) { OnCanvasMouseDown(this, e); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (UseLayeredSurface) { OnCanvasMouseMove(this, e); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (UseLayeredSurface) { OnCanvasMouseUp(this, e); }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (UseLayeredSurface) { OnMouseCaptureChangedForCanvas(e); }
        }

        /// <summary>默认什么都不做；RSS 之类要感知"鼠标捕获被别人抢走"的挂件自己覆盖它。</summary>
        protected virtual void OnMouseCaptureChangedForCanvas(EventArgs e)
        {
        }




        protected virtual string DisplayName
        {
            get { return "桌面挂件"; }
        }

        protected virtual void OnCanvasPaint(object sender, PaintEventArgs e)
        {
        }

        /// <summary>滚轮。两种模式都从这里进来（见构造函数里的两处挂接）。</summary>
        protected virtual void OnSurfaceMouseWheel(MouseEventArgs e)
        {
        }

        /// <summary>
        /// 鼠标捕获。分层模式下没有子控件，捕获只能走窗体自己；
        /// 对 Canvas 设 Capture 会给一个"没被加进 Controls"的面板强行建句柄。
        /// </summary>
        protected void SetSurfaceCapture(bool value)
        {
            if (UseLayeredSurface) { Capture = value; }
            else { Canvas.Capture = value; }
        }

        /// <summary>当前是不是我们抓着鼠标（正在拖动/缩放）。</summary>
        protected bool SurfaceHasCapture
        {
            get { return UseLayeredSurface ? Capture : Canvas.Capture; }
        }


        protected virtual void BuildMenu()
        {
        }

        /// <summary>右键菜单每次弹出前的钩子（子类用来刷新勾选状态）。</summary>
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
            lockItem.Click += delegate { ToggleLock(); };
            Menu.Items.Add(lockItem);

            // 只保留"只在桌面显示"这一个模式开关。
            // "窗口置顶"已删除：它和"只在桌面显示"互斥，而用户要的是后者。
            // "全屏窗口在前台时自动隐藏"也已删除：桌面显示模式下本来就一直让位
            //（挂进桌面层，z 序由 explorer 托管），这个开关没有意义；
            // 保留它只会让人误以为还有两种行为，实际早就是同一种了。
            desktopLayerItem = new ToolStripMenuItem("只在桌面显示（不压在程序上面）");
            desktopLayerItem.Click += delegate
            {
                Store.Settings.DesktopLayer = !(Store.Settings.DesktopLayer ?? true);
                Store.Save();
                if (Store.Settings.DesktopLayer == true)
                {
                    // 桌面层模式：置顶一定关掉，两者互斥
                    if (TopMost) { TopMost = false; }
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
            foreach (int size in FontSizes())
            {
                int captured = size;
                var item = new ToolStripMenuItem(size.ToString());
                item.Tag = "font:" + size;
                item.Click += delegate { OnMenuClick("font:" + captured); };
                fontMenu.DropDownItems.Add(item);
            }
            Menu.Items.Add(fontMenu);

            opacityMenu = new ToolStripMenuItem("透明度");
            // 0 到 100 每 10% 一档，都列出来让人直接挑，不用猜
            int[] levels = { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
            foreach (int level in levels)
            {
                int captured = level;
                var item = new ToolStripMenuItem(level == 0 ? "无背景（完全透明）" : level + "%");
                item.Tag = "opacity:" + level;
                item.Click += delegate { OnMenuClick("opacity:" + captured); };
                opacityMenu.DropDownItems.Add(item);
            }
            Menu.Items.Add(opacityMenu);
            var home = new ToolStripMenuItem("回到默认位置");
            home.Click += delegate { ResetPosition(); };
            Menu.Items.Add(home);

            var autoStart = new ToolStripMenuItem("开机启动");
            autoStart.Click += delegate { ToggleAutoStart(); };
            Menu.Items.Add(autoStart);

            Menu.Items.Add(new ToolStripSeparator());
            BuildMenu();
            Menu.Items.Add(new ToolStripSeparator());
            var about = new ToolStripMenuItem("关于");
            about.Click += delegate { ShowAbout(); };
            Menu.Items.Add(about);
            var exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { Close(); };
            Menu.Items.Add(exit);
        }

        /// <summary>
        /// 强行把"置顶"标记去掉。
        /// 之前踩过的坑：BringToFront() 内部会先 HWND_TOPMOST 再 HWND_NOTOPMOST 来抬高窗口，
        /// 只要中间被打断，WS_EX_TOPMOST 就留在窗口上不掉了——设置里明明写着 TopMost=false，
        /// 挂件却一直压在所有最大化窗口上面。所以每次都要显式清一次。
        /// </summary>
        private void ClearTopMostStyle()
        {
            if (TopMost) { return; }
            if (!IsHandleCreated) { return; }
            SetWindowPos(Handle, HwndNotTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        /// <summary>
        /// 找出"负责画壁纸、在图标层后面"的那个 WorkerW 窗口。
        ///
        /// 以前这里直接返回 Progman，挂件也确实挂上去了、IsWindowVisible 也是 true，
        /// 但屏幕上就是看不见——因为 Win10/11 的壁纸不是 Progman 画的，而是画在一个
        /// 独立的 WorkerW 上，那个 WorkerW 是 Progman 的兄弟窗口、z 序更靠前。
        /// 挂在 Progman 上的子窗口会整个被那个 WorkerW 盖住，于是"存在但看不见"。
        ///
        /// 正确做法是挂到壁纸层那个 WorkerW 上：它的子窗口在它自己的背景之上、
        /// 又在 SHELLDLL_DefView（图标层）之下，正好实现"图标后面"。
        /// </summary>
        private static IntPtr cachedDesktopHost = IntPtr.Zero;

        private static IntPtr DesktopHostWindow()
        {
            if (cachedDesktopHost != IntPtr.Zero && IsWindowVisible(cachedDesktopHost))
            {
                return cachedDesktopHost;
            }

            IntPtr progman = FindWindowW("Progman", null);
            if (progman == IntPtr.Zero) { return IntPtr.Zero; }

            // 让 Explorer 额外分出一个 WorkerW 来挂壁纸（经典做法，只发一次）
            IntPtr spawn;
            SendMessageTimeoutW(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out spawn);

            // ⚠️ 这里**故意不挂桌面层**。
            //
            // 桌面层这条路在本机走不通，试遍了所有宿主（2026-10-02 反复实测）：
            //     Progman (可见)
            //      ├─ SHELLDLL_DefView (可见, 全屏)  ← 图标层
            //      │   └─ SysListView32 (可见, 全屏)  ← 桌面图标列表
            //      └─ WorkerW (可见, 全屏)            ← 壁纸层
            //   挂 SHELLDLL_DefView 内部 → explorer 不接受外部子窗口进它的图标视图，
            //                              实测主窗口直接 vis=False，挂件凭空消失；
            //   挂 壁纸层 WorkerW        → 同样 vis=False；
            //   挂 Progman 本体          → 被同级 SHELLDLL_DefView 全屏盖住。
            // 关键在于：**这些失败都查不出来** —— `IsWindowVisible` 返回的是 true，
            // 窗口"存在"，只是屏幕上不画东西。安全网救不回来，只能靠肉眼发现。
            //
            // 所以桌面层这条路**放弃**，改用等效方案（见 SendToBack）：
            // 保持顶层窗口 + 永远不置顶 + 压在普通窗口之下。
            // 观感和"贴在桌面上"完全一样 —— 任何程序窗口（浏览器、资源管理器、全屏）
            // 都在挂件上面，只有看空桌面时才看得见 —— 而且不闪、不会凭空消失。
            cachedDesktopHost = IntPtr.Zero;
            return IntPtr.Zero;
        }

        private void EnterDesktopLayer()
        {
            TopMost = false;

            // 桌面层宿主这条路在本机走不通（DesktopHostWindow 里列了所有试过的宿主），
            // 所以**不走 SetParent 挂接**，直接用等效方案：保持顶层窗口，
            // 永远不置顶，并压在普通窗口之下。
            //
            // 这样挂件永远可见、永远不压在程序上面，行为和"贴在桌面上"一样。
            // 想哪天真的挂上桌面层，只要让 DesktopHostWindow 返回非 0 即可，
            // 下面这段挂接代码原样保留着。
            SendToBack();
        }

        private void LeaveDesktopLayer()
        {
            SetParent(Handle, IntPtr.Zero);
            SetWindowLongW(Handle, -16, new IntPtr(GetWindowLongW(Handle, -16).ToInt32() & ~0x40000000));
            SetWindowLongW(Handle, -20, new IntPtr(GetWindowLongW(Handle, -20).ToInt32() & ~0x40000000));
            SetWindowPos(Handle, new IntPtr(-2 /* HWND_NOTOPMOST */), 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate | 0x0020 /* SWP_FRAMECHANGED */);
            ShowWindow(Handle, 5 /* SW_SHOW */);
            inDesktopLayer = false;
            RestorePosition();
        }

        /// <summary>
        /// 有全屏/最大化窗口开着就自动藏起来，回桌面再出现。
        /// 判定不看 z 序先后、也不看前台是谁：挂件自己刚启动时就是前台，而且它自己
        /// 就压在最大化窗口上面（z 序更靠前），所以只要屏幕上存在一个"盖住 92% 以上、
        /// 又不是工具窗口、也不是桌面本身"的窗口，就说明有人在干正事，挂件让位。
        /// 工具窗口要排除：鼠标拖尾、桌面增强这类整屏浮层常年是 topmost 工具窗。
        /// </summary>
        private void PollFullscreen()
        {
            if (inDesktopLayer) { return; }
            if (closing || IsDisposed || !IsHandleCreated) { return; }

            // ⚠️ 自己在全屏播放时**绝对不能压 z 序**。
            // 全屏窗口本来就是置顶的（自己会设 TopMost），这时候去压底就是自己和自己打架，
            // 全屏画面会一闪一闪，任务栏也会冒出来。子类（比如桌面视频）全屏时覆写返回 true。
            if (WantsTopMostWhileActive) { return; }

            // "只在桌面显示"模式：**持续压到普通窗口之下**，不需要检测"有没有全屏窗口"。
            // 用户要的就是"只在桌面看见挂件"，那就不该等程序铺满屏幕才让位 ——
            // 任何普通窗口（浏览器、资源管理器、记事本）都应该在挂件上面。
            // 可见性自始至终不变，所以不会闪。
            //
            // ⚠️ 必须**每秒都压**，不能只在第一次压。
            // 别人每开一个新窗口，系统都会把它插到非置顶层最前，挂件就跑到它前面去了。
            // 只压一次的话，第二个窗口打开时挂件就已经不对了（实测踩到）。
            if (Store.Settings.DesktopLayer != false)
            {
                // ⚠️ 菜单开着的时候**绝对不能压 z 序**。
                // 菜单是独立顶层窗口，弹出时被系统插到挂件前面；
                // 这里再压底就是两者来回抢，挂件会一闪一闪（用户实机反馈）。
                // 菜单关掉时 Menu.Closed 里会补压一次。
                if (Visible && !menuOpen)
                {
                    hiddenForFullscreen = true;
                    SendToBack();
                }
                return;
            }

            // 菜单里手动取消"只在桌面显示"之后，才回到老逻辑：检测全屏才让位
            if (!(Store.Settings.HideOnFullscreen ?? true)) { return; }

            bool covered = false;
            IntPtr walk = GetTopWindow(IntPtr.Zero);
            uint myPid = GetCurrentProcessId();
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            double screenArea = (double)bounds.Width * bounds.Height;

            while (walk != IntPtr.Zero && !covered)
            {
                uint pid;
                GetWindowThreadProcessId(walk, out pid);
                if (pid != myPid && IsWindowVisible(walk) && !IsShellOverlay(walk))
                {
                    int exStyle = GetWindowLongW(walk, -20).ToInt32();
                    if ((exStyle & 0x80) == 0)          // 排除工具窗口（浮层类）
                    {
                        // 还要么是前台、要么是置顶，才算"有人在干正事"。
                        // 不加这条的话，"Windows 输入体验"这种铺满全屏的后台外壳窗口
                        // 会被误判成全屏应用，挂件就永远再也不出现了。
                        bool topmost = (exStyle & 0x8) != 0;
                        if (topmost || walk == GetForegroundWindow())
                        {
                            NativeRect area;
                            if (GetWindowRect(walk, out area))
                            {
                                double windowArea = (double)(area.Right - area.Left) * (area.Bottom - area.Top);
                                if (windowArea >= screenArea * 0.92) { covered = true; }
                            }
                        }
                    }
                }

                walk = GetWindow(walk, 2 /* GW_HWNDPREV */);
            }

            // ⚠️ 这里**绝对不能用 Hide()/Show()** 来"藏"挂件。
            //
            // 挂件是分层窗口(WS_EX_LAYERED)，画面靠 UpdateLayeredWindow 一帧帧送上去的。
            // Hide()/Show() 会真的去改可见性：
            //   * Show() 时 Windows 认为窗口刚"出现"，之前送上去的分层画面已经作废，
            //     必须等下一次 RenderSurface() 重画一帧才补上；
            //     在这一帧到下一帧之间，屏幕上就是空白/残影 —— **这就是"退出全屏时闪一下"的根源**；
            //   * Hide() 同理，窗口消失时那一帧也可能被合成器捕捉到。
            // 而且 Show() 还会顺带把置顶状态、恢复位置那套流程走一遍，更容易看见。
            //
            // 正确做法：**可见性一直不变，只动 z 序**。
            // 窗口根本没被隐藏过，一帧画面都没丢，所以**一点都不闪**。
            // 桌面图标不闪也正是因为它压根没有"隐藏"这个概念，只是 z 序靠后。
            if (covered && !hiddenForFullscreen && Visible)
            {
                hiddenForFullscreen = true;
                SendToBack();
                DebugLog("有全屏窗口在前台，已压到 z 序底层");
            }
            else if (!covered && hiddenForFullscreen)
            {
                hiddenForFullscreen = false;
                RestoreZOrder();
                DebugLog("全屏窗口没了，已恢复 z 序");
            }
        }

        /// <summary>
        /// 压到"桌面壁纸之上、所有程序窗口之下"，但**不碰可见性**，分层画面一帧都不丢。
        ///
        /// ⚠️ 两次踩坑，别再改回去：
        /// 1. **不能用 `HWND_BOTTOM`** —— 那会把窗口压到 Progman/WorkerW（桌面本身）
        ///    **之下**，结果挂件把整个桌面盖黑。
        /// 2. **不能用 `hWndInsertAfter = Progman`** —— Progman 是顶层窗口，
        ///    挂件插在它旁边仍参与正常 z 序排序，结果照样压在浏览器上面（实测）。
        ///
        /// 正确做法：两步走。
        /// 1. 先 `HWND_NOTOPMOST` 明确把窗口丢进**非置顶层**（桌面壁纸之上、所有置顶窗口之下）；
        /// 2. 再用 `SetWindowPos(HWND_BOTTOM)` 压到**该层内部最底**。
        /// 效果 = 任何普通程序窗口都在挂件上面，桌面图标和壁纸不受影响。
        /// </summary>
        private void SendToBack()
        {
            if (IsDisposed || !IsHandleCreated) { return; }
            try
            {
                // ⚠️ 已经压好就别再压。
                // `SetWindowPos` 即使目标顺序没变也会引发 z 序变化通知 + 窗口重绘，
                // 每秒无条件调一次 = 每秒闪一次（用户实机反馈"右键点完再点，播放窗口就闪"）。
                if (!NeedsSendToBack()) { return; }

                // 先记住原来的置顶设定，恢复 z 序时要用。
                // ⚠️ 全屏期间不记：全屏的 TopMost 是临时设的，
                // 拿它当"用户原本的设定"会污染 RestoreZOrder。
                if (!WantsTopMostWhileActive) { zOrderWasTopMost = TopMost; }

                // 第一步：明确丢进非置顶层（必须在桌面壁纸之上，不能掉下去）
                if (TopMost) { TopMost = false; }
                SetWindowPos(Handle, HwndNotTopMost, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);

                // 第二步：压到非置顶层内部最底
                SetWindowPos(Handle, HwndBottom, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
            }
            catch
            {
                // z 序调整失败也不能让挂件消失，交给下一轮轮询再试
            }
        }

        /// <summary>
        /// 判断挂件现在是不是已经被别的窗口插到前面去了。
        /// 沿着 z 序从自己往下看：**只要下面还有一个"该被盖住"的普通窗口，就说明被插到前面了**。
        /// 已经待在最底（下面只剩自己那类无关窗口）时返回 false，调用方就不用动它，避免闪。
        /// </summary>
        private bool NeedsSendToBack()
        {
            if (TopMost) { return true; }

            // ⚠️ 不要用"从自己往下找有没有普通窗口"来判断 —— 那个逻辑实测会漏判：
            // 全屏视频窗口（VLC PotPlayer 等）常常带 WS_EX_TOOLWINDOW 或被列进
            // IsShellOverlay 的排除条件，于是被判成"下面没有需要让位的窗口" → 不压 →
            // **全屏播放时挂件全都浮在画面上**（用户实机反馈）。
            //
            // 可靠判据：**自己是不是当前 z 序里最靠前的那个**。
            // 从 HWND_TOP 往下走到自己为止，中间只要有任何一个别的进程窗口，
            // 就说明自己被插到前面去了，需要压回去。桌面外壳（Progman/WorkerW/托盘/
            // 输入法）和自己的其它窗口、工具窗口都要排除 —— 这些不参与"遮挡"关系。
            IntPtr walk = GetTopWindow(IntPtr.Zero);
            uint myPid = GetCurrentProcessId();
            int guard = 0;
            while (walk != IntPtr.Zero && guard++ < 256)
            {
                if (walk == Handle) { return false; }   // 走到自己了 => 前面没人 => 已经贴桌面

                if (IsWindowVisible(walk))
                {
                    uint pid;
                    GetWindowThreadProcessId(walk, out pid);
                    if (pid != myPid && !IsShellOverlay(walk))
                    {
                        int exStyle = GetWindowLongW(walk, -20).ToInt32();
                        if ((exStyle & 0x80) == 0)
                        {
                            // 找到一个别的进程的普通窗口在自己前面 => 需要压底
                            return true;
                        }
                    }
                }

                walk = GetWindow(walk, 2 /* GW_HWNDPREV */);
            }

            // 走到底都没遇到自己（窗口被重建/句柄失效）=> 保守压一次
            return true;
        }

        /// <summary>全屏窗口走了，把 z 序恢复原样。</summary>
        private void RestoreZOrder()
        {
            if (IsDisposed || !IsHandleCreated) { return; }
            try
            {
                // ⚠️ 这里**不能**用 `|| Store.Settings.FirstRun`。
                // FirstRun 是"位置还没存过"的标记，不是"用户想要置顶"。
                // 拿它当置顶依据会让**首次运行的挂件自己置顶**，
                // 和"只在桌面显示"直接冲突（用户实机反馈过挂件压在程序上面）。
                // 置顶只认用户显式勾选的 TopMost（而桌面显示模式下那个菜单项已删除）。
                bool wantTop = zOrderWasTopMost || Store.Settings.TopMost;
                if (wantTop)
                {
                    TopMost = true;
                }
                else
                {
                    // 先明确落到非置顶，再提到正常顶部
                    if (TopMost) { TopMost = false; }
                    ClearTopMostStyle();
                    SetWindowPos(Handle, HwndTop, 0, 0, 0, 0,
                        SwpNoMove | SwpNoSize | SwpNoActivate);
                }
            }
            catch
            {
            }
        }

        private static void DebugLog(string message)
        {
            if (!DebugLogging) { return; }
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fswatch.log"),
                    DateTime.Now.ToString("HH:mm:ss") + " " + System.Diagnostics.Process.GetCurrentProcess().ProcessName + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 桌面本身和 Windows 外壳自己铺满全屏的透明窗口，都不算"别人在干活"。
        /// 这些窗口可见、尺寸等于整屏，但用户根本看不到它们（桌面壁纸、文字输入宿主、手写板…）。
        /// </summary>
        private static bool IsShellOverlay(IntPtr handle)
        {
            var builder = new System.Text.StringBuilder(64);
            GetClassNameW(handle, builder, 64);
            string cls = builder.ToString();
            if (cls == "Progman" || cls == "WorkerW") { return true; }
            if (cls == "Windows.UI.Core.CoreWindow" || cls == "ShellHandwritingCanvas") { return true; }
            if (cls.StartsWith("Wnd_StartFeed", StringComparison.Ordinal)) { return true; }
            return handle == FindWindowW("Progman", null) || handle == FindWindowW("WorkerW", null);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder text, int count);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int NativeGetWindowTextW(IntPtr hWnd, System.Text.StringBuilder text, int count);

        private void ShowAbout()
        {
            Version version = typeof(WidgetForm).Assembly.GetName().Version;
            string number = version == null ? "2.0.1" : version.Major + "." + version.Minor + "." + version.Build;
            MessageBox.Show(this,
                DisplayName + "  版本 " + number + "（当前最新）\n\n" +
                "数据目录：" + AppPaths.DataDirectory + "\n\n" +
                "右键菜单可锁定位置、调整字号与背景透明度、设置开机启动。",
                DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RefreshMenuState()
        {
            OnMenuOpening();

            if (lockItem != null) { lockItem.Text = Store.Settings.Locked ? "解锁位置" : "锁定位置"; }

            if (desktopLayerItem != null)
            {
                desktopLayerItem.Checked = Store.Settings.DesktopLayer ?? true;
            }

            if (fontMenu != null)
            {
                foreach (ToolStripItem item in fontMenu.DropDownItems)
                {
                    var entry = item as ToolStripMenuItem;
                    if (entry == null || entry.Tag == null) { continue; }
                    entry.Checked = string.Equals(entry.Tag.ToString(), "font:" + Store.Settings.FontSize, StringComparison.Ordinal);
                }
            }

            if (opacityMenu != null)
            {
                foreach (ToolStripItem item in opacityMenu.DropDownItems)
                {
                    var entry = item as ToolStripMenuItem;
                    if (entry == null || entry.Tag == null) { continue; }
entry.Checked = string.Equals(entry.Tag.ToString(), "opacity:" + Store.Settings.Opacity, StringComparison.Ordinal);
                }
            }

            var autoStart = FindMenuItem("开机启动");
            if (autoStart != null) { autoStart.Checked = IsAutoStartEnabled(); }
        }

        protected ToolStripMenuItem FindMenuItem(string text)
        {
            foreach (ToolStripItem item in Menu.Items)
            {
                if (item.Text == text) { return item as ToolStripMenuItem; }
                var parent = item as ToolStripMenuItem;
                if (parent == null) { continue; }
                foreach (ToolStripItem child in parent.DropDownItems)
                {
                    if (child.Text == text) { return child as ToolStripMenuItem; }
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
                if (UseLayeredSurface) { InvalidateSurface(); }
                else if (Canvas != null && Canvas.IsHandleCreated) { Canvas.Invalidate(); Canvas.Update(); }
            }
            catch
            {
            }
        }

        public void ApplyOpacity(int level)
        {
            Store.Settings.Opacity = level;
            ApplyBackground();
            // 立刻重绘并合成，不要等定时器（记账挂件的刷新定时器是 30 秒一次，
            // 万年历 1 秒一次）——不强制刷新的话看起来就像"改了没反应，过一会儿才变"。
            try
            {
                if (UseLayeredSurface) { InvalidateSurface(); }
                else if (Canvas != null && Canvas.IsHandleCreated) { Canvas.Invalidate(); Canvas.Update(); }
                else { Invalidate(); Update(); }
            }
            catch
            {
            }

            RefreshMenuState();
            ScheduleSave();
        }

        /// <summary>存盘挪到后台做，别让写文件卡住 UI 线程（点菜单的手感）。</summary>
        private void ScheduleSave()
        {
            if (saveTimer == null)
            {
                saveTimer = new System.Windows.Forms.Timer { Interval = 400 };
                saveTimer.Tick += delegate
                {
                    saveTimer.Stop();
                    Store.Save();
                };
            }

            saveTimer.Stop();
            saveTimer.Start();
        }

        public virtual Rectangle DefaultBounds
        {
            get
            {
                Rectangle area = PlacementArea;
                int width = Math.Max(Width, 300);
                int height = Math.Max(Height, 200);
                return new Rectangle(area.Right - width - 30, area.Top + 60, width, height);
            }
        }

        public void ResetPosition()
        {
            Rectangle bounds = DefaultBounds;
            Store.Settings.X = bounds.Left;
            Store.Settings.Y = bounds.Top;
            Store.Settings.FirstRun = false;
            Store.Save();
            Bounds = bounds;
        }

        protected void RestorePosition()
        {
            Rectangle area = PlacementArea;
            int width = Math.Max(Width, 300);
            int height = Math.Max(Height, 200);
            int x = Store.Settings.X;
            int y = Store.Settings.Y;
            if (Store.Settings.FirstRun || x <= 0 && y <= 0)
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
                int maxX = area.Right - width;
                int maxY = area.Bottom - height;
                x = maxX < area.Left ? area.Left : Math.Max(area.Left, Math.Min(x, maxX));
                y = maxY < area.Top ? area.Top : Math.Max(area.Top, Math.Min(y, maxY));
            }

            Bounds = new Rectangle(x, y, width, height);
        }

        private void SavePosition()
        {
            if (Store.Settings.Locked || suppressSave) { return; }
            Store.Settings.X = Left;
            Store.Settings.Y = Top;
            Store.Save();
        }

        protected virtual void OnCanvasMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                try { PopupPanel.CloseAll(); } catch { }
            }

            if (e.Button == MouseButtons.Left && !Store.Settings.Locked)
            {
                dragStart = new Point(e.X, e.Y);
            }
        }

        protected virtual void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || dragStart == Point.Empty || Store.Settings.Locked) { return; }
            int x = Left + e.X - dragStart.X;
            int y = Top + e.Y - dragStart.Y;
            // ⚠️ 必须夹在屏幕内：以前能一直往四个方向拖出屏幕外，
            // 结果挂件"不见了"，用户还以为程序崩了（七个挂件都有这个毛病）。
            // 规则和 RestorePosition 一致：整个窗口留在工作区里。
            // ⚠️ 高度用 DragClampHeight 而不是 Height：桌面视频有一截"弹出来"的控制块，
            // 拿整窗高度去夹的话画面永远被挡在控制块上面，拖不到任务栏（用户反馈过好几次）。
            Rectangle area = PlacementArea;
            int clampHeight = Math.Max(60, Math.Min(Height, DragClampHeight));
            x = Math.Max(area.Left, Math.Min(x, Math.Max(area.Left, area.Right - Width)));
            y = Math.Max(area.Top, Math.Min(y, Math.Max(area.Top, area.Bottom - clampHeight)));
            Bounds = new Rectangle(x, y, Width, Height);
        }

        /// <summary>
        /// 拖动时用来夹住下边界的高度。默认就是窗口高度；
        /// 有"临时多出来的一截"的挂件（桌面视频的控制块）要把它排除掉，
        /// 否则那一截会一直占着位置，画面永远拖不到屏幕下沿。
        /// </summary>
        protected virtual int DragClampHeight
        {
            get { return Height; }
        }


        protected virtual void OnCanvasMouseUp(object sender, MouseEventArgs e)
        {
            dragStart = Point.Empty;
            if (e.Button == MouseButtons.Left) { SavePosition(); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // 用户要的是"只在桌面看见挂件，不要压在别的程序上面"。
            // 默认开启（Store 里没有这个字段时也按开启处理）。
            if (Store.Settings.DesktopLayer != false) { EnterDesktopLayer(); }

            if (fullscreenTimer == null)
            {
                // 每秒看一眼前台窗口：别人开全屏/最大化就自动让位。
                // 分层窗口顺便核对一下画面位置：挂件被挪动过而画面没跟上时自动补画一次。
                fullscreenTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                fullscreenTimer.Tick += delegate
                {
                    PollFullscreen();
                    if (UseLayeredSurface && SurfaceOutOfPlace()) { InvalidateSurface(); }
                };
            }

            fullscreenTimer.Start();

            // 分层窗口的画面不会自动出现，第一次合成要我们自己来
            if (UseLayeredSurface) { InvalidateSurface(); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // 分层窗口的位图跟着客户区大小走，改完大小要重新画、重新合成
            if (UseLayeredSurface) { InvalidateSurface(); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            if (fullscreenTimer != null) { fullscreenTimer.Stop(); }
            if (inDesktopLayer) { LeaveDesktopLayer(); }
            // 存盘被延后了，退出前一定要落盘，否则刚改的设置会丢
            if (saveTimer != null && saveTimer.Enabled)
            {
                saveTimer.Stop();
                Store.Save();
            }

            base.OnFormClosing(e);
        }

        protected void SetupTray()
        {
            // WidgetHub 统一托盘管理：默认不创建挂件自带的托盘图标（Win11 下外部无法隐藏，
            // 只能由程序自身不创建）。需要单独托盘时，启动参数加 --tray 即可恢复。
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
                if (!Visible)
                {
                    Show();
                }

                BringToFront();
            };
        }

        private static bool TrayAllowed()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase))
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
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupRegistryPath))
                {
                    if (key == null) { return false; }
                    object value = key.GetValue(RegistryName);
                    if (value == null) { return false; }
                    // 按当前 exe 路径判断。以前只认路径里带 "Desktop" 的程序，
                    // 换个名字（比如 RssWidget.exe）就永远显示"未开启"，勾了也看不出状态。
                    string text = value.ToString();
                    return text.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
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
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(StartupRegistryPath))
                {
                    if (key == null) { return; }
                    if (IsAutoStartEnabled())
                    {
                        key.DeleteValue(RegistryName, false);
                    }
                    else
                    {
                        key.SetValue(RegistryName, "\"" + Application.ExecutablePath + "\"");
                    }
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "设置开机启动失败：" + exception.Message, DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            RefreshMenuState();
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value);
            if (!value)
            {
                if (TrayIcon != null) { TrayIcon.Visible = true; }
            }
        }

        public bool IsClosing
        {
            get { return closing; }
        }

        // ---------------- 工作区（避开任务栏） ----------------

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MonitorInfoEx
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public int Flags;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Device;
        }

        /// <summary>
        /// 屏幕工作区（去掉任务栏）。
        ///
        /// 不能用 <see cref="Screen.PrimaryScreen"/>.WorkingArea：这个挂件在 app.manifest 里
        /// 声明了 PerMonitorV2，进程本身是 DPI 感知的，但 .NET Framework 4.x 的 Screen 类
        /// 走的是老接口，在 150% 缩放下会返回 2560×1440 那一套数字
        /// （实测高度 1400，真屏幕工作区是 2080），
        /// 于是"默认高度到任务栏上方 1 厘米"直接算错，面板只有真高度的三分之二。
        /// 改用 Win32 的 GetMonitorInfo，返回的一定是物理像素。
        /// </summary>
        protected virtual Rectangle PlacementArea
        {
            get
            {
                try
                {
                    IntPtr monitor = MonitorFromWindow(Handle, 2 /* MONITOR_DEFAULTTONEAREST */);
                    if (monitor != IntPtr.Zero)
                    {
                        MonitorInfoEx info = new MonitorInfoEx();
                        info.Size = Marshal.SizeOf(typeof(MonitorInfoEx));
                        if (GetMonitorInfo(monitor, ref info))
                        {
                            return new Rectangle(info.Work.Left, info.Work.Top,
                                info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
                        }
                    }
                }
                catch
                {
                }

                return Screen.PrimaryScreen.WorkingArea;
            }
        }

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
}
