#!/usr/bin/env python3
from widget_font import SYS_FONT as _SYS_FONT
# -*- coding: utf-8 -*-
"""桌面电台挂件 - 背景透明, 可移动/锁定/缩放, 基于 mpv 播放喜马拉雅电台"""
import signal
import json
import os
import socket
import subprocess
import sys
import threading
import time

import gi
gi.require_version('Gtk', '3.0')
from gi.repository import Gtk, Gdk, GLib, Pango, PangoCairo
import cairo

GLib.set_prgname('desktop-radio')

APP_DIR = os.path.dirname(os.path.abspath(__file__))
CONF_DIR = os.path.expanduser('~/.config/desktop-radio')
STATE_PATH = os.path.join(CONF_DIR, 'state.json')
LOG_PATH = os.path.join(CONF_DIR, 'radio.log')
PID_PATH = os.path.join(CONF_DIR, 'pid')
SOCK_PATH = '/tmp/desktop-radio-mpv.sock'
# 电台源查找顺序: 用户自定义 -> 随包 -> 系统共享 -> 兼容旧路径
RADIO_JSON_CANDIDATES = ['/usr/share/dtbox/radioData.json', os.path.join(APP_DIR, 'radioData.json')]

MIN_W, MIN_H = 520, 170
MAX_W, MAX_H = 1600, 1400
PAD = 14
CORNER = 26

BTN_FONT = _SYS_FONT + ' Bold 11'
BTN_COLORS = {
    'prev': (0.96, 0.55, 0.16),
    'toggle': (0.22, 0.62, 0.92),
    'next': (0.96, 0.55, 0.16),
    'stop': (0.88, 0.26, 0.24),
    'list': (0.45, 0.55, 0.88),
}
BTN_HOVER = 1.25
BTN_GAP = 10
BTN_W = 84
BTN_H = 32
BTN_KEYS = ['prev', 'toggle', 'next', 'stop', 'list']

TITLE_COLOR = (0.92, 0.93, 0.97)
ARTIST_COLOR = (0.66, 0.70, 0.80)
HINT_COLOR = (0.55, 0.58, 0.68)


def log(msg):
    line = '%s %s\n' % (time.strftime('%m-%d %H:%M:%S'), msg)
    for p in (LOG_PATH, os.path.join(APP_DIR, 'radio.log')):
        try:
            os.makedirs(os.path.dirname(p), exist_ok=True)
            with open(p, 'a') as f:
                f.write(line)
            return
        except Exception:
            continue


def load_state():
    st = {'x': None, 'y': None, 'w': 640, 'h': 170, 'locked': False,
          'font_scale': 1.0, 'volume': 80, 'playing': False}
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
    except Exception as e:
        log('state save: ' + str(e))


def check_single():
    try:
        with open(PID_PATH) as f:
            old = int(f.read().strip())
        if os.path.exists('/proc/%d' % old):
            c = open('/proc/%d/cmdline' % old, 'rb').read().decode(errors='ignore')
            if 'radio.py' in c:
                sys.exit(0)
    except Exception:
        pass
    try:
        with open(PID_PATH, 'w') as f:
            f.write(str(os.getpid()))
    except Exception:
        pass


def load_radios():
    """返回 [(分类, [(名称, url, id)])], 支持多路径电台源查找"""
    data = None
    for p in RADIO_JSON_CANDIDATES:
        try:
            with open(p) as f:
                data = json.load(f)
            log('radios loaded: ' + p)
            break
        except Exception as e:
            log('radios load skip %s: %s' % (p, e))
    if not data:
        return []
    out = []
    for cat in data:
        items = []
        for r in cat.get('RadioList', []):
            url = ''
            pu = r.get('playUrl') or {}
            url = pu.get('aac64') or pu.get('aac24') or pu.get('ts64') or ''
            if url:
                items.append((r.get('name', '未命名'), url, r.get('id', 0)))
        if items:
            out.append((cat.get('categoryName', '未分类'), items))
    return out


VOL_MAX = 200        # mpv --volume-max, 放开上限
VOL_GAIN = 2         # 界面 0-100 → mpv 0-200, 解决“音量太小”


