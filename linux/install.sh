#!/usr/bin/env bash
#==================================================================
#  桌面挂件合集 · Linux 端 本机安装 / 回滚 / 状态检查
#  用法:
#    sudo bash install.sh install     # 停挂件 → 装新包 → 校验
#    sudo bash install.sh rollback    # 回滚到安装前备份
#    sudo bash install.sh status      # 查看守护与挂件进程
#  说明: 本脚本必须在【本机终端】以 root 运行（不可变系统需要 admin exec）。
#        它会先停掉所有桌面挂件，再安装新 .deb，最后重启守护以加载新包。
#  装完后: 打开「桌面挂件控制中心」(命令 desktop-widgets-hub 或开始菜单) 统管各挂件。
#  字体: 本版挂件运行时读取系统默认界面字体(方正宋黑)，不再写死。
#==================================================================
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
DEB="$HERE/dist/desktop-widgets-suite_2.9.79_all.deb"
ROLLBACK="/home/user/.desktop-widgets-rollback/20261020-184139"
SUP="/usr/local/lib/desktop-widgets-supervisor/supervisor.py"
SUP_PIDF="/home/user/.local/share/desktop-widgets-supervisor/supervisor.pid"

WIDGETS=(desktop-calendar desktop-frame desktop-monitor desktop-movie desktop-music \
         desktop-notes desktop-radio desktop-rss desktop-timer desktop-wallet \
         desktop-weather edge-scroll-control monitor-input-switch taskbar-info \
         shot-ocr mouse-trail copy-toolbar deepin-taskbar-look system-cleaner \
         detect-mouse-button)

stop_widgets() {
  echo "==> 停止守护与全部挂件"
  pkill -f desktop-widgets-supervisor 2>/dev/null || true
  for w in "${WIDGETS[@]}"; do
    pkill -f "$w" 2>/dev/null || true
  done
  sleep 2
  echo "    剩余挂件进程: $(pgrep -fc 'desktop-' 2>/dev/null || echo 0)"
}

install_pkg() {
  if [ ! -f "$DEB" ]; then echo "找不到安装包: $DEB"; exit 1; fi
  stop_widgets
  # 清掉可能残留的 root 启动的守护，避免重复实例
  pkill -f desktop-widgets-supervisor 2>/dev/null || true
  sleep 1
  # 旧版安装包（2.9.74 及更早）的 data.tar 含 './' 根条目，dpkg 会把它记进
  # /var/lib/dpkg/info/<pkg>.list 成为一行 '/'；升级时 dpkg 读旧清单遇到这行
  # 会报「文件列表含有空的文件名」致命错。先清掉该行再安装。
  deepin-immutable-ctl admin exec -- bash -c '
    f=/var/lib/dpkg/info/desktop-widgets-suite.list
    if [ -f "$f" ] && grep -qx "/" "$f"; then
      sed -i "/^\/$/d" "$f" && echo "    已清理旧清单中的空文件名条目('/')"
    fi'
  echo "==> 安装新包 (deepin-immutable-ctl admin exec, dpkg -i 强制重装)"
  deepin-immutable-ctl admin exec -- dpkg -i --force-confask,confnew,overwrite "$DEB" 2>&1 || {
     echo "!! dpkg -i 失败，尝试修复依赖后重试"
     deepin-immutable-ctl admin exec -- apt-get install -f -y
     deepin-immutable-ctl admin exec -- dpkg -i --force-overwrite "$DEB" 2>&1
  }
  # postinst 会为本机用户拉起守护与挂件；脚本不再手动启动，避免重复 / 以 root 运行
  sleep 8
  # 关掉安装前就在运行的旧版控制中心：旧进程内存里是旧代码，
  # 不关会继续显示旧版本号、且「检查更新」判断失真。
  if pgrep -f "desktop-widgets-hub" >/dev/null 2>&1; then
    pkill -f "desktop-widgets-hub" 2>/dev/null || true
    echo "==> 已关闭旧版控制中心，重新打开「桌面挂件控制中心」即为新版本"
  fi
  # 兜底：若守护未自动拉起，则以登录用户身份启动
  if ! pgrep -f desktop-widgets-supervisor >/dev/null 2>&1; then
    echo "==> 守护未自动拉起，尝试以用户身份启动"
    for u in /home/*; do
      user=$(basename "$u"); uid=$(id -u "$user" 2>/dev/null)
      case "$uid" in ''|*[!0-9]*) continue ;; esac
      [ "$uid" -ge 1000 ] || continue
      command -v runuser >/dev/null && runuser -u "$user" -- setsid nohup python3 "$SUP" >/dev/null 2>&1 < /dev/null &
    done
    sleep 4
  fi
  status
}

do_rollback() {
  echo "==> 回滚到备份点: $ROLLBACK"
  if [ ! -d "$ROLLBACK" ]; then echo "备份点不存在!"; exit 1; fi
  stop_widgets
  deepin-immutable-ctl admin exec -- apt-get remove --purge -y desktop-widgets-suite 2>/dev/null || true
  if [ -d "$ROLLBACK/files" ]; then
    tar -xzf "$ROLLBACK/files"/*.tgz -C / 2>/dev/null || \
      echo "   (无 files 归档，请用 apt 安装原版 .deb: $ROLLBACK/deb/)"
  fi
  echo "    回滚完成。原版 .deb 位于: $ROLLBACK/deb/"
  echo "    可手工重装: sudo deepin-immutable-ctl admin exec -- apt-get install -y $ROLLBACK/deb/*.deb"
}

status() {
  echo "==> 守护进程"
  pgrep -af supervisor.py || echo "    (未运行)"
  echo "==> 挂件进程数: $(pgrep -fc 'desktop-' 2>/dev/null || echo 0)"
  echo "==> 最近守护日志"
  tail -n 8 "$HOME/.local/share/desktop-widgets-supervisor/supervisor.log" 2>/dev/null || echo "    (无日志)"
  echo "==> 控制中心: 在终端运行 desktop-widgets-hub 或开始菜单打开「桌面挂件控制中心」"
}

case "${1:-status}" in
  install)  install_pkg ;;
  rollback) do_rollback ;;
  status)   status ;;
  *) echo "用法: sudo bash install.sh {install|rollback|status}"; exit 1 ;;
esac
