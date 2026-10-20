#!/bin/bash
# =====================================================================
# 一键清理：套件(.deb)安装之前「单独装进系统」的旧挂件/任务栏工具
#
# 背景：copy-toolbar / mouse-trail / shot-ocr 这三个旧包的文件早已被
#       desktop-widgets-suite 套件接管（dpkg 记录里它们的文件清单已空），
#       只剩无用的包记录。直接 purge 是安全的（不会删任何文件），
#       但旧包的 postrm 会删掉 ~/.config/autostart/<名字>.desktop——
#       而那现在是套件建的自启动项，所以 purge 后必须按套件的 .desktop
#       重建，否则重启后这三个工具不会自动起来。
#
# 不在清理范围（属于你自己的其他工具，与挂件无关，保持原样）：
#   quicklaunch(快捷启动) / echoflow(按键工具) / zdm(下载管理器) /
#   localmusic / another-migu-music / lx-music-desktop / deepin-widget-toolbar
# =====================================================================
set -u

PKGS="copy-toolbar mouse-trail shot-ocr"     # 要清除的旧包记录
APPS="copy-toolbar mouse-trail shot-ocr"     # purge 后要重建自启动的

if command -v deepin-immutable-ctl >/dev/null 2>&1; then
    A() { deepin-immutable-ctl admin exec -- "$@"; }
else
    A() { "$@"; }
fi

echo "==> [1/3] 清除旧包记录（文件归套件所有，不会被删除）"
for p in $PKGS; do
    st=$(A dpkg-query -W -f='${Status}' "$p" 2>/dev/null || true)
    case "$st" in
        *"install ok installed"*)
            if A dpkg -P "$p"; then
                echo "    已清除旧包记录: $p"
            else
                echo "    !! $p 清除失败（不影响使用，可稍后重试）"
            fi
            ;;
        *) echo "    $p 已是清除状态，跳过" ;;
    esac
done

echo "==> [2/3] 重建自启动项（防止旧包 postrm 误删后开机不启动）"
n=0
for u in /home/*; do
    [ -d "$u/.config" ] || continue
    mkdir -p "$u/.config/autostart"
    for app in $APPS; do
        src="/usr/share/applications/$app.desktop"
        if [ -f "$src" ]; then
            cp -f "$src" "$u/.config/autostart/$app.desktop" && n=$((n+1))
        fi
    done
done
echo "    重建自启动项: $n 个"

echo "==> [3/3] 刷新应用菜单缓存"
A update-desktop-database -q /usr/share/applications 2>/dev/null || true
echo "    完成"

echo
echo "清理结束。当前桌面挂件/任务栏工具全部由 desktop-widgets-suite 套件"
echo "统一管理（控制中心 desktop-widgets-hub 里可开关/设置），无需再单独卸载。"
echo "如想连套件一起卸载：sudo apt purge desktop-widgets-suite"