class MpvPlayer:
    def __init__(self, volume=80):
        self.proc = None
        self.volume = volume
        self.started = False      # 没点播放前不拉起 mpv
        self._lock = threading.Lock()
        self._buf = b''

    def _mpv_vol(self, v=None):
        """界面音量(0-100) → mpv 音量(0-VOL_MAX)"""
        v = self.volume if v is None else v
        return max(0, min(VOL_MAX, int(round(v * VOL_GAIN))))

    def _sock_alive(self):
        """检测是否有活跃的 mpv 播放器在监听 socket(复用, 避免杀进程牵连)"""
        try:
            s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            s.settimeout(0.5)
            s.connect(SOCK_PATH)
            s.close()
            return True
        except Exception:
            return False

    def start(self):
        """确保有可用的 mpv 播放器(优先复用已有实例, 无则启动新的)"""
        if self.proc and self.proc.poll() is None:
            return True
        if self._sock_alive():
            self.proc = None  # 复用已运行的播放器
            return True
        try:
            os.unlink(SOCK_PATH)
        except Exception:
            pass
        try:
            self.proc = subprocess.Popen(
                ['/usr/local/bin/mpv', '--idle=yes', '--no-terminal', '--really-quiet',
                 '--vo=null', '--audio-display=no', '--osd-level=0',
                 '--keep-open=no', '--no-input-default-bindings',
                 '--input-ipc-server=' + SOCK_PATH,
                 '--volume-max=%d' % VOL_MAX,
                 '--volume=%d' % self._mpv_vol()],
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                start_new_session=True)
        except Exception as e:
            log('mpv start: ' + str(e))
            return False
        deadline = time.time() + 6
        while time.time() < deadline:
            if self.proc.poll() is not None:
                log('mpv exited early')
                return False
            if os.path.exists(SOCK_PATH):
                return True
            time.sleep(0.05)
        return os.path.exists(SOCK_PATH)

    def _ipc(self, payload, timeout=3.0):
        with self._lock:
            if not self.start():
                return None
            try:
                s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
                s.settimeout(timeout)
                s.connect(SOCK_PATH)
                s.sendall((json.dumps(payload) + '\n').encode())
                buf = b''
                while True:
                    chunk = s.recv(65536)
                    if not chunk:
                        break
                    buf += chunk
                    if b'\n' in buf:
                        break
                s.close()
                line = buf.split(b'\n', 1)[0]
                return json.loads(line.decode(errors='ignore'))
            except Exception as e:
                log('ipc: ' + str(e))
                return None

    def load(self, url):
        self.started = True
        r = self._ipc({'command': ['loadfile', url, 'replace']})
        self.unpause()
        return r

    def unpause(self):
        return self._ipc({'command': ['set_property', 'pause', False]})

    def pause(self, want):
        return self._ipc({'command': ['set_property', 'pause', bool(want)]})

    def stop(self):
        self._ipc({'command': ['stop']})

    def set_volume(self, v):
        self.volume = max(0, min(100, int(v)))
        self._apply_vol()
        return self.volume

    def _apply_vol(self):
        """实际写入 mpv 音量(尝试放开上限, 复用旧播放器时也生效)"""
        self._ipc({'command': ['set_property', 'volume-max', VOL_MAX]})
        self._ipc({'command': ['set_property', 'volume', self._mpv_vol()]})

    def state(self, volume=True):
        # 还没点过播放 → 根本不去惊动 mpv，开机不会后台空跑一个播放器
        if not self.started and not self._sock_alive():
            return {'paused': True, 'title': '', 'volume': self.volume}
        r = self._ipc({'command': ['get_property', 'pause']}, timeout=1.2)
        paused = r.get('data', True) if r else True
        r2 = self._ipc({'command': ['get_property', 'media-title']}, timeout=1.2)
        title = r2.get('data', '') if r2 else ''
        r4 = self._ipc({'command': ['get_property', 'idle-active']}, timeout=1.2)
        idle = r4.get('data', True) if r4 else True
        if volume:
            r3 = self._ipc({'command': ['get_property', 'volume']}, timeout=1.2)
            vol = r3.get('data', self._mpv_vol()) if r3 else self._mpv_vol()
            if isinstance(vol, (int, float)):
                self.volume = int(round(float(vol) / VOL_GAIN))
        # 空闲(没加载电台)也算没在放, 否则界面会误报“播放中”
        return {'paused': bool(paused) or bool(idle), 'title': title,
                'volume': self.volume}

    def kill(self):
        if self.proc:
            try:
                self.proc.terminate()
            except Exception:
                pass


