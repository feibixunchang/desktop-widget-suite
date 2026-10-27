using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WeatherWidget48;

public sealed class MainForm : Form
{
    public static TimeSpan RefreshInterval => TimeSpan.FromMinutes(15);

    private static readonly int RefreshIntervalMilliseconds = (int)RefreshInterval.TotalMilliseconds;
    private readonly SettingsStore _settingsStore;
    private readonly WeatherApiClient _weatherApiClient;
    private readonly DismissibleContextMenuStrip _contextMenu;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _refreshMenuItem;
    private readonly ToolStripMenuItem _lockPositionMenuItem;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _fullscreenTimer = new();
    private bool _hiddenForFullscreen;

    /// <summary>被压到底层之前是不是置顶的，恢复 z 序时要用。</summary>
    private bool _wasTopMostBeforeFullscreen;
    private bool _desktopLayer;

    /// <summary>右键菜单是否正开着。开着时不能压 z 序，否则两者来回抢、窗口乱闪。</summary>
    private bool _menuOpen;
    private readonly Icon _appIcon;
    private AppSettings _settings;
    private Font? _firstLineFont;
    private Font? _secondLineFont;
    private CancellationTokenSource? _refreshCancellation;
    private string _firstLineText;
    private string _secondLineText;
    private ToolStripMenuItem? _versionMenuItem;
    private bool _isDragging;
    private bool _positionReady;
    private bool _isClosing;
    private bool _isRefreshing;
    private bool _refreshPending;
    private bool _pendingShowError;
    private int _locationVersion;
    private bool _hasWeather;
    private Point _dragOffset;
    private int _dragStartLeft;

    /// <summary>上一次真正画出来的内容指纹。内容没变就不必重建位图（拖动时尤其重要）。</summary>
    private string? _lastSurfaceSignature;
    private Size _lastSurfaceSize;

    /// <summary>用户横向拖动超过这个像素数才算"他要自己摆位置"。</summary>
    private const int HorizontalFreeThreshold = 2;

    public MainForm(SettingsStore? settingsStore = null, WeatherApiClient? weatherApiClient = null)
    {
        _settingsStore = settingsStore ?? new SettingsStore();
        _settings = _settingsStore.Load();
        // 必须在加载完立刻记下来：渲染过程中会往 _settings 里写当前位置
        // （宽度会随天气文字变），到 RestorePosition 时就分不清
        // "用户保存过位置"和"这是刚算出来的占位位置"了（这个坑我自己踩了一次，
        // 结果永远进不了居中模式，挂件停在离中线 771px 的地方）。
        _settings.TopMost = false;
        _weatherApiClient = weatherApiClient ?? new WeatherApiClient();
        _appIcon = AppIconProvider.Load();
        Icon = _appIcon;
        _firstLineText = $"{_settings.LocationName} | 正在获取天气…";
        _secondLineText = "七天天气预报加载中…";

        AutoScaleMode = AutoScaleMode.None;
        AutoSize = false;
        BackColor = Color.Black;
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(1, 1);
        Opacity = 1.0;
        Padding = Padding.Empty;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "桌面天气小挂件";
        TopMost = false;

        _contextMenu = new DismissibleContextMenuStrip
        {
            BackColor = Color.White,
            Font = FontProvider.Create(12),
            ForeColor = Color.Black,
            Renderer = new WhiteMenuRenderer(),
            ShowCheckMargin = true,
            ShowImageMargin = false
        };
        _refreshMenuItem = new ToolStripMenuItem("立即刷新（每15分钟自动刷新）");
        _refreshMenuItem.Click += async (_, _) => await RefreshWeatherAsync(true);
        _lockPositionMenuItem = new ToolStripMenuItem("锁定位置");
        _lockPositionMenuItem.Click += (_, _) => TogglePositionLock();
        var cityMenuItem = new ToolStripMenuItem("更换城市或地点…");
        cityMenuItem.Click += async (_, _) => await ChangeCityAsync();
          var appearanceMenuItem = new ToolStripMenuItem("字体与颜色…");
          appearanceMenuItem.Click += (_, _) => ChangeAppearance();

          // 背景透明度 0%~100%，每 10% 一档
          var opacityMenuItem = new ToolStripMenuItem("背景透明度") { Name = "opacityMenu" };
          for (var level = 0; level <= 100; level += 10)
          {
              var captured = level;
              var levelItem = new ToolStripMenuItem(level == 0 ? "0%（全透明，只有字）" : $"{level}%")
              {
                  Tag = $"opacity:{level}"
              };
              levelItem.Click += (_, _) =>
              {
                  _settings.OpacityPercent = captured;
                  SaveSettings();
                  RenderWidget();
              };
              opacityMenuItem.DropDownItems.Add(levelItem);
          }

          // 只保留"只在桌面显示"这一个模式开关。
          // "全屏窗口在前台时自动隐藏"已删除：桌面显示模式下本来就一直让位
          //（挂进桌面层，z 序由 explorer 托管），这个开关已经没有意义。
          // "置顶"也早已删除 —— 天气CPU 从来就是强制 TopMost=false。
          var desktopLayerItem = new ToolStripMenuItem("只在桌面显示（不压在程序上面）")
          {
              CheckOnClick = false,
              Tag = "desktopLayer"
          };
          desktopLayerItem.Click += (_, _) =>
          {
              _settings.DesktopLayer = !_settings.DesktopLayer;
              SaveSettings();
              if (_settings.DesktopLayer)
              {
                  // 挂接 + 摆位置（顺序有讲究：先屏幕坐标定位，再挂，再补一次定位）
                  RestorePositionForDesktopLayer();
              }
              else
              {
                  LeaveDesktopLayer();
                  RestorePosition();
              }

              SyncMenuState();
          };

        var centerMenuItem = new ToolStripMenuItem("回到默认位置（对齐屏幕垂直中线）");
        centerMenuItem.Click += (_, _) => ResetPosition();
        var centerXMenuItem = new ToolStripMenuItem("水平对齐屏幕中线（保留当前高度）");
        centerXMenuItem.Click += (_, _) => CenterHorizontally();
        var resetPositionMenuItem = new ToolStripMenuItem("移到主屏幕右下角");
        resetPositionMenuItem.Click += (_, _) => MoveToBottomRightCorner();
        var aboutMenuItem = new ToolStripMenuItem("关于…");
        aboutMenuItem.Click += (_, _) => ShowAbout();
        var exitMenuItem = new ToolStripMenuItem("退出程序");
        exitMenuItem.Click += (_, _) => Close();

        var showMenuItem = new ToolStripMenuItem("显示挂件");
        showMenuItem.Click += (_, _) => ShowWidget();
        _contextMenu.Items.Add(showMenuItem);
        _contextMenu.Items.Add(_refreshMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_lockPositionMenuItem);
          _contextMenu.Items.Add(cityMenuItem);
        _contextMenu.Items.Add(opacityMenuItem);
          _contextMenu.Items.Add(desktopLayerItem);
          _contextMenu.Items.Add(appearanceMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(centerMenuItem);
        _contextMenu.Items.Add(resetPositionMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(aboutMenuItem);
        _contextMenu.Items.Add(exitMenuItem);
        // 版本提示：菜单最下面这一行灰字就是当前版本号，一眼能确认是不是最新的
        _versionMenuItem = new ToolStripMenuItem(BuildVersionText())
        {
            Enabled = false
        };
        _contextMenu.Items.Add(_versionMenuItem);
            // 菜单弹出时**暂停压 z 序**：菜单是独立顶层窗口，弹出时被系统插到挂件前面，
        // 这边再压底就是两者来回抢，窗口会一闪一闪（用户实机反馈）。
        _contextMenu.Opening += (_, _) => { _menuOpen = true; SyncMenuState(); };
        _contextMenu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (_settings.DesktopLayer) { SendToBackForFullscreen(); }
        };
        ContextMenuStrip = _contextMenu;
        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = _contextMenu,
            Icon = _appIcon,
            Text = "桌面天气小挂件 " + BuildVersionNumber(),
            // WidgetHub 统一托盘：默认不显示挂件自带托盘图标（需要时启动参数加 --tray）
            Visible = false
        };
        _trayIcon.DoubleClick += (_, _) => ShowWidget();

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = RefreshIntervalMilliseconds
        };
        _refreshTimer.Tick += async (_, _) => await RefreshWeatherAsync(false);

