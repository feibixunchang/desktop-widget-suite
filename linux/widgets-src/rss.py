#!/usr/bin/env python3
from widget_font import SYS_FONT as _SYS_FONT
# -*- coding: utf-8 -*-
"""桌面RSS挂件 - 背景透明, 显示日期/IP/RSS订阅内容"""
import datetime
import json
import os
import re
import socket
import shlex
import subprocess
import sys
import threading
import time
import urllib.request
import xml.etree.ElementTree as ET
from concurrent.futures import ThreadPoolExecutor
from urllib.parse import urljoin

import gi
gi.require_version('Gtk', '3.0')
from gi.repository import Gtk, Gdk, GLib, Pango, PangoCairo
import cairo

GLib.set_prgname('desktop-rss')

APP_DIR = os.path.dirname(os.path.abspath(__file__))
STATE_PATH = os.path.expanduser('~/.config/desktop-rss/state.json')
PID_PATH = os.path.expanduser('~/.config/desktop-rss/pid')

MIN_W, MIN_H = 700, 300
MAX_W, MAX_H = 2000, 2600
# 自动铺满屏幕时, 窗口底边与底部任务栏(状态栏)保持的间距(厘米)
BOTTOM_GAP_CM = 1.0
# 布局版本: 升级后触发一次重新铺满(不影响此后用户手动调整的尺寸)
FIT_VER = 2
PAD = 14
CORNER = 26
HEAD_H = 108
FOOT_H = 40
FOOT_FONT = _SYS_FONT + ' 13'
FOOT_BTN_FONT = _SYS_FONT + ' 14'
# 状态栏左侧文字放不下时依次缩小字号, 再退化为只报数量, 保证不被截成省略号
FOOT_FONTS = ('FZSongHei-B07 13', 'FZSongHei-B07 12', 'FZSongHei-B07 11')
FOOT_MIN_W = 160

RED = (0.95, 0.20, 0.16)
BLUE = (0.40, 0.62, 0.98)
YELLOW = (0.98, 0.84, 0.30)
WHITE = (0.92, 0.93, 0.97)
DIM = (0.66, 0.70, 0.80)
NAME_C = (0.45, 0.95, 0.55)
SEL_C = (1.0, 0.70, 0.10)

SOURCES = [
    ('少数派', 'https://sspai.com/feed'),
    ('V2EX', 'https://www.v2ex.com/index.xml'),
    ('爱范儿', 'https://www.ifanr.com/feed'),
    ('IT之家', 'https://ithome.com/rss'),
    ('知乎', 'https://rsshub.rssforever.com/zhihu/daily'),
    ('中时', 'https://www.chinanews.com.cn/rss/china.xml'),
    ('中即', 'https://www.chinanews.com.cn/rss/scroll-news.xml'),
    ('中社', 'https://www.chinanews.com.cn/rss/society.xml'),
    ('社会', 'http://www.people.com.cn/rss/society.xml'),
]
ALL_TAB = '全部'
# 第二行来源标签: 「全部」在最前, 其后是各订阅源(与 SOURCES 顺序一致)
TABS = [ALL_TAB] + [n for n, _ in SOURCES]
PER_SOURCE = 40

UA = ('Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 '
      '(KHTML, like Gecko) Chrome/126.0 Safari/537.36')


def check_single():
    try:
        with open(PID_PATH) as f:
            old = int(f.read().strip())
        if os.path.exists('/proc/%d' % old):
            c = open('/proc/%d/cmdline' % old, 'rb').read().decode(errors='ignore')
            if 'desktop-rss' in c or 'rss.py' in c:
                sys.exit(0)
    except Exception:
        pass
    try:
        os.makedirs(os.path.dirname(PID_PATH), exist_ok=True)
        with open(PID_PATH, 'w') as f:
            f.write(str(os.getpid()))
    except Exception:
        pass


def load_state():
    st = {'x': None, 'y': None, 'w': 720, 'h': 720, 'locked': False,
          'auto': 600}
    try:
        with open(STATE_PATH) as f:
            st.update(json.load(f))
    except Exception:
        pass
    st['w'] = max(MIN_W, min(MAX_W, int(st['w'])))
    st['h'] = max(MIN_H, min(MAX_H, int(st['h'])))
    return st


def save_state(st):
    try:
        os.makedirs(os.path.dirname(STATE_PATH), exist_ok=True)
        with open(STATE_PATH, 'w') as f:
            json.dump(st, f, ensure_ascii=False, indent=2)
    except Exception:
        pass


def cm_to_px(mon, cm):
    """按显示器物理尺寸把厘米换算成像素; 取不到物理尺寸时按 96dpi 估算"""
    try:
        mm = mon.get_height_mm()
        h = mon.get_geometry().height
        if mm and h:
            return max(1, int(round(h * 10.0 * cm / mm)))
    except Exception:
        pass
    return max(1, int(round(37.8 * cm)))