class RadioWidget(Gtk.Window):
    def _autostart_desktop_name(self):
        return 'desktop-radio'

    def _autostart_enabled(self):
        name = self._autostart_desktop_name()
        path = os.path.expanduser(f'~/.config/autostart/{name}.desktop')
        return os.path.exists(path)

    def _launcher(self):
        """自启优先走 /usr/local/bin 下的包装脚本, 保证守护标记被清掉"""
        p = '/usr/local/bin/' + self._autostart_desktop_name()
        if os.path.exists(p):
            return p
        me = os.path.abspath(sys.argv[0])
        if me.endswith('.py'):
            return 'python3 ' + me
        return me

    def _toggle_autostart(self, enable):
        name = self._autostart_desktop_name()
        autostart_dir = os.path.expanduser('~/.config/autostart')
        desktop_path = os.path.join(autostart_dir, f'{name}.desktop')
        if enable:
            os.makedirs(autostart_dir, exist_ok=True)
            with open(desktop_path, 'w') as f:
                f.write('[Desktop Entry]\n')
                f.write('Type=Application\n')
                f.write('Name=桌面收音机挂件\n')
                f.write('Exec=%s\n' % self._launcher())
                f.write('X-GNOME-Autostart-enabled=true\n')
                f.write('X-GNOME-Autostart-Delay=2\n')
                f.write('NoDisplay=true\n')
        else:
            if os.path.exists(desktop_path):
                os.remove(desktop_path)

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

    def __init__(self):
        super().__init__(type=Gtk.WindowType.TOPLEVEL, decorated=False)
        self.set_title('desktop-radio')
        self.set_icon_name('desktop-radio')
        self.set_keep_below(True)
        self.set_skip_taskbar_hint(True)
        self.set_skip_pager_hint(True)
        self.set_app_paintable(True)
        vis = self.get_screen().get_rgba_visual()
        if vis:
            self.set_visual(vis)

        self.st = load_state()
        self.w, self.h = self.st['w'], self.st['h']
        if self.st['x'] is not None:
            self.move(self.st['x'], self.st['y'])
        self.resize(self.w, self.h)
        self.connect('draw', self.on_draw)
        self.connect('button-press-event', self.on_press)
        self.connect('motion-notify-event', self.on_motion)
        self.connect('button-release-event', self.on_release)
        self.connect('leave-notify-event', self.on_leave)
        self.add_events(Gdk.EventMask.BUTTON_PRESS_MASK |
                        Gdk.EventMask.BUTTON_RELEASE_MASK |
                        Gdk.EventMask.POINTER_MOTION_MASK |
                        Gdk.EventMask.LEAVE_NOTIFY_MASK)

        self.player = MpvPlayer(self.st.get('volume', 80))
        self.radios = load_radios()
        self.cur = int(self.st.get('cur', 0))          # 当前电台全局索引
        self.playing = False
        self.paused = True
        self.cur_title = ''
        self.hover = None
        self.corner_hover = False
        self.dragging = False
        self.drag_start = None
        self.drag_orig = None
        self.resizing = False
        self.size_orig = None
        self.list_win = None
        self.list_grabbed = False
        # 开机/注销/重启一律不自动播放, 只有用户点了播放/选台才开始
        self.want_play = False
        self.user_paused = False
        self._last_ok = 0.0
        self._retry_gap = 4.0
        self._vol_tick = 0

        log('start: radios=%d cur=%d (不自动播放)' % (len(self._flat()), self.cur))
        GLib.timeout_add(400, self._poll)
        GLib.timeout_add(4000, self._watchdog)
        GLib.timeout_add(2000, self._reload_radios)
        GLib.timeout_add(30000, self._all_desktops)
        self._all_desktops()

    def _flat(self):
        out = []
        for cat, items in self.radios:
            out.extend(items)
        return out

    def _cur_radio(self):
        flat = self._flat()
        if not flat:
            return None
        self.cur = max(0, min(self.cur, len(flat) - 1))
        return flat[self.cur]

    def _reload_radios(self):
        """电台源偶尔会读不到(磁盘/挂载慢), 空列表时自己补一次"""
        if self._flat() or self.player.started:
            return False
        self.radios = load_radios()
        if self._flat():
            log('radios reloaded: %d' % len(self._flat()))
            self.queue_draw()
            return False
        return True

    def _watchdog(self):
        """播放看门狗: mpv 因网络/音频设备未就绪而自己暂停时, 自动重新拉起"""
        if not self.want_play or self.user_paused:
            return True
        if self.playing and not self.paused:
            return True
        now = time.time()
        if now - self._last_ok < self._retry_gap:
            return True
        if not self._flat():
            self.radios = load_radios()
        if self._flat():
            log('watchdog: replay #%d (%s)' % (
                self.cur, 'paused' if self.playing else 'idle'))
            self._play_index(self.cur, save=False)
        self._retry_gap = min(30.0, max(4.0, self._retry_gap * 1.5))
        return True

    def _play_index(self, idx, save=True):
        flat = self._flat()
        if not flat:
            return None
        idx = max(0, min(idx, len(flat) - 1))
        self.cur = idx
        name, url, rid = flat[idx]
        self.cur_title = name
        self.paused = False
        self.playing = True
        self.want_play = True
        self.user_paused = False
        self.st['cur'] = idx
        self.st['playing'] = True
        if save:
            save_state(self.st)
        self.player.load(url)
        self.queue_draw()

    def _toggle(self):
        if self.playing and not self.paused:
            self.paused = True
            self.user_paused = True
            self.player.pause(True)
            self.queue_draw()
            return
        # 播放: 重新加载当前电台, 保证真的出声(mpv 卡住时也能恢复)
        self.want_play = True
        self.user_paused = False
        self._retry_gap = 4.0
        self._play_index(self.cur)

    def _stop(self):
        self.playing = False
        self.paused = True
        self.want_play = False
        self.user_paused = False
        self.st['playing'] = False
        save_state(self.st)
        self.player.stop()
        self.queue_draw()

    # ---------- 电台列表窗口 ----------
    def _open_list(self):
        if self.list_win and self.list_win.get_visible():
            self.list_win.present()
            self._grab_list()
            return
        win = Gtk.Window(type=Gtk.WindowType.TOPLEVEL)
        win.set_keep_above(True)
        win.set_decorated(False)          # 去掉系统标题栏
        win.set_titlebar(Gtk.Label())     # 空 titlebar, 杜绝 CSD 深灰带
        win.set_type_hint(Gdk.WindowTypeHint.POPUP_MENU)
        win.set_app_paintable(True)
        win.connect('draw', self._list_draw)
        win.set_title('选择电台')
        gx, gy = self.get_position()
        gw, gh = self.get_size()
        # 弹窗宽度与主面板保持一致
        lw = max(320, gw)
        lh = 460
        win.set_default_size(lw, lh)
        self.list_win = win
        store = Gtk.TreeStore(str, str)
        for cat, items in self.radios:
            parent = store.append(None, [cat, ''])
            for name, url, rid in items:
                store.append(parent, [name, url])
        tree = Gtk.TreeView(model=store)
        css_provider = Gtk.CssProvider()
        css_provider.load_from_data(b'window, window.background { background-color: #000000; color: #ffffff; } scrolledwindow, scrolledwindow.frame { background-color: #000000; color: #ffffff; } treeview, treeview.view { background-color: #000000; color: #ffffff; background-image: none; } treeview:focus, treeview.view:focus { background-color: #000000; color: #ffffff; } treeview:selected, treeview.view:selected { background-color: #333333; color: #ffffff; }')
        win.get_style_context().add_provider(css_provider, Gtk.STYLE_PROVIDER_PRIORITY_USER)
        renderer = Gtk.CellRendererText()
        renderer.set_property('foreground', '#ffffff')
        renderer.set_property('weight', Pango.Weight.BOLD)
        col = Gtk.TreeViewColumn('电台', renderer, text=0)
        tree.append_column(col)
        tree.set_headers_visible(False)
        tree.connect('row-activated', self._on_pick, store)
        scroll = Gtk.ScrolledWindow()
        scroll.set_policy(Gtk.PolicyType.AUTOMATIC, Gtk.PolicyType.AUTOMATIC)
        scroll.add(tree)
        # provider 也要挂到 TreeView / ScrolledWindow 上, 否则主题背景会盖过黑色
        for _sc in (tree.get_style_context(), scroll.get_style_context()):
            _sc.add_provider(css_provider, Gtk.STYLE_PROVIDER_PRIORITY_USER)
        win.add(scroll)
        win.add_events(Gdk.EventMask.BUTTON_PRESS_MASK |
                       Gdk.EventMask.BUTTON_RELEASE_MASK |
                       Gdk.EventMask.KEY_PRESS_MASK |
                       Gdk.EventMask.POINTER_MOTION_MASK)
        win.connect('button-press-event', self._on_list_press)
        win.connect('key-press-event', self._on_list_key)
        win.connect('destroy', self._on_list_destroy)
        scr = Gdk.Screen.get_default()
        wx = max(0, min(gx, scr.get_width() - lw))
        wy = gy + gh + 8
        if wy + lh > scr.get_height():
            wy = max(0, gy - lh - 8)
        win.move(wx, wy)
        win.show_all()
        win.set_opacity(1.0)
        # 抓住指针, 这样鼠标点到面板外的桌面空白处也能收到事件并自动收回
        self._grab_tries = 0
        GLib.timeout_add(120, self._try_grab_list)

    def _try_grab_list(self):
        """窗口刚映射时 GDK 可能认为不可抓取, 稍后重试几次"""
        if not self.list_win or not self.list_win.get_visible():
            return False
        self._grab_tries += 1
        if self._grab_list():
            return False
        if self._grab_tries >= 15:
            log('list grab give up after %d tries' % self._grab_tries)
            return False
        return True

    def _grab_list(self):
        if not self.list_win or self.list_grabbed:
            return self.list_grabbed
        try:
            seat = self.list_win.get_display().get_default_seat()
            st = seat.grab(self.list_win.get_window(),
                           Gdk.SeatCapabilities.ALL_POINTING, True,
                           None, None, None, None)
            if st == Gdk.GrabStatus.SUCCESS:
                self.list_grabbed = True
                return True
            log('list grab status: %s' % (st,))
        except Exception as e:
            log('list grab: ' + str(e))
        return False

    def _ungrab_list(self):
        if not self.list_grabbed:
            return
        self.list_grabbed = False
        try:
            seat = Gdk.Display.get_default().get_default_seat()
            seat.ungrab()
        except Exception:
            pass

    def _close_list(self):
        self._ungrab_list()
        win, self.list_win = self.list_win, None
        if win:
            win.destroy()

    def _on_list_destroy(self, win, *a):
        self._ungrab_list()
        if self.list_win is win:
            self.list_win = None

    def _on_list_press(self, win, event):
        """点在弹窗外面(桌面空白处等) → 直接收回"""
        w = win.get_allocated_width()
        h = win.get_allocated_height()
        if event.x < 0 or event.y < 0 or event.x > w or event.y > h:
            self._close_list()
            return True
        return False

    def _on_list_key(self, win, event):
        if event.keyval == Gdk.KEY_Escape:
            self._close_list()
            return True
        return False

    def _list_draw(self, win, ctx):
        """弹窗整体刷成纯黑底, 标题栏与内容区是一个整体"""
        import cairo
        w, h = win.get_allocated_width(), win.get_allocated_height()
        ctx.set_operator(cairo.OPERATOR_SOURCE)
        ctx.set_source_rgb(0, 0, 0)
        ctx.rectangle(0, 0, w, h)
        ctx.fill()
        ctx.set_operator(cairo.OPERATOR_OVER)
        return False

    def _on_pick(self, tree, path, col, store):
        it = store.get_iter(path)
        name = store.get_value(it, 0) or ''
        url = store.get_value(it, 1) or ''
        if not url:
            return                      # 分类标题行, 不处理
        self._close_list()
        flat = self._flat()
        for i, (n, u, rid) in enumerate(flat):
            if u == url:
                self._play_index(i)
                break

    # ---------- 右键菜单 ----------
    def show_menu(self, event):
        menu = Gtk.Menu()
        lock_item = Gtk.CheckMenuItem.new_with_label('锁定位置')
        lock_item.set_active(self.st['locked'])
        lock_item.connect('toggled', self._toggle_lock)
        list_item = Gtk.MenuItem.new_with_label('选择电台…')
        list_item.connect('activate', lambda *a: self._open_list())
        stop_item = Gtk.MenuItem.new_with_label('停止播放')
        stop_item.connect('activate', lambda *a: self._stop())
        font_up = Gtk.MenuItem.new_with_label('增大字号')
        font_up.connect('activate', lambda *a: self._adj_font(0.1))
        font_down = Gtk.MenuItem.new_with_label('减小字号')
        font_down.connect('activate', lambda *a: self._adj_font(-0.1))
        vol_up = Gtk.MenuItem.new_with_label('音量 +10')
        vol_up.connect('activate', lambda *a: self._adj_vol(10))
        vol_down = Gtk.MenuItem.new_with_label('音量 -10')
        vol_down.connect('activate', lambda *a: self._adj_vol(-10))
        quit_item = Gtk.MenuItem.new_with_label('退出')
        quit_item.connect('activate', lambda *a: self._quit())
        menu.append(lock_item)
        menu.append(Gtk.SeparatorMenuItem())
        menu.append(list_item)
        menu.append(stop_item)
        menu.append(Gtk.SeparatorMenuItem())
        menu.append(font_up)
        menu.append(font_down)
        menu.append(Gtk.SeparatorMenuItem())
        menu.append(vol_up)
        menu.append(vol_down)
        menu.append(Gtk.SeparatorMenuItem())
        autostart_item = Gtk.CheckMenuItem(label='开机自启')
        autostart_item.set_active(self._autostart_enabled())
        autostart_item.connect('toggled', lambda w: self._toggle_autostart(w.get_active()))
        menu.append(autostart_item)
        menu.append(quit_item)
        menu.show_all()
        menu.popup(None, None, None, None, event.button, event.time)

    def _toggle_lock(self, item):
        self.st['locked'] = item.get_active()
        save_state(self.st)
        self.queue_draw()

    def _adj_font(self, d):
        self.st['font_scale'] = max(0.6, min(2.0, self.st['font_scale'] + d))
        save_state(self.st)
        self.queue_draw()

    def _adj_vol(self, d):
        v = self.player.set_volume(self.player.volume + d)
        self.st['volume'] = v
        save_state(self.st)

    def _quit(self):
        self._save_pos()
        self._close_list()
        self.player.kill()
        self.destroy()
        Gtk.main_quit()

    # ---------- 交互 ----------
    def _hit_btn(self, x, y):
        rects = self._btn_rects()
        for k, (bx, by, bw, bh) in rects.items():
            if bx <= x <= bx + bw and by <= y <= by + bh:
                return k
        return None

    def _hit_corner(self, x, y):
        return x >= self.w - CORNER and y >= self.h - CORNER

    def on_press(self, widget, event):
        x, y = event.x, event.y
        if event.button == 1:
            btn = self._hit_btn(x, y)
            if btn:
                self.hover = btn
                self._activate(btn)
                return True
            if self._hit_corner(x, y):
                self.resizing = True
                self.size_orig = (self.w, self.h, event.x_root, event.y_root)
                return True
            if not self.st['locked']:
                self.dragging = True
                self.drag_start = (event.x_root, event.y_root)
                self.drag_orig = self.get_position()
            return True
        if event.button == 3:
            self.show_menu(event)
            return True
        return False

    def on_motion(self, widget, event):
        if self.dragging:
            dx = event.x_root - self.drag_start[0]
            dy = event.y_root - self.drag_start[1]
            self.move(self.drag_orig[0] + dx, self.drag_orig[1] + dy)
            return True
        if self.resizing:
            w = max(MIN_W, min(MAX_W, self.size_orig[0] + event.x_root - self.size_orig[2]))
            h = max(MIN_H, min(MAX_H, self.size_orig[1] + event.y_root - self.size_orig[3]))
            self.w, self.h = w, h
            self.resize(w, h)
            return True
        corner = self._hit_corner(event.x, event.y)
        if corner != self.corner_hover:
            self.corner_hover = corner
            self.queue_draw()
        self.set_hover(self._hit_btn(event.x, event.y))
        return False

    def on_release(self, widget, event):
        if event.button == 1:
            if self.dragging:
                self.dragging = False
                self._save_pos()
            if self.resizing:
                self.resizing = False
                self._save_pos()
        return False

    def on_leave(self, widget, event):
        if self.hover:
            self.hover = None
            self.queue_draw()

    def _save_pos(self):
        try:
            x, y = self.get_position()
            self.st['x'], self.st['y'] = x, y
            self.st['w'], self.st['h'] = self.w, self.h
            save_state(self.st)
        except Exception:
            pass

    def _activate(self, key):
        flat = self._flat()
        n = len(flat)
        if key == 'prev':
            self._play_index((self.cur - 1) % n if n else 0)
        elif key == 'toggle':
            self._toggle()
        elif key == 'next':
            self._play_index((self.cur + 1) % n if n else 0)
        elif key == 'stop':
            self._stop()
        elif key == 'list':
            self._open_list()

    def set_hover(self, key):
        if key != self.hover:
            self.hover = key
            self.queue_draw()

    # ---------- 轮询 ----------
    def _poll(self):
        """以 mpv 的真实状态为准, 顺便给看门狗喂确认信号"""
        st = self.player.state(volume=(self._vol_tick % 5 == 1))
        self._vol_tick += 1
        self.st['volume'] = st['volume']
        # mpv 的 media-title 只是 "64.m3u8" 这种文件名, 不能盖掉电台名
        if st['title'] and not self.cur_title:
            self.cur_title = st['title']
        if not st['paused'] and st['title']:
            if not self.playing or self.paused:
                log('playing: ' + (self.cur_title or st['title']))
            self.playing = True
            self.paused = False
            self._last_ok = time.time()
        elif self.playing and st['paused'] and not self.user_paused:
            self.paused = True
            self._last_ok = 0.0
        self.queue_draw()
        return True

    # ---------- 绘制 ----------
    def fs(self, base):
        return max(8, int(base * self.st['font_scale']))

    def _text(self, ctx, s, x, y, maxw, font, color):
        layout = self.create_pango_layout(s)
        layout.set_font_description(Pango.FontDescription.from_string(font))
        layout.set_width(int(maxw * Pango.SCALE))
        layout.set_ellipsize(Pango.EllipsizeMode.END)
        ctx.set_source_rgba(0.05, 0.05, 0.06, 0.55)
        ctx.move_to(x + 1, y + 1)
        PangoCairo.show_layout(ctx, layout)
        ctx.move_to(x, y)
        ctx.set_source_rgba(*color)
        PangoCairo.show_layout(ctx, layout)

    def _btn_rects(self):
        rects = {}
        n = len(BTN_KEYS)
        total = n * BTN_W + (n - 1) * BTN_GAP
        x0 = (self.w - total) / 2
        y = self.h - PAD - BTN_H
        for i, k in enumerate(BTN_KEYS):
            rects[k] = (x0 + i * (BTN_W + BTN_GAP), y, BTN_W, BTN_H)
        return rects

    def _btn_draw(self, ctx, key, label, rect):
        bx, by, bw, bh = rect
        r, g, b = BTN_COLORS[key]
        if self.hover == key:
            r = min(1.0, r * BTN_HOVER)
            g = min(1.0, g * BTN_HOVER)
            b = min(1.0, b * BTN_HOVER)
        layout = self.create_pango_layout(label)
        layout.set_font_description(Pango.FontDescription.from_string((_SYS_FONT + ' Bold %d') % self.fs(11)))
        lw, lh = layout.get_pixel_size()
        self._text(ctx, label, bx + max(0, (bw - lw) / 2), by + (bh - lh) / 2 + 2,
                   max(bw, lw + 8), (_SYS_FONT + ' Bold %d') % self.fs(11), (r, g, b))

    def on_draw(self, widget, ctx):
        try:
            self._draw(ctx)
        except Exception as e:
            import traceback
            log('draw error: ' + traceback.format_exc())

    def _draw(self, ctx):
        w, h = self.w, self.h
        ctx.set_operator(cairo.OPERATOR_CLEAR)
        ctx.paint()
        ctx.set_operator(cairo.OPERATOR_OVER)

        flat = self._flat()
        text_w = w - 2 * PAD
        if self.cur_title:
            self._text(ctx, self.cur_title, PAD, 16, text_w,
                       (_SYS_FONT + ' Bold %d') % self.fs(21), TITLE_COLOR)
        elif flat:
            self._text(ctx, '未播放 · 点击「选台」挑选电台', PAD, 16, text_w,
                       (_SYS_FONT + ' Bold %d') % self.fs(18), HINT_COLOR)
        else:
            self._text(ctx, '电台列表加载失败', PAD, 16, text_w,
                       (_SYS_FONT + ' Bold %d') % self.fs(18), HINT_COLOR)

        # 第二行: 类别/状态
        cat = ''
        if flat:
            c, items = self._cat_of()
            cat = c
        if self.playing and not self.paused:
            status = '播放中'
        elif self.playing and not self.user_paused and self.want_play:
            status = '连接中…'      # 正在自动重连
        elif self.playing:
            status = '已暂停'
        else:
            status = '未播放'
        info = ' · '.join(x for x in [cat, status, '音量 %d' % self.st['volume']] if x)
        self._text(ctx, info, PAD, 68, text_w, (_SYS_FONT + ' %d') % self.fs(13), ARTIST_COLOR)

        rects = self._btn_rects()
        self._btn_draw(ctx, 'prev', '上一台', rects['prev'])
        if self.playing and not self.paused:
            toggle_label = '暂停'
        else:
            toggle_label = '播放'
        self._btn_draw(ctx, 'toggle', toggle_label, rects['toggle'])
        self._btn_draw(ctx, 'next', '下一台', rects['next'])
        self._btn_draw(ctx, 'stop', '停止', rects['stop'])
        self._btn_draw(ctx, 'list', '选台', rects['list'])

        if self.st['locked']:
            ctx.set_font_size(11)
            ctx.move_to(w - PAD - 32, 22)
            ctx.set_source_rgba(0.6, 0.6, 0.6, 0.7)
            ctx.show_text('🔒')

        if self.corner_hover:
            ctx.move_to(w - CORNER + 5, h - 6)
            ctx.line_to(w - 6, h - CORNER + 5)
            ctx.set_line_width(2)
            ctx.set_source_rgba(0.7, 0.7, 0.75, 0.5)
            ctx.stroke()
        return False

    def _cat_of(self):
        idx = self.cur
        for c, items in self.radios:
            if idx < len(items):
                return c, items[idx]
            idx -= len(items)
        return '', []


def main():
    check_single()
    widget = RadioWidget()
    widget.show_all()
    GLib.timeout_add(500, widget._all_desktops)

    def _bye(*a):
        # 收到注销/重启信号: 保存状态后用非 0 退出码退出,
        # 这样包装脚本不会误写成“用户主动退出”标记
        try:
            widget._close_list()
            widget._save_pos()
        except Exception:
            pass
        os._exit(3)

    for sig in (signal.SIGTERM, signal.SIGHUP, signal.SIGINT):
        try:
            signal.signal(sig, _bye)
        except Exception:
            pass
    Gtk.main()


if __name__ == '__main__':
    main()