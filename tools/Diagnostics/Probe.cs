using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// 坐标探针：验证「DPI 感知的管理器」如何把那 10 个 DPI 不感知的挂件窗口摆到目标物理坐标。
// 对每个候选换算方式实测，打印窗口真实物理落点，找出唯一有效的那种。
internal static class Probe
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] private static extern int GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("shcore.dll")] private static extern int GetProcessDpiAwareness(IntPtr h, out int v);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private static string RectStr(IntPtr h)
    {
        bool ok = GetWindowRect(h, out RECT r);
        return ok ? $"{r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}" : "GetWindowRect FAILED";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder sb, int max);
    // 按标题关键字找出所有窗口所属的进程 id
    public static List<uint> FindWindowPids(string keyword)
    {
        var result = new List<uint>();
        EnumWindows((h, _) =>
        {
            var sb = new System.Text.StringBuilder(512);
            GetWindowTextW(h, sb, sb.Capacity);
            var title = sb.ToString();
            if (title.Length > 0 && title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                GetWindowThreadProcessId(h, out uint p);
                if (!result.Contains(p)) result.Add(p);
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static void Run(string exeName, int targetX, int targetY, double scale)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var proc = Process.GetProcessesByName(exeName).FirstOrDefault();
        if (proc == null) { Console.WriteLine($"{exeName} not running"); return; }

        IntPtr win = IntPtr.Zero;
        long best = -1;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p != (uint)proc.Id || !IsWindowVisible(h)) return true;
            GetWindowRect(h, out RECT r);
            long a = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (a > best) { best = a; win = h; }
            return true;
        }, IntPtr.Zero);
        if (win == IntPtr.Zero) { Console.WriteLine("no window"); return; }

        GetProcessDpiAwareness(IntPtr.Zero, out int awareness);
        Console.WriteLine($"exe={exeName} pid={proc.Id} hwnd={win} sysDpi={GetDpiForSystem()} winDpi={GetDpiForWindow(win)} 本进程awareness={awareness}(0=unaware,1=system,2=permonitor)");
        Console.WriteLine($"target(physical) = {targetX},{targetY}   scale={scale}");
        Console.WriteLine($"initial: {RectStr(win)}");

        var variants = new (string name, int x, int y)[]
        {
            ("raw            ", targetX, targetY),
            ("div scale      ", (int)Math.Round(targetX / scale), (int)Math.Round(targetY / scale)),
            ("mul scale      ", (int)Math.Round(targetX * scale), (int)Math.Round(targetY * scale)),
        };

        foreach (var v in variants)
        {
            // 用 MoveWindow 传"当前尺寸"，避免改尺寸
            GetWindowRect(win, out RECT cur);
            int w = cur.Right - cur.Left, h = cur.Bottom - cur.Top;
            bool ok = SetWindowPos(win, IntPtr.Zero, v.x, v.y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            Thread.Sleep(600);
            string imm = RectStr(win);
            Thread.Sleep(1600);
            string later = RectStr(win);
            Console.WriteLine($"SetWindowPos {v.name} -> ({v.x},{v.y}) ok={ok}  600ms={imm}   2s={later}");
        }

        // MoveWindow 传实际尺寸
        {
            GetWindowRect(win, out RECT cur);
            int w = cur.Right - cur.Left, h = cur.Bottom - cur.Top;
            bool ok = MoveWindow(win, targetX, targetY, w, h, true);
            Thread.Sleep(600);
            string imm = RectStr(win);
            Thread.Sleep(1600);
            Console.WriteLine($"MoveWindow  raw             -> ({targetX},{targetY}) {w}x{h} ok={ok}  600ms={imm}   2s={RectStr(win)}");
        }
        // MoveWindow 用 除以 scale 的坐标
        {
            GetWindowRect(win, out RECT cur);
            int w = cur.Right - cur.Left, h = cur.Bottom - cur.Top;
            int x = (int)Math.Round(targetX / scale), y = (int)Math.Round(targetY / scale);
            bool ok = MoveWindow(win, x, y, w, h, true);
            Thread.Sleep(600);
            string imm = RectStr(win);
            Thread.Sleep(1600);
            Console.WriteLine($"MoveWindow  div scale       -> ({x},{y}) {w}x{h} ok={ok}  600ms={imm}   2s={RectStr(win)}");
        }

        Console.WriteLine($"final: {RectStr(win)}  IsWindow={IsWindow(win)}");
    }
}
