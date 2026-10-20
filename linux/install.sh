#!/usr/bin/env bash
#==================================================================
#  桌面挂件合集 · Linux 端 本机安装 / 回滚 / 状态检查
#  用法:
#    sudo bash install.sh install     # 停挂件 → 装新包 → 校验
#    sudo bash install.sh rollback    # 回滚到安装前备份
#    sudo bash install.sh status      # 查看守护与挂件进程
#  说明: 本脚本必须在【本机终端】以 root 运行（不可变系统需要 admin exec）。
#        它会先停掉所有桌面挂件，再安装新 .deb，最后重启守护以加载新包。
#==================================================================
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
DEB="$HERE/dist/desktop-widgets-suite_2.9.74_all.deb"
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
  echo "==> 安装新包 (deepin-immutable-ctl admin exec)"
  deepin-immutable-ctl admin exec -- apt-get install -y "$DEB"
  echo "==> 重启守护以加载新包"
  nohup python3 "$SUP" >/dev/null 2>&1 &
  sleep 6
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
}

case "${1:-status}" in
  install)  install_pkg ;;
  rollback) do_rollback ;;
  status)   status ;;
  *) echo "用法: sudo bash install.sh {install|rollback|status}"; exit 1 ;;
esac
