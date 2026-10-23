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
