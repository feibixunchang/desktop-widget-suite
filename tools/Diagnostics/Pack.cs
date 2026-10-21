using System.IO.Compression;

// 打包：把 WidgetHub 发布目录 + 12 个挂件 exe 打进一个 payload.zip（供安装程序内嵌）
internal static class Pack
{
    private static readonly (string name, string sub, string exe)[] Skins =
    {
        ("小普河天气", "XiaoPuHeWeather", "XiaoPuHeWeather.exe"),
        ("桌面万年历", "DesktopCalendar", "DesktopCalendar.exe"),
        ("天气CPU网速", "DesktopMonitor", "DesktopMonitor.exe"),
        ("桌面音乐", "DesktopMusic", "DesktopMusic.exe"),
        ("桌面收音机", "DesktopRadio", "DesktopRadio.exe"),
        ("倒计时闹钟", "DesktopTimer", "DesktopTimer.exe"),
        ("桌面视频", "DesktopVideo", "DesktopVideo.exe"),
        ("桌面记账", "DesktopWallet", "DesktopWallet.exe"),
        ("桌面RSS", "RssWidget", "RssWidget.exe"),
        ("桌面记事", "DesktopNotes", "DesktopNotes.exe"),
        ("桌面相框", "DesktopPhoto", "DesktopPhoto.exe"),
        ("炫彩鼠标尾巴", "NeonMouseTrail", "NeonMouseTrail.exe"),
    };

    public static void Run(string publishDir, string programsDir, string stageDir, string zipPath)
    {
        if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true);
        Directory.CreateDirectory(stageDir);

        // 1) 管理程序本体
        var appDir = Path.Combine(stageDir, "WidgetHub");
        Directory.CreateDirectory(appDir);
        foreach (var f in Directory.GetFiles(publishDir))
        {
            var name = Path.GetFileName(f);
            if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(f, Path.Combine(appDir, name), true);
        }

        // 2) 12 个挂件（只取主 exe 和附属数据，不带卸载器/备份）
        foreach (var s in Skins)
        {
            string src = Path.Combine(programsDir, s.sub);
            string dst = Path.Combine(stageDir, "Skins", s.sub);
            Directory.CreateDirectory(dst);
            string exe = Path.Combine(src, s.exe);
            if (!File.Exists(exe)) { Console.WriteLine("!! 缺少 " + exe); continue; }
            // 主程序 + 依赖 dll + 配置文件 + 附属文本（不要 .bak / 卸载器）
            foreach (var file in Directory.GetFiles(src))
            {
                var name = Path.GetFileName(file);
                var ext = Path.GetExtension(name).ToLowerInvariant();
                // 只收主程序本身（排除 unins000.exe / 卸载.exe 之类的卸载器）
                bool keep = string.Equals(name, s.exe, StringComparison.OrdinalIgnoreCase)
                            || ext == ".dll" || ext == ".config" || ext == ".txt";
                if (!keep || name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(dst, name), true);
            }
        }

        // 3) 卸载器
        var uninstaller = Path.Combine(Path.GetDirectoryName(stageDir) ?? "", "WidgetHubUninstall", "out", "WidgetHubUninstall.exe");
        if (File.Exists(uninstaller))
        {
            var unDir = Path.Combine(stageDir, "Uninstall");
            Directory.CreateDirectory(unDir);
            File.Copy(uninstaller, Path.Combine(unDir, "WidgetHubUninstall.exe"), true);
        }

        // 4) 安全软件排除脚本（Defender 可能误判这些小工具）
        var excludeBat = Path.Combine(stageDir, "Defender-Exclude.bat");
        var batSource = Path.Combine(Path.GetDirectoryName(stageDir) ?? "", "Defender-Exclude.bat");
        if (File.Exists(batSource)) File.Copy(batSource, excludeBat, true);

        // 5) 说明
        File.WriteAllText(Path.Combine(stageDir, "安装说明.txt"),
            "桌面挂件总管 WidgetHub\r\n" +
            "=================================\r\n\r\n" +
            "安装目录里有两个部分：\r\n" +
            "  WidgetHub\\    统一管理器（托盘里只有它一个图标）\r\n" +
            "  Skins\\        12 个桌面挂件本体\r\n\r\n" +
            "用法：\r\n" +
            "  · 双击 WidgetHub\\WidgetHub.exe 打开管理器界面\r\n" +
            "  · 关闭管理器只是收进托盘，不会退出；在托盘图标上右键可以真正退出\r\n" +
            "  · 在管理器里可以一键全部启动/停止、记录位置、设置贴桌面层或浮在最前\r\n\r\n" +
            "卸载：直接删除整个安装目录，再从任务管理器里结束 WidgetHub.exe 即可。\r\n");

        // 6) 打包
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(stageDir, zipPath, CompressionLevel.Optimal, false);
        var fi = new FileInfo(zipPath);
        Console.WriteLine($"payload 已生成: {zipPath}  {fi.Length / 1024.0 / 1024.0:F1} MB");
    }
}
