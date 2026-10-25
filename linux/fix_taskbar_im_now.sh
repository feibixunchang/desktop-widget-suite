#!/bin/bash
# =====================================================================
# 任务栏信息条「设置天气城市」输不了中文 — 立即修复（免重装）
#
# 根因：挂件由 dde-autostart 用 runuser+env 精简环境启动，
#       GTK_IM_MODULE / XMODIFIERS 丢失；taskbar-info 的启动器
#       (/usr/bin/taskbar-info) 没有输入法修正段，而 /usr 是只读
#       overlay，不能直接改。天气挂件启动器有该修正段所以能输中文。
#
# 本脚本（普通用户运行，不要 sudo）：
#   1) 建用户级包装 ~/.local/bin/taskbar-info-im（自动探测 IM 模块）
#   2) 把 ~/.config/autostart/taskbar-info.desktop 的 Exec 指向包装
#      （重启后仍有效；注意：重装套件包时 postinst 会重写该文件，
#       但 v2.9.87 起 /usr/bin/taskbar-info 自带修正，无需包装）
#   3) 立即重拉 taskbar-info（带 IM 环境）。
#      supervisor 巡检间隔 5s、连续缺失 2 次才拉起，约 10s 窗口，
#      本步骤 0.3s 后启动，抢在它前面，不会被旧环境顶掉
#
# 装 v2.9.87 后启动器自带输入法修正，本脚本不再需要（留着无害）。
# =====================================================================
set -u

LAUNCHER=/usr/bin/taskbar-info
WRAP="$HOME/.local/bin/taskbar-info-im"
DESKTOP="$HOME/.config/autostart/taskbar-info.desktop"
PATTERN='/usr/lib/taskbar-info/taskbar-info.py'

echo "==> [1/5] 探测 GTK 输入法模块"
imd=""
for d in /usr/lib/x86_64-linux-gnu/gtk-3.0/3.0.0/immodules \
         /usr/lib/gtk-3.0/3.0.0/immodules; do
    [ -d "$d" ] && { imd="$d"; break; }
done
if [ -z "$imd" ]; then
    echo "✗ 未找到 GTK 输入法模块目录（im-fcitx5.so 等），中止。"
    exit 1
fi
echo "    模块目录: $imd"

echo "==> [2/5] 生成用户级包装 $WRAP"
mkdir -p "$(dirname "$WRAP")"
cat > "$WRAP" <<'WRAPPER'
#!/bin/bash
# taskbar-info 输入法环境包装（fix_taskbar_im_now.sh 生成）
# 装 v2.9.87+ 后 /usr/bin/taskbar-info 自带修正，本文件可删
_wgt_imd=""
for _d in /usr/lib/x86_64-linux-gnu/gtk-3.0/3.0.0/immodules \
         /usr/lib/gtk-3.0/3.0.0/immodules; do
    [ -d "$_d" ] && { _wgt_imd="$_d"; break; }
done
if [ -n "$_wgt_imd" ]; then
    if [ -n "${GTK_IM_MODULE:-}" ] && [ -f "$_wgt_imd/im-$GTK_IM_MODULE.so" ]; then
        :
    else
        for _m in fcitx5 fcitx ibus xim; do
            if [ -f "$_wgt_imd/im-$_m.so" ]; then
                export GTK_IM_MODULE="$_m"
                export XMODIFIERS="@im=$_m"
                break
            fi
        done
    fi
fi
exec /usr/bin/taskbar-info "$@"
WRAPPER
chmod +x "$WRAP"
echo "    已生成"

echo "==> [3/5] 更新开机自启项（重启后仍有效）"
if [ -f "$DESKTOP" ]; then
    if grep -q 'taskbar-info-im' "$DESKTOP"; then
        echo "    已指向包装，跳过"
    else
        [ -f "$DESKTOP.bak" ] || cp "$DESKTOP" "$DESKTOP.bak"
        sed -i "s|^Exec=.*|Exec=$WRAP|" "$DESKTOP"
        echo "    Exec -> $(grep '^Exec=' "$DESKTOP")"
    fi
else
    echo "    ⚠ 未找到 $DESKTOP（不影响当前会话修复）"
fi

echo "==> [4/5] 立即重启 taskbar-info（带输入法环境）"
: "${DISPLAY:=:0}"; export DISPLAY
pkill -f "$PATTERN" && echo "    已停止旧进程" || echo "    旧进程未在运行"
sleep 0.3
setsid nohup "$WRAP" >/dev/null 2>&1 &
sleep 2

echo "==> [5/5] 验证"
NEWPID=$(pgrep -f "$PATTERN" | head -1)
if [ -n "$NEWPID" ]; then
    GOT=$(tr '\0' '\n' < "/proc/$NEWPID/environ" | grep '^GTK_IM_MODULE=' || echo '(未设置!)')
    echo "    ✓ taskbar-info 已重启 (PID $NEWPID)，$GOT"
    echo
    echo "修复完成。现在右键任务栏信息条 →「设置天气城市...」，"
    echo "输入城市或乡村名（如：北京市 / 成都市 / 某某镇）试试中文输入。"
else
    echo "    ✗ 重启失败，请检查日志: tail ~/.local/share/desktop-widget/widget.log"
    exit 1
fi
