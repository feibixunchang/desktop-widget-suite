using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

// 诊断工具：对比目标坐标 / 实际坐标，并可实测 MoveWindow 是否被挂件自己覆盖
internal static class Check
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    private sealed class Skin
    {
        public string Name { get; set; } = "";
        public string Folder { get; set; } = "";
        public string ExeName { get; set; } = "";
        public int X { get; set; } = -1;
        public int Y { get; set; } = -1;
        public int Width { get; set; } = 0;
        public int Height { get; set; } = 0;
    }
    private sealed class Cfg { public List<Skin> Skins { get; set; } = new(); }

    private static List<(IntPtr h, string cls, int x, int y, int w, int hh)> WindowsOf(uint pid)
    {
        var list = new List<(IntPtr, string, int, int, int, int)>();
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p != pid || !IsWindowVisible(h)) return true;
            GetWindowRect(h, out RECT r);
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            list.Add((h, sb.ToString(), r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static void Main(string[] args)
    {
        SetProcessDPIAware();

        // 坐标探针模式：--probe <exeName> <targetX> <targetY> <scale>
        if (args.Length >= 5 && args[0] == "--probe")
        {
            Probe.Run(args[1], int.Parse(args[2]), int.Parse(args[3]), double.Parse(args[4]));
            return;
        }

        // 屏幕截图：--cap <out.png>
        if (args.Length >= 2 && args[0] == "--cap")
        {
            Cap.Capture(args[1]);
            return;
        }

        // 按标题关键字找窗口并结束它所属进程：--killwin <关键字>
        if (args.Length >= 2 && args[0] == "--killwin")
        {
            foreach (var pid in Probe.FindWindowPids(args[1]))
            {
                Console.WriteLine($"kill pid {pid}");
                try { var pr = Process.GetProcessById((int)pid); pr.Kill(); } catch { }
            }
            return;
        }

        // 抓取指定进程窗口：--capwin <exe> <out.png>
        if (args.Length >= 3 && args[0] == "--capwin")
        {
            Cap.CaptureWindow(args[1], args[2]);
            return;
        }

        // 打包：--pack <publishDir> <programsDir> <stageDir> <zipPath>
        if (args.Length >= 5 && args[0] == "--pack")
        {
            Pack.Run(args[1], args[2], args[3], args[4]);
            return;
        }

        // 覆盖测试窗：--cover <x> <y> <w> <h>
        if (args.Length >= 5 && args[0] == "--cover")
        {
            Shapes.Cover(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]));
            return;
        }

        // 形状对照实验：--shapes
        if (args.Length >= 1 && args[0] == "--shapes")
        {
            Shapes.Show();
            return;
        }

        bool move = args.Contains("--move");

        string cfgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WidgetHub", "config.json");
        var cfg = JsonSerializer.Deserialize<Cfg>(File.ReadAllText(cfgPath))!;

        Console.OutputEncoding = Encoding.UTF8;
        foreach (var s in cfg.Skins)
        {
            string exe = Path.GetFileNameWithoutExtension(s.ExeName);
            var procs = Process.GetProcessesByName(exe);
            Console.WriteLine($"== {s.Name}  target={s.X},{s.Y} {s.Width}x{s.Height}");
            if (procs.Length == 0) { Console.WriteLine("   (not running)"); continue; }
            var wins = WindowsOf((uint)procs[0].Id);
            foreach (var w in wins)
                Console.WriteLine($"   hwnd={w.h} class={w.cls} parent={GetParent(w.h)} rect={w.x},{w.y} {w.w}x{w.hh}");

            if (move && wins.Count > 0)
            {
                // 选面积最大的可见窗
                var t = wins.OrderByDescending(v => (long)v.w * v.hh).First();
                MoveWindow(t.h, s.X, s.Y, s.Width, s.Height, true);
                GetWindowRect(t.h, out RECT r1);
                Console.WriteLine($"   after move:  {r1.Left},{r1.Top} {r1.Right - r1.Left}x{r1.Bottom - r1.Top}");
                Thread.Sleep(3000);
                GetWindowRect(t.h, out RECT r2);
                Console.WriteLine($"   3s later:    {r2.Left},{r2.Top} {r2.Right - r2.Left}x{r2.Bottom - r2.Top}");
            }
            foreach (var p in procs) p.Dispose();
        }
    }
}
