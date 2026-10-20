# 桌面挂件合集 · Linux 端

与 Windows 端的 **WidgetHub**（一个托盘图标统管所有挂件）对应，Linux 端用
**「自愈守护进程 + 一组挂件 + 图形控制中心」** 实现同样的目标：**一个软件统管全部桌面挂件**。

- **桌面挂件控制中心** (`desktop-widgets-hub`)：图形界面，列出全部 17 个挂件，可逐项
  **启用/停用、启动/停止/重启/⚙ 独立设置**，也有「全部启动 / 全部停止 / 重启守护」一键操作。
  相当于 Linux 版的 WidgetHub 主界面。
  - **系统托盘图标 + 右键菜单**：打开 / 全部启动 / 全部停止 / 重启守护 / 开机自启 / 检查更新 / 退出；
  - **每个挂件的设置**：启动命令、附加参数、崩溃重启延迟、是否自动重启、单独指定字体；
  - **版本号 + 一键更新**：标题栏与底栏显示版本，自动/手动检查最新版，点「立即更新」即
    自动下载 .deb 并经 pkexec 提权安装（Deepin 不可变系统自动走 `deepin-immutable-ctl`）；
  - **平滑滚动**：滚轮带缓动、列表可按住拖动，状态刷新改为单次进程快照，不再卡顿。
- **桌面影视挂件修复（2.9.77）**：修复全屏返回后控制窗被置底、点击/右键全部失灵
  （电影关不掉）的根因；播放中新增常开「✕ 关闭」钮、Esc 停播、停止挂件时
  mpv 随之干净退出；右键菜单改用 `popup_at_pointer`，并修复开机自启菜单项失效。
- **mpv / tesseract 全自包含（2.9.78）**：mpv 0.37 二进制 + 93 个依赖库 +
  tesseract 5.5 + chi_sim/eng 语言数据全部装进包内 `/usr/local/lib/mpv/`，
  经 wrapper（`/usr/local/bin/mpv`、`/usr/local/bin/tesseract`）加载，
  **换到任何 Linux 机器安装即可播放影片/收音机/截图OCR，无需另行安装 mpv/tesseract，
  也不需要联网补依赖**（安装包约 61MB，装好后约占 200MB 磁盘）。
  已在隔离环境（env -i 模拟干净系统）实测：播放、OCR 均正常。
- **全挂件暗病修复（2.9.78）**：
  - 修复 **9 个挂件的开机自启失效**（`f'...{{name}}...'` 被写成字面量，
    生成的自启动文件名/名称都是 `{name}`）——天气/便签/日历/相框/RSS/音乐/计时器/影视；
  - 修复 **音乐/电台/相框/截图OCR/日历便签**里硬编码的 `Sans` 字体
    （共 21 处，全部改为跟随系统默认字体，如方正宋黑）；
  - 修复**参考包自带的 `libjpeg.so.62` 冒充文件**（turbo8 改名而来，缺
    `LIBJPEG_6.2` 符号版本，会让加载它的库直接报错起不来）。

- 17 个挂件：日历 / 相框 / 监听 / 视频 / 音乐 / 记事 / 电台 / RSS / 计时 / 钱包 / 天气 /
  Edge 滚动控制 / 显示器输入切换 / 任务栏信息 / 截图 OCR / 鼠标拖尾 / 复制工具栏
- 守护进程 `supervisor.py`：任一挂件崩溃/被误关，5 秒内自动拉起；用户在挂件菜单点「退出」，
  守护写入 `<name>.off` 标记，本会话不再拉起。
- 登录自启：通过 `/etc/xdg/autostart/desktop-widgets-supervisor.desktop` 拉起守护。

> 本目录即 Linux 端的「源码 + 构建 + 安装」完整工程，与仓库根目录的 Windows 源码（C#/WinForms）
> 并列，保证 `v1.0` Release 同时提供 **Windows 安装包 (.exe)** 与 **Linux 安装包 (.deb)**。

---

## 1. 安装（Deepin 25 不可变系统）

Deepin 25 是 **不可变系统**（ostree），系统目录只读，必须用 `deepin-immutable-ctl admin`
在可写层安装。在**本机终端**执行：

```bash
# 安装包位于本仓库 linux/dist/desktop-widgets-suite_2.9.78_all.deb
# 用 dpkg -i 强制重装（版本号不变时 apt 会跳过，dpkg 会覆盖文件）
sudo deepin-immutable-ctl admin exec -- dpkg -i \
    /path/to/linux/dist/desktop-widgets-suite_2.9.78_all.deb
```

安装后守护进程会随登录自启；若想立刻生效（不重登录）：

