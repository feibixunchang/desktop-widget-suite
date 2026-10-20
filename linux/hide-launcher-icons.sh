#!/bin/bash
# =====================================================================
# 立即隐藏启动器里的挂件图标（不用重装，当前机器马上生效）
#
# 原理：给套件自带的各个 .desktop 菜单项加 NoDisplay=true，只保留
#       「桌面挂件控制中心」可见。NoDisplay 只影响启动器/菜单显示，
#       不影响开机自启（自启读的是 ~/.config/autostart 里的另一份文件），
#       也不影响在控制中心里启动/停止各个挂件。
# 幂等：重复执行结果一致；想恢复某个图标，删掉对应文件里的 NoDisplay 行即可。
# =====================================================================
set -u

HUB="desktop-widgets-hub.desktop"
DIRS="/usr/local/share/applications /usr/share/applications"

if command -v deepin-immutable-ctl >/dev/null 2>&1; then
    A() { deepin-immutable-ctl admin exec -- "$@"; }
else
    A() { "$@"; }
fi

echo "==> 给挂件/任务栏工具菜单项加 NoDisplay=true（保留控制中心）"
n=0
for d in $DIRS; do
    for f in "$d"/*.desktop; do
        [ -f "$f" ] || continue
        base=$(basename "$f")
        [ "$base" = "$HUB" ] && continue
        # 只处理套件相关的项（桌面挂件/任务栏工具/mpv 等）
        case "$base" in
            desktop-*|taskbar-*|edge-scroll-control*|monitor-input-switch*|\
            copy-toolbar*|mouse-trail*|shot-ocr*|system-cleaner*|\
            deepin-taskbar-look*|mpv*) ;;
            *) continue ;;
        esac
        if ! grep -q '^NoDisplay=true' "$f" 2>/dev/null; then
            A sed -i '/^NoDisplay=/d' "$f"
            A bash -c "printf 'NoDisplay=true\n' >> '$f'"
            n=$((n+1))
        fi
    done
done
echo "    已隐藏: $n 个"

echo "==> 刷新菜单缓存"
A update-desktop-database -q /usr/local/share/applications 2>/dev/null || true
A update-desktop-database -q /usr/share/applications 2>/dev/null || true

echo
echo "完成。启动器现在只显示「桌面挂件控制中心」。"
echo "挂件的开机自启与控制中心里的开关不受影响；想恢复某个图标，"
echo "删掉 /usr/local/share/applications/<对应>.desktop 里的 NoDisplay=true 行即可。"