        // 每秒看一眼有没有全屏/最大化窗口在前台，有就自动让位。
        // 这个挂件是分层窗口（UpdateLayeredWindow 画的），在 z 序里天然浮在普通窗口
        // 之上，TopMost=false 也拦不住，所以只能靠"检测 + 隐藏"，
        // 否则会一直压在最大化窗口上面。
        _fullscreenTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _fullscreenTimer.Tick += (_, _) =>
        {
            PollFullscreen();
            // 顺带做位置自检：系统偶尔会把窗口挪走（DPI 变化、隐藏再显示、分辨率变化），
            // 表现为"锁定了位置也会慢慢往左漂"（用户反馈过）。
            EnsurePosition();
        };

        CreateFonts();
        RenderWidget();
        MouseDown += WidgetMouseDown;
        MouseMove += WidgetMouseMove;
        MouseUp += WidgetMouseUp;
        MouseDoubleClick += WidgetMouseDoubleClick;
        Shown += OnWidgetShown;
        FormClosing += OnWidgetClosing;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= LayeredWindow.WsExLayered | LayeredWindow.WsExToolWindow | LayeredWindow.WsExNoActivate;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);

        // 用户要的是"只在桌面看见挂件，不要压在别的程序上面"。
        //
        // ⚠️ 挂桌面层有个**坐标系陷阱**：SetParent 之后窗口变成子窗口，
        // `Location` / `Top` / `Left` 的含义从**屏幕坐标变成父窗口客户区坐标**。
        // 如果先按屏幕坐标设好位置、再挂进去，位置会被父窗口原点（0,0，即屏幕左上角）
        // 重新解释 —— 天气挂件就从"屏幕下方"跳到了"屏幕顶部"（用户实机反馈过）。
        //
        // 所以顺序必须是：**先按屏幕坐标定位 → 再挂进去 → 挂完重新定位一次**。
        if (_settings.DesktopLayer)
        {
            RestorePositionForDesktopLayer();
        }

        RenderWidget();
    }

    /// <summary>
    /// 桌面显示模式：把挂件压到普通窗口之下。
    /// 不再 SetParent 挂进 explorer 内部（那条路会让主窗口 vis=False，挂件凭空消失），
    /// 所以没有坐标系转换问题，位置直接按屏幕坐标处理。
    /// </summary>
    private void RestorePositionForDesktopLayer()
    {
        // ⚠️ 定位期间**必须**置 _positionReady=false。
        // RestorePosition() 内部会 RenderWidget()，而渲染会写
        // `_settings.WindowX/Y = Left/Top`。在 OnHandleCreated 阶段窗口尺寸还没定，
        // 第一次渲染算出来的位置是错的（实测 Top=0 = 屏幕顶部），
        // 随后被 SaveSettings 存进配置文件，于是**每次启动都跑到屏幕顶部，再也回不来**。
        // 这个坑就是"桌面天气小挂件跑到屏幕顶部"的真正原因。
        bool wasReady = _positionReady;
        _positionReady = false;
        try
        {
            RestorePosition();
        }
        finally
        {
            _positionReady = wasReady;
        }

        SendToBackForFullscreen();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        if (IsHandleCreated && !_isClosing)
        {
            RenderWidget();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshTimer.Dispose();

            _fullscreenTimer.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _appIcon.Dispose();
            _contextMenu.Dispose();
            _firstLineFont?.Dispose();
            _secondLineFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void OnWidgetShown(object? sender, EventArgs eventArgs)
    {
        RestorePosition();
        RenderWidget();
        _refreshTimer.Start();
        await RefreshWeatherAsync(false);
    }

    private void OnWidgetClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        _isClosing = true;
        _refreshTimer.Stop();
        _refreshCancellation?.Cancel();
        SaveSettings();
    }

    private void CreateFonts()
    {
        _firstLineFont?.Dispose();
        _secondLineFont?.Dispose();
        _firstLineFont = FontProvider.Create(_settings.FirstLineFontSize);
        _secondLineFont = FontProvider.Create(_settings.SecondLineFontSize);
        // 字体换了，缓存的内容指纹必须作废，否则会认为"内容没变"而跳过重绘
        _lastSurfaceSignature = null;
    }

    private void RenderWidget()
    {
        if (_isClosing || _firstLineFont is null || _secondLineFont is null)
        {
            return;
        }

        // ⚠️ 内容没变就别重画。
        // 以前每收到一次 WM_MOUSEMOVE（拖动时每秒几十次）都重建一次位图 +
        // 逐像素 alpha 预乘 + UpdateLayeredWindow，既卡又抖。
        var signature = string.Join(
            "\u0001",
            Handle.ToInt64().ToString(),
            _firstLineText,
            _secondLineText,
            _settings.FirstLineFontSize.ToString(),
            _settings.SecondLineFontSize.ToString(),
            _settings.FirstLineColor,
            _settings.SecondLineColor,
            _settings.OpacityPercent.ToString());
        if (signature == _lastSurfaceSignature && ClientSize == _lastSurfaceSize)
        {
            return;
        }

        using var surface = WidgetSurface.Create(
            _firstLineText,
            _secondLineText,
            _firstLineFont,
            ParseColor(_settings.FirstLineColor),
            _secondLineFont,
            ParseColor(_settings.SecondLineColor),
            (int)Math.Round(Compat.Clamp(_settings.OpacityPercent, 0, 100) * 2.55));
        if (ClientSize != surface.Bitmap.Size)
        {
            // ⚠️ 窗口宽度是跟着天气文字长度变的（"多云" ↔ "小到中雨"）。
            // 改宽度时如果不管位置，挂件就会一点点往左漂 ——
            // 用户反馈"锁定位置以后过一段时间还是往左移，每次都得手动往右挪"。
            // 默认模式下锁住**中心**：宽度变了中心不动，位置就不会漂。
            // 用户自己横向拖过之后（FreeHorizontalPosition）则锁左边：他觉得放哪就待在哪。
            //
            // ⚠️ `_positionReady` 这个前提不能少：构造函数里先 `ClientSize = 1x1`、位置 (0,0)，
            // 而且 `OnResize` 会回头再调 RenderWidget()，此时真正的位置还没恢复。
            // 这时候就去"锁中心"，会拿 (0,0) 当基准把窗口算到屏幕外
            // （实测存成 -771，每次启动都被拉到左边）—— 这个坑我自己踩了两次。
            if (_positionReady && Width > 1 && !_isDragging)
            {
                var keepCenterX = Left + Width / 2;
                var keepLeft = Left;
                ClientSize = surface.Bitmap.Size;
                var wantedLeft = _settings.FreeHorizontalPosition
                    ? keepLeft
                    : keepCenterX - Width / 2;
                wantedLeft = ClampLeftToScreen(wantedLeft);
                if (Left != wantedLeft)
                {
                    Location = new Point(wantedLeft, Top);
                }

                // 只更新内存里的位置，不在这里存盘：
                // 存盘交给正常的保存路径，避免"渲染顺手改设置"。
                if (_positionReady)
                {
                    _settings.WindowX = Left;
                    _settings.WindowY = Top;
                }
            }
            else
            {
                ClientSize = surface.Bitmap.Size;
            }

            _lastSurfaceSignature = signature;
            _lastSurfaceSize = surface.Bitmap.Size;
            return;
        }


        if (IsHandleCreated)
        {
            LayeredWindow.Update(Handle, surface.Bitmap, Location);
        }

        _lastSurfaceSignature = signature;
        _lastSurfaceSize = surface.Bitmap.Size;

        // 画完再居中：这一刻 ClientSize 已经是真实宽度了
        ApplyCenteredX();
    }

    private async Task RefreshWeatherAsync(bool showError)
    {
        if (_isRefreshing)
        {
            _refreshPending = true;
            _pendingShowError |= showError;
            return;
        }

        if (_isClosing)
        {
            return;
        }

        _isRefreshing = true;
        _refreshMenuItem.Enabled = false;
        _refreshMenuItem.Text = "正在刷新…";
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        var refreshCancellation = new CancellationTokenSource();
        _refreshCancellation = refreshCancellation;
        var location = _settings.ToGeoLocation();
        var locationVersion = _locationVersion;
        try
        {
            var weather = await _weatherApiClient.GetWeatherAsync(location, refreshCancellation.Token);
            if (_isClosing || locationVersion != _locationVersion)
            {
                return;
            }

            _firstLineText = WeatherFormatter.FormatCurrentLine(weather);
            _secondLineText = WeatherFormatter.FormatForecastLine(weather);
            _hasWeather = true;
            RenderWidget();
            _refreshTimer.Stop();
            _refreshTimer.Start();
        }
        catch (OperationCanceledException) when (refreshCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_isClosing || locationVersion != _locationVersion)
            {
                return;
            }

            try
            {
                var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeatherWidget48", "weather-error.log");
                System.IO.File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss") + " " + exception + Environment.NewLine + Environment.NewLine);
            }
            catch { }

            if (!_hasWeather)
            {
                _firstLineText = $"{location.Name} | 天气获取失败";
                _secondLineText = "请检查网络后右键选择“立即刷新”";
                RenderWidget();
            }

            if (showError)
            {
                AppMessageBox.Show(
                    this,
                    $"天气刷新失败：{exception.Message}",
                    "桌面天气小挂件",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, refreshCancellation))
            {
                _refreshCancellation = null;
            }

            refreshCancellation.Dispose();
            _isRefreshing = false;
            if (!_isClosing)
            {
                _refreshMenuItem.Enabled = true;
                _refreshMenuItem.Text = "立即刷新（每15分钟自动刷新）";
            }

            if (_refreshPending && !_isClosing)
            {
                var nextShowError = _pendingShowError;
                _refreshPending = false;
                _pendingShowError = false;
                await RefreshWeatherAsync(nextShowError);
            }
        }
    }

    private async Task ChangeCityAsync()
    {
        using var dialog = new CitySearchDialog(_weatherApiClient, _settings.ToGeoLocation());
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedLocation is null)
        {
            return;
        }

        var location = dialog.SelectedLocation;
        _locationVersion++;
        _settings.LocationName = location.Name;
        _settings.Admin1 = location.Admin1;
        _settings.Admin2 = location.Admin2;
        _settings.Country = location.Country;
        _settings.Latitude = location.Latitude;
        _settings.Longitude = location.Longitude;
        _settings.TopMost = false;
        _hasWeather = false;
        _refreshCancellation?.Cancel();
        _firstLineText = $"{location.Name} | 正在获取天气…";
        _secondLineText = "七天天气预报加载中…";
        RenderWidget();
        SaveSettings();
        _refreshTimer.Stop();
        _refreshTimer.Start();
        await RefreshWeatherAsync(true);
    }

    private void ChangeAppearance()
    {
        using var dialog = new AppearanceDialog(_settings);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settings = dialog.SelectedSettings;
        _settings.TopMost = false;
        CreateFonts();
        RenderWidget();
        SaveSettings();
        EnsureWindowVisible();
    }

    private void TogglePositionLock()
    {
        _settings.PositionLocked = !_settings.PositionLocked;
        SaveSettings();
        SyncMenuState();
    }

    private void ResetPosition()
    {
        _settings.FreeHorizontalPosition = false;
        Location = DefaultLocation();
        RenderWidget();
        SaveSettings();
    }

    /// <summary>备用动作：硬贴到主屏幕右下角（不是默认位置）。</summary>
    private void MoveToBottomRightCorner()
    {
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.AllScreens[0].WorkingArea;
        _settings.FreeHorizontalPosition = true;
        Location = new Point(
            Math.Max(workingArea.Left, workingArea.Right - Width - 20),
            Math.Max(workingArea.Top, workingArea.Bottom - Height - 20));
        RenderWidget();
        SaveSettings();
        SyncMenuState();
    }

    /// <summary>
    /// 默认位置 = **水平居中在屏幕垂直中线** + 底边距工作区下边缘 <see cref="DefaultBottomGap"/>。
    ///
    /// 这两个数都是按用户摆出来的布局定的：
    /// 天气CPU 自己贴任务栏（高 51px，顶部在工作区下边缘往上 51px 处），
    /// 天气挂件在它**上方 4px**（用户指定）⇒ 底边距工作区下边缘 51 + 4 = 55。
    /// 两个挂件的中心都正好压在屏幕垂直中线上（3840 屏 ⇒ x=1920）。
    ///
    /// 水平位置不写死像素：宽度会随天气文字变（"多云" ↔ "小到中雨"），
    /// 所以是每次渲染完按当前宽度重算一次中心（见 ApplyCenteredX）。
    /// </summary>
    // 天气CPU 贴任务栏（高 51px）⇒ 底边距工作区下边缘 51 + 4（间距）= 55。
    // 注意：行间距 2→4 以后天气挂件本身高了 2px（88→90），这个数是按 90px 高算的。
    private const int DefaultBottomGap = 55;

    private Point DefaultLocation()
    {
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.AllScreens[0].WorkingArea;
        var line = workingArea.Left + workingArea.Width / 2;
        var x = line - Width / 2;
        if (x < workingArea.Left) { x = workingArea.Left; }
        if (x + Width > workingArea.Right) { x = Math.Max(workingArea.Left, workingArea.Right - Width); }
        var y = workingArea.Bottom - Height - DefaultBottomGap;
        if (y < workingArea.Top) { y = workingArea.Top; }
        return new Point(x, y);
    }

    /// <summary>
    /// 水平居中到屏幕垂直中线，纵向保持不动。
    /// （菜单上的"回到默认位置"走的是 <see cref="ResetPosition"/>，那个会连纵向一起复位。）
    /// </summary>
    private void CenterHorizontally()
    {
        _settings.FreeHorizontalPosition = false;
        var workingArea = Screen.FromRectangle(Bounds).WorkingArea;
        var line = workingArea.Left + workingArea.Width / 2;
        Location = new Point(line - Width / 2, Top);
        RenderWidget();
        SaveSettings();
        SyncMenuState();
    }


    /// <summary>
    /// 位置自检：系统偶尔会把窗口挪走（DPI 变化、隐藏再显示、分辨率变化），
    /// 表现为"锁定了位置也会慢慢往左漂"。每秒核对一次真实窗口位置，
    /// 和设定位置不一致就拉回去（跟共享基类的 SurfaceOutOfPlace 同一个思路）。
    /// </summary>
    private void EnsurePosition()
    {
        if (_isClosing || !IsHandleCreated || !Visible || _isDragging)
        {
            return;
        }

        // 水平：默认模式永远居中在屏幕垂直中线（每次按**当前**宽度重算）；
        //       用户自己横向拖过之后（FreeHorizontalPosition）就**不再动他的位置**，
        //       只保证窗口还在屏幕里。
        // 纵向：用户拖到哪就记到哪，被系统挪走了就拉回去。
        if (_settings.FreeHorizontalPosition)
        {
            var clampedLeft = ClampLeftToScreen(Left);
            if (clampedLeft != Left)
            {
                Location = new Point(clampedLeft, Top);
            }
        }
        else
        {
            ApplyCenteredX();
        }

        if (_settings.WindowY.HasValue && Top != _settings.WindowY.Value)
        {
            Location = new Point(Left, _settings.WindowY.Value);
        }

        if (Left != _settings.WindowX || Top != _settings.WindowY)
        {
            _settings.WindowX = Left;
            _settings.WindowY = Top;
            SaveSettings();
        }
    }

    /// <summary>把一个左上角 X 夹到"至少还露在屏幕里"的范围（多屏用虚拟桌面范围）。</summary>
    private int ClampLeftToScreen(int wantedLeft)
    {
        var bounds = SystemInformation.VirtualScreen;
        var minimumVisible = Math.Min(80, Math.Max(24, Width));
        var min = bounds.Left - Width + minimumVisible;
        var max = bounds.Right - minimumVisible;
        if (wantedLeft < min)
        {
            return min;
        }

        return wantedLeft > max ? max : wantedLeft;
    }

    /// <summary>
    /// 把挂件水平居中到屏幕垂直中线（和天气CPU 用同一条线，两者左右对称）。
    ///
    /// ⚠️ 必须**每次都按当前宽度重算**，不能在启动时算一次：
    /// 挂件宽度跟着天气文字变（"多云" ↔ "小到中雨"），刚启动时宽度还是占位值，
    /// 那时算出来的中心会偏 771px（这个坑我踩了三次，用户三次看到"没居中"）。
    /// 所以在 RenderWidget 画完之后调它，那时宽度才是真的。
    ///
    /// ⚠️ 拖动中**绝不能**调用：拖一下就被拉回中线 = 用户看到的"乱颤"。
    /// </summary>
    private void ApplyCenteredX()
    {
        if (_isClosing || !IsHandleCreated || _isDragging)
        {
            return;
        }

        if (_settings.FreeHorizontalPosition)
        {
            return;
        }

        var workingArea = Screen.FromRectangle(Bounds).WorkingArea;
        int wanted = workingArea.Left + workingArea.Width / 2 - Width / 2;
        if (wanted < workingArea.Left) { wanted = workingArea.Left; }
        if (wanted + Width > workingArea.Right) { wanted = Math.Max(workingArea.Left, workingArea.Right - Width); }
        if (Left != wanted) { Location = new Point(wanted, Top); }
    }


    private static string BuildVersionNumber()
    {
        var version = typeof(MainForm).Assembly.GetName().Version;
        return version == null
            ? "1.0.2"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string BuildVersionText()
    {
        return $"版本 {BuildVersionNumber()}（当前最新）";
    }

    private void ShowWidget()
    {
        if (_isClosing)
        {
            return;
        }

        Show();
        WindowState = FormWindowState.Normal;
        EnsureWindowVisible();
    }

    private void ShowAbout()
    {
        var fontStatus = FontProvider.IsRequestedFamilyAvailable
            ? "方正宋黑_GBK"
            : $"方正宋黑_GBK 未安装，当前使用 {FontProvider.FamilyName}";
        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "1.0.1";
        AppMessageBox.Show(
            this,
            $"桌面天气小挂件 {version}\n\n默认地点：北京市\n天气与地点搜索：Open-Meteo 免费公共 API\n自动刷新：每15分钟\n普通桌面窗口，不置顶\n统一字体：{fontStatus}\n菜单自动关闭：{_contextMenu.AutoCloseDelay.TotalSeconds:F0} 秒无操作，或点击菜单外、按下 ESC\n\n右键菜单或托盘菜单可更换地点、调整显示、锁定位置、开机自启或退出。",
            "关于",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void WidgetMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left || _settings.PositionLocked)
        {
            return;
        }
        _isDragging = true;
        _dragStartLeft = Left;
        _dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
        Capture = true;
    }

    /// <summary>
    /// 拖动：**只挪窗口**，不重画、不居中。
    ///
    /// 以前每次 WM_MOUSEMOVE 都调 RenderWidget()，而 RenderWidget 末尾会
    /// ApplyCenteredX() 把挂件拉回屏幕中线 —— 用户横向拖时窗口被"拖走 → 拉回 → 拖走"
    /// 每秒几十次来回抢，看起来就是**乱颤**，而且永远拖不到想去的地方
    /// （结果天气挂件一直压在天气CPU 那条上面，两行字叠在一起）。
    /// </summary>
    private void WidgetMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        if (!_isDragging || _settings.PositionLocked)
        {
            return;
        }

        var target = new Point(Cursor.Position.X - _dragOffset.X, Cursor.Position.Y - _dragOffset.Y);
        target.X = ClampLeftToScreen(target.X);

        // 横向真的动了 ⇒ 尊重用户的选择，从此不再自动居中
        if (!_settings.FreeHorizontalPosition
            && Math.Abs(target.X - _dragStartLeft) > HorizontalFreeThreshold)
        {
            _settings.FreeHorizontalPosition = true;
        }

        if (target != Location)
        {
            Location = target;
        }
    }

    private void WidgetMouseUp(object? sender, MouseEventArgs eventArgs)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        Capture = false;
        // 落点必须立刻记进设置：不记的话 1 秒后的位置自检会拿旧坐标把它拽回去
        _settings.WindowX = Left;
        _settings.WindowY = Top;
        SaveSettings();
        SyncMenuState();
    }

    private async void WidgetMouseDoubleClick(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left)
        {
            await RefreshWeatherAsync(true);
        }
    }

    private void RestorePosition()
    {
        if (_settings.WindowX.HasValue && _settings.WindowY.HasValue)
        {
            Location = new Point(_settings.WindowX.Value, _settings.WindowY.Value);
            EnsureWindowVisible();
        }
        else
        {
            // 没有保存过位置 ⇒ 进入"水平居中"模式：
            // 以后每次都按当前宽度重算中线上的位置（宽度会随天气文字变）。
            ResetPosition();
        }

        RenderWidget();
    }

    private void EnsureWindowVisible()
    {
        var bounds = new Rectangle(Location, ClientSize);
        var isVisible = Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds));
        if (!isVisible)
        {
            ResetPosition();
        }

        RenderWidget();
    }

    private void SaveSettings()
    {
        // ⚠️ 位置还没恢复完（OnHandleCreated 阶段、窗口尺寸未定）时**不要**写位置，
        // 否则会把中间态的坐标（常见是 Top=0，也就是屏幕顶部）存进配置文件，
        // 之后每次启动都被这个错值锁死。菜单触发和拖动结束时的保存不受影响。
        if (_positionReady)
        {
            _settings.WindowX = Left;
            _settings.WindowY = Top;
        }

        _settings.TopMost = false;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            if (!_isClosing)
            {
                AppMessageBox.Show(
                    this,
                    $"无法保存设置：{exception.Message}",
                    "桌面天气小挂件",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }

    private void SyncMenuState()
      {
          _lockPositionMenuItem.Checked = _settings.PositionLocked;
          foreach (ToolStripItem entry in _contextMenu.Items)
          {
              if (entry is not ToolStripMenuItem parent)
              {
                  continue;
              }

              switch (parent.Tag as string)
              {
                  case "desktopLayer":
                      parent.Checked = _settings.DesktopLayer;
                      break;
              }

              if (parent.Name != "opacityMenu")
              {
                  continue;
              }

              foreach (ToolStripItem child in parent.DropDownItems)
              {
                  if (child is ToolStripMenuItem level && level.Tag is not null)
                  {
                      level.Checked = string.Equals(
                          level.Tag.ToString(),
                          $"opacity:{_settings.OpacityPercent}",
                          StringComparison.Ordinal);
                  }
              }
          }

      }

    private static Color ParseColor(string color)
    {
        return ColorHtml.TryParse(color, out var parsed) ? parsed : Color.Black;
    }

    /// <summary>
    /// 挂到桌面图标层（SHELLDLL_DefView），并排到图标列表 SysListView32 **之上**。
    /// 返回是否真的挂上了 —— 挂不上时窗口仍是顶层，调用方要自己处理退路。
    /// </summary>
    private static IntPtr _cachedDesktopHost = IntPtr.Zero;

    private bool AttachToDesktopLayer()
    {
        // 桌面层挂接这条路在本机走不通（ResolveDesktopHost 里列了所有试过的宿主：
        // SHELLDLL_DefView 内部、壁纸层 WorkerW 都会让主窗口 vis=False，挂了件凭空消失，
        // 而且 IsWindowVisible 查不出来）。所以不挂，改用等效方案：
        // 保持顶层窗口、永远不置顶、压在普通窗口之下。
        // 观感和"贴在桌面上"一样，且挂件永远可见。
        _refreshTimer.Start();
        SendToBackForFullscreen();
        _desktopLayer = true;
        _hiddenForFullscreen = true;
        _settings.DesktopLayer = true;
        SaveSettings();
        return true;
    }

    private static IntPtr ResolveDesktopHost()
    {
        if (_cachedDesktopHost != IntPtr.Zero && IsWindowVisible(_cachedDesktopHost))
        {
            return _cachedDesktopHost;
        }

        var progman = FindWindowW("Progman", null);
        if (progman == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr spawn;
        SendMessageTimeoutW(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out spawn);

        // ⚠️ 宿主选 **Progman 下面那个可见的 WorkerW（壁纸层）**。
        // 本机实测：Progman(可见) 下挂着 SHELLDLL_DefView(图标层) 和 WorkerW(壁纸层, 可见全屏)。
        // 挂到壁纸层 = 压在壁纸之上、所有程序窗口之下，正是要的效果。
        //
        // 试过但**不行**的：
        //   * SHELLDLL_DefView 内部 —— explorer 不接受外部子窗口进图标视图，
        //     实测挂上去主窗口 vis=False，挂件直接消失；
        //   * Progman 本体 —— 被同级的 SHELLDLL_DefView 全屏盖住。
        IntPtr host = IntPtr.Zero;
        var wall = FindWindowExW(progman, IntPtr.Zero, "WorkerW", null);
        while (wall != IntPtr.Zero)
        {
            if (IsWindowVisible(wall)) { host = wall; break; }
            wall = FindWindowExW(progman, wall, "WorkerW", null);
        }

        // 退而求其次：顶层 WorkerW 里可见且不含图标层的那些
        if (host == IntPtr.Zero)
        {
            var walk = FindWindowExW(IntPtr.Zero, IntPtr.Zero, "WorkerW", null);
            while (walk != IntPtr.Zero)
            {
                if (IsWindowVisible(walk) && FindWindowExW(walk, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                {
                    host = walk;
                    break;
                }
                walk = FindWindowExW(IntPtr.Zero, walk, "WorkerW", null);
            }
        }

        // 都找不到就返回 0 表示"别挂"，调用方保持顶层窗口（退路可用）
        if (host == IntPtr.Zero || !IsWindowVisible(host))
        {
            _cachedDesktopHost = IntPtr.Zero;
            return IntPtr.Zero;
        }

        _cachedDesktopHost = host;
        return _cachedDesktopHost;
    }

    private void LeaveDesktopLayer()
    {
        SetParent(Handle, IntPtr.Zero);
        _fullscreenTimer.Start();
        _refreshTimer.Start();
    }

    private void PollFullscreen()
    {
        if (_isClosing || !IsHandleCreated)
        {
            return;
        }

        // ⚠️ 不要用 Hide()/Show()：这个窗口是分层窗口(WS_EX_LAYERED)，画面靠
        // UpdateLayeredWindow 逐帧送上去。Show() 会让系统认为窗口"刚出现"，
        // 之前送上去的分层画面作废，得等下一帧重画 —— 中间那一瞬是空白/残影，
        // **这就是退出全屏时"闪一下"的根源**。

        // "只在桌面显示"模式：压到普通窗口之下。
        // 菜单开着的时候**绝对不能压** —— 菜单会被系统插到挂件前面，
        // 这边再压就是来回抢，窗口一闪一闪（用户实机反馈）。
        // 用户要的就是"只在桌面看见挂件"，任何普通窗口都该在挂件上面。
        // 可见性自始至终不变，所以不会闪。
        //
        // ⚠️ 必须**每秒都压**，不能只在第一次压。
        // 别人每开一个新窗口，系统都会把它插到非置顶层最前，挂件就被顶到它后面去了
        // ——不对，是挂件被顶到它**前面**（挂件 z 序比新窗口高）。
        // 只压一次的话，第二个窗口打开时挂件就已经不对了。
        if (_settings.DesktopLayer)
        {
            if (Visible && !_menuOpen)
            {
                SendToBackForFullscreen();
            }
            return;
        }

        // 菜单里手动取消"桌面层"之后，才回到老逻辑：检测全屏才让位
        if (!(_settings.HideOnFullscreen ?? true))
        {
            return;
        }

        if (IsFullscreenWindowInFront() && !_hiddenForFullscreen && Visible)
        {
            _hiddenForFullscreen = true;
            SendToBackForFullscreen();
            return;
        }

        if (!IsFullscreenWindowInFront() && _hiddenForFullscreen)
        {
            _hiddenForFullscreen = false;
            RestoreZOrderAfterFullscreen();
        }
    }

    /// <summary>
    /// 压到"桌面壁纸之上、所有程序窗口之下"，不碰可见性，分层画面一帧都不丢。
    ///
    /// ⚠️ 两次踩坑，别再改回去：
    /// 1. **不能用 HWND_BOTTOM 单步** —— 会压到桌面壁纸之下，把整个桌面盖黑。
    /// 2. **不能用 hWndInsertAfter = Progman** —— Progman 是顶层窗口，插在它旁边
    ///    仍参与正常 z 序排序，结果照样压在浏览器上面。
    /// 正确做法：先 `HWND_NOTOPMOST` 明确进非置顶层，再压到该层内部最底。
    /// </summary>
      private void SendToBackForFullscreen()
      {
          if (_isClosing || !IsHandleCreated) return;

          // ⚠️ 已经压好就别再压。SetWindowPos 即使顺序没变也会引发 z 序通知 + 重绘，
          // 每秒无条件调一次 = 每秒闪一次（用户实机反馈"右键点完再点，窗口就闪"）。
          if (!NeedsSendToBack()) return;

          try
          {
              _wasTopMostBeforeFullscreen = TopMost;   // 记下来，恢复时要用

              // 第一步：明确丢进非置顶层
              if (TopMost) TopMost = false;
              SetWindowPos(Handle, HwndNotTopMost, 0, 0, 0, 0,
                  SwpNoMove | SwpNoSize | SwpNoActivate);

              // 第二步：压到非置顶层内部最底（不会掉到桌面壁纸之下）
              SetWindowPos(Handle, HwndBottom, 0, 0, 0, 0,
                  SwpNoMove | SwpNoSize | SwpNoActivate);
          }
          catch
          {
              // z 序失败也不能让挂件消失，下一轮轮询会再试
          }
      }

      /// <summary>
      /// 挂件是不是已经被别的普通窗口插到前面去了。
      /// 沿 z 序往下看：只要下面还有一个"该被盖住"的普通窗口，就需要压回去。
      /// 已经待在最底时返回 false，调用方就不动它 —— 避免无意义的重绘和闪烁。
      /// </summary>
      private bool NeedsSendToBack()
      {
          if (TopMost) return true;

          // ⚠️ 不要用"从自己往下找有没有普通窗口"来判断 —— 实测会漏判：
          // 全屏视频窗口（VLC/PotPlayer 等）常带 WS_EX_TOOLWINDOW，会被判成
          // "下面没有需要让位的窗口" → 不压 → **全屏播放时挂件浮在画面上**。
          //
          // 可靠判据：从 HWND_TOP 往下走到自己，中间有别的进程普通窗口 => 自己被插到前面了。
          var walk = GetTopWindow(IntPtr.Zero);
          var myPid = (uint)(uint)Process.GetCurrentProcess().Id;
          for (var guard = 0; walk != IntPtr.Zero && guard < 256; guard++)
          {
              if (walk == Handle) return false;   // 走到自己 => 前面没人 => 已贴桌面

              if (IsWindowVisible(walk))
              {
                  uint pid;
                  GetWindowThreadProcessId(walk, out pid);
                  if (pid != myPid)
                  {
                      var cls = new System.Text.StringBuilder(256);
                      GetClassNameW(walk, cls, cls.Capacity);
                      var name = cls.ToString();
                      if (name != "Progman" && name != "WorkerW" && name != "SHELLDLL_DefView")
                      {
                          var ex = GetWindowLongW(walk, -20);
                          if ((ex & 0x80) == 0) return true;   // 有别的进程普通窗口在前面
                      }
                  }
              }

              walk = GetWindow(walk, 2 /* GW_HWNDPREV */);
          }

          return true;   // 走到底没遇到自己 => 保守压一次
      }

    /// <summary>
    /// 全屏窗口走了，把 z 序恢复原样。
    /// ⚠️ 光设 TopMost=false 不够：SendToBack 压到了 HWND_BOTTOM（全局最底），
    /// 非置顶窗口不会因为 TopMost 变化就自动回到前面，挂件会一直卡在所有普通窗口底下
    /// （实测踩到过：全屏关掉后挂件被浏览器压着看不见）。必须显式提到 HWND_TOP。
    /// 仍然不碰可见性，所以不会闪。
    /// </summary>
      private void RestoreZOrderAfterFullscreen()
      {
          if (_isClosing || !IsHandleCreated) return;

          // 桌面显示模式下**永远不置顶**：这个函数只在"用户手动关掉只在桌面显示"之后才会被走到，
          // 但加一道保险，免得以后又被别处调用、把挂件顶到所有程序上面。
          if (_settings.DesktopLayer) { SendToBackForFullscreen(); return; }

          try
          {
              if (_wasTopMostBeforeFullscreen || _settings.TopMost)
              {
                  TopMost = true;
              }
              else
              {
                  if (TopMost) TopMost = false;
                  SetWindowPos(Handle, HwndTop, 0, 0, 0, 0,
                      SwpNoMove | SwpNoSize | SwpNoActivate);
              }
          }
          catch
          {
          }
      }

    /// <summary>
    /// 屏幕上是否存在一个"盖住 92% 以上、不是工具窗口、也不是桌面外壳"的窗口。
    /// 要排除工具窗口和外壳窗口：鼠标拖尾、桌面增强这类整屏浮层常年是 topmost 工具窗，
    /// "Windows 输入体验"这种铺满全屏的透明外壳窗口也不算别人在干正事。
    /// </summary>
    private static bool IsFullscreenWindowInFront()
    {
        var screen = Screen.PrimaryScreen?.Bounds;
        if (screen is null || screen.Value.Width <= 0)
        {
            return false;
        }

        double screenArea = (double)screen.Value.Width * screen.Value.Height;
        IntPtr walk = GetTopWindow(IntPtr.Zero);
        uint myPid = (uint)(uint)Process.GetCurrentProcess().Id;
        while (walk != IntPtr.Zero)
        {
            GetWindowThreadProcessId(walk, out var pid);
            if (pid != myPid && IsWindowVisible(walk) && !IsShellOverlay(walk))
            {
                var style = GetWindowLongW(walk, GwlExStyle);
                bool toolWindow = (style & WsExToolWindow) != 0;
                if (!toolWindow && GetWindowRect(walk, out var area))
                {
                    double windowArea = (double)(area.Right - area.Left) * (area.Bottom - area.Top);
                    if (windowArea >= screenArea * 0.92)
                    {
                        return true;
                    }
                }
            }

            walk = GetWindow(walk, GwHwndPrev);
        }

        return false;
    }

    private static bool IsShellOverlay(IntPtr handle)
    {
        var builder = new System.Text.StringBuilder(64);
        GetClassNameW(handle, builder, 64);
        var cls = builder.ToString();
        return cls is "Progman" or "WorkerW" or "Windows.UI.Core.CoreWindow" or "ShellHandwritingCanvas"
            || cls.StartsWith("Wnd_StartFeed", StringComparison.Ordinal);
    }

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;
    private const uint GwHwndPrev = 2;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>z 序正常顶部（HWND_TOP = 0）。</summary>
    private static readonly IntPtr HwndTop = new(0);

    /// <summary>非置顶层顶部（HWND_NOTOPMOST = -2）。</summary>
    private static readonly IntPtr HwndNotTopMost = new(-2);

    /// <summary>
    /// z 序最底（HWND_BOTTOM = 1）。
    /// ⚠️ 这里原来写的是 <c>new(-1)</c>，那是 **HWND_TOPMOST**（置顶），不是底部！
    /// 名字叫 HwndBottom 值却是 TOPMOST，桌面层 AttachToDesktopLayer 和全屏压底都在用，
    /// 等于"挂到桌面层"实际把自己顶到最前面，行为完全反了。
    /// </summary>
    private static readonly IntPtr HwndBottom = new(1);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string className, string windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string className, string windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);


    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int index, int newLong);

    /// <summary>32/64 位都兼容：64 位进程要用 SetWindowLongPtr，索引传 -16/-20 即可。</summary>
    private static IntPtr SetWindowLongW(IntPtr hWnd, int index, IntPtr newLong)
        => IntPtr.Size == 8 ? SetWindowLongPtr(hWnd, index, newLong) : new IntPtr(SetWindowLong32(hWnd, index, newLong.ToInt32()));

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLongW(IntPtr hWnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
