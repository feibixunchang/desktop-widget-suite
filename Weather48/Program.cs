namespace WeatherWidget48;

internal static class Program
{
    private const string MutexName = @"Local\WeatherWidget48.Widget";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            AppMessageBox.Show(null, "桌面天气小挂件已在运行。", "桌面天气小挂件", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        EnableDpiAwareness();
        // net48 上 ServicePointManager 有可能只协商到 TLS 1.0/1.1，而 open-meteo 只接受
        // TLS 1.2+。表现就是"天气获取失败 / 未能与 SSL/TLS 安全通道建立信任关系"，
        // 而且时好时坏（取决于中间代理怎么应答）。天气CPU 挂件早就显式打开了现代 TLS，
        // 所以它的天气一直能出、这个挂件却老失败 —— 这里补齐。
        EnableModernTls();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ShowException(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                ShowException(exception);
            }
        };

        Application.Run(new MainForm());
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    // net48 没有 Application.SetHighDpiMode，改用系统 API 声明 PerMonitorV2
    private static void EnableDpiAwareness()
    {
        try
        {
            // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
            if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
        }
        catch { }
        try { SetProcessDPIAware(); } catch { }
    }

    private static void EnableModernTls()
    {
        try
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        }
        catch { }
        try
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls13;
        }
        catch { }
    }

    private static void ShowException(Exception exception)
    {
        AppMessageBox.Show(
            null,
            $"发生未处理的错误：{exception.Message}",
            "桌面天气小挂件",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