LOG_PATH = '/tmp/opencode/url_click.log'


def log_line(msg):
    try:
        with open(LOG_PATH, 'a') as f:
            f.write('%s %s\n' % (datetime.datetime.now().strftime('%H:%M:%S'), msg))
    except Exception:
        pass


def find_desktop(name):
    dirs = []
    if os.environ.get('XDG_DATA_HOME'):
        dirs.append(os.path.join(os.environ['XDG_DATA_HOME'], 'applications'))
    for d in os.environ.get('XDG_DATA_DIRS', '').split(':'):
        if d:
            dirs.append(os.path.join(d, 'applications'))
    dirs += ['/usr/local/share/applications', '/usr/share/applications']
    for d in dirs:
        p = os.path.join(d, name)
        if os.path.exists(p):
            return p
    return None


def open_url(url):
    log_line('点击: %s' % url)
    try:
        out = subprocess.check_output(
            ['xdg-mime', 'query', 'default', 'text/html'],
            stderr=subprocess.DEVNULL, timeout=3).decode().strip()
        log_line('默认浏览器: %s' % out)
        desk = find_desktop(out) if out else None
        if desk:
            for line in open(desk, errors='ignore'):
                if line.startswith('Exec='):
                    cmd = line[5:].strip()
                    for ph in ('%U', '%u', '%F', '%f'):
                        cmd = cmd.replace(ph, '')
                    parts = shlex.split(cmd)
                    if parts:
                        log_line('执行: %s %s' % (parts[0], url))
                        subprocess.Popen(parts + [url],
                                         stdout=subprocess.DEVNULL,
                                         stderr=subprocess.DEVNULL)
                        return
    except Exception as e:
        log_line('默认浏览器解析失败: %s' % e)
    try:
        import shutil
        for b in ('brave-browser', 'google-chrome', 'chromium',
                  'chromium-browser', 'microsoft-edge', 'firefox'):
            if shutil.which(b):
                log_line('兜底执行: %s %s' % (b, url))
                subprocess.Popen(['nohup', b, url],
                                 stdout=subprocess.DEVNULL,
                                 stderr=subprocess.DEVNULL)
                return
    except Exception as e:
        log_line('兜底失败: %s' % e)
    try:
        log_line('最后兜底 xdg-open')
        subprocess.Popen(['xdg-open', url])
    except Exception:
        pass


def get_ip():
    try:
        out = subprocess.check_output(['hostname', '-I'], timeout=3,
                                      stderr=subprocess.DEVNULL).decode().split()
        if out:
            return out[0]
    except Exception:
        pass
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.settimeout(2)
        s.connect(('114.114.114.114', 53))
        ip = s.getsockname()[0]
        s.close()
        return ip
    except Exception:
        return '无'


def _parse_xml(data):
    """严格解析,失败时做宽容处理(转义裸&与非法控制字符)"""
    try:
        return ET.fromstring(data)
    except Exception:
        text = data.decode('utf-8', 'ignore')
        text = re.sub(r'[\x00-\x08\x0b\x0c\x0e-\x1f]', '', text)
        text = re.sub(r'&(?!amp;|lt;|gt;|quot;|apos;|#\d+;|#x[0-9a-fA-F]+;)', '&amp;', text)
        return ET.fromstring(text.encode('utf-8', 'ignore'))


def fetch_one(src):
    name, url = src
    for _attempt in range(2):
        try:
            req = urllib.request.Request(url, headers={'User-Agent': UA})
            with urllib.request.urlopen(req, timeout=15) as r:
                data = r.read(2 * 1024 * 1024)
            root = _parse_xml(data)
            titles = []              # [(标题, 链接), ...]
            for item in root.iter('item'):
                t = item.findtext('title')
                if t:
                    t = ' '.join(t.split())
                    if t:
                        link = item.findtext('link')
                        titles.append((t, (link or '').strip()))
                if len(titles) >= PER_SOURCE:
                    break
            if not titles:
                for e in root.iter('{http://www.w3.org/2005/Atom}entry'):
                    t = e.findtext('{http://www.w3.org/2005/Atom}title')
                    if t:
                        t = ' '.join(t.split())
                        if t:
                            l = e.find('{http://www.w3.org/2005/Atom}link')
                            link = l.get('href') if l is not None else None
                            titles.append((t, (link or '').strip()))
                    if len(titles) >= PER_SOURCE:
                        break
            if titles:
                base = url.split('?')[0]
                titles = [(t, urljoin(base, ln)) if ln else (t, '')
                          for t, ln in titles]
                return (name, titles)
        except Exception as e:
            log_line('fetch失败 %s: %r' % (name, e))
            time.sleep(2)
    return (name, None)


