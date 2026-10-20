# 桌面挂件合集 · WidgetHub

把 10 个桌面挂件收进**一个托盘图标**、用一个窗口统一管理，并打包成**一个 2.7MB 的安装包**，
目标电脑**不需要装任何 .NET 运行时**（全部编译成 .NET Framework 4.8，Windows 自带）。

> 仓库名必须是 ASCII：GitHub 会把仓库名里的中文全部剥掉（`abc桌面挂件合集def` → `abc-def`），
> 所以仓库叫 `desktop-widget-suite`，中文名「桌面挂件合集」放在描述、README 和程序界面里。

---

## 功能一览

| 能力 | 说明 |
|---|---|
| 统一托盘 | 10 个挂件在源码/IL 层去掉了各自的托盘图标，托盘里只剩 WidgetHub 一个 |
| 位置记忆 | 「位置记忆」按钮记录挂件窗口坐标；停止/启动后自动摆回原位 |
| 桌面层 | 默认沉到桌面层（壁纸之上、程序之下），打开任何程序都不会被遮挡；可按挂件切「置顶」 |
| 每挂件设置 | 表格里每个挂件都有「设置」按钮，直接读写该挂件自己的配置文件（字号/透明度/贴桌面层/音量…） |
| 拖尾特效 | 鼠标拖尾的 10 种特效、8 种配色、3 档性能，直接在管理器里切换 |
| 版本与更新 | 头部显示 `v1.0`（点它打开「关于·设置」），可检查 GitHub Release 新版并一键下载安装 |
| 卸载 | 控制面板「程序和功能」里可卸载，也可在开始菜单/托盘里卸载 |
| 安全软件 | 安装包自带 `Defender-Exclude.bat`，一键给 Defender 加排除项（避免未签名小工具被误删） |

## 目录结构

```
WidgetHub/            统一管理器源码（net48，WinForms）
installer/
  WidgetHubSetup/     一键安装程序（net48，内嵌 payload.zip）
  WidgetHubUninstall/ 卸载程序（net48）
  Defender-Exclude.bat 给 Defender 加排除项
Weather48/            小普河天气挂件（从 net8 降到 net48，去掉 .NET 8 运行时依赖）
tools/Diagnostics/     开发诊断工具：实屏截图、窗口坐标对比、打包负载
release.sh            一键发布：改版本号 → 构建 → 提交 → 推送 → 发 GitHub Release
```

## 构建

```bash
# 管理器（net48，发布到 publish48/）
dotnet publish WidgetHub/WidgetHub.csproj -c Release -o WidgetHub/publish48

# 卸载器
dotnet publish installer/WidgetHubUninstall/WidgetHubUninstall.csproj -c Release -o installer/WidgetHubUninstall/out

# 打包负载（管理器 + 10 个挂件 → payload.zip）
dotnet run --project tools/Diagnostics -c Release -- --pack \
  <WidgetHub/publish48> <%LOCALAPPDATA%/Programs> <stage> <installer/WidgetHubSetup/payload.zip>

# 安装程序
dotnet publish installer/WidgetHubSetup/WidgetHubSetup.csproj -c Release -o installer/WidgetHubSetup/out
```

产物：`installer/WidgetHubSetup/out/WidgetHubSetup.exe`（约 2.7MB，单文件）。

## 发布新版本

```bash
export GITHUB_TOKEN=你的PAT        # 需要 repo 权限（私有仓库）
bash release.sh 1.1                # 改版本号 → 构建 → 提交 → 推送 → 打 tag → 发 Release
```

发布后，已经安装的用户在管理器里点右上角版本号 →「检查更新」就能看到新版本并一键升级。

## 踩过的坑（避免重复踩）

1. **GDI+ 的 `AddArc` 圆角路径会自相交**，实屏渲染时按钮左侧出现 V 形白缺口 → 一律改用三次贝塞尔（k=0.5523·r）。
2. **这些挂件收到 `SetWindowPos` 会把自己位置重置到 0,0** → 定位必须用 `MoveWindow`（并传窗口当前尺寸，否则挂件会被改小）。
3. **定位顺序**：先调层级（置顶/贴桌面层）再 `MoveWindow`，反过来位置会被置顶调用打回原点。
4. **分层窗口被外部移动后内容不会跟着走** → 移动后必须 `RedrawWindow` 强制重绘。
5. **net48 里 `System.Net.Http` 只是类型转发外观**，还要引用 `System.Net.Http.WebRequest`；隐式 using 不含 `System.Net.Http`。
6. **TimeSpan 不支持 `HH` 格式符**（只有 `hh`），DateTime 才支持。
7. **未签名的桌面小工具会被 Defender 判成 PUA 直接删除** → 安装时加排除项。

---

## Linux 端（Deepin 25 不可变系统）

仓库同时维护 Linux 版本，目标与第一版 `v1.0` Release **并列提供 Windows 与 Linux 两种安装包**：

- **Windows**：`WidgetHubSetup.exe`（见上方「发布新版本」）
- **Linux**：`linux/dist/desktop-widgets-suite_2.9.74_all.deb`

Linux 端同样贯彻「一个软件统管全部桌面挂件」的理念：由自愈守护进程
`linux/supervisor/supervisor.py` 保活 17 个挂件，任一崩溃自动拉起。
构建、安装（含 Deepin 25 不可变系统的 `deepin-immutable-ctl admin exec` 安装方式）、
回滚说明均在 `linux/README.md` 与 `linux/install.sh`。
