#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""系统默认字体解析（供所有挂件统一调用）。

让挂件不再写死字体族名，而是跟随桌面设置的「界面默认字体」。
这样用户改了系统默认字体（如方正宋黑），挂件也会同步变化。

字体来源优先级（从高到低）：
  1. 控制中心为该挂件单独设置的字体：
     ~/.config/desktop-widgets/font-<key>.txt   （key=挂件目录名，如 desktop-music）
  2. 全局覆盖字体：~/.config/desktop-widgets/font.txt
  3. gsettings org.gnome.desktop.interface font-name
  4. fc-match 的默认族名
"""
import os
import subprocess

_CONFIG_DIR = os.path.join(os.path.expanduser('~'), '.config', 'desktop-widgets')


def _gsettings_font():
    try:
        out = subprocess.check_output(
            ['gsettings', 'get', 'org.gnome.desktop.interface', 'font-name'],
            stderr=subprocess.DEVNULL,
        ).decode().strip()
    except Exception:
        return None
    if not out:
        return None
    # 形如 'FZSongHei-B07 11' 或 '"方正宋黑" 11'
    out = out.strip().strip("'").strip('"')
    # 去掉末尾的像素/磅值，只保留族名
    parts = out.rsplit(None, 1)
    if len(parts) == 2 and parts[1].isdigit():
        return parts[0]
    return out


def _fontconfig_default():
    try:
        out = subprocess.check_output(
            ['fc-match', '--format', '%{family}'],
            stderr=subprocess.DEVNULL,
        ).decode().strip()
        if out:
            return out
    except Exception:
        pass
    return None


def _read_override(path):
    try:
        with open(path) as f:
            value = f.read().strip()
        return value or None
    except OSError:
        return None


def widget_key():
    """本模块被复制到每个挂件自己的目录，目录名即挂件 key。"""
    try:
        return os.path.basename(os.path.dirname(os.path.abspath(__file__)))
    except Exception:
        return ''


def override_font():
    key = widget_key()
    if key:
        name = _read_override(os.path.join(_CONFIG_DIR, 'font-%s.txt' % key))
        if name:
            return name
    return _read_override(os.path.join(_CONFIG_DIR, 'font.txt'))


def system_font_family():
    """返回挂件应使用字体的「族名」（不含尺寸）。"""
    name = override_font()
    if name:
        return name
    for fn in (_gsettings_font, _fontconfig_default):
        name = fn()
        if name:
            return name
    return 'sans'


# 模块加载时即解析一次，供挂件直接 import 使用
SYS_FONT = system_font_family()


if __name__ == '__main__':
    print(SYS_FONT)