class RssWidget(Gtk.Window):
    COLOR_SCHEMES = {
        '默认': ((0.92, 0.93, 0.97), (0.98, 0.84, 0.30), (0.40, 0.62, 0.98)),
        '红色': ((0.95, 0.20, 0.16), (0.95, 0.20, 0.16), (0.95, 0.20, 0.16)),
        '蓝色': ((0.40, 0.62, 0.98), (0.40, 0.62, 0.98), (0.40, 0.62, 0.98)),
        '绿色': ((0.20, 0.80, 0.40), (0.20, 0.80, 0.40), (0.20, 0.80, 0.40)),
        '橙色': ((0.98, 0.60, 0.20), (0.98, 0.60, 0.20), (0.98, 0.60, 0.20)),
        '白色': ((0.92, 0.93, 0.97), (0.92, 0.93, 0.97), (0.92, 0.93, 0.97)),
    }

    def _autostart_desktop_name(self):
        return 'desktop-rss'

    def _autostart_enabled(self):
        name = self._autostart_desktop_name()
        path = os.path.expanduser(f'~/.config/autostart/{name}.desktop')
        return os.path.exists(path)

    def _toggle_autostart(self, enable):
        name = self._autostart_desktop_name()
        autostart_dir = os.path.expanduser('~/.config/autostart')
        desktop_path = os.path.join(autostart_dir, f'{name}.desktop')
        if enable:
            os.makedirs(autostart_dir, exist_ok=True)
            with open(desktop_path, 'w') as f:
                f.write('[Desktop Entry]\n')
                f.write('Type=Application\n')
                f.write(f'Name={name}\n')
                f.write(f'Exec=python3 {os.path.abspath(sys.argv[0])}\n')
                f.write('X-GNOME-Autostart-enabled=true\n')
                f.write('NoDisplay=true\n')
        else:
            if os.path.exists(desktop_path):
                os.remove(desktop_path)

    def _set_color_scheme(self, name):
        self.st['color_scheme'] = name
        save_state(self.st)
        self.queue_draw()

    def _get_colors(self):
        scheme = self.st.get('color_scheme', '默认')
        return self.COLOR_SCHEMES.get(scheme, self.COLOR_SCHEMES['默认'])

    def __init__(self):
        super().__init__(type=Gtk.WindowType.TOPLEVEL, decorated=False)
        self.set_title('desktop-rss')
        self.set_icon_name('desktop-rss')
        self.set_keep_below(True)
        self.set_skip_taskbar_hint(True)
        self.set_skip_pager_hint(True)
        self.set_app_paintable(True)
        self.set_accept_focus(False)
        self.set_can_focus(False)
        vis = self.get_screen().get_rgba_visual()
        if vis:
            self.set_visual(vis)

        self.st = load_state()
        # 默认铺满屏幕右侧, 底边与底部任务栏(状态栏)留 1 厘米间距
        # 仅在首次运行或布局升级时自动摆放, 之后保留用户手动调整过的大小
        try:
            if (not self.st.get('fit_full')
                    or self.st.get('fit_ver') != FIT_VER):
                dpy = Gdk.Display.get_default()
                mon = None
                if dpy is not None:
                    try:
                        mon = dpy.get_primary_monitor() or dpy.get_monitor(0)
                    except Exception:
                        mon = None
                if mon is not None:
                    wa = mon.get_workarea()
                    gap = cm_to_px(mon, BOTTOM_GAP_CM)
                    self.st['h'] = max(MIN_H, min(MAX_H, wa.height - gap - 2))
                    self.st['y'] = wa.y + 2
                    if self.st.get('x') is None:
                        self.st['x'] = wa.x + wa.width - self.st['w'] - 6
                    self.st['fit_full'] = True
                    self.st['fit_ver'] = FIT_VER
                    save_state(self.st)
        except Exception:
            pass
        self.w, self.h = self.st['w'], self.st['h']
        self.locked = bool(self.st['locked'])
        self.auto = int(self.st.get('auto', 600))
        self.ip = get_ip()
        self.data = []              # [(名称, [标题...])]
        self.refresh_time = None
        self.refreshing = False
        self._round_start = None
        self._gen = 0
        self.failed = 0
        self.failed_names = []
        self.selected = None        # 选中的源索引(第二行点击), None=全部
        self._refresh_pressed = False
        self.corner_hover = False
        self.dragging = False
        self.drag_start = None
        self.drag_orig = None
        self.resizing = False
        self.size_orig = None
        self.scroll = 0
        self._spin = 0.0

        if self.st['x'] is not None:
            self.move(self.st['x'], self.st['y'])
        else:
            self.move(60, 100)
        self.resize(self.w, self.h)
        self.connect('draw', self.on_draw)
        self.connect('button-press-event', self.on_press)
        self.connect('motion-notify-event', self.on_motion)
        self.connect('button-release-event', self.on_release)
        self.connect('leave-notify-event', self.on_leave)
        self.connect('scroll-event', self.on_scroll)
        self.add_events(Gdk.EventMask.BUTTON_PRESS_MASK |
                        Gdk.EventMask.BUTTON_RELEASE_MASK |
                        Gdk.EventMask.POINTER_MOTION_MASK |
                        Gdk.EventMask.LEAVE_NOTIFY_MASK |
                        Gdk.EventMask.SCROLL_MASK)

        self._refresh()
        if self.auto > 0:
            GLib.timeout_add_seconds(self.auto, self._auto_tick)
        GLib.timeout_add(800, self._all_desktops)
        GLib.timeout_add(5000, self._all_desktops)
        self.show_all()

    def _all_desktops(self):
        """固定到所有桌面: 进程内直接改 _NET_WM_DESKTOP(不再起子进程)"""
        if not getattr(self, '_ad_done', False):
            try:
                win = self.get_window()
                wid = win.get_xid() if win else 0
                if wid:
                    from Xlib import display as _xd
                    _d = _xd.Display()
                    _w = _d.create_resource_object('window', wid)
                    _w.change_property(_d.intern_atom('_NET_WM_DESKTOP'),
                                       _d.intern_atom('CARDINAL'), 32,
                                       [0xFFFFFFFF])
                    _d.sync()
                    _d.close()
                    self._ad_done = True
            except Exception:
                pass
        return False

    def log(self, msg):
        try:
            p = os.path.join(os.path.expanduser('~/.config/desktop-rss'), 'rss.log')
            os.makedirs(os.path.dirname(p), exist_ok=True)
            with open(p, 'a') as f:
                f.write(datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
                        + ' ' + str(msg) + '\n')
        except Exception:
            pass

    # ---------- 抓取 ----------
    def _refresh(self):
        now = time.monotonic()
        if self.refreshing and (self._round_start is None
                                or now - self._round_start < 60):
            return
        self.refreshing = True
        self._spin = 0.0
        GLib.timeout_add(80, self._spin_tick)
        self._round_start = now
        self._gen += 1
        gen = self._gen
        self.queue_draw()

        def work():
            try:
                with ThreadPoolExecutor(max_workers=5) as ex:
                    results = list(ex.map(fetch_one, SOURCES))
            except Exception as e:
                results = []
                self.log('fetch: %s' % e)
            GLib.idle_add(self._apply, results, gen)

        threading.Thread(target=work, daemon=True).start()

    def _apply(self, results, gen):
        if gen != self._gen:
            return
        self.refreshing = False
        self._refresh_pressed = False
        if results:
            old = {n: ts for n, ts in self.data}
            merged = []
            for n, ts in results:
                if ts is None and old.get(n):
                    ts = old[n]
                merged.append((n, ts))
            self.data = merged
        self.refresh_time = datetime.datetime.now()
        self.failed = sum(1 for _, ts in self.data if ts is None)
        self.failed_names = sorted(n for n, ts in self.data if ts is None)
        self.scroll = 0
        self.queue_draw()

    def _auto_tick(self):
        self._refresh()
        return True

    # ---------- 菜单 ----------
    def _autostart_desktop_name(self):
        return os.path.basename(sys.argv[0]).replace('.py', '')

    def _autostart_enabled(self):
        name = self._autostart_desktop_name()
        path = os.path.expanduser(f'~/.config/autostart/{name}.desktop')
        return os.path.exists(path)

    def _toggle_autostart(self, enable):
        name = self._autostart_desktop_name()
        autostart_dir = os.path.expanduser('~/.config/autostart')
        desktop_path = os.path.join(autostart_dir, f'{name}.desktop')
        if enable:
            os.makedirs(autostart_dir, exist_ok=True)
            with open(desktop_path, 'w') as f:
                f.write('[Desktop Entry]\n')
                f.write('Type=Application\n')
                f.write(f'Name={name}\n')
                if sys.argv[0].endswith('.py'):
                    f.write(f'Exec=python3 {os.path.abspath(sys.argv[0])}\n')
                else:
                    f.write(f'Exec={os.path.abspath(sys.argv[0])}\n')
                f.write('X-GNOME-Autostart-enabled=true\n')
                f.write('NoDisplay=true\n')
        else:
            if os.path.exists(desktop_path):
                os.remove(desktop_path)

    def show_menu(self, event):
        menu = Gtk.Menu()
        ref = Gtk.MenuItem.new_with_label('刷新')
        ref.connect('activate', lambda *a: self._refresh())
        auto_item = Gtk.MenuItem.new_with_label('自动刷新')
        auto_sub = Gtk.Menu()
        for s, label in ((0, '关闭'), (300, '5 分钟'), (600, '10 分钟'),
                         (1800, '30 分钟'), (3600, '1 小时')):
            it = Gtk.CheckMenuItem.new_with_label(label)
            it.set_active(self.auto == s)
            it.connect('activate', lambda w, v=s, sub=auto_sub: self._set_auto_menu(w, v, sub))
            auto_sub.append(it)
        auto_item.set_submenu(auto_sub)
        lock_item = Gtk.CheckMenuItem.new_with_label('锁定位置')
        lock_item.set_active(self.locked)
        lock_item.connect('toggled', self._toggle_lock)
        quit_item = Gtk.MenuItem.new_with_label('退出')
        quit_item.connect('activate', lambda *a: (self.destroy(), Gtk.main_quit()))

        color_item = Gtk.MenuItem.new_with_label('改变颜色')
        color_sub = Gtk.Menu()
        color_schemes = [
            ('默认', (0.92, 0.93, 0.97), (0.98, 0.84, 0.30), (0.40, 0.62, 0.98)),
            ('红色', (0.95, 0.20, 0.16), (0.95, 0.20, 0.16), (0.95, 0.20, 0.16)),
            ('蓝色', (0.40, 0.62, 0.98), (0.40, 0.62, 0.98), (0.40, 0.62, 0.98)),
            ('绿色', (0.20, 0.80, 0.40), (0.20, 0.80, 0.40), (0.20, 0.80, 0.40)),
            ('橙色', (0.98, 0.60, 0.20), (0.98, 0.60, 0.20), (0.98, 0.60, 0.20)),
            ('白色', (0.92, 0.93, 0.97), (0.92, 0.93, 0.97), (0.92, 0.93, 0.97)),
        ]
        current_scheme = self.st.get('color_scheme', '默认')
        for name, c1, c2, c3 in color_schemes:
            it = Gtk.CheckMenuItem.new_with_label(name)
            it.set_active(current_scheme == name)
            it.connect('activate', lambda w, n=name: self._set_color_scheme(n))
            color_sub.append(it)
        color_item.set_submenu(color_sub)

        menu.append(ref)
        menu.append(auto_item)
        menu.append(Gtk.SeparatorMenuItem())
        menu.append(lock_item)
        menu.append(Gtk.SeparatorMenuItem())
        menu.append(color_item)
        menu.append(Gtk.SeparatorMenuItem())
        autostart_item = Gtk.CheckMenuItem(label='开机自启')
        autostart_item.set_active(self._autostart_enabled())
        autostart_item.connect('toggled', lambda w: self._toggle_autostart(w.get_active()))
        menu.append(autostart_item)
        menu.append(quit_item)
        menu.show_all()
        menu.popup(None, None, None, None, event.button, event.time)

    def _set_auto(self, v):
        self.auto = v
        self.st['auto'] = v
        save_state(self.st)

    def _set_auto_menu(self, item, v, sub):
        for it in sub.get_children():
            it.set_active(it is item)
        self._set_auto(v)

    def _toggle_lock(self, item):
        self.locked = item.get_active()
        self.st['locked'] = self.locked
        save_state(self.st)

    # ---------- 事件 ----------
    def on_press(self, wdg, ev):
        if ev.button == 3:
            self.show_menu(ev)
            return True
        if ev.button == 1:
            if HEAD_H - 2 <= ev.y < self.h - FOOT_H:
                for y0, y1, link in getattr(self, '_row_hits', []):
                    if y0 - 3 <= ev.y <= y1 + 3 and link:
                        open_url(link)
                        return True
            rb = getattr(self, '_refresh_btn', None)
            if rb and rb[0] <= ev.x <= rb[0] + rb[2] and rb[1] <= ev.y <= rb[1] + rb[3]:
                self._refresh_pressed = True
                self.queue_draw()
                self._refresh()
                return True
            idx = self._hit_name(ev.x, ev.y)
            if idx is not None:
                new_sel = self._sel_from_tab(idx)
                self.selected = None if self.selected == new_sel else new_sel
                self.scroll = 0
                self.queue_draw()
                return True
            if not self.locked:
                if (self.w - CORNER <= ev.x <= self.w and
                        self.h - CORNER <= ev.y <= self.h):
                    self.resizing = True
                    self.size_orig = (self.w, self.h, ev.x_root, ev.y_root)
                    return True
                self.dragging = True
                self.drag_start = (ev.x_root, ev.y_root)
                self.drag_orig = (self.x if self.x is not None else 0,
                                  self.y if self.y is not None else 0)
                return True
        return False

    def _hit_name(self, x, y):
        if not (64 <= y <= 88):
            return None
        for i, (nx, nw) in enumerate(self._name_rects()):
            if nx <= x <= nx + nw:
                return i
        return None

    def _sel_from_tab(self, tab_idx):
        """标签下标 → self.selected (0=全部 → None, 其后 → 源下标)"""
        return None if tab_idx == 0 else tab_idx - 1

    def _tab_from_sel(self):
        """self.selected → 标签下标"""
        return 0 if self.selected is None else self.selected + 1

    def _name_rects(self):
        rects = []
        sz = self._name_sz()
        x = PAD
        for name in TABS:
            lw = self._measure(name, 'FZSongHei-B07 %d' % sz)[0] + 12
            if x + lw > self.w - PAD:
                break
            rects.append((x, lw))
            x += lw
        return rects

    def _name_sz(self):
        sz = 20
        while sz > 10:
            total = sum(self._measure(n, 'FZSongHei-B07 %d' % sz)[0] + 12
                        for n in TABS)
            if total <= self.w - 2 * PAD:
                break
            sz -= 1
        return sz

    def on_motion(self, wdg, ev):
        if self.resizing:
            nw = max(MIN_W, min(MAX_W, self.size_orig[0] + ev.x_root - self.size_orig[2]))
            nh = max(MIN_H, min(MAX_H, self.size_orig[1] + ev.y_root - self.size_orig[3]))
            self.resize(nw, nh)
            self.w, self.h = nw, nh
            self.st['w'], self.st['h'] = nw, nh
            save_state(self.st)
            self.queue_draw()
            return
        if self.dragging and not self.locked:
            dx = ev.x_root - self.drag_start[0]
            dy = ev.y_root - self.drag_start[1]
            self.move(self.drag_orig[0] + dx, self.drag_orig[1] + dy)
            return
        ch = (not self.locked and
              self.w - CORNER <= ev.x <= self.w and self.h - CORNER <= ev.y <= self.h)
        if ch != self.corner_hover:
            self.corner_hover = ch
            self.queue_draw()

    def on_release(self, wdg, ev):
        if self.dragging:
            self.x, self.y = self.get_position()
            self.st['x'], self.st['y'] = self.x, self.y
            save_state(self.st)
        if self.resizing:
            self.st['w'], self.st['h'] = self.w, self.h
            save_state(self.st)
        self.dragging = False
        self.resizing = False

    def on_leave(self, wdg, ev):
        self.corner_hover = False
        self.queue_draw()

    def on_scroll(self, wdg, ev):
        ct = self._content_h()
        vh = self.h - HEAD_H - FOOT_H
        max_scroll = max(0, ct - vh)
        if ev.direction == Gdk.ScrollDirection.UP:
            self.scroll = max(0, self.scroll - 48)
        elif ev.direction == Gdk.ScrollDirection.DOWN:
            self.scroll = min(max_scroll, self.scroll + 48)
        elif ev.direction == Gdk.ScrollDirection.SMOOTH:
            self.scroll = min(max_scroll, max(0, self.scroll + ev.delta_y * 24))
        self.queue_draw()
        return True

    # ---------- 绘制 ----------
    def _measure(self, txt, font):
        lay = self.create_pango_layout(txt)
        lay.set_font_description(Pango.FontDescription.from_string(font))
        return lay.get_pixel_size()

    def _text(self, ctx, txt, x, y, maxw, font, color, alpha=1.0):
        layout = self.create_pango_layout(txt)
        layout.set_font_description(Pango.FontDescription.from_string(font))
        layout.set_ellipsize(Pango.EllipsizeMode.END)
        layout.set_width(int(maxw * Pango.SCALE))
        ctx.set_source_rgba(color[0], color[1], color[2], alpha)
        ctx.move_to(x, y)
        PangoCairo.show_layout(ctx, layout)
        return layout.get_pixel_size()

    def _text_wrap(self, ctx, txt, x, y, maxw, font, color, alpha=1.0,
                   hanging=False):
        """标题用: 不省略, 一行放不下就自动换行完整显示
        hanging=True: 折行后的第一个字与第一行的第一个字对齐(悬挂缩进)"""
        layout = self.create_pango_layout(txt)
        layout.set_font_description(Pango.FontDescription.from_string(font))
        off = 0
        if hanging:
            lb = self.create_pango_layout('· ')
            lb.set_font_description(Pango.FontDescription.from_string(font))
            off = lb.get_pixel_size()[0]
            maxw = max(40, maxw - off)
            layout.set_indent(-off)
        layout.set_width(int(maxw * Pango.SCALE))
        layout.set_wrap(Pango.WrapMode.WORD_CHAR)
        ctx.set_source_rgba(color[0], color[1], color[2], alpha)
        ctx.move_to(x + off, y)
        PangoCairo.show_layout(ctx, layout)
        return layout.get_pixel_size()

    def _wrapped_height(self, txt, font, maxw, hanging=False):
        layout = self.create_pango_layout(txt)
        layout.set_font_description(Pango.FontDescription.from_string(font))
        if hanging:
            lb = self.create_pango_layout('· ')
            lb.set_font_description(Pango.FontDescription.from_string(font))
            off = lb.get_pixel_size()[0]
            maxw = max(40, maxw - off)
            layout.set_indent(-off)
        layout.set_width(int(maxw * Pango.SCALE))
        layout.set_wrap(Pango.WrapMode.WORD_CHAR)
        return layout.get_pixel_size()[1]

    def _text_right(self, ctx, txt, right_x, y, font, color, alpha=1.0):
        """右对齐一行文字(右边缘固定在 right_x)"""
        tw, _ = self._measure(txt, font)
        self._text(ctx, txt, right_x - tw, y, tw + 6, font, color, alpha)
        return tw

    def _rounded(self, ctx, x, y, w, h, r):
        ctx.new_path()
        ctx.arc(x + r, y + r, r, 3.1416, 1.5 * 3.1416)
        ctx.arc(x + w - r, y + r, r, 1.5 * 3.1416, 2 * 3.1416)
        ctx.arc(x + w - r, y + h - r, r, 0, 0.5 * 3.1416)
        ctx.arc(x + r, y + h - r, r, 0.5 * 3.1416, 3.1416)
        ctx.close_path()

    def _spin_tick(self):
        # 底部已去掉旋转箭头图标, 仅保留一次刷新收尾重绘以刷新"最后刷新时间"
        self._spin = 0.0
        return False

    def on_draw(self, wdg, ctx):
        try:
            self._draw(ctx)
        except Exception:
            pass
        return False

    def _rows(self):
        """当前要显示的 (标题, 链接) 行列表 (始终不带源名)"""
        rows = []
        if self.selected is not None and self.selected < len(self.data):
            _, titles = self.data[self.selected]
            rows = titles if titles else []
        else:
            # 「全部」: 合并所有源的完整列表
            for _, titles in self.data:
                if titles:
                    rows.extend(titles)
        return rows

    def _content_h(self):
        return 6 + sum(self._row_heights()) + 6

    ROW_FONT = _SYS_FONT + ' 13'
    ROW_MIN_H = 34
    ROW_IND = 15

    def _row_heights(self):
        """每行标题换行后的实际像素高度"""
        maxw = max(60, self.w - 2 * PAD - self.ROW_IND)
        out = []
        for t, _link in self._rows():
            h = self._wrapped_height(t, self.ROW_FONT, maxw)
            out.append(max(self.ROW_MIN_H, h + 6))
        return out

    def _footer_msg(self, avail):
        """状态栏左侧文字, 在 avail 宽度内放得下, 不出现省略号
        放不下时依次: 缩小字号 → 失败名单退化为「失败 N 项」 → 只留刷新时间"""
        if self.refreshing:
            return ('正在刷新…', FOOT_FONT)
        if not self.refresh_time:
            return ('', FOOT_FONT)
        s = '最后刷新: %s' % self.refresh_time.strftime('%H:%M:%S')
        if not self.failed:
            return (s, FOOT_FONT)
        msg = '%s  (失败: %s)' % (s, '、'.join(self.failed_names))
        for font in FOOT_FONTS:
            if self._measure(msg, font)[0] <= avail:
                return (msg, font)
        msg = '%s  (失败 %d 项)' % (s, self.failed)
        if self._measure(msg, FOOT_FONTS[-1])[0] <= avail:
            return (msg, FOOT_FONTS[-1])
        return (s, FOOT_FONT)

    def _draw(self, ctx):
        w = self.w
        ctx.set_operator(cairo.OPERATOR_CLEAR)
        ctx.paint()
        ctx.set_operator(cairo.OPERATOR_OVER)

        # 第一行: 日期(大红色) + IP(右对齐, 不与日期重叠)
        now = datetime.datetime.now()
        week = '一二三四五六日'[now.weekday()]
        d = '%d年%d月%d日 星期%s' % (now.year, now.month, now.day, week)
        dw, _ = self._measure(d, 'FZSongHei-B07 Bold 16')
        self._text(ctx, d, PAD, 12, w - 2 * PAD, 'FZSongHei-B07 Bold 16', self._get_colors()[0])
        ip_text = 'IP: %s' % self.ip
        ipw, _ = self._measure(ip_text, 'FZSongHei-B07 Bold 14')
        # 确保IP不与日期重叠: 右对齐但至少从日期结束后20像素开始
        ip_x = max(PAD + dw + 20, w - PAD - ipw)
        self._text_right(ctx, ip_text, w - PAD, 14,
                         'FZSongHei-B07 Bold 14', self._get_colors()[1])

        # 第二行: 来源名称(可点击, 选中高亮, 字号自适应宽度, 充满整体宽度)
        # 从大字号开始尝试，找到能充满宽度的最大字号
        sz = 20
        while sz > 10:
            total = sum(self._measure(n, 'FZSongHei-B07 %d' % sz)[0] + 12
                        for n in TABS)
            if total <= w - 2 * PAD:
                break
            sz -= 1
        # 如果字号太小，尝试增加间距来充满宽度
        total_at_sz = sum(self._measure(n, 'FZSongHei-B07 %d' % sz)[0] + 12
                          for n in TABS)
        if total_at_sz < w - 2 * PAD and len(TABS) > 1:
            # 计算额外间距
            extra = (w - 2 * PAD - total_at_sz) // (len(TABS) - 1)
        else:
            extra = 0
        x = PAD
        failed_set = set(self.failed_names)
        sel_tab = self._tab_from_sel()
        for i, name in enumerate(TABS):
            nw = self._measure(name, 'FZSongHei-B07 %d' % sz)[0]
            lw = nw + 12
            if x + lw > w - PAD:
                break
            is_failed = name in failed_set
            if i == sel_tab:
                self._rounded(ctx, x - 3, 60, lw + extra, 24, 6)
                ctx.set_source_rgba(1.0, 0.70, 0.10, 0.22)
                ctx.fill()
                self._text(ctx, name, x, 65, lw + extra, 'FZSongHei-B07 %d' % sz, SEL_C, 1.0)
            elif is_failed:
                self._text(ctx, name, x, 65, lw + extra, 'FZSongHei-B07 %d' % sz, RED, 1.0)
            else:
                self._text(ctx, name, x, 65, lw + extra, 'FZSongHei-B07 %d' % sz, NAME_C, 0.95)
            x += lw + extra

        # 内容区
        ly_top = HEAD_H
        lh_area = self.h - HEAD_H - FOOT_H
        ctx.save()
        ctx.rectangle(0, HEAD_H, w, lh_area)
        ctx.clip()
        y = HEAD_H + 4 - self.scroll
        line_h = 34
        colors = self._get_colors()
        ci = 0
        self._row_hits = []
        heights = self._row_heights()
        if not self.data and self.refreshing:
            self._text(ctx, '正在刷新…', PAD, y, w - 2 * PAD, 'FZSongHei-B07 12', DIM, 0.8)
        elif not self.data:
            self._text(ctx, '刷新失败, 请右键菜单刷新重试', PAD, y,
                       w - 2 * PAD, 'FZSongHei-B07 12', DIM, 0.8)
        else:
            rows = self._rows()
            if not rows:
                self._text(ctx, '该源暂无内容', PAD, y, w - 2 * PAD,
                           'FZSongHei-B07 12', DIM, 0.8)
            for i, (t, link) in enumerate(rows):
                lh = heights[i] if i < len(heights) else line_h
                # 未滚动时: 放不下整行的就不画, 避免出现"半条"
                if self.scroll <= 0 and (y + lh) > (ly_top + lh_area):
                    break
                self._row_hits.append((y, y + lh, link or None))
                col = colors[ci % 3]
                # 项目符号: 自己画的小圆点(避免全角'·'把文字挤走)
                ctx.set_source_rgba(col[0], col[1], col[2], 0.9)
                ctx.arc(PAD + 4, y + 11, 3.0, 0, 6.2832)
                ctx.fill()
                # 标题: 文本块左边缘固定, 折行后的字与第一行第一个字对齐
                self._text_wrap(ctx, t, PAD + self.ROW_IND, y + 3,
                                w - 2 * PAD - self.ROW_IND, self.ROW_FONT,
                                col, 0.97)
                ci += 1
                y += lh
        ctx.restore()

        # 底部状态栏: 左侧最后刷新时间, 右侧(刷新)按钮
        lab = '刷新中' if self.refreshing else '刷新'
        lw, _lh = self._measure(lab, FOOT_BTN_FONT)
        lcol = SEL_C if self.refreshing else (0.85, 0.90, 1.0) if \
            self._refresh_pressed else NAME_C
        # 避开右下角缩放柄
        lab_right = self.w - CORNER - 2
        self._text(ctx, lab, lab_right - lw - 6, self.h - 27, lw + 12,
                   FOOT_BTN_FONT, lcol, 1.0)
        self._refresh_btn = (int(lab_right - lw - 8), int(self.h - 32),
                             int(lw + 14), 32)
        # 左侧: 正在刷新… / 最后刷新时间(失败源名单), 到「刷新」按钮前为止
        left_w = max(FOOT_MIN_W, lab_right - lw - 12 - PAD)
        msg, fnt = self._footer_msg(left_w)
        if msg:
            self._text(ctx, msg, PAD, self.h - 27, left_w + 4, fnt,
                       SEL_C if self.refreshing else NAME_C, 1.0)

        # 缩放柄(常显)
        ctx.move_to(w - CORNER + 8, self.h - 9)
        ctx.line_to(w - 9, self.h - CORNER + 8)
        ctx.set_line_width(2)
        ctx.set_source_rgba(0.7, 0.7, 0.8, 0.55 if self.corner_hover else 0.28)
        ctx.stroke()


if __name__ == '__main__':
    check_single()
    RssWidget()
    Gtk.main()
