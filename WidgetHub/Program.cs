using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WidgetHub
{
    // 轻量诊断日志：排查"挂件没有摆到记录位置"这类问题时全靠它。
    // 位置：%AppData%\WidgetHub\log.txt，每次启动覆盖。
    public static class Log
    {
        private static readonly string File_ =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WidgetHub", "log.txt");
        private static readonly object Gate = new();

        public static void Reset()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
                File.WriteAllText(File_, $"=== WidgetHub {DateTime.Now:yyyy-MM-dd HH:mm:ss} dpi={Environment.GetEnvironmentVariable("__none")} ===\r\n");
            }
            catch { }
        }

        public static void W(string msg)
        {
            try
            {
                lock (Gate) File.AppendAllText(File_, $"{DateTime.Now:HH:mm:ss.fff} {msg}\r\n");
            }
            catch { }
        }
    }

    internal static class Net
    {
        // net48 没有 Math.Clamp
        public static int Clamp255(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);
    }

    /// <summary>版本与更新源信息。</summary>
    public static class AppInfo
    {
        public const string Version = "1.0";
        public const string DisplayName = "桌面挂件合集";
        public const string RepoOwner = "feibixunchang";
        // GitHub 不允许仓库名带中文（会被剥成空），所以仓库用 ASCII 名，中文名放在描述/README
        public const string RepoName = "desktop-widget-suite";
        public const string ReleaseApi =
            "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/releases/latest";
        public const string RepoUrl = "https://github.com/" + RepoOwner + "/" + RepoName;
        public const string AssetName = "WidgetHubSetup.exe";
    }

    public class Skin
    {
        public string Name { get; set; } = "";
        public string Folder { get; set; } = "";      // 位于 LocalAppData\Programs 下的目录名
        public string ExeName { get; set; } = "";      // 主程序文件名
        public bool Enabled { get; set; } = true;      // 随管理器自启
        public bool TopMost { get; set; } = false;     // true=浮在最前；false=桌面层(被其它程序盖住)
        public bool ClickThrough { get; set; } = false;// 鼠标穿透
        public int Opacity { get; set; } = 255;        // 0-255，255=不透明
        public int X { get; set; } = -1;
        public int Y { get; set; } = -1;
        public int Width { get; set; } = 0;
        public int Height { get; set; } = 0;
        public bool PositionSaved { get; set; } = false;   // 是否已记录过位置
        public bool Positionless { get; set; } = false;    // 本身没有"位置"概念（如鼠标尾巴的全屏遮罩）

        [JsonIgnore] public Process? Process { get; set; } = null;

        public string FullPath()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Programs", Folder, ExeName);
        }
    }

    /// <summary>
    /// 鼠标拖尾（NeonMouseTrail）的配置：它自己没有源码，托盘图标又被我去掉了，
    /// 所以这里直接读写它的 config.ini，让用户能在管理器里切换特效和配色。
    /// </summary>
    public static class TrailConfig
    {
        public static readonly string[] Effects =
        {
            "彩虹烟雾", "炫彩粒子", "霓虹光带", "星河星尘", "极光幻雾",
            "彩虹泡泡", "彗星拖尾", "火焰流光", "幻彩方块", "光晕脉冲"
        };

        public static readonly string[] Palettes =
        {
            "彩虹", "极光", "霓虹", "火焰", "冰蓝", "黄金", "星云", "糖果"
        };

        public static string[] PerfModes = { "节能", "均衡", "流畅" };

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NeonMouseTrail", "config.ini");

        public static Dictionary<string, string> Load()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return map;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    int i = t.IndexOf('=');
                    if (i <= 0) continue;
                    map[t.Substring(0, i).Trim()] = t.Substring(i + 1).Trim();
                }
            }
            catch { }
            return map;
        }

        public static void Save(Dictionary<string, string> map)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var sb = new StringBuilder();
                sb.AppendLine("# 炫彩鼠标尾巴 配置文件（由 WidgetHub 写入）");
                foreach (var kv in map) sb.AppendLine(kv.Key + "=" + kv.Value);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        public static int GetInt(Dictionary<string, string> map, string key, int fallback)
            => map.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;
    }

    /// <summary>一个可调项的定义。</summary>
    public class SettingDef
    {
        public string Key = "";
        public string Label = "";
        public string Kind = "num";           // num / bool / combo
        public int Min = 0;
        public int Max = 100;
        public int Step = 1;
        public string[] Options = Array.Empty<string>();
        public int ComboOffset = 0;          // 下拉框写入配置时的起始值偏移
    }

    /// <summary>
    /// 各挂件自己的设置：每个挂件都有一份独立配置文件（多数是 settings.json，拖尾是 config.ini）。
    /// 这里统一描述"哪个文件、哪些键、什么类型"，管理器就能给每个挂件都开一个设置面板。
    /// </summary>
    public static class WidgetSettings
    {
        public sealed class Profile
        {
            public string ConfigPath = "";
            public bool Ini = false;
            public List<SettingDef> Defs = new();
        }

        static SettingDef Num(string key, string label, int min, int max, int step = 1)
            => new SettingDef { Key = key, Label = label, Kind = "num", Min = min, Max = max, Step = step };

        static SettingDef Bool(string key, string label)
            => new SettingDef { Key = key, Label = label, Kind = "bool" };

        static SettingDef Combo(string key, string label, string[] options, int offset = 0)
            => new SettingDef { Key = key, Label = label, Kind = "combo", Options = options, ComboOffset = offset };

        /// <summary>取某个挂件的设置档案；返回 null 表示它没有可调项（去它自己界面里改）。</summary>
        public static Profile? For(Skin s)
        {
            string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string exe = s.ExeName ?? "";

            // 5 个共用 Shared/WidgetForm 的挂件：同一套 schema
            string[] shared = { "DesktopCalendar", "DesktopTimer", "DesktopVideo", "DesktopWallet", "RssWidget" };
            if (Array.IndexOf(shared, s.Folder) >= 0 || shared.Any(x => exe.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
            {
                return new Profile
                {
                    ConfigPath = Path.Combine(app, s.Folder, "settings.json"),
                    Defs = new List<SettingDef>
                    {
                        Num("FontSize", "字号", 8, 48),
                        Num("Opacity", "背景不透明度", 0, 100),
                        Bool("DesktopLayer", "贴在桌面层（不被程序遮挡）"),
                        Bool("TopMost", "总在最前"),
                        Bool("Locked", "锁定位置"),
                        Bool("HideOnFullscreen", "全屏程序时隐藏")
                    }
                };
            }

            if (exe.IndexOf("XiaoPuHeWeather", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Profile
                {
                    ConfigPath = Path.Combine(local, "XiaoPuHeWeather", "settings.json"),
                    Defs = new List<SettingDef>
                    {
                        Num("FirstLineFontSize", "主行字号", 12, 48),
                        Num("SecondLineFontSize", "副行字号", 8, 30),
                        Num("OpacityPercent", "背景不透明度", 0, 100),
                        Bool("DesktopLayer", "贴在桌面层（不被程序遮挡）"),
                        Bool("TopMost", "总在最前"),
                        Bool("PauseFullscreen", "全屏程序时暂停")
                    }
                };
            }

            if (exe.IndexOf("NeonMouseTrail", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Profile
                {
                    ConfigPath = TrailConfig.FilePath,
                    Ini = true,
                    Defs = new List<SettingDef>
                    {
                        Combo("Effect", "特效", TrailConfig.Effects, 1),
                        Combo("Palette", "配色", TrailConfig.Palettes),
                        Combo("PerfMode", "性能", TrailConfig.PerfModes),
                        Num("Density", "密度", 1, 10),
                        Num("SizeScale", "粒子大小", 1, 10),
                        Num("Brightness", "亮度", 1, 10),
                        Num("Trail", "拖尾长度", 1, 10),
                        Num("HueSpeed", "变色速度", 0, 200),
                        Bool("ClickBurst", "点击时爆裂"),
                        Bool("OnlyWhenMoving", "仅鼠标移动时显示"),
                        Bool("Enabled", "启用特效")
                    }
                };
            }

            if (exe.IndexOf("DesktopMusic", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Profile
                {
                    ConfigPath = Path.Combine(app, "DesktopMusic", "settings.json"),
                    Defs = new List<SettingDef>
                    {
                        Num("FontSize", "字号", 8, 36),
                        Num("Opacity", "背景不透明度", 0, 100),
                        Num("Volume", "音量", 0, 100),
                        Bool("Muted", "静音"),
                        Bool("TopMost", "总在最前"),
                        Bool("Locked", "锁定位置"),
                        Bool("HideOnFullscreen", "全屏程序时隐藏"),
                        Bool("StartWithWindows", "开机自动启动")
                    }
                };
            }

            if (exe.IndexOf("DesktopRadio", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Profile
                {
                    ConfigPath = Path.Combine(app, "DesktopRadio", "settings.json"),
                    Defs = new List<SettingDef>
                    {
                        Num("FontSize", "字号", 8, 36),
                        Num("Opacity", "背景不透明度", 0, 100),
                        Num("Volume", "音量", 0, 100),
                        Bool("TopMost", "总在最前"),
                        Bool("Locked", "锁定位置"),
                        Bool("HideOnFullscreen", "全屏程序时隐藏"),
                        Bool("StartWithWindows", "开机自动启动"),
                        Bool("AutoPlayOnStart", "启动即播放")
                    }
                };
            }

            if (exe.IndexOf("DesktopMonitor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // 它用的是 settings.ini，可能在程序目录，也可能在 AppData
                string ini1 = Path.Combine(local, "Programs", "DesktopMonitor", "settings.ini");
                string ini2 = Path.Combine(app, "DesktopMonitor", "settings.ini");
                string ini = File.Exists(ini1) ? ini1 : ini2;
                return new Profile
                {
                    ConfigPath = ini,
                    Ini = true,
                    Defs = new List<SettingDef>
                    {
                        Num("FontSize", "字号", 8, 36),
                        Num("Opacity", "背景不透明度", 0, 100),
                        Bool("TopMost", "总在最前"),
                        Bool("Locked", "锁定位置"),
                        Bool("HideOnFullscreen", "全屏程序时隐藏")
                    }
                };
            }

            return null;
        }

        /// <summary>通用 ini 读写（key=value）。</summary>
        public static Dictionary<string, string> ReadIni(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return map;
                foreach (var line in File.ReadAllLines(path))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("#") || t.StartsWith("[")) continue;
                    int i = t.IndexOf('=');
                    if (i <= 0) continue;
                    map[t.Substring(0, i).Trim()] = t.Substring(i + 1).Trim();
                }
            }
            catch { }
            return map;
        }

        public static void WriteIni(string path, Dictionary<string, string> values)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var sb = new StringBuilder();
                sb.AppendLine("# 由 WidgetHub 写入");
                foreach (var kv in values) sb.AppendLine(kv.Key + "=" + kv.Value);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>读出设置值（统一用字符串表示）。</summary>
        public static Dictionary<string, string> Read(Profile p)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(p.ConfigPath)) return map;
                if (p.Ini)
                {
                    foreach (var kv in ReadIni(p.ConfigPath)) map[kv.Key] = kv.Value;
                    return map;
                }
                var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(p.ConfigPath));
                if (node is not System.Text.Json.Nodes.JsonObject obj) return map;
                foreach (var kv in obj)
                {
                    if (kv.Value == null) continue;
                    map[kv.Key] = kv.Value.GetValueKind() == System.Text.Json.JsonValueKind.String
                        ? kv.Value.GetValue<string>()
                        : kv.Value.ToJsonString().Trim('"');
                }
            }
            catch { }
            return map;
        }

        /// <summary>写回设置值（只改我们知道的键，其余原样保留）。</summary>
        public static void Write(Profile p, Dictionary<string, string> values)
        {
            if (p.Ini)
            {
                var map = ReadIni(p.ConfigPath);
                foreach (var kv in values) map[kv.Key] = kv.Value;
                WriteIni(p.ConfigPath, map);
                return;
            }
            try
            {
                System.Text.Json.Nodes.JsonObject obj;
                if (File.Exists(p.ConfigPath))
                {
                    obj = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(p.ConfigPath)) as System.Text.Json.Nodes.JsonObject
                          ?? new System.Text.Json.Nodes.JsonObject();
                }
                else obj = new System.Text.Json.Nodes.JsonObject();

                foreach (var kv in values)
                {
                    var def = p.Defs.FirstOrDefault(d => string.Equals(d.Key, kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (def == null) continue;
                    if (def.Kind == "bool") obj[def.Key] = kv.Value == "1" || string.Equals(kv.Value, "true", StringComparison.OrdinalIgnoreCase);
                    else if (def.Kind == "num") { if (int.TryParse(kv.Value, out int n)) obj[def.Key] = n; }
                    else obj[def.Key] = kv.Value;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(p.ConfigPath)!);
                File.WriteAllText(p.ConfigPath, obj.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }

    public class UpdateInfo
    {
        public string TagVersion = "";
        public string HtmlUrl = "";
        public string AssetUrl = "";
        public string AssetName = "";
        public long AssetId = 0;

        /// <summary>私有仓库必须走 API 附件接口：browser_download_url 会 302 到 S3，
        /// 而 .NET 的 HttpClient 跳转时会丢掉 Authorization 头，下载直接 404。</summary>
        public string AssetApiUrl => AssetId > 0
            ? "https://api.github.com/repos/" + AppInfo.RepoOwner + "/" + AppInfo.RepoName + "/releases/assets/" + AssetId
            : "";
    }

    /// <summary>从 GitHub Releases 检查新版本并下载安装包。</summary>
    public static class UpdateService
    {
        public static async Task<UpdateInfo?> CheckAsync(string token)
        {
            try
            {
                using var client = NewClient();
                using var req = new HttpRequestMessage(HttpMethod.Get, AppInfo.ReleaseApi);
                if (!string.IsNullOrWhiteSpace(token))
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", token.Trim());
                using var resp = await client.SendAsync(req).ConfigureAwait(false);
                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                var node = System.Text.Json.Nodes.JsonNode.Parse(body) as System.Text.Json.Nodes.JsonObject;
                if (node == null) return null;
                var info = new UpdateInfo
                {
                    TagVersion = node["tag_name"]?.ToString().TrimStart('v', 'V') ?? "",
                    HtmlUrl = node["html_url"]?.ToString() ?? AppInfo.RepoUrl
                };
                if (node["assets"] is System.Text.Json.Nodes.JsonArray assets)
                {
                    foreach (var a in assets)
                    {
                        if (a == null) continue;
                        string name = a["name"]?.ToString() ?? "";
                        if (string.Equals(name, AppInfo.AssetName, StringComparison.OrdinalIgnoreCase))
                        {
                            info.AssetName = name;
                            info.AssetUrl = a["browser_download_url"]?.ToString() ?? "";
                            if (a["id"] != null && long.TryParse(a["id"]!.ToString(), out long aid)) info.AssetId = aid;
                            break;
                        }
                    }
                }
                return info;
            }
            catch { return null; }
        }

        /// <summary>下载新版本安装包，返回本地路径。</summary>
        public static async Task<string?> DownloadAsync(UpdateInfo info, string token, Action<int>? progress = null)
        {
            // 优先用 API 附件接口（私有仓库必需），退回 browser_download_url
            string url = info.AssetApiUrl;
            bool apiMode = !string.IsNullOrEmpty(url);
            if (!apiMode) url = info.AssetUrl;
            if (string.IsNullOrEmpty(url)) return null;
            string dir = Path.Combine(Path.GetTempPath(), "WidgetHubUpdate");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, AppInfo.AssetName);
            try
            {
                using var client = NewClient();
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (apiMode) req.Headers.Accept.ParseAdd("application/octet-stream");
                if (!string.IsNullOrWhiteSpace(token))
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", token.Trim());
                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                long total = resp.Content.Headers.ContentLength ?? 0;
                using var input = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                    done += read;
                    if (total > 0) progress?.Invoke((int)Math.Min(100, done * 100 / total));
                }
                return path;
            }
            catch { return null; }
        }

        static HttpClient NewClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WidgetHub/" + AppInfo.Version);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        /// <summary>比较版本号：a 比 b 新返回 true（只比数字部分）。</summary>
        public static bool IsNewer(string a, string b)
        {
            if (!Version.TryParse(Normalize(a), out var va)) return false;
            if (!Version.TryParse(Normalize(b), out var vb)) return false;
            return va > vb;
        }

        static string Normalize(string v)
        {
            var t = (v ?? "").Trim().TrimStart('v', 'V');
            var parts = t.Split('.');
            var nums = new List<int>();
            foreach (var p in parts)
            {
                int n;
                nums.Add(int.TryParse(p, out n) ? n : 0);
            }
            while (nums.Count < 2) nums.Add(0);
            return string.Join(".", nums.Take(3));
        }
    }

    public class Config
    {
        public bool AutoStartManager { get; set; } = false;
        public string UpdateToken { get; set; } = "";      // 私有仓库的更新令牌（可选）
        public bool HideWidgetTrayIcons { get; set; } = false;  // 旧版兜底，10 个挂件已从源码/IL 层去托盘，默认关闭
        public List<Skin> Skins { get; set; } = new();

        public static string Path_ =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WidgetHub", "config.json");

        public static Config Load()
        {
            try
            {
                if (File.Exists(Path_))
                {
                    var c = JsonSerializer.Deserialize<Config>(File.ReadAllText(Path_));
                    if (c != null) { c.Normalize(); return c; }
                }
            }
            catch { }
            return Defaults();
        }

        // 老配置升级：补齐后来新增的字段
        public void Normalize()
        {
            if (Skins == null) Skins = Defaults().Skins;
            foreach (var s in Skins)
            {
                if (string.Equals(s.Folder, "NeonMouseTrail", StringComparison.OrdinalIgnoreCase))
                    s.Positionless = true;
                if (s.X >= 0 && s.Y >= 0 && !s.PositionSaved) s.PositionSaved = true; // 老配置里存过坐标的视为已记录
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
                File.WriteAllText(Path_, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public static Config Defaults()
        {
            var skins = new List<Skin>
            {
                new() { Name = "小普河天气",     Folder = "XiaoPuHeWeather", ExeName = "XiaoPuHeWeather.exe" },
                new() { Name = "桌面万年历",     Folder = "DesktopCalendar", ExeName = "DesktopCalendar.exe" },
                new() { Name = "天气CPU网速",    Folder = "DesktopMonitor",  ExeName = "DesktopMonitor.exe" },
                new() { Name = "桌面音乐",       Folder = "DesktopMusic",    ExeName = "DesktopMusic.exe" },
                new() { Name = "桌面收音机",     Folder = "DesktopRadio",    ExeName = "DesktopRadio.exe" },
                new() { Name = "倒计时闹钟",     Folder = "DesktopTimer",    ExeName = "DesktopTimer.exe" },
                new() { Name = "桌面视频",       Folder = "DesktopVideo",    ExeName = "DesktopVideo.exe" },
                new() { Name = "桌面记账",       Folder = "DesktopWallet",   ExeName = "DesktopWallet.exe" },
                new() { Name = "桌面RSS",        Folder = "RssWidget",       ExeName = "RssWidget.exe" },
                new() { Name = "炫彩鼠标尾巴",   Folder = "NeonMouseTrail",  ExeName = "NeonMouseTrail.exe", Positionless = true },
            };
            return new Config { Skins = skins };
        }
    }

    public static class Win32
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_TOPMOST = 0x00008;
        public const int WS_EX_TRANSPARENT = 0x20;
        [DllImport("user32.dll")]
        public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lRect, IntPtr rgn, uint flags);

        public const uint RDW_INVALIDATE = 0x0001;
        public const uint RDW_ERASE = 0x0004;
        public const uint RDW_ALLCHILDREN = 0x0080;
        public const uint RDW_UPDATENOW = 0x0100;
        public const uint RDW_FRAME = 0x0400;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint LWA_ALPHA = 0x2;
        public static readonly IntPtr HWND_TOPMOST = new(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new(-2);
        public static readonly IntPtr HWND_BOTTOM = new(1);

        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        // 找出该进程"真正的挂件窗口"：取面积最大的可见窗口，但排除铺满某块显示器的透明覆盖层
        // （如鼠标尾巴的遮罩窗，搬动它会破坏效果）。全部是覆盖层时返回 0，交给调用方跳过。
        public static IntPtr FindMainWindow(uint pid)
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = -1;
            var screenSizes = new List<(int W, int H)>();
            try
            {
                foreach (var sc in Screen.AllScreens) screenSizes.Add((sc.Bounds.Width, sc.Bounds.Height));
            }
            catch { }

            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint p);
                if (p != pid) return true;
                if (!IsWindowVisible(hWnd)) return true;
                GetWindowRect(hWnd, out RECT r);
                int w = r.Right - r.Left, hh = r.Bottom - r.Top;
                if (w <= 0 || hh <= 0) return true;
                foreach (var (sw, sh) in screenSizes)
                    if (w >= sw * 9 / 10 && hh >= sh * 9 / 10) return true;   // 全屏覆盖层，忽略
                long area = (long)w * hh;
                if (area > bestArea) { bestArea = area; best = hWnd; }
                return true;
            }, IntPtr.Zero);

            return best;
        }

        public static void ApplyLayout(Skin s, IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            GetWindowRect(hWnd, out RECT before);
            bool moved = false;

            // 顺序很重要！实测：这些挂件只要收到 SetWindowPos（哪怕只改 Z 序、只带 SWP_NOMOVE），
            // 就会把自己的位置重置到 0,0。所以必须"先调层级、最后 MoveWindow"，
            // 让定位成为最后一次操作，才不会被层级调用打回原点。
            if (s.TopMost)
            {
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            }
            else
            {
                // 桌面层：清掉"总在最前"标志并压到 Z 序最底部。
                // 效果 = 挂件只在桌面（壁纸层）显示，打开任何程序都会被程序盖住。
                int ex0 = GetWindowLong(hWnd, GWL_EXSTYLE);
                if ((ex0 & WS_EX_TOPMOST) != 0)
                    SetWindowLong(hWnd, GWL_EXSTYLE, ex0 & ~WS_EX_TOPMOST);
                SetWindowPos(hWnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            }

            int ex = GetWindowLong(hWnd, GWL_EXSTYLE);
            if (s.Opacity < 255 || s.ClickThrough) ex |= WS_EX_LAYERED;
            if (s.ClickThrough) ex |= WS_EX_TRANSPARENT;
            SetWindowLong(hWnd, GWL_EXSTYLE, ex);
            if (s.Opacity < 255)
                SetLayeredWindowAttributes(hWnd, 0, (byte)Net.Clamp255(s.Opacity), LWA_ALPHA);

            // 定位只能用 MoveWindow，且宽高必须传"窗口当前尺寸"：
            // 传记录里的旧尺寸会把挂件改小（万年历就是这样被缩小的）。
            // 坐标是物理像素（管理器已声明 DPI 感知），实测可以直接命中目标。
            if (!s.Positionless && s.X >= 0 && s.Y >= 0)
            {
                GetWindowRect(hWnd, out RECT cur);
                int cw = cur.Right - cur.Left, ch = cur.Bottom - cur.Top;
                if (cw > 0 && ch > 0)
                    moved = MoveWindow(hWnd, s.X, s.Y, cw, ch, true);

                // ⚠️ 这些挂件是"分层窗口"（layered）：窗口被外部移动后，
                // 已经画好的内容不会跟着走，会留在原地（RSS 挂件就出现过
                // "窗口在右上角、内容画在屏幕中间"的错位）。移动完强制重绘一次。
                RedrawWindow(hWnd, IntPtr.Zero, IntPtr.Zero,
                    RDW_INVALIDATE | RDW_ERASE | RDW_UPDATENOW | RDW_FRAME | RDW_ALLCHILDREN);
            }

            GetWindowRect(hWnd, out RECT after);
            Log.W($"  ApplyLayout {s.Name} hwnd={hWnd} target={s.X},{s.Y} ok={moved} " +
                  $"before={before.Left},{before.Top} after={after.Left},{after.Top}");
        }
    }

    // 隐藏其他进程在通知区域（托盘）的图标。标准做法：枚举任务栏的 ToolbarWindow32，
    // 按所属进程匹配后把对应按钮设为 HIDDEN。无需注入，但依赖 Windows 托盘内部结构（Win10 有效，Win11 失效）。
    // 本软件已从挂件源码层面去除托盘，此功能作为 Win10 兼容的兜底保留。
    public static class TrayHider
    {
        private const uint TB_BUTTONCOUNT = 0x418;
        private const uint TB_GETBUTTON = 0x417;
        private const uint TB_SETBUTTONINFO = 0x44F;
        private const int TBSTATE_HIDDEN = 0x0008;
        private const int TBIF_STATE = 0x0001;
        private const uint MEM_COMMIT = 0x1000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_OPERATION = 0x0008;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpClassName, string? lpWindowName);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref TBBUTTONINFO lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint dwAccess, bool bInherit, uint dwProcessId);
        [DllImport("kernel32.dll")] private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll")] private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, uint dwFreeType);
        [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TBBUTTONINFO
        {
            public int cbSize;
            public int dwMask;
            public int idCommand;
            public int fsState;
            public int fsStyle;
            public short cx;
            public IntPtr lpszText;
            public int cchText;
        }

        private static List<IntPtr> GetTrayToolbars()
        {
            var list = new List<IntPtr>();
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero)
            {
                IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
                if (notify != IntPtr.Zero)
                {
                    IntPtr pager = FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
                    IntPtr tb = pager != IntPtr.Zero
                        ? FindWindowEx(pager, IntPtr.Zero, "ToolbarWindow32", null)
                        : FindWindowEx(notify, IntPtr.Zero, "ToolbarWindow32", null);
                    if (tb != IntPtr.Zero) list.Add(tb);
                }
            }
            IntPtr overflow = FindWindow("NotifyIconOverflowWindow", null);
            if (overflow != IntPtr.Zero)
            {
                IntPtr tb = FindWindowEx(overflow, IntPtr.Zero, "ToolbarWindow32", null);
                if (tb != IntPtr.Zero) list.Add(tb);
            }
            IntPtr sec = FindWindow("Shell_SecondaryTrayWnd", null);
            if (sec != IntPtr.Zero)
            {
                IntPtr tb = FindWindowEx(sec, IntPtr.Zero, "ToolbarWindow32", null);
                if (tb != IntPtr.Zero) list.Add(tb);
            }
            return list;
        }

        public static void HideIconsForProcesses(HashSet<uint> pids)
        {
            if (pids.Count == 0) return;
            try
            {
                foreach (var tb in GetTrayToolbars())
                {
                    int count = (int)SendMessage(tb, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
                    if (count <= 0) continue;
                    uint tbPid; GetWindowThreadProcessId(tb, out tbPid);
                    IntPtr hProc = OpenProcess(PROCESS_VM_READ | PROCESS_VM_OPERATION, false, tbPid);
                    if (hProc == IntPtr.Zero) continue;
                    try
                    {
                        int btnSize = 64;
                        IntPtr buf = VirtualAllocEx(hProc, IntPtr.Zero, btnSize, MEM_COMMIT, PAGE_READWRITE);
                        if (buf == IntPtr.Zero) continue;
                        try
                        {
                            for (int i = 0; i < count; i++)
                            {
                                SendMessage(tb, TB_GETBUTTON, (IntPtr)i, buf);
                                var bytes = new byte[btnSize];
                                if (!ReadProcessMemory(hProc, buf, bytes, btnSize, out _)) continue;
                                int idCommand = BitConverter.ToInt32(bytes, 4);
                                uint dwData = BitConverter.ToUInt32(bytes, 12);
                                if (dwData == 0) continue;
                                var hwndBytes = new byte[8];
                                if (!ReadProcessMemory(hProc, (IntPtr)dwData, hwndBytes, 8, out _)) continue;
                                IntPtr hwnd = (IntPtr)BitConverter.ToInt64(hwndBytes, 0);
                                if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) continue;
                                GetWindowThreadProcessId(hwnd, out uint ownerPid);
                                if (pids.Contains(ownerPid))
                                {
                                    var info = new TBBUTTONINFO
                                    {
                                        cbSize = Marshal.SizeOf<TBBUTTONINFO>(),
                                        dwMask = TBIF_STATE,
                                        idCommand = idCommand,
                                        fsState = TBSTATE_HIDDEN
                                    };
                                    SendMessage(tb, TB_SETBUTTONINFO, (IntPtr)idCommand, ref info);
                                }
                            }
                        }
                        finally { VirtualFreeEx(hProc, buf, 0, MEM_RELEASE); }
                    }
                    finally { CloseHandle(hProc); }
                }
            }
            catch { }
        }
    }

    public static class Launcher
    {
        // 按"进程名 + 完整路径"匹配，避免把别处的同名程序当成挂件
        public static Process? FindRunning(Skin s)
        {
            var target = s.FullPath();
            var exeName = Path.GetFileNameWithoutExtension(s.ExeName);
            Process[] list;
            try { list = Process.GetProcessesByName(exeName); } catch { return null; }
            foreach (var p in list)
            {
                bool hit = false;
                try
                {
                    if (p.MainModule != null)
                        hit = string.Equals(p.MainModule.FileName, target, StringComparison.OrdinalIgnoreCase);
                }
                catch { hit = true; }   // 读不到模块信息但进程名一致，视为运行中
                if (hit) { s.Process = p; return p; }
                p.Dispose();
            }
            return null;
        }

        // 以操作系统为准判断是否在运行（不依赖内存里可能过期的 Process 对象）
        public static bool IsRunning(Skin s)
        {
            if (s.Process != null)
            {
                try { if (!s.Process.HasExited) return true; } catch { }
                s.Process = null;
            }
            return FindRunning(s) != null;
        }

        public static Process? Start(Skin s)
        {
            var existing = FindRunning(s);
            if (existing != null)
            {
                s.Process = existing;
                var h0 = Win32.FindMainWindow((uint)existing.Id);
                if (h0 != IntPtr.Zero) Win32.ApplyLayout(s, h0);
                int epid = existing.Id;
                Task.Run(() => ReapplyLoop(s, epid));
                return existing;
            }
            var psi = new ProcessStartInfo
            {
                FileName = s.FullPath(),
                WorkingDirectory = Path.GetDirectoryName(s.FullPath())!
            };
            Process? p = null;
            try { p = Process.Start(psi); } catch (Exception ex) { Log.W($"Start {s.Name} 失败: {ex.Message}"); return null; }
            if (p == null) { Log.W($"Start {s.Name} 返回 null"); return null; }
            s.Process = p;
            Log.W($"Start {s.Name} pid={p.Id} target={s.X},{s.Y} size={s.Width}x{s.Height}");
            // 有些挂件（如天气CPU网速）1.5 秒内还建不出窗口，这里最多等 3.6 秒
            for (int i = 0; i < 30; i++)
            {
                try { if (p.HasExited) { Log.W($"Start {s.Name} 启动后立即退出"); return null; } } catch { return null; }
                var hwnd = Win32.FindMainWindow((uint)p.Id);
                if (hwnd != IntPtr.Zero)
                {
                    Log.W($"Start {s.Name} 第{i}次找到窗口 hwnd={hwnd}");
                    Win32.ApplyLayout(s, hwnd);
                    break;
                }
                if (i == 29) Log.W($"Start {s.Name} 30 次仍未找到窗口");
                Thread.Sleep(120);
            }
            // 挂件启动后自己会"恢复上次位置"，只挪一次会被它覆盖。这里在随后几秒内反复校正，
            // 保证最终停在你记录的位置上。
            int pid = p.Id;
            Task.Run(() => ReapplyLoop(s, pid));
            return p;
        }

        // 挂件启动后会自己"恢复上次位置"（常常是 0,0），会把我们的定位覆盖掉。
        // 这里持续校验：只要偏离目标就再摆一次，连续稳定 3 秒才收工（最多 30 秒）。
        private static void ReapplyLoop(Skin s, int pid)
        {
            if (s.Positionless || s.X < 0 || s.Y < 0) return;
            int stable = 0;
            Log.W($"ReapplyLoop {s.Name} 开始 target={s.X},{s.Y}");
            for (int i = 0; i < 100; i++)
            {
                Thread.Sleep(300);
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (p.HasExited) { Log.W($"ReapplyLoop {s.Name} 进程已退出"); return; }
                    var hwnd = Win32.FindMainWindow((uint)pid);
                    if (hwnd == IntPtr.Zero) { stable = 0; if (i % 10 == 0) Log.W($"ReapplyLoop {s.Name} 未找到窗口 i={i}"); continue; }
                    Win32.GetWindowRect(hwnd, out Win32.RECT r);
                    bool ok = Math.Abs(r.Left - s.X) <= 2 && Math.Abs(r.Top - s.Y) <= 2;
                    if (ok) { if (++stable >= 10) { Log.W($"ReapplyLoop {s.Name} 位置已稳定"); return; } }
                    else { stable = 0; Log.W($"ReapplyLoop {s.Name} 偏离({r.Left},{r.Top}) 重摆"); Win32.ApplyLayout(s, hwnd); }
                }
                catch (Exception ex) { Log.W($"ReapplyLoop {s.Name} 异常 {ex.GetType().Name}"); return; }
            }
            Log.W($"ReapplyLoop {s.Name} 循环结束仍未稳定");
        }

        // 手动"归位"：立刻把正在运行的挂件摆回记录位置，并开启一段校验
        public static bool Reapply(Skin s)
        {
            if (s.Positionless || s.X < 0 || s.Y < 0) return false;
            var p = FindRunning(s);
            if (p == null) return false;
            var hwnd = Win32.FindMainWindow((uint)p.Id);
            if (hwnd == IntPtr.Zero) return false;
            Win32.ApplyLayout(s, hwnd);
            int pid = p.Id;
            Task.Run(() => ReapplyLoop(s, pid));
            return true;
        }

        public static void Stop(Skin s)
        {
            var p = s.Process ?? FindRunning(s);
            if (p != null)
            {
                try
                {
                    if (!p.CloseMainWindow()) p.Kill();
                    if (!p.WaitForExit(3000)) p.Kill();
                }
                catch { try { p.Kill(); } catch { } }
            }
            s.Process = null;
        }
    }

    // 关于 / 设置 / 检查更新
    internal sealed class AboutForm : Form
    {
        readonly Config config;
        readonly Label lblState = new() { AutoSize = true, ForeColor = Color.FromArgb(100, 116, 139), Location = new Point(24, 118) };
        readonly TextBox txtToken = new() { UseSystemPasswordChar = true, Location = new Point(24, 158), Width = 300 };
        readonly Button btnCheck = new() { Text = "检查更新", Location = new Point(24, 196), Width = 96, Height = 30 };
        readonly Button btnInstall = new() { Text = "下载并安装新版本", Location = new Point(128, 196), Width = 136, Height = 30, Enabled = false };
        readonly Button btnRepo = new() { Text = "打开发布页", Location = new Point(272, 196), Width = 96, Height = 30 };
        readonly ProgressBar bar = new() { Style = ProgressBarStyle.Continuous, Location = new Point(24, 238), Width = 344, Height = 14, Visible = false };
        UpdateInfo? latest = null;

        public AboutForm(Config config)
        {
            this.config = config;
            Text = "关于 · 设置";
            ClientSize = new Size(392, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei", 9F);

            var title = new Label
            {
                Text = AppInfo.DisplayName + "  WidgetHub",
                Font = new Font("Microsoft YaHei", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                AutoSize = true,
                Location = new Point(24, 22)
            };
            var ver = new Label
            {
                Text = "当前版本：v" + AppInfo.Version + "     发布仓库：" + AppInfo.RepoOwner + "/" + AppInfo.RepoName,
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = true,
                Location = new Point(24, 58)
            };
            var tokenLabel = new Label
            {
                Text = "更新令牌（私有仓库需要，填 GitHub PAT 即可）：",
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = true,
                Location = new Point(24, 138)
            };
            txtToken.Text = config.UpdateToken ?? "";

            btnCheck.Click += async (_, _) => await CheckAsync();
            btnInstall.Click += async (_, _) => await InstallAsync();
            btnRepo.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(latest?.HtmlUrl ?? AppInfo.RepoUrl) { UseShellExecute = true }); } catch { }
            };

            var close = new Button { Text = "关闭", Location = new Point(292, 262), Width = 76, Height = 28, DialogResult = DialogResult.Cancel };

            Controls.AddRange(new Control[]
            {
                title, ver, lblState, tokenLabel, txtToken, btnCheck, btnInstall, btnRepo, bar, close
            });
            lblState.Text = "点击「检查更新」查看是否有新版本。";
        }

        async Task CheckAsync()
        {
            btnCheck.Enabled = false;
            lblState.Text = "正在检查…";
            try
            {
                config.UpdateToken = txtToken.Text.Trim();
                config.Save();
                var info = await UpdateService.CheckAsync(config.UpdateToken);
                if (info == null)
                {
                    lblState.Text = "检查失败：无法访问发布仓库（私有仓库请填写有效令牌）";
                    return;
                }
                latest = info;
                if (string.IsNullOrEmpty(info.TagVersion))
                {
                    lblState.Text = "仓库里还没有发布版本。";
                    btnInstall.Enabled = false;
                    return;
                }
                if (UpdateService.IsNewer(info.TagVersion, AppInfo.Version))
                {
                    lblState.Text = $"发现新版本 v{info.TagVersion}（当前 v{AppInfo.Version}），点「下载并安装新版本」即可升级。";
                    btnInstall.Enabled = !string.IsNullOrEmpty(info.AssetUrl);
                }
                else
                {
                    lblState.Text = $"已经是最新版本（v{AppInfo.Version}）。";
                    btnInstall.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                lblState.Text = "检查出错：" + ex.Message;
            }
            finally { btnCheck.Enabled = true; }
        }

        async Task InstallAsync()
        {
            if (latest == null) return;
            btnInstall.Enabled = false;
            btnCheck.Enabled = false;
            bar.Visible = true;
            lblState.Text = "正在下载新版本…";
            try
            {
                int last = -1;
                var path = await UpdateService.DownloadAsync(latest, config.UpdateToken, pct =>
                {
                    if (pct == last) return;
                    last = pct;
                    try { bar.Value = pct; } catch { }
                });
                if (path == null)
                {
                    lblState.Text = "下载失败，请检查网络或令牌权限（Contents: read）。";
                    return;
                }
                lblState.Text = "下载完成，正在启动安装程序…";
                // 安装程序会结束旧的管理器并覆盖安装
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! });
                Application.Exit();
            }
            catch (Exception ex)
            {
                lblState.Text = "更新失败：" + ex.Message;
            }
            finally
            {
                btnCheck.Enabled = true;
                btnInstall.Enabled = true;
            }
        }
    }

    // 通用挂件设置对话框：按各挂件自己的配置项动态生成
    internal sealed class WidgetSettingsForm : Form
    {
        readonly WidgetSettings.Profile profile;
        readonly Dictionary<string, Control> editors = new(StringComparer.OrdinalIgnoreCase);

        public WidgetSettingsForm(string title, WidgetSettings.Profile profile, Dictionary<string, string> current)
        {
            this.profile = profile;
            Text = title + " · 设置";
            ClientSize = new Size(430, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei", 9F);

            var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(18, 14, 18, 8) };
            int y = 6;
            foreach (var def in profile.Defs)
            {
                Control editor;
                if (def.Kind == "bool")
                {
                    var cb = new CheckBox { Text = def.Label, AutoSize = true, Location = new Point(18, y) };
                    cb.Checked = current.TryGetValue(def.Key, out var bv) && (bv == "1" || string.Equals(bv, "true", StringComparison.OrdinalIgnoreCase));
                    editor = cb;
                }
                else if (def.Kind == "combo")
                {
                    var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(150, y - 3), Width = 230 };
                    cb.Items.AddRange(def.Options);
                    int idx = 0;
                    if (current.TryGetValue(def.Key, out var cv) && int.TryParse(cv, out int cn)) idx = cn - def.ComboOffset;
                    cb.SelectedIndex = Math.Min(Math.Max(idx, 0), def.Options.Length - 1);
                    editor = cb;
                    panel.Controls.Add(new Label { Text = def.Label, Location = new Point(18, y), AutoSize = true });
                }
                else
                {
                    var nu = new NumericUpDown
                    {
                        Location = new Point(150, y - 3),
                        Width = 100,
                        Minimum = def.Min,
                        Maximum = def.Max
                    };
                    if (current.TryGetValue(def.Key, out var nv) && int.TryParse(nv, out int nn))
                        nu.Value = Math.Min(Math.Max(nn, def.Min), def.Max);
                    editor = nu;
                    panel.Controls.Add(new Label { Text = def.Label, Location = new Point(18, y), AutoSize = true });
                }
                editors[def.Key] = editor;
                if (def.Kind != "bool") panel.Controls.Add(editor);
                else panel.Controls.Add(editor);
                y += def.Kind == "bool" ? 30 : 34;
            }

            var tip = new Label
            {
                Text = "配置文件：" + profile.ConfigPath,
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(18, y + 4),
                Size = new Size(396, 34)
            };
            panel.Controls.Add(tip);

            var ok = new Button { Text = "保存并重启挂件", Location = new Point(222, 372), Size = new Size(120, 30) };
            var cancel = new Button { Text = "取消", Location = new Point(348, 372), Size = new Size(64, 30), DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(panel);
            Controls.Add(ok);
            Controls.Add(cancel);
        }

        /// <summary>把界面上的值收集成"键→值"。</summary>
        public Dictionary<string, string> Values()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var def in profile.Defs)
            {
                if (!editors.TryGetValue(def.Key, out var c)) continue;
                if (def.Kind == "bool") map[def.Key] = ((CheckBox)c).Checked ? "1" : "0";
                else if (def.Kind == "combo")
                {
                    int i = ((ComboBox)c).SelectedIndex;
                    map[def.Key] = (i + def.ComboOffset).ToString();
                }
                else map[def.Key] = ((NumericUpDown)c).Value.ToString();
            }
            return map;
        }
    }

    // 鼠标拖尾特效设置对话框
    internal sealed class TrailSettingsForm : Form
    {
        public int EffectIndex { get; private set; }
        public int PaletteIndex { get; private set; }
        public int PerfIndex { get; private set; }

        readonly ComboBox cEffect = new() { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ComboBox cPalette = new() { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ComboBox cPerf = new() { DropDownStyle = ComboBoxStyle.DropDownList };

        public TrailSettingsForm(Dictionary<string, string> map)
        {
            Text = "鼠标拖尾 · 特效设置";
            ClientSize = new Size(360, 236);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei", 9F);

            cEffect.Items.AddRange(TrailConfig.Effects);
            cPalette.Items.AddRange(TrailConfig.Palettes);
            cPerf.Items.AddRange(TrailConfig.PerfModes);

            int eff = Math.Max(1, TrailConfig.GetInt(map, "Effect", 1)) - 1;
            EffectIndex = Math.Min(Math.Max(eff, 0), TrailConfig.Effects.Length - 1);
            PaletteIndex = Math.Min(Math.Max(TrailConfig.GetInt(map, "Palette", 0), 0), TrailConfig.Palettes.Length - 1);
            PerfIndex = Math.Min(Math.Max(TrailConfig.GetInt(map, "PerfMode", 1), 0), TrailConfig.PerfModes.Length - 1);
            cEffect.SelectedIndex = EffectIndex;
            cPalette.SelectedIndex = PaletteIndex;
            cPerf.SelectedIndex = PerfIndex;

            var l1 = new Label { Text = "特效", Location = new Point(24, 34), AutoSize = true };
            var l2 = new Label { Text = "配色", Location = new Point(24, 76), AutoSize = true };
            var l3 = new Label { Text = "性能", Location = new Point(24, 118), AutoSize = true };
            cEffect.SetBounds(100, 30, 230, 26);
            cPalette.SetBounds(100, 72, 230, 26);
            cPerf.SetBounds(100, 114, 230, 26);

            var tip = new Label
            {
                Text = "更多参数（密度/大小/亮度/拖尾/变色速度）可直接编辑：\r\n" + TrailConfig.FilePath,
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(24, 156),
                Size = new Size(312, 34)
            };
            var ok = new Button { Text = "确定并应用", Location = new Point(164, 196), Size = new Size(90, 30) };
            var cancel = new Button { Text = "取消", Location = new Point(260, 196), Size = new Size(76, 30), DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) =>
            {
                EffectIndex = Math.Max(cEffect.SelectedIndex, 0);
                PaletteIndex = Math.Max(cPalette.SelectedIndex, 0);
                PerfIndex = Math.Max(cPerf.SelectedIndex, 0);
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.AddRange(new Control[] { l1, l2, l3, cEffect, cPalette, cPerf, tip, ok, cancel });
        }
    }

    // 现代风格圆角按钮：自绘 + 抗锯齿路径填充。
    // 不能用 Region 裁剪圆角——Region 不做抗锯齿，四角会被"啃"成锯齿缺口（看起来残缺）。
    public class ModernButton : Button
    {
        public Color NormalColor = Color.FromArgb(59, 130, 246);
        public Color HoverColor = Color.FromArgb(37, 99, 235);
        public int CornerRadius = 8;
        private bool hovered;

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.White;   // 与工具栏同色：圆角外侧不会露出异色方块
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei", 10F);
            Cursor = Cursors.Hand;
        }

        internal static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            // 用三次贝塞尔曲线构造圆角矩形。
            // 不要再用 AddArc×4+CloseFigure：本机 GDI+ 生成的这种路径会自相交，
            // 实屏渲染时按钮左侧会出现一个 V 形白色缺口（用户看到的"残缺"）。
            // 实测对照：AddArc 构造 6 种写法全部带缺口，贝塞尔构造完全干净。
            float rr = Math.Max(0f, Math.Min(radius, Math.Min(r.Width, r.Height) / 2f));
            if (rr <= 0.5f)
            {
                var flat = new GraphicsPath();
                flat.AddRectangle(r);
                return flat;
            }
            float k = rr * 0.5523f;   // 圆弧的三次贝塞尔逼近常数
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

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (r.Width > 0 && r.Height > 0)
            {
                using var gp = RoundPath(r, CornerRadius);
                using var br = new SolidBrush(Enabled ? (hovered ? HoverColor : NormalColor) : Color.FromArgb(203, 213, 225));
                g.FillPath(br, gp);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle,
                Enabled ? ForeColor : Color.FromArgb(248, 250, 252),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    }

    public class ManagerForm : Form
    {
        private static readonly Color CBack = Color.FromArgb(245, 247, 250);
        private static readonly Color CWhite = Color.White;
        private static readonly Color CText = Color.FromArgb(31, 41, 55);
        private static readonly Color CMuted = Color.FromArgb(100, 116, 139);
        private static readonly Color CAccent = Color.FromArgb(59, 130, 246);
        private static readonly Color CAccentD = Color.FromArgb(37, 99, 235);
        private static readonly Color CSecondary = Color.FromArgb(100, 116, 139);
        private static readonly Color CSecondaryD = Color.FromArgb(71, 85, 105);
        private static readonly Color CGreen = Color.FromArgb(22, 163, 74);

        private DataGridView grid = null!;
        private NotifyIcon tray = null!;
        private Config config = null!;
        private CheckBox chkAutoManager = null!;
        private System.Windows.Forms.Timer statusTimer = null!;
        private System.Windows.Forms.Timer layerTimer = null!;
        private (int Row, int Col) hoverCell = (-1, -1);
        private bool silent = false;

        public ManagerForm(bool headless = false)
        {
            config = Config.Load();
            BuildUI();
            if (headless)
            {
                // 仅用于离屏渲染自检：不建托盘、不拉起挂件
                RefreshGrid();
                return;
            }
            SetupTray();
            Log.Reset();
            Log.W($"管理器启动 dpi={DeviceDpi} 主屏={Screen.PrimaryScreen?.Bounds} 屏数={Screen.AllScreens.Length}");
            // 拉起挂件放到后台线程：逐个等窗口创建会阻塞 UI，界面要十几秒才出来
            var toStart = new List<Skin>(config.Skins);
            Task.Run(() =>
            {
                foreach (var s in toStart) if (s.Enabled) Launcher.Start(s);
            });
            UpdateStatuses();
            ApplyAutoStartManager();
            StartStatusTimer();
        }

        private void BuildUI()
        {
            Text = "WidgetHub";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei", 10F);
            Width = 1080; Height = 660;
            MinimumSize = new Size(880, 520);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            BackColor = CBack;
            Padding = new Padding(0);

            // 顶部品牌栏（渐变）。文字按实测字高纵向堆叠 + 随 DPI 缩放坐标，避免标题与副标题重叠
            var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = CAccent };
            header.Paint += (_, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (var br = new LinearGradientBrush(header.ClientRectangle,
                           Color.FromArgb(70, 140, 250), Color.FromArgb(37, 99, 235), 0f))
                    g.FillRectangle(br, header.ClientRectangle);

                float k = header.DeviceDpi / 96f;
                int iconSize = (int)(40 * k);
                int iconLeft = (int)(20 * k);
                int iconTop = (header.Height - iconSize) / 2;
                try
                {
                    using var ic = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                    if (ic != null)
                    {
                        using var bmp = ic.ToBitmap();
                        g.DrawImage(bmp, new Rectangle(iconLeft, iconTop, iconSize, iconSize));
                    }
                }
                catch { }

                using var f1 = new Font("Microsoft YaHei", 17F, FontStyle.Bold);
                using var f2 = new Font("Microsoft YaHei", 9F);
                using var sf = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
                const string title = "WidgetHub";
                const string sub = "桌面挂件统一管理器 · 一个图标管理全部挂件 · 默认贴桌面层，不遮挡任何程序";
                var sz1 = g.MeasureString(title, f1, PointF.Empty, sf);
                var sz2 = g.MeasureString(sub, f2, PointF.Empty, sf);
                float textLeft = iconLeft + iconSize + 14 * k;
                float gap = 6 * k;
                float total = sz1.Height + gap + sz2.Height;
                float y = (header.Height - total) / 2f;
                g.DrawString(title, f1, Brushes.White, textLeft, y, sf);
                using (var sb = new SolidBrush(Color.FromArgb(226, 236, 255)))
                    g.DrawString(sub, f2, sb, textLeft + 2 * k, y + sz1.Height + gap, sf);

                // 右上角：版本号（点一下可检查更新）
                using (var fv = new Font("Microsoft YaHei", 10F, FontStyle.Bold))
                using (var sb2 = new SolidBrush(Color.FromArgb(226, 236, 255)))
                {
                    string vText = "v" + AppInfo.Version;
                    var vSize = g.MeasureString(vText, fv);
                    float vx = header.Width - vSize.Width - 18 * k;
                    g.DrawString(vText, fv, Brushes.White, vx, 18 * k, sf);

                    string hText = "点击检查更新";
                    var hSize = g.MeasureString(hText, f2);
                    g.DrawString(hText, f2, sb2, header.Width - hSize.Width - 18 * k, 18 * k + vSize.Height + 4 * k, sf);
                }
            };
            header.Cursor = Cursors.Hand;
            header.Click += (_, _) => ShowAbout();

            // 工具栏：用 FlowLayoutPanel 自动排版，DPI 缩放下也不会互相压字
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 58,
                BackColor = CWhite,
                WrapContents = false,
                Padding = new Padding(18, 12, 18, 10),
                FlowDirection = FlowDirection.LeftToRight
            };
            toolbar.Paint += (_, e) =>
            {
                using var pen = new Pen(Color.FromArgb(228, 232, 238));
                e.Graphics.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
            };

            ModernButton B(string text, Action act, Color normal, Color hover)
            {
                var b = new ModernButton
                {
                    Text = text,
                    NormalColor = normal,
                    HoverColor = hover,
                    Width = 104,
                    Height = 34,
                    Margin = new Padding(0, 0, 10, 0)
                };
                b.Click += (_, _) => act();
                return b;
            }
            toolbar.Controls.Add(B("全部启动", () => { foreach (var s in config.Skins) Launcher.Start(s); UpdateStatuses(); }, CAccent, CAccentD));
            toolbar.Controls.Add(B("全部停止", () => { foreach (var s in config.Skins) Launcher.Stop(s); UpdateStatuses(); }, Color.FromArgb(239, 68, 68), Color.FromArgb(220, 38, 38)));
            toolbar.Controls.Add(B("全部归位", () => { foreach (var s in config.Skins) Launcher.Reapply(s); UpdateStatuses(); }, Color.FromArgb(16, 185, 129), Color.FromArgb(5, 150, 105)));
            toolbar.Controls.Add(B("保存配置", () => { SyncAll(); config.Save(); MessageBox.Show("配置已保存。", "WidgetHub"); }, CSecondary, CSecondaryD));
            toolbar.Controls.Add(B("刷新状态", () => { RefreshGrid(); ClearHighlight(); UpdateStatuses(); }, CSecondary, CSecondaryD));
            toolbar.Controls.Add(B("拖尾特效…", () => ShowTrailSettings(), Color.FromArgb(139, 92, 246), Color.FromArgb(124, 58, 237)));

            chkAutoManager = new CheckBox
            {
                Text = "管理器开机自启",
                AutoSize = true,
                Margin = new Padding(12, 8, 0, 0),
                Font = new Font("Microsoft YaHei", 10F)
            };
            chkAutoManager.Checked = config.AutoStartManager;
            chkAutoManager.CheckedChanged += (_, _) => { config.AutoStartManager = chkAutoManager.Checked; ApplyAutoStartManager(); };
            toolbar.Controls.Add(chkAutoManager);

            // 表格
            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = CWhite,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.None,
                RowTemplate = { Height = 40 },
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeight = 42,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                DefaultCellStyle =
                {
                    Font = new Font("Microsoft YaHei", 10F),
                    ForeColor = CText,
                    BackColor = CWhite,
                    SelectionBackColor = Color.FromArgb(219, 234, 254),
                    SelectionForeColor = CText,
                    Padding = new Padding(6, 0, 6, 0)
                },
                ColumnHeadersDefaultCellStyle =
                {
                    Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold),
                    ForeColor = CMuted,
                    BackColor = CWhite,
                    SelectionBackColor = CWhite,      // 表头不跟随当前列泛蓝
                    SelectionForeColor = CMuted,
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                },
                AlternatingRowsDefaultCellStyle = { BackColor = Color.FromArgb(247, 249, 252) },
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            grid.CellContentClick += Grid_CellContentClick;
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CellFormatting += Grid_CellFormatting;
            grid.CellPainting += Grid_CellPainting;
            grid.CellMouseMove += (_, e) =>
            {
                if (e.RowIndex >= 0 && (grid.Columns[e.ColumnIndex].Name == "Toggle" || grid.Columns[e.ColumnIndex].Name == "Capture" || grid.Columns[e.ColumnIndex].Name == "Settings"))
                {
                    if (hoverCell != (e.RowIndex, e.ColumnIndex))
                    {
                        var prev = hoverCell; hoverCell = (e.RowIndex, e.ColumnIndex);
                        if (prev.Row >= 0) grid.InvalidateCell(prev.Col, prev.Row);
                        grid.InvalidateCell(e.ColumnIndex, e.RowIndex);
                    }
                }
            };
            grid.CellMouseLeave += (_, _) => { if (hoverCell.Row >= 0) { var p = hoverCell; hoverCell = (-1, -1); grid.InvalidateCell(p.Col, p.Row); } };

            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "挂件（皮肤）", ReadOnly = true, FillWeight = 24, MinimumWidth = 130 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "自启", FillWeight = 8, MinimumWidth = 50 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "TopMost", HeaderText = "置顶", FillWeight = 8, MinimumWidth = 56 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "ClickThrough", HeaderText = "穿透", FillWeight = 8, MinimumWidth = 50 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Opacity", HeaderText = "透明度", FillWeight = 9, MinimumWidth = 60 });
            grid.Columns.Add(new DataGridViewButtonColumn { Name = "Toggle", HeaderText = "启动 / 停止", FillWeight = 12, MinimumWidth = 100 });
            grid.Columns.Add(new DataGridViewButtonColumn { Name = "Capture", HeaderText = "位置记忆", FillWeight = 12, MinimumWidth = 100 });
            grid.Columns.Add(new DataGridViewButtonColumn { Name = "Settings", HeaderText = "设置", FillWeight = 8, MinimumWidth = 74 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "状态", ReadOnly = true, FillWeight = 9, MinimumWidth = 64 });

            Controls.Add(grid);
            Controls.Add(toolbar);
            Controls.Add(header);

            RefreshGrid();
            ClearHighlight();   // 默认不选中任何行，否则首行/首列表头会被系统高亮色冲淡
            // 句柄创建后 DataGridView 会自动把首行设为当前行并选中，这里在显示时再清一次
            Shown += (_, _) => ClearHighlight();
        }

        // 彻底清掉高亮：ClearSelection 只清"选中"，CurrentCell 仍在，
        // 表头会按"当前列"泛蓝。两件事都做，界面才干净。
        private void ClearHighlight()
        {
            grid.ClearSelection();
            try { grid.CurrentCell = null; } catch { }
        }

        private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (grid.Columns[e.ColumnIndex].Name == "Status")
            {
                var row = grid.Rows[e.RowIndex];
                if (row.Tag is Skin s)
                {
                    e.CellStyle.ForeColor = Launcher.IsRunning(s) ? CGreen : CMuted;
                    e.CellStyle.Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold);
                }
            }
        }

        private static readonly Font CellBtnFont = new("Microsoft YaHei", 9.5F);

        private void RoundedButton(Graphics g, Rectangle bounds, Color fill, string text, bool hover, bool warn = false)
        {
            var r = new Rectangle(bounds.X + 9, bounds.Y + 6, bounds.Width - 18, bounds.Height - 12);
            if (r.Width < 20 || r.Height < 10) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int rad = Math.Min(7, r.Height / 2);
            using (var gp = ModernButton.RoundPath(r, rad))
            using (var br = new SolidBrush(hover ? CAccentD : fill))
                g.FillPath(br, gp);
            var color = warn ? Color.FromArgb(255, 244, 214) : Color.White;
            TextRenderer.DrawText(g, text, CellBtnFont, r, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var name = grid.Columns[e.ColumnIndex].Name;
            if (name != "Toggle" && name != "Capture" && name != "Settings") return;

            // 关键：按"这一行真实的底色"填充。以前传 selected=true 会把整格刷成选中蓝，
            // 圆角外侧就露出蓝色方块，看起来像按钮残缺。只有真正被选中的行才用选中色。
            bool selected = grid.Rows[e.RowIndex].Selected;
            Color back = selected ? Color.FromArgb(219, 234, 254) : e.CellStyle.BackColor;
            using (var bb = new SolidBrush(back)) e.Graphics.FillRectangle(bb, e.CellBounds);

            var s = grid.Rows[e.RowIndex].Tag as Skin;
            bool hover = hoverCell == (e.RowIndex, e.ColumnIndex);
            Color fill;
            string text;
            if (name == "Settings")
            {
                RoundedButton(e.Graphics, e.CellBounds, Color.FromArgb(139, 92, 246), "设置", hover);
                e.Handled = true;
                return;
            }
            if (name == "Capture")
            {
                if (s != null && s.Positionless) { fill = Color.FromArgb(148, 163, 184); text = "无需记录"; }
                else if (s != null && s.PositionSaved) { fill = CGreen; text = "已记录位置"; }
                else { fill = CSecondary; text = "记录位置"; }
            }
            else
            {
                fill = CAccent;
                text = e.Value?.ToString() ?? "启动";
            }
            RoundedButton(e.Graphics, e.CellBounds, fill, text, hover);
            e.Handled = true;
        }

        private void RefreshGrid()
        {
            silent = true;
            grid.Rows.Clear();
            foreach (var s in config.Skins)
            {
                bool run = Launcher.IsRunning(s);
                string capText = s.Positionless ? "无需记录" : (s.PositionSaved ? "已记录位置" : "记录位置");
                int i = grid.Rows.Add(s.Name, s.Enabled, s.TopMost, s.ClickThrough, s.Opacity,
                                      run ? "停止" : "启动", capText, run ? "运行中" : "已停止");
                grid.Rows[i].Tag = s;
            }
            silent = false;
        }

        // 只刷新"状态"和"启动/停止"两列，不重建整张表，避免闪烁和选中丢失
        private void UpdateStatuses()
        {
            if (grid == null || grid.IsDisposed) return;
            silent = true;
            try
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.Tag is not Skin s) continue;
                    bool run = Launcher.IsRunning(s);
                    bool changed = false;
                    string st = run ? "运行中" : "已停止";
                    if (!Equals(row.Cells["Status"].Value as string, st)) { row.Cells["Status"].Value = st; changed = true; }
                    string tg = run ? "停止" : "启动";
                    if (!Equals(row.Cells["Toggle"].Value as string, tg)) { row.Cells["Toggle"].Value = tg; changed = true; }
                    string cap = s.Positionless ? "无需记录" : (s.PositionSaved ? "已记录位置" : "记录位置");
                    if (!Equals(row.Cells["Capture"].Value as string, cap)) { row.Cells["Capture"].Value = cap; changed = true; }
                    if (changed) grid.InvalidateRow(row.Index);
                }
            }
            finally { silent = false; }
        }

        private void SyncRow(Skin s, DataGridViewRow row)
        {
            s.Enabled = row.Cells["Enabled"].Value is bool be && be;
            s.TopMost = row.Cells["TopMost"].Value is bool bt && bt;
            s.ClickThrough = row.Cells["ClickThrough"].Value is bool bc && bc;
            if (row.Cells["Opacity"].Value != null && int.TryParse(row.Cells["Opacity"].Value.ToString(), out int o))
                s.Opacity = Net.Clamp255(o);
        }

        private void SyncAll()
        {
            grid.EndEdit();
            foreach (DataGridViewRow row in grid.Rows)
                if (row.Tag is Skin s) SyncRow(s, row);
        }

        private void Grid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = grid.Rows[e.RowIndex];
            if (row.Tag is not Skin s) return;
            var col = grid.Columns[e.ColumnIndex].Name;
            if (col == "Toggle")
            {
                if (Launcher.IsRunning(s)) Launcher.Stop(s);
                else Launcher.Start(s);
                UpdateStatuses();
                ClearHighlight();
            }
            else if (col == "Settings")
            {
                ShowWidgetSettings(s);
                ClearHighlight();
            }
            else if (col == "Capture")
            {
                if (s.Positionless)
                {
                    MessageBox.Show($"「{s.Name}」是跟随鼠标的全屏特效，没有固定的窗口位置，因此无需记录。",
                        "WidgetHub", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var p = Launcher.FindRunning(s);
                if (p == null)
                {
                    MessageBox.Show($"「{s.Name}」当前没有运行，请先启动它，再拖动到想要的位置后点「记录位置」。",
                        "WidgetHub", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var hwnd = Win32.FindMainWindow((uint)p.Id);
                if (hwnd == IntPtr.Zero || !Win32.GetWindowRect(hwnd, out Win32.RECT r))
                {
                    MessageBox.Show($"找不到「{s.Name}」的挂件窗口，无法记录位置。", "WidgetHub",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                s.X = r.Left; s.Y = r.Top; s.Width = r.Right - r.Left; s.Height = r.Bottom - r.Top;
                s.PositionSaved = true;
                config.Save();
                // 记录后立刻摆一次并开启校验，保证挂件不会又飘回原处
                Launcher.Reapply(s);
                grid.InvalidateRow(e.RowIndex);
                UpdateStatuses();
                ClearHighlight();
            }
        }

        private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (silent) return;
            if (e.RowIndex < 0) return;
            var row = grid.Rows[e.RowIndex];
            if (row.Tag is not Skin s) return;
            SyncRow(s, row);
            var col = grid.Columns[e.ColumnIndex].Name;
            if ((col == "TopMost" || col == "ClickThrough" || col == "Opacity") && Launcher.IsRunning(s))
            {
                var p = Launcher.FindRunning(s);
                if (p != null)
                {
                    var hwnd = Win32.FindMainWindow((uint)p.Id);
                    if (hwnd != IntPtr.Zero) Win32.ApplyLayout(s, hwnd);
                }
            }
            // 勾选后该格会变成"当前单元格"，行跟着被选中，按钮格又会泛蓝，这里再清一次
            ClearHighlight();
        }

        private bool _reallyExit = false;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SyncAll();
            config.Save();
            // 用户点右上角 X：收进托盘继续运行（不退出），真正的退出走托盘菜单"退出"
            if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        private void SetupTray()
        {
            tray = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                Text = "WidgetHub 桌面挂件管理器",
                Visible = true
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开管理器", null, (_, _) => ShowManager());
            menu.Items.Add("全部启动", null, (_, _) => { foreach (var s in config.Skins) Launcher.Start(s); });
            menu.Items.Add("全部停止", null, (_, _) => { foreach (var s in config.Skins) Launcher.Stop(s); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("拖尾特效…", null, (_, _) => ShowTrailSettings());
            menu.Items.Add("卸载桌面挂件总管…", null, (_, _) => LaunchUninstaller());
            menu.Items.Add("退出（同时关闭所有挂件）", null, (_, _) =>
            {
                foreach (var s in config.Skins) Launcher.Stop(s);
                _reallyExit = true;
                tray.Visible = false;
                Close();
                Application.Exit();
            });
            menu.Items.Add("仅退出管理器（挂件继续运行）", null, (_, _) =>
            {
                _reallyExit = true;
                tray.Visible = false;
                Close();
                Application.Exit();
            });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (_, _) => ShowManager();
        }

        private void ShowManager()
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
        }

        // 关于 / 检查更新
        private void ShowAbout()
        {
            using var dlg = new AboutForm(config);
            dlg.ShowDialog(this);
        }

        // 打开某个挂件自己的设置面板：读它自己的配置文件 -> 改 -> 存 -> 重启它
        private void ShowWidgetSettings(Skin s)
        {
            var profile = WidgetSettings.For(s);
            if (profile == null)
            {
                MessageBox.Show($"「{s.Name}」没有可直接调整的设置项。\r\n" +
                                "请在桌面上直接右键这个挂件，它的右键菜单里就是它的设置。",
                                "设置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!File.Exists(profile.ConfigPath))
            {
                MessageBox.Show($"「{s.Name}」还没有生成配置文件（{Path.GetFileName(profile.ConfigPath)}），\r\n" +
                                "它的设置要等它正常退出一次才会保存。\r\n\r\n" +
                                "在此之前，可以直接右键桌面上的这个挂件来调整。",
                    "设置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                var current = WidgetSettings.Read(profile);
                using var dlg = new WidgetSettingsForm(s.Name, profile, current);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                WidgetSettings.Write(profile, dlg.Values());

                // 配置文件是它启动时读的，保存后重启一次才生效
                bool wasRunning = Launcher.IsRunning(s);
                if (wasRunning) Launcher.Stop(s);
                Thread.Sleep(500);
                Launcher.Start(s);
                UpdateStatuses();
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存设置失败：" + ex.Message, "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // 鼠标拖尾的特效设置：它没有源码、托盘图标又被去掉了，
        // 所以这里直接改它的 config.ini 再重启它。
        private void ShowTrailSettings()
        {
            try
            {
                var map = TrailConfig.Load();
                using var dlg = new TrailSettingsForm(map);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                map["Effect"] = dlg.EffectIndex.ToString();
                map["Palette"] = dlg.PaletteIndex.ToString();
                map["PerfMode"] = dlg.PerfIndex.ToString();
                map["ShowTray"] = "0";        // 继续保持"托盘只有一个图标"
                TrailConfig.Save(map);

                // 配置文件是它启动时读的，所以要重启一下才生效
                var trail = config.Skins.FirstOrDefault(s => s.ExeName.IndexOf("NeonMouseTrail", StringComparison.OrdinalIgnoreCase) >= 0);
                if (trail != null)
                {
                    Launcher.Stop(trail);
                    Thread.Sleep(600);
                    Launcher.Start(trail);
                }
                UpdateStatuses();
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置拖尾特效失败：" + ex.Message, "拖尾特效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // 托盘菜单里的"卸载"：启动安装目录下的卸载器
        private void LaunchUninstaller()
        {
            try
            {
                // 卸载器在 <安装目录>\Uninstall\WidgetHubUninstall.exe
                var dir = AppContext.BaseDirectory.TrimEnd('\\');
                var candidates = new[]
                {
                    Path.Combine(dir, "Uninstall", "WidgetHubUninstall.exe"),
                    Path.Combine(Path.GetDirectoryName(dir) ?? dir, "Uninstall", "WidgetHubUninstall.exe")
                };
                foreach (var path in candidates)
                {
                    if (!File.Exists(path)) continue;
                    _reallyExit = true;
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! });
                    tray.Visible = false;
                    Close();
                    Application.Exit();
                    return;
                }
                MessageBox.Show("没有找到卸载程序（Uninstall\\WidgetHubUninstall.exe）。\r\n" +
                                "可以到开始菜单里找“卸载桌面挂件总管”，或直接删除安装目录。",
                                "卸载", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch { }
        }

        // 定时把"真实运行状态"同步到表格，挂件被手动关掉/异常退出也能及时反映
        private void StartStatusTimer()
        {
            statusTimer = new System.Windows.Forms.Timer { Interval = 1200 };
            statusTimer.Tick += (_, _) => UpdateStatuses();
            statusTimer.Start();

            // 层级维持：不少挂件自己会设 TopMost=true，会重新浮到所有程序上面。
            // 每 4 秒把"桌面层"的挂件重新压到 Z 序底部（并把它因 SetWindowPos 丢掉的
            // 位置原样搬回去，不干扰用户手动拖动）。
            layerTimer = new System.Windows.Forms.Timer { Interval = 4000 };
            layerTimer.Tick += (_, _) => KeepDesktopLevel();
            layerTimer.Start();
        }

        private void KeepDesktopLevel()
        {
            try
            {
                foreach (var s in config.Skins)
                {
                    if (s.TopMost || s.Positionless) continue;
                    if (!Launcher.IsRunning(s)) continue;
                    var p = Launcher.FindRunning(s);
                    if (p == null) continue;
                    var hwnd = Win32.FindMainWindow((uint)p.Id);
                    if (hwnd == IntPtr.Zero) continue;

                    int ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
                    bool needDemote = (ex & Win32.WS_EX_TOPMOST) != 0;
                    if (!needDemote) continue;   // 已经在普通层且没被顶起来，无需打扰

                    Win32.GetWindowRect(hwnd, out Win32.RECT r0);
                    Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, ex & ~Win32.WS_EX_TOPMOST);
                    Win32.SetWindowPos(hwnd, Win32.HWND_BOTTOM, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE);
                    Win32.GetWindowRect(hwnd, out Win32.RECT r1);
                    int w = r1.Right - r1.Left, h = r1.Bottom - r1.Top;
                    if ((r1.Left != r0.Left || r1.Top != r0.Top) && w > 0 && h > 0)
                        Win32.MoveWindow(hwnd, r0.Left, r0.Top, w, h, true);
                }
            }
            catch { }
        }

        private void ApplyAutoStartManager()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;
                if (config.AutoStartManager) key.SetValue("WidgetHub", Application.ExecutablePath);
                else key.DeleteValue("WidgetHub", false);
            }
            catch { }
        }
    }

    public static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        public static void Main(string[] args)
        {
            // 明确声明"系统级 DPI 感知"，与诊断程序一致：这样用 SetWindowPos 移动挂件
            // （它们本身是 DPI 不感知的进程）时坐标不会被系统换算歪掉。
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 自检模式：WidgetHub.exe --shot <png路径>  把管理器界面离屏渲染成图片后退出
            if (args.Length >= 2 && args[0] == "--shot")
            {
                try
                {
                    var f = new ManagerForm(headless: true) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
                    f.Show();
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(500);
                    Application.DoEvents();
                    using var bmp = new Bitmap(f.Width, f.Height);
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    f.Close();
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(args[1] + ".err.txt", ex.ToString()); } catch { }
                }
                return;
            }

            Application.Run(new ManagerForm());
        }
    }
}
