using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Shared;

namespace DesktopNotes
{
    internal static class Program
    {
        private const string MutexName = "DesktopNotes.Widget.SingleInstance";

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [STAThread]
        private static void Main()
        {
            EnableDpiAwareness();
            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    ActivateExisting();
                    return;
                }

                try
                {
                    AppPaths.AppId = "DesktopNotes";
                    PopupPanel.UseBlueBorder = true;
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    // 挂件是不值守的：任何没接住的异常都不能弹对话框停在那儿，
                    // 否则桌面上会留一个没人关的框，挂件自己反而不见了。
                    Application.ThreadException += delegate { };
                    Application.Run(new NotesForm());
                }
                catch (Exception exception)
                {
                    MessageBox.Show("启动失败：" + exception.Message, "桌面记事挂件",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static void ActivateExisting()
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName("DesktopNotes"))
                {
                    IntPtr handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero) { continue; }
                    ShowWindow(handle, 9);
                    SetForegroundWindow(handle);
                    break;
                }
            }
            catch
            {
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