```bash
nohup python3 /usr/local/lib/desktop-widgets-supervisor/supervisor.py >/dev/null 2>&1 &
```

打开控制中心（统管挂件）：开始菜单搜「桌面挂件控制中心」，或终端执行 `desktop-widgets-hub`。

---

## 2. 目录结构

```
linux/
├── README.md                 本文件
├── dist/
│   └── desktop-widgets-suite_2.9.78_all.deb   # Linux 安装包（v1.0 Release 资源）
├── build/
│   └── build-deb.py          # 纯标准库 .deb 构建器（无需 dpkg-deb/ar/tar 命令）
├── debian/                   # 打包元数据：control / postinst / prerm / postrm
├── supervisor/
│   └── supervisor.py         # 自愈守护进程（统管 17 个挂件，遵守 settings.json 的参数）
├── hub/                      # 桌面挂件控制中心（图形界面，GTK3）
│   ├── desktop-widgets-hub   #   自包含单文件启动器（含全部 GUI 逻辑）
│   └── desktop-widgets-hub.desktop  # 开始菜单项
├── common/
│   └── widget_font.py        # 运行时读取「系统默认界面字体」；打包时复制到各挂件目录
├── launchers/                # 各挂件的启动器脚本
│   ├── usr-local-bin/        #   /usr/local/bin/desktop-* 等
│   └── usr-bin/              #   /usr/bin/{taskbar-info,shot-ocr,mouse-trail,copy-toolbar,...}
├── doc/                      # 安装使用说明.txt / changelog.gz / copyright
└── install.sh                # 本机安装 / 回滚 / 状态检查 一键脚本
```

---

## 3. 重新构建 .deb

无需 `dpkg-deb` / `ar` / `tar`（在受限环境也能跑）。两种模式：

```bash
# 方式 A：以现有参考包为基准，剔除运行期垃圾（.pyc / .pid / .log）并重算 md5sums
python3 build/build-deb.py \
    --reference dist/desktop-widgets-suite_2.9.78_all.deb \
    --output    dist/desktop-widgets-suite_2.9.78_all.deb

# 方式 A+：在 A 基础上注入「控制中心 + 字体解析模块 widget_font.py」，
#          并把各挂件写死的字体改为「运行时读取系统默认字体」
python3 build/build-deb.py --patch \
    --reference dist/desktop-widgets-suite_2.9.78_all.deb \
    --output    dist/desktop-widgets-suite_2.9.78_all.deb

# 方式 B：直接在已安装的本机上，以「已安装的挂件目录」为源重建
python3 build/build-deb.py --from-live --output dist/desktop-widgets-suite_2.9.78_all.deb

# 仅校验产出包的 md5sums 自洽性
python3 build/build-deb.py --check --output dist/desktop-widgets-suite_2.9.78_all.deb
```

### 字体跟随系统默认（本版重点）

挂件不再写死字体族名（如 `FZSongHei-B07`），而是 **运行时读取系统「界面默认字体」**：

- `common/widget_font.py`：优先读**控制中心里为该挂件单独设置的字体**
  （`~/.config/desktop-widgets/font-<挂件key>.txt`），其次全局覆盖字体
  （`~/.config/desktop-widgets/font.txt`），再读 `gsettings org.gnome.desktop.interface font-name`，
  最后回退到 `fc-match` 的默认族名。你改了系统默认字体（如方正宋黑），挂件会同步变化。
  构建时（`--patch`）会把它复制到**每个挂件自己的目录**，挂件 `import widget_font` 即可用，
  无需额外 sys.path 配置。
- 构建时（`--patch`）会把 12 个挂件源码里的 `DEFAULT_FONT` / `*_FONT` 常量自动改写为
  引用上述系统字体，过程幂等、可重复构建。

---

## 4. 桌面挂件控制中心（图形界面）

相当于 Linux 版的 WidgetHub 主界面，用于**统管全部挂件**，无需记命令。

- 打开：开始菜单搜「桌面挂件控制中心」，或终端执行 `desktop-widgets-hub`。
- 列表展示 17 个挂件，每行显示状态（运行中 / 未运行 / 已禁用）。
- **开关**：启用/停用该挂件（底层即 `.off` 标记 + 启停进程）。
- **重启 / 停止**：对单个挂件操作。
- 每行右侧：**⚙ 设置 / 重启 / 停止** 三个按钮；点「⚙ 设置」可改启动参数、崩溃重启延迟、
  是否自动重启、该挂件字体（留空=跟随系统默认）。
