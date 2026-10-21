using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WidgetHubSetup
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 静默模式：无人值守安装（用于批量部署 / 自动化测试）
            if (args.Contains("--silent") || args.Contains("/S"))
            {
                string dir = "";
                int i = Array.IndexOf(args, "--dir");
                if (i >= 0 && i + 1 < args.Length) dir = args[i + 1];
                int code = SetupForm.SilentInstall(dir);
                Environment.Exit(code);
            }
            Application.Run(new SetupForm());
        }
    }

    // 皮肤清单：与 WidgetHub 的 config.json 结构一一对应
    internal sealed class SkinDef
    {
        public string Name = "";
        public string Sub = "";       // 安装目录下的相对子目录
        public string Exe = "";
        public bool Positionless = false;
    }

    internal static class Skins
    {
        public static readonly SkinDef[] All =
        {
            new SkinDef { Name = "小普河天气", Sub = "XiaoPuHeWeather", Exe = "XiaoPuHeWeather.exe" },
            new SkinDef { Name = "桌面万年历", Sub = "DesktopCalendar", Exe = "DesktopCalendar.exe" },
            new SkinDef { Name = "天气CPU网速", Sub = "DesktopMonitor", Exe = "DesktopMonitor.exe" },
            new SkinDef { Name = "桌面音乐", Sub = "DesktopMusic", Exe = "DesktopMusic.exe" },
            new SkinDef { Name = "桌面收音机", Sub = "DesktopRadio", Exe = "DesktopRadio.exe" },
            new SkinDef { Name = "倒计时闹钟", Sub = "DesktopTimer", Exe = "DesktopTimer.exe" },
            new SkinDef { Name = "桌面视频", Sub = "DesktopVideo", Exe = "DesktopVideo.exe" },
            new SkinDef { Name = "桌面记账", Sub = "DesktopWallet", Exe = "DesktopWallet.exe" },
            new SkinDef { Name = "桌面RSS", Sub = "RssWidget", Exe = "RssWidget.exe" },
            new SkinDef { Name = "桌面记事", Sub = "DesktopNotes", Exe = "DesktopNotes.exe" },
            new SkinDef { Name = "桌面相框", Sub = "DesktopPhoto", Exe = "DesktopPhoto.exe" },
            new SkinDef { Name = "炫彩鼠标尾巴", Sub = "NeonMouseTrail", Exe = "NeonMouseTrail.exe", Positionless = true },
        };
    }

    internal sealed class SetupForm : Form
    {
        // ---- 配色（与 WidgetHub 主程序一致）----
        static readonly Color CBlue = Color.FromArgb(59, 130, 246);
        static readonly Color CBlueD = Color.FromArgb(37, 99, 235);
        static readonly Color CText = Color.FromArgb(15, 23, 42);
        static readonly Color CMuted = Color.FromArgb(100, 116, 139);
        static readonly Color CBorder = Color.FromArgb(226, 232, 240);
        static readonly Color CBg = Color.FromArgb(248, 250, 252);

        readonly string installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "WidgetHubSuite");

        Label lblTitle = null!, lblSub, lblPath, lblStatus, lblDetail;
        TextBox txtPath = null!;
        CheckBox chkDesktop = null!, chkAutoStart = chkAutoStartPlaceholder();
        RoundedButton btnInstall = null!, btnClose = null!;
        ProgressBarEx bar = null!;
        Panel header = null!;
        bool installed = false;

        static CheckBox chkAutoStartPlaceholder() => new CheckBox();

        // DPI 缩放系数：自己算，不依赖 WinForms 的自动缩放
        // （AutoScaleMode.Dpi + 手动设置 ClientSize 会导致"窗口放大了、控件没放大"）
        readonly float k;

        public SetupForm()
        {
            Text = "桌面挂件总管 · 安装向导";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = CBg;
            Font = new Font("Microsoft YaHei", 9F);
            AutoScaleMode = AutoScaleMode.None;

            using (var g = CreateGraphics()) k = g.DpiX / 96f;
            if (k <= 0.5f) k = 1f;

            // 窗口必须放得进当前屏幕工作区（小屏 / 高缩放时尤其重要）
            var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 900);
            ClientSize = new Size(
                (int)Math.Min(600 * k, wa.Width - 40),
                (int)Math.Min(540 * k, wa.Height - 60));
            BuildUI();
        }

        private int PX(double v) => (int)Math.Round(v * k);

        // 内容区可用宽度（左右各留 28 的边距）
        private int ContentWidth() => Math.Max(PX(200), ClientSize.Width - PX(56));

        private void BuildUI()
        {
            // ---------- 顶部品牌区 ----------
            header = new Panel { Dock = DockStyle.Top, Height = PX(104), BackColor = CBlue };
            header.Paint += (s, e) =>
            {
                using var g = e.Graphics;
                using var br = new LinearGradientBrush(header.ClientRectangle,
                    Color.FromArgb(37, 99, 235), Color.FromArgb(59, 130, 246), 0f);
                g.FillRectangle(br, header.ClientRectangle);
                var ico = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ico != null) g.DrawIcon(ico, new Rectangle(PX(22), PX(22), PX(60), PX(60)));
                using var f1 = new Font("Microsoft YaHei", 17F, FontStyle.Bold);
                using var f2 = new Font("Microsoft YaHei", 9F);
                using var sb = new SolidBrush(Color.FromArgb(226, 236, 255));
                g.DrawString("桌面挂件总管", f1, Brushes.White, PX(96), PX(24));
                g.DrawString("WidgetHub · 12 个桌面挂件 + 一个统一托盘图标", f2, sb, PX(98), PX(62));
            };

            // ---------- 底部操作区（Dock=Bottom，永远贴在窗口底边）----------
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = PX(104), BackColor = CBg };
            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = PX(48),
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = CBg,
                Padding = new Padding(0, 0, 0, 0)
            };
            btnInstall = NewButton("开始安装", PX(140), PX(42), CBlue);
            btnInstall.Margin = new Padding(0, 0, PX(10), 0);
            btnInstall.Click += (_, _) => { if (installed) LaunchApp(); else StartInstall(); };
            btnClose = NewButton("退出", PX(100), PX(42), Color.FromArgb(100, 116, 139));
            btnClose.Margin = new Padding(0, 0, 0, 0);
            btnClose.Click += (_, _) => Close();
            btnRow.Controls.Add(btnInstall);
            btnRow.Controls.Add(btnClose);

            bar = new ProgressBarEx { Dock = DockStyle.Top, Height = PX(10) };
            bottom.Controls.Add(btnRow);
            bottom.Controls.Add(bar);

            // ---------- 中间内容区（Dock=Fill）----------
            var body = new Panel { Dock = DockStyle.Fill, BackColor = CBg, AutoScroll = true };

            lblTitle = new Label
            {
                Text = "安装到本机",
                Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold),
                ForeColor = CText,
                AutoSize = true,
                Location = new Point(PX(28), PX(20)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            lblSub = new Label
            {
                Text = "把 1 个管理程序 + 12 个挂件装到同一目录，并生成开始菜单快捷方式。无需安装任何运行库。",
                ForeColor = CMuted,
                AutoSize = false,
                Size = new Size(ContentWidth(), PX(40)),
                Location = new Point(PX(28), PX(50)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            lblPath = new Label
            {
                Text = "安装位置（可直接修改）",
                ForeColor = CText,
                AutoSize = true,
                Location = new Point(PX(28), PX(102)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            txtPath = new TextBox
            {
                Text = installDir,
                Location = new Point(PX(28), PX(124)),
                Size = new Size(PX(500), PX(26)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            chkDesktop = new CheckBox
            {
                Text = "创建桌面快捷方式",
                Checked = true,
                AutoSize = true,
                Location = new Point(PX(28), PX(162)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            chkAutoStart = new CheckBox
            {
                Text = "开机自动启动（登录后自动拉起挂件）",
                Checked = true,
                AutoSize = true,
                Location = new Point(PX(28), PX(190)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            lblStatus = new Label
            {
                Text = "准备就绪",
                ForeColor = CText,
                Font = new Font("Microsoft YaHei", 9.5F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(PX(28), PX(226)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            lblDetail = new Label
            {
                Text = "",
                ForeColor = CMuted,
                AutoSize = false,
                Size = new Size(ContentWidth(), PX(40)),
                Location = new Point(PX(28), PX(250)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            body.Controls.AddRange(new Control[]
            {
                lblTitle, lblSub, lblPath, txtPath, chkDesktop, chkAutoStart, lblStatus, lblDetail
            });

            Controls.Add(body);
            Controls.Add(bottom);
            Controls.Add(header);
        }

        private static RoundedButton NewButton(string text, int w, int h, Color fill)
        {
            return new RoundedButton
            {
                Text = text,
                Width = w,
                Height = h,
                Fill = fill,
                Hover = ControlPaint.Dark(fill, 0.08f),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei", 9.5F, FontStyle.Bold)
            };
        }

        /// <summary>静默安装：返回 0 成功，1 失败。</summary>
        public static int SilentInstall(string dir = "")
        {
            var f = new SetupForm();
            f.SilentMode = true;
            f.txtPath.Text = string.IsNullOrEmpty(dir) ? f.installDir : dir;
            f.chkDesktop.Checked = true;
            f.chkAutoStart.Checked = true;
            try
            {
                f.InstallSilent();
                return f.installed ? 0 : 1;
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "install.log"),
                    DateTime.Now.ToString("HH:mm:ss") + " 静默安装失败: " + ex + Environment.NewLine);
                return 1;
            }
        }

        public void InstallSilent()
        {
            btnInstall.Enabled = false;
            Install();
        }

        private void StartInstall()
        {
            btnInstall.Enabled = false;
            btnClose.Enabled = false;
            Task.Run(() => Install()).ContinueWith(_ =>
            {
                BeginInvoke(new Action(() =>
                {
                    btnClose.Enabled = true;
                    if (installed)
                    {
                        lblStatus.Text = "安装完成";
                        btnInstall.Text = "启动桌面挂件总管";
                        btnInstall.Enabled = true;
                        btnInstall.Fill = CBlueD;
                        lblDetail.Text = "已安装 1 个管理程序 + 12 个挂件。托盘里只会出现 WidgetHub 一个图标。";
                    }
                    else
                    {
                        lblStatus.Text = "安装失败";
                        btnInstall.Enabled = true;
                        btnInstall.Text = "重试";
                        btnInstall.Click += (_, _) => { if (installed) LaunchApp(); else StartInstall(); };
                    }
                }));
            });
        }

        private string TargetDir => string.IsNullOrWhiteSpace(txtPath.Text) ? installDir : txtPath.Text.Trim();

        private void Install()
        {
            try
            {
                Report("正在结束正在运行的挂件…", 5);
                Kill("WidgetHub");
                foreach (var s in Skins.All) Kill(Path.GetFileNameWithoutExtension(s.Exe));

                Report("正在解压文件…", 20);
                string target = TargetDir;
                Directory.CreateDirectory(target);
                ExtractPayload(target);

                Report("正在写入配置…", 70);
                WriteConfig();

                Report("正在创建快捷方式…", 82);
                CreateShortcuts();
                RegisterUninstallEntry();

                if (chkAutoStart.Checked) SetAutoStart(true);
                else SetAutoStart(false);

                Report("正在收尾…", 92);

                Report("安装完成", 100);
                installed = true;
                OfferDefenderExclusion(target);
                if (chkAutoStart.Checked) { /* 开机自启已写入注册表 */ }
            }
            catch (Exception ex)
            {
                Report("安装出错：" + ex.Message, 100);
                MessageBox.Show("安装过程中出错：\r\n" + ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>提示用户添加安全软件排除项：Defender 有时会把这类未签名小工具判成 PUA 并删掉。</summary>
        internal bool SilentMode = false;

        private void OfferDefenderExclusion(string target)
        {
            try
            {
                if (SilentMode) return;
                string bat = Path.Combine(target, "Defender-Exclude.bat");
                if (!File.Exists(bat)) return;
                var r = MessageBox.Show(
                    "建议再添加一条安全软件排除项：\r\n\r\n" +
                    "Windows Defender 偶尔会把这类未签名的桌面小工具误判为\"不需要的应用程序\"并直接删除，\r\n" +
                    "加上排除项后就不会再被误删。\r\n\r\n是否现在添加？（会弹出一次管理员授权）",
                    "添加安全软件排除项", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (r != DialogResult.Yes) return;
                var psi = new ProcessStartInfo(bat)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = target
                };
                Process.Start(psi);
            }
            catch { }
        }

        private void Report(string msg, int pct)
        {
            lblStatus.Text = msg;
            lblDetail.Text = msg;
            try
            {
                bar.Value = Math.Max(0, Math.Min(100, pct));
                bar.Refresh();
                Application.DoEvents();
            }
            catch { }
        }

        private void ExtractPayload(string target)
        {
            var asm = Assembly.GetExecutingAssembly();
            using var res = asm.GetManifestResourceStream("payload.zip");
            if (res == null) throw new Exception("安装包数据缺失（payload.zip 未嵌入）");
            using var zip = new ZipArchive(res, ZipArchiveMode.Read);
            int total = zip.Entries.Count;
            int i = 0;
            foreach (var entry in zip.Entries)
            {
                i++;
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string rel = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                string dest = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, true);
                if (i % 8 == 0) Report($"正在解压文件… {i}/{total}", 20 + 45 * i / total);
            }
        }

        private void WriteConfig()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WidgetHub");
            Directory.CreateDirectory(dir);

            // Folder 字段是相对 %LocalAppData%\Programs 的；安装目录就在它下面，
            // 因此写相对路径即可（Path.Combine 遇到绝对路径也会直接返回绝对路径）。
            string programs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
            string target = TargetDir;
            // 安装目录在 %LocalAppData%\Programs 下就写相对路径，否则写绝对路径
            // （Skin.FullPath 用 Path.Combine，遇到绝对路径会直接采用绝对路径）
            bool underPrograms = target.StartsWith(programs, StringComparison.OrdinalIgnoreCase);
            var sb = new System.Text.StringBuilder();
            sb.Append("{\r\n  \"AutoStartManager\": ").Append(chkAutoStart.Checked ? "true" : "false").Append(",\r\n");
            sb.Append("  \"HideWidgetTrayIcons\": true,\r\n  \"Skins\": [\r\n");
            for (int i = 0; i < Skins.All.Length; i++)
            {
                var s = Skins.All[i];
                string folder = underPrograms
                    ? Path.Combine(target.Substring(programs.Length).TrimStart('\\'), "Skins", s.Sub)
                    : Path.Combine(target, "Skins", s.Sub);
                sb.Append("    {\r\n");
                sb.Append("      \"Name\": \"").Append(Esc(s.Name)).Append("\",\r\n");
                sb.Append("      \"Folder\": \"").Append(Esc(folder)).Append("\",\r\n");
                sb.Append("      \"ExeName\": \"").Append(Esc(s.Exe)).Append("\",\r\n");
                sb.Append("      \"Enabled\": true,\r\n");
                sb.Append("      \"TopMost\": false,\r\n");
                sb.Append("      \"ClickThrough\": false,\r\n");
                sb.Append("      \"Opacity\": 255,\r\n");
                sb.Append("      \"X\": -1,\r\n      \"Y\": -1,\r\n");
                sb.Append("      \"Width\": 0,\r\n      \"Height\": 0,\r\n");
                sb.Append("      \"PositionSaved\": false,\r\n");
                sb.Append("      \"Positionless\": ").Append(s.Positionless ? "true" : "false").Append("\r\n");
                sb.Append("    }").Append(i == Skins.All.Length - 1 ? "" : ",").Append("\r\n");
            }
            sb.Append("  ]\r\n}\r\n");
            File.WriteAllText(Path.Combine(dir, "config.json"), sb.ToString(), new UTF8Encoding(false));
        }

        private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private void CreateShortcuts()
        {
            string exe = Path.Combine(TargetDir, "WidgetHub", "WidgetHub.exe");
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "桌面挂件总管", "桌面挂件总管.lnk");
            Directory.CreateDirectory(Path.GetDirectoryName(startMenu)!);
            CreateShortcut(startMenu, exe, "桌面挂件总管");

            if (chkDesktop.Checked)
            {
                string desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "桌面挂件总管.lnk");
                CreateShortcut(desk, exe, "桌面挂件总管");
            }

            // 卸载入口：开始菜单里一个"卸载"快捷方式 + 安装目录里的 Uninstall.bat
            string uninstaller = Path.Combine(TargetDir, "Uninstall", "WidgetHubUninstall.exe");
            if (File.Exists(uninstaller))
            {
                string unLink = Path.Combine(Path.GetDirectoryName(startMenu)!, "卸载桌面挂件总管.lnk");
                CreateShortcut(unLink, uninstaller, "卸载桌面挂件总管");
                File.WriteAllText(Path.Combine(TargetDir, "Uninstall.bat"),
                    "@echo off\r\n\"" + uninstaller + "\"\r\n", new UTF8Encoding(false));
            }
        }

        /// <summary>写控制面板的"卸载程序"条目，这样在 设置→应用→卸载程序 里也能卸。</summary>
        private void RegisterUninstallEntry()
        {
            try
            {
                string exe = Path.Combine(TargetDir, "WidgetHub", "WidgetHub.exe");
                string uninstaller = Path.Combine(TargetDir, "Uninstall", "WidgetHubUninstall.exe");
                if (!File.Exists(uninstaller)) return;
                using var key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WidgetHub");
                if (key == null) return;
                key.SetValue("DisplayName", "桌面挂件总管");
                key.SetValue("DisplayVersion", "1.0.0");
                key.SetValue("Publisher", "WidgetHub");
                key.SetValue("DisplayIcon", exe);
                key.SetValue("InstallLocation", TargetDir);
                key.SetValue("UninstallString", "\"" + uninstaller + "\"");
                key.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /silent");
                key.SetValue("NoModify", 1);
                key.SetValue("NoRepair", 1);
                long size = 0;
                try { size = new DirectoryInfo(TargetDir).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch { }
                key.SetValue("EstimatedSize", (int)(size / 1024));
            }
            catch { }
        }

        private static void CreateShortcut(string linkPath, string target, string desc)
        {
            try
            {
                Type? t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                dynamic shell = Activator.CreateInstance(t)!;
                dynamic lnk = shell.CreateShortcut(linkPath);
                lnk.TargetPath = target;
                lnk.WorkingDirectory = Path.GetDirectoryName(target)!;
                lnk.Description = desc;
                lnk.IconLocation = target + ",0";
                lnk.Save();
            }
            catch { }
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                string exe = Path.Combine(TargetDir, "WidgetHub", "WidgetHub.exe");
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;
                if (enable) key.SetValue("WidgetHub", "\"" + exe + "\"");
                else key.DeleteValue("WidgetHub", false);
            }
            catch { }
        }

        /// <summary>检测 .NET 8 桌面运行时是否已安装（看运行时目录，最可靠）。</summary>
        private static bool HasDotNet8Desktop()
        {
            try
            {
                var roots = new List<string>();
                string env = Environment.GetEnvironmentVariable("DOTNET_ROOT");
                if (!string.IsNullOrEmpty(env)) roots.Add(env);
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(pf)) roots.Add(Path.Combine(pf, "dotnet"));
                roots.Add(@"C://Program Files//dotnet");

                foreach (var root in roots)
                {
                    string dir = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
                    if (!Directory.Exists(dir)) continue;
                    foreach (var v in Directory.GetDirectories(dir))
                        if (v.StartsWith("8.", StringComparison.OrdinalIgnoreCase)) return true;
                }

                // 兜底：注册表
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\desktop\SharedFX\Microsoft.WindowsDesktop.App");
                if (key != null)
                    foreach (var name in key.GetSubKeyNames())
                        if (name.StartsWith("8.")) return true;
            }
            catch { }
            return false;
        }

        private static void InstallDotNet8()
        {
            try
            {
                var psi = new ProcessStartInfo("winget", "install --id Microsoft.DotNet.Runtime.8 --silent --accept-package-agreements --accept-source-agreements")
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
                MessageBox.Show("已调用 winget 开始安装 .NET 8 运行时，安装完成后重新运行本安装程序即可安装天气挂件。",
                    "正在安装", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch
            {
                MessageBox.Show("自动安装失败，请手动前往微软官网下载「.NET 8 桌面运行时」安装。\r\nhttps://dotnet.microsoft.com/download/dotnet/8.0",
                    "需要手动安装", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static void Kill(string exeName)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(exeName))
                {
                    try { p.Kill(); p.WaitForExit(2000); } catch { }
                }
            }
            catch { }
        }

        private void LaunchApp()
        {
            try
            {
                string exe = Path.Combine(TargetDir, "WidgetHub", "WidgetHub.exe");
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)! });
            }
            catch { }
            Close();
        }
    }

    // 自绘进度条：默认 WinForms 进度条在 Win11 上很丑，这里用圆角条 + 蓝色填充
    internal sealed class ProgressBarEx : Control
    {
        private int _value;
        public int Value
        {
            get => _value;
            set { _value = Math.Max(0, Math.Min(100, value)); Invalidate(); }
        }

        public ProgressBarEx()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(248, 250, 252);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var h = Height;
            int r = h / 2;
            using (var track = new SolidBrush(Color.FromArgb(226, 232, 240)))
                g.FillPath(track, RoundedButton.RoundPath(new Rectangle(0, 0, Math.Max(1, Width - 1), h - 1), r));
            int w = (int)Math.Round(Width * (_value / 100.0));
            if (w > 2)
            {
                using var fill = new SolidBrush(Color.FromArgb(59, 130, 246));
                g.FillPath(fill, RoundedButton.RoundPath(new Rectangle(0, 0, Math.Max(2, w - 1), h - 1), r));
            }
        }
    }

    // 圆角按钮（自绘 + 抗锯齿贝塞尔，避免 GDI+ AddArc 路径自相交导致的缺口）
    internal sealed class RoundedButton : Button
    {
        public Color Fill = Color.FromArgb(59, 130, 246);
        public Color Hover = Color.FromArgb(37, 99, 235);
        private bool hot;

        public RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundPath(r, 8))
            using (var br = new SolidBrush(Enabled ? (hot ? Hover : Fill) : Color.FromArgb(203, 213, 225)))
                g.FillPath(br, path);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        internal static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            float rr = Math.Max(0f, Math.Min(radius, Math.Min(r.Width, r.Height) / 2f));
            if (rr <= 0.5f) { var p0 = new GraphicsPath(); p0.AddRectangle(r); return p0; }
            float k = rr * 0.5523f;
            var gp = new GraphicsPath();
            gp.StartFigure();
            gp.AddLine(r.Left + rr, r.Top, r.Right - rr, r.Top);
            gp.AddBezier(r.Right - rr, r.Top, r.Right - rr + k, r.Top, r.Right, r.Top + rr - k, r.Right, r.Top + rr);
            gp.AddLine(r.Right, r.Top + rr, r.Right, r.Bottom - rr);
            gp.AddBezier(r.Right, r.Bottom - rr, r.Right, r.Bottom - rr + k, r.Right - rr + k, r.Bottom, r.Right - rr, r.Bottom);
            gp.AddLine(r.Right - rr, r.Bottom, r.Left + rr, r.Bottom);
            gp.AddBezier(r.Left + rr, r.Bottom, r.Left + rr - k, r.Bottom, r.Left, r.Bottom - rr + k, r.Left, r.Bottom - rr);
            gp.AddLine(r.Left, r.Bottom - rr, r.Left, r.Top + rr);
            gp.AddBezier(r.Left, r.Top + rr, r.Left, r.Top + rr - k, r.Left + rr - k, r.Top, r.Left + rr, r.Top);
            gp.CloseFigure();
            return gp;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    }
}
