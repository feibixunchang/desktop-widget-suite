# 桌面挂件合集 · Linux 端

与 Windows 端的 **WidgetHub**（一个托盘图标统管所有挂件）对应，Linux 端用
**「自愈守护进程 + 一组挂件」** 实现同样的目标：**一个软件（守护进程）统管全部桌面挂件**。

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
# 安装包位于本仓库 linux/dist/desktop-widgets-suite_2.9.74_all.deb
sudo deepin-immutable-ctl admin exec -- apt-get install -y \
    /path/to/linux/dist/desktop-widgets-suite_2.9.74_all.deb
```

安装后守护进程会随登录自启；若想立刻生效（不重登录）：

```bash
nohup python3 /usr/local/lib/desktop-widgets-supervisor/supervisor.py >/dev/null 2>&1 &
```

---

## 2. 目录结构

```
linux/
├── README.md                 本文件
├── dist/
│   └── desktop-widgets-suite_2.9.74_all.deb   # Linux 安装包（v1.0 Release 资源）
├── build/
│   └── build-deb.py          # 纯标准库 .deb 构建器（无需 dpkg-deb/ar/tar 命令）
├── debian/                   # 打包元数据：control / postinst / prerm / postrm
├── supervisor/
│   └── supervisor.py         # 自愈守护进程（统管 17 个挂件）
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
    --reference dist/desktop-widgets-suite_2.9.74_all.deb \
    --output    dist/desktop-widgets-suite_2.9.74_all.deb

# 方式 B：直接在已安装的本机上，以「已安装的挂件目录」为源重建
python3 build/build-deb.py --from-live --output dist/desktop-widgets-suite_2.9.74_all.deb

# 仅校验产出包的 md5sums 自洽性
python3 build/build-deb.py --check --output dist/desktop-widgets-suite_2.9.74_all.deb
```

---

## 4. 停用 / 启用单个挂件

守护依据 `~/.config/desktop-widgets/<name>.off` 标记决定是否拉起：

```bash
touch ~/.config/desktop-widgets/desktop-weather.off   # 停用天气挂件（下次退出后不再拉起）
rm     ~/.config/desktop-widgets/desktop-weather.off   # 恢复
```

---

## 5. 回滚

安装前请用 `install.sh rollback` 或手动保留备份。本机已生成备份点：

```
/home/ooo/.desktop-widgets-rollback/20261020-184139/
```

一键回滚到安装前状态（详见 `install.sh`）：

```bash
sudo bash linux/install.sh rollback
```