- 顶部工具条：**全部启动 / 全部停止 / 重启守护 / 刷新**。
- 标题栏左侧有「**开机自启**」开关：打开后控制中心会在你每次登录桌面时自动弹出。
- 右下角显示当前版本与「检查更新」；有新版本时顶部出现横幅，点「立即更新」自动完成
  下载 → 提权安装 → 重启守护。私有仓库首次检查需在「⋮ → 更新设置」里填入 GitHub
  访问令牌（只保存在本机 `~/.config/desktop-widgets/update.json`，权限 600）。
- 关闭窗口只是隐藏到托盘，托盘图标右键可再次打开或退出。
- 状态由自愈守护 `supervisor.py` 统一管理，控制中心通过标记文件与信号与之协作；
  设置写在 `~/.config/desktop-widgets/settings.json`，守护按其中的参数拉起挂件。

---

## 5. 停用 / 启用单个挂件

守护依据 `~/.config/desktop-widgets/<name>.off` 标记决定是否拉起（控制中心开关即操作此标记）：

```bash
touch ~/.config/desktop-widgets/desktop-weather.off   # 停用天气挂件（下次退出后不再拉起）
rm     ~/.config/desktop-widgets/desktop-weather.off   # 恢复
```

---

## 6. 回滚

安装前请用 `install.sh rollback` 或手动保留备份。本机已生成备份点：

```
/home/ooo/.desktop-widgets-rollback/20261020-184139/
```

一键回滚到安装前状态（详见 `install.sh`）：

```bash
sudo bash linux/install.sh rollback
```

---

## 6.6 启动器只显示「桌面挂件控制中心」

从 v2.9.76 起，安装包内全部挂件/任务栏工具的菜单项都带 `NoDisplay=true`，
启动器里只显示**桌面挂件控制中心**一个图标——挂件统一由它启动/停止/设置。
开机自启读的是 `~/.config/autostart/` 里的另一份文件，不受影响。

已在用旧版的机器想立即生效（不用重装）：

```bash
sudo bash hide-launcher-icons.sh
```

想恢复某个挂件的图标：删掉 `/usr/local/share/applications/<对应>.desktop`
里的 `NoDisplay=true` 行，再执行 `update-desktop-database -q /usr/local/share/applications`。

## 6.5 清理旧版单独安装的挂件/任务栏工具

装本套件之前，若曾把「划选复制 / 鼠标拖尾 / 截图OCR」单独装进系统
（旧包名 `copy-toolbar`、`mouse-trail`、`shot-ocr`），它们的文件早已由本套件接管，
dpkg 里只剩无用记录。一键清理（自动重建自启动项，功能不受影响）：

```bash
sudo bash clean-old-widgets.sh
```

> `quicklaunch`、`echoflow`、`zdm`、音乐软件等是其他工具，与本套件无关，脚本不会动。
> 连套件一起卸载：`sudo apt purge desktop-widgets-suite`。

## 7. 在其他 Linux 机器上安装

本 `.deb` 是**自包含安装包**：17 个挂件 + 控制中心 + 字体跟随修复全部打包在一起，
复制这一个文件即可在任何同架构机器上一步装好，**无需联网拉源码、无需手动装依赖**。

**适用环境**
- 桌面环境：Deepin 25 / 统信 UOS（DDE）、其他 Debian / Ubuntu 系发行版（GTK3 环境）。
- 架构：**x86_64 (amd64)**。原包捆绑了一个 amd64 的 `mpv` 二进制，故目前仅支持该架构；
  若需在 arm64 等架构使用，需把 `mpv` 改为目标机 `apt` 安装后重新打包（属原包设计，非本工程新增）。

**安装步骤**
```bash
# 1) 拷贝安装包到目标机后，一条命令安装
sudo dpkg -i desktop-widgets-suite_2.9.78_all.deb

# 2) 若提示缺运行库（python3-gi / gir1.2-gtk-3.0 等），让 apt 自动补全
sudo apt-get install -f

# 3) 打开控制中心（统管全部挂件）
desktop-widgets-hub        # 或开始菜单搜「桌面挂件控制中心」
```

**说明**
- 依赖声明已写入 `debian/control`（`python3-gi`、`gir1.2-gtk-3.0`、`python3-xlib`、
  `fonts-noto-cjk` 等），目标机执行 `apt-get install -f` 会自动拉齐 GTK3 与 PyGObject 运行时。
- 守护进程随登录自启（通过 `/etc/xdg/autostart/desktop-widgets-supervisor.desktop`），
  控制中心按需打开；如需登录即弹出，拨动其标题栏「开机自启」开关即可。
- 字体默认跟随目标机「系统界面默认字体」，无需逐机改配置。
