using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Shared;

namespace DesktopLunar
{
    internal static class Program
    {
        private const string MutexName = "DesktopLunar.Widget.SingleInstance";

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            EnableDpiAwareness();

            // 必须在 new LunarClockForm() 之前设：基类构造时就要按这个 id 去找 settings.json。
            // C# 是先跑基类构造再跑子类构造，写在子类构造函数里就晚了。
            AppPaths.AppId = "DesktopLunar";

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    MessageBox.Show("桌面阴历挂件已经在运行了。", "桌面阴历",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new LunarClockForm());
            }
        }

        private static void EnableDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) { return; }
            }
            catch
            {
            }

            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }
        }
    }
}
