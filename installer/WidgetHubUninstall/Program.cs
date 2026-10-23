using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WidgetHubUninstall
{
    internal static class Program
    {
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WidgetHub";

        // 会被一起结束的进程（管理程序 + 13 个挂件）
        private static readonly string[] ProcessNames =
        {
            "WidgetHub", "WeatherWidget48", "DesktopCalendar", "DesktopMonitor", "DesktopMusic",
            "DesktopRadio", "DesktopTimer", "DesktopVideo", "DesktopWallet", "RssWidget", "NeonMouseTrail",
            "DesktopNotes", "DesktopPhoto", "DesktopLunar"
        };

        [STAThread]
        private static void Main(string[] args)
        {
            bool silent = args != null && args.Length > 0 &&
                          (args[0] == "/silent" || args[0] == "--silent" || args[0] == "/S");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 安装目录 = 卸载器所在目录的上一级
            string uninstallerDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string installDir = Path.GetDirectoryName(uninstallerDir) ?? uninstallerDir;

            if (!silent)
            {
                using var confirm = new ConfirmForm(installDir);
                if (confirm.ShowDialog() != DialogResult.OK) return;
            }

            try
            {
                StopProcesses();
                RemoveShortcuts(installDir);
                RemoveAutoStart();
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                RemoveDirectoryDelayed(installDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("卸载过程中出现问题：\r\n" + ex.Message, "卸载", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void StopProcesses()
        {
            foreach (var name in ProcessNames)
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try { p.Kill(); p.WaitForExit(2000); } catch { }
                    }
                }
                catch { }
            }
        }

        private static void RemoveShortcuts(string installDir)
        {
            try
            {
                string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "桌面挂件合集");
                if (Directory.Exists(startMenu)) Directory.Delete(startMenu, true);
            }
            catch { }
            try
            {
                string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "桌面挂件合集.lnk");
                if (File.Exists(desktop)) File.Delete(desktop);
            }
            catch { }
        }

        private static void RemoveAutoStart()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                key?.DeleteValue("WidgetHub", false);
            }
            catch { }
        }

        // 程序正在运行，不能直接删自己的目录：交给一个临时批处理延迟删除
        private static void RemoveDirectoryDelayed(string installDir)
        {
            try
            {
                string bat = Path.Combine(Path.GetTempPath(), "WidgetHubUninstall_" + Guid.NewGuid().ToString("N") + ".bat");
                File.WriteAllText(bat,
                    "@echo off\r\n" +
                    "ping -n 3 127.0.0.1 >nul\r\n" +
                    "rmdir /s /q \"" + installDir + "\"\r\n" +
                    "del \"%~f0\"\r\n");
                var psi = new ProcessStartInfo("cmd.exe", "/c start \"\" /min \"" + bat + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            catch
            {
                try { Directory.Delete(installDir, true); } catch { }
            }
        }
    }

    internal sealed class ConfirmForm : Form
    {
        readonly string installDir;
        readonly CheckBox chkConfig = new CheckBox
        {
            Text = "同时删除配置（含已记录的位置），下次安装需重新摆放",
            AutoSize = true,
            Location = new Point(28, 150)
        };

        public ConfirmForm(string dir)
        {
            installDir = dir;
            Text = "卸载 桌面挂件合集";
            ClientSize = new Size(470, 230);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Microsoft YaHei", 9F);

            var title = new Label
            {
                Text = "确定要卸载桌面挂件合集吗？",
                Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                AutoSize = true,
                Location = new Point(28, 26)
            };
            var desc = new Label
            {
                Text = "将删除以下内容：\r\n" +
                       "· 安装目录（管理器 + 13 个挂件）\r\n" +
                       "· 开始菜单与桌面快捷方式\r\n" +
                       "· 开机自启设置\r\n\r\n" +
                       "安装目录：" + dir,
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = false,
                Size = new Size(414, 110),
                Location = new Point(28, 58)
            };
            var btnOk = new Button { Text = "卸载", DialogResult = DialogResult.OK, Location = new Point(300, 184), Size = new Size(70, 30) };
            var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(378, 184), Size = new Size(70, 30) };

            Controls.AddRange(new Control[] { title, desc, chkConfig, btnOk, btnCancel });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK && chkConfig.Checked)
            {
                try
                {
                    string cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WidgetHub");
                    if (Directory.Exists(cfg)) Directory.Delete(cfg, true);
                }
                catch { }
            }
            base.OnFormClosing(e);
        }
    }
}
