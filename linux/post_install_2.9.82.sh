#!/bin/bash
# 2.9.81 装机后收尾（用户态，不需要 root）：
#   1. 启用新拖尾（删 .off 禁用标记）
#   2. 清理旧版平行安装（~/.local/lib/mouse-trail 旧代码 + 旧 wrapper）
#   3. 去掉独立版 neon-trail 的启动入口（套件已是同一份新代码，防双实例）
#   4. 重启控制中心（加载新 hub 代码）
#   5. 启动新拖尾并验证
set -u
echo "==> 1. 启用鼠标拖尾"
rm -f "$HOME/.config/desktop-widgets/mouse-trail.off" && echo "    已删除 mouse-trail.off"

echo "==> 2. 清理旧版平行安装"
for p in "$HOME/.local/lib/mouse-trail" "$HOME/.local/bin/mouse-trail"; do
  if [ -e "$p" ]; then rm -rf "$p" && echo "    已删除 $p"; fi
done

echo "==> 3. 移除独立版 neon-trail 启动入口（代码保留作后备，不会自启）"
for p in "$HOME/.local/share/applications/neon-trail.desktop" "$HOME/.local/bin/neon-trail"; do
  if [ -e "$p" ]; then rm -f "$p" && echo "    已删除 $p"; fi
done

echo "==> 4. 重启控制中心（旧进程内存里是旧代码）"
if pgrep -f "desktop-widgets-hub" >/dev/null 2>&1; then
  pkill -f "desktop-widgets-hub" 2>/dev/null && echo "    已关闭旧控制中心，请重新打开「桌面挂件控制中心」"
else
  echo "    控制中心未在运行"
fi

echo "==> 5. 启动新拖尾"
if ! pgrep -f "/usr/lib/mouse-trail/main.py" >/dev/null 2>&1; then
  setsid nohup /usr/bin/mouse-trail >/dev/null 2>&1 < /dev/null &
  sleep 4
fi
PID=$(cat "$HOME/.config/mouse-trail/instance.lock" 2>/dev/null)
if [ -n "$PID" ] && [ -d "/proc/$PID" ]; then
  echo "    新拖尾运行中 PID=$PID"
else
  echo "    !! 拖尾似乎未起来，查看: /usr/bin/mouse-trail"
fi

echo "==> 6. 验证 SNI 注册"
if busctl --user list 2>/dev/null | grep -q "com.deepin.DesktopWidgetsMouseTrail"; then
  echo "    OK: com.deepin.DesktopWidgetsMouseTrail 已注册（托盘可点）"
else
  echo "    !! 未找到 SNI bus name"
fi
echo "==> 完成。打开控制中心应显示：鼠标炫彩拖尾 · 运行中"
