using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;

// 屏幕截图工具：抓取整个虚拟桌面（所有显示器）到 PNG。
// 用途：验证真实屏幕上的渲染效果（离屏渲染与实屏可能不同），以及验证挂件的桌面层级。
internal static class Cap
{
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    private delegate bool EnumProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    // 直接抓某个进程的窗口（不受遮挡影响）
    public static void CaptureWindow(string exeName, string outPath)
    {
        var proc = Process.GetProcessesByName(exeName).FirstOrDefault();
        if (proc == null) { Console.WriteLine($"{exeName} 未运行"); return; }
        IntPtr target = IntPtr.Zero; long best = -1;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p != (uint)proc.Id || !IsWindowVisible(h)) return true;
            GetWindowRect(h, out RECT r);
            long a = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (a > best) { best = a; target = h; }
            return true;
        }, IntPtr.Zero);
        if (target == IntPtr.Zero) { Console.WriteLine("找不到窗口"); return; }
        GetWindowRect(target, out RECT rc);
        int w = rc.Right - rc.Left, hh = rc.Bottom - rc.Top;
        using var bmp = new Bitmap(w, hh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr dc = g.GetHdc();
            PrintWindow(target, dc, 2);   // PW_RENDERFULLCONTENT
            g.ReleaseHdc(dc);
        }
        bmp.Save(outPath, ImageFormat.Png);
        Console.WriteLine($"saved {outPath} ({w}x{hh}) hwnd={target}");
    }

    public static void Capture(string outPath)
    {
        var bounds = SystemInformation.VirtualScreen;
        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        bmp.Save(outPath, ImageFormat.Png);
        Console.WriteLine($"saved {outPath} ({bounds.Width}x{bounds.Height})");
    }
}
