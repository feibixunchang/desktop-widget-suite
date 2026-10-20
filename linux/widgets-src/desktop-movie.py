#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""桌面影视挂件 - 迷你播放器(mpv 引擎, 自渲染视频画面)
透明背景、可拖动、可锁定位置、可缩放、按钮全部内置，
完全置底，只在桌面显示。
"""
import json
import os
import re
import shutil
import signal
import socket
import subprocess
import sys
import threading
import time
from urllib.parse import unquote

import gi
gi.require_version('Gtk', '3.0')
gi.require_version('Pango', '1.0')
from gi.repository import Gtk, Gdk, GLib, Pango, PangoCairo, Gio
import cairo

try:
    from widget_font import SYS_FONT
except Exception:
    SYS_FONT = 'Sans'

GLib.set_prgname('desktop-movie')   # WM_CLASS 实例名，用于 dock 图标关联

APP_DIR = os.path.dirname(os.path.abspath(__file__))
STATE_PATH = os.path.expanduser('~/.config/desktop-movie/state.json')
LOG_PATH = '/tmp/opencode/movie_click.log'
DBUS = Gio.BusType.SESSION
PREFER = ('Deepinmovie', 'vlc')      # 优先:深度影院, 其次 VLC

# ---------- 外观配置 ----------
TITLE_COLOR = (0.98, 0.98, 1.0)      # 标题: 亮白
ARTIST_COLOR = (0.55, 0.87, 0.95)    # 副信息: 浅蓝
HINT_COLOR = (0.72, 0.75, 0.82)
SHADOW_COLOR = (0.05, 0.07, 0.10)
PLAY_COLOR = (0.18, 0.81, 0.44)
STOP_COLOR = (0.91, 0.24, 0.24)
BTN_HOVER = 1.25
PAD = 14
VIDEO_EXTS = ('.mp4', '.mkv', '.avi', '.webm', '.mov', '.flv',
              '.ts', '.wmv', '.m4v', '.rmvb', '.mpg', '.mpeg', '.3gp')
CORNER = 28
MIN_W, MIN_H = 480, 270
MAX_W, MAX_H = 1600, 900
DEFAULT_W, DEFAULT_H = 640, 380
EMBED_X, EMBED_Y = 100, 850
BTN_COLORS = {
    'prev':   (0.90, 0.49, 0.20),   # 上一部 橙色
    'toggle': (0.18, 0.81, 0.44),   # 播放/暂停 绿色
    'next':   (0.20, 0.57, 0.86),   # 下一部 蓝色
    'stop':   (0.91, 0.24, 0.24),   # 停止 红色
    'close':  (0.80, 0.30, 0.35),   # 关闭播放器 暗红
    'bwd':    (0.70, 0.50, 0.25),   # 快退 棕
    'fwd':    (0.70, 0.50, 0.25),   # 快进 棕
    'sm':     (0.45, 0.55, 0.75),   # 缩小 蓝灰
    'lg':     (0.45, 0.55, 0.75),   # 放大 蓝灰
    'fs':     (0.60, 0.80, 0.30),   # 全屏 黄绿
}
ROW1 = ['prev', 'toggle', 'next', 'stop', 'close', 'bwd', 'fwd', 'fs']
ROW2 = []
BTN_W, BTN_H, BTN_GAP = 64, 28, 8
STEP_US = 10 * 1000 * 1000          # 快进/快退 10 秒


def log(msg):
    try:
        with open(LOG_PATH, 'a') as f:
            f.write('%s %s\n' % (time.strftime('%H:%M:%S'), msg))
    except Exception:
        pass


def load_state():
    try:
        with open(STATE_PATH) as f:
            st = json.load(f)
    except Exception:
        st = {'x': EMBED_X, 'y': EMBED_Y, 'w': DEFAULT_W, 'h': DEFAULT_H,
              'locked': False, 'fullscreen': False}
    st.setdefault('net_history', [])
    st.setdefault('base_w', st.get('w', DEFAULT_W))
    st.setdefault('base_h', st.get('h', DEFAULT_H))
    return st


def save_state(st):
    try:
        os.makedirs(os.path.dirname(STATE_PATH), exist_ok=True)
        with open(STATE_PATH, 'w') as f:
            json.dump(st, f)
    except Exception:
        pass


class MovieWidget(Gtk.Window):
    def _autostart_desktop_name(self):
        return 'desktop-movie'

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

    def __init__(self):
        super().__init__(type=Gtk.WindowType.TOPLEVEL, decorated=False)
        self.set_title('desktop-movie')
        self.set_icon_name('desktop-movie')
        self.set_keep_below(False)
        self.set_skip_taskbar_hint(True)
        self.set_skip_pager_hint(True)
        self.set_accept_focus(True)
        self.set_can_focus(True)
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
        self.connect('key-press-event', self.on_key)
        self.connect('motion-notify-event', self.on_motion)
        self.connect('button-release-event', self.on_release)
        self.connect('leave-notify-event', self.on_leave)
        self.add_events(Gdk.EventMask.BUTTON_PRESS_MASK |
                        Gdk.EventMask.BUTTON_RELEASE_MASK |
                        Gdk.EventMask.POINTER_MOTION_MASK |
                        Gdk.EventMask.LEAVE_NOTIFY_MASK |
                        Gdk.EventMask.ENTER_NOTIFY_MASK |
                        Gdk.EventMask.KEY_PRESS_MASK |
                        Gdk.EventMask.KEY_RELEASE_MASK |
                        Gdk.EventMask.FOCUS_CHANGE_MASK)
        self.connect('enter-notify-event', self.on_enter)
        self.connect('configure-event', self.on_configure)

        # ---------- 播放引擎状态 ----------
        self.status = 'Stopped'
        self.title = '未检测到播放器'
        self.artist = ''
        self.duration_us = 0
        self.pos_us = 0
        self.video_path = None
        self.playlist = []
        self.pl_idx = -1
        self.pl_root = ''
        self.pl_win = None
        self.scan_state = ''
        self.mpv_proc = None
        self.mpv_sock = '/tmp/desktop-movie-mpv-%d.sock' % os.getpid()
        self.hover = None
        self.corner_hover = False
        self.dragging = False
        self.drag_start = None
        self.drag_orig = None
        self.resizing = False
        self.size_orig = None
        self.fs_state = False          # 全屏标记(视频窗口全屏)
        self.rotate_deg = 0            # 旋转角度 0/90/180/270
        self.mouse_inside = False      # 鼠标是否在挂件内(控制条显隐)
        self._last_embed_sync = 0      # 缩放嵌入节流时间戳

        self._create_vwin()
        self._setup_dnd()
        GLib.timeout_add(500, self._poll)
        self._poll()
        GLib.timeout_add(800, self._all_desktops)
        GLib.timeout_add(1200, self._all_desktops_vwin)

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

    # ========== 视频窗口 + mpv 引擎 ==========
    def _create_vwin(self):
        self.vwin = Gtk.Window(type=Gtk.WindowType.TOPLEVEL, decorated=False)
        self.vwin.set_title('desktop-movie-video')
        self.vwin.set_keep_below(True)
        self.vwin.set_skip_taskbar_hint(True)
        self.vwin.set_skip_pager_hint(True)
        self.vwin.set_accept_focus(False)
        self.vwin.set_can_focus(False)
        self.vwin.set_default_size(self.w, self.h)
        self.vwin.set_app_paintable(True)
        vis = self.get_screen().get_rgba_visual()
        if vis:
            self.vwin.set_visual(vis)
        self.vwin.show()
        log('视频窗口创建')

    def _all_desktops_vwin(self):
        """把视频窗口也固定到所有桌面(按窗口名在客户端列表里找)"""
        try:
            from Xlib import display as _xd
            _d = _xd.Display()
            root = _d.screen().root
            prop = root.get_full_property(_d.intern_atom('_NET_CLIENT_LIST'),
                                          _d.intern_atom('CARDINAL'))
            for wid in (list(prop.value) if prop else []):
                _w = _d.create_resource_object('window', wid)
                try:
                    txt = (_w.get_wm_name() or '') + ' ' + \
                          ' '.join(_w.get_wm_class() or [])
                except Exception:
                    continue
                if 'desktop-movie-video' in txt:
                    _w.change_property(_d.intern_atom('_NET_WM_DESKTOP'),
                                       _d.intern_atom('CARDINAL'), 32,
                                       [0xFFFFFFFF])
            _d.sync()
            _d.close()
        except Exception:
            pass
        return False

    def _sync_vwin(self):
        x, y = self.get_position()
        w, h = self.get_size()
        if self.fs_state:
            return
        self.vwin.move(x, y)
        self.vwin.resize(w, h)

    def _mpv_start(self, path):
        try:
            self._mpv_quit()
            xid = self.vwin.get_window().get_xid()
            try:
                os.remove(self.mpv_sock)
            except Exception:
                pass
            mpv_bin = '/usr/local/bin/mpv' if os.path.exists('/usr/local/bin/mpv') \
            else (shutil.which('mpv') or 'mpv')
            if not (mpv_bin and os.path.exists(mpv_bin)):
                log('找不到 mpv：请先安装 (sudo apt install mpv)')
                self.title = '缺少 mpv，请 sudo apt install mpv'
                self.status = 'Stopped'
                self.video_path = None
                self.queue_draw()
                return
            cmd = [mpv_bin, '--wid=%d' % xid, '--no-osc', '--no-border',
                   '--input-ipc-server=' + self.mpv_sock,
                   '--keep-open=yes', '--idle=yes', '--force-window=yes',
                   '--volume=100', path]
            self.mpv_proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL,
                                             stderr=subprocess.DEVNULL,
                                             start_new_session=True)
            # 播放即对齐层级：控制窗(普通层)必在视频窗(置底层)之上
            try:
                if not self.fs_state:
                    self.set_keep_above(False)
                    self.set_keep_below(False)
                    self.vwin.set_keep_above(False)
                    self.vwin.set_keep_below(True)
            except Exception:
                pass
            self.video_path = path
            self.title = os.path.basename(path)
            self.status = 'Playing'
            self.pos_us = 0
            self.duration_us = 0
            log('mpv 播放: ' + path)
        except Exception as e:
            log('mpv 启动失败: ' + str(e))

    def _mpv_cmd(self, args):
        if not self.mpv_proc or self.mpv_proc.poll() is not None:
            return None
        try:
            sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            sock.settimeout(0.8)
            sock.connect(self.mpv_sock)
            sock.sendall((json.dumps({'command': args}) + '\n').encode())
            buf = b''
            while True:
                chunk = sock.recv(4096)
                if not chunk:
                    break
                buf += chunk
                if b'\n' in buf:
                    break
            sock.close()
            return json.loads(buf.decode().strip())
        except Exception:
            return None

    def _mpv_quit(self):
        if self.mpv_proc and self.mpv_proc.poll() is None:
            self._mpv_cmd(['quit'])
            try:
                self.mpv_proc.wait(timeout=2)
            except Exception:
                try:
                    self.mpv_proc.kill()
                except Exception:
                    pass
        self.mpv_proc = None
        self.video_path = None
        self.title = '未检测到播放器'
        self.status = 'Stopped'
        self.pos_us = 0
        self.duration_us = 0

    def _mpv_state(self):
        if not self.mpv_proc or self.mpv_proc.poll() is not None:
            if self.mpv_proc:
                log('mpv 进程退出')
                self.mpv_proc = None
                self.video_path = None
                self.title = '未检测到播放器'
                self.status = 'Stopped'
            return False
        pos = self._mpv_cmd(['get_property', 'time-pos'])
        if pos is None:
            return False
        if pos.get('error') == 'success' and pos.get('data') is not None:
            self.pos_us = int(float(pos['data']) * 1000000)
        dur = self._mpv_cmd(['get_property', 'duration'])
        if dur is None:
            return True
        if dur.get('error') == 'success' and dur.get('data') is not None:
            self.duration_us = int(float(dur['data']) * 1000000)
        pau = self._mpv_cmd(['get_property', 'pause'])
        if pau and pau.get('error') == 'success':
            self.status = 'Paused' if pau['data'] else 'Playing'
        return True

    def _raise_control_above_video(self):
        """全屏时两个窗都在置顶层：用 X 协议明确把控制窗抬到视频窗之上。

        两个 keep_above 窗口之间的堆叠顺序由 WM 自定，kwin 可能把视频窗放在
        上面——那时控制窗收不到任何点击（右键菜单弹不出的直接原因）。
        """
        try:
            from Xlib import display as _xd, X as _X
            d = _xd.Display()
            try:
                vw = d.create_resource_object(
                    'window', self.vwin.get_window().get_xid())
                cw = d.create_resource_object(
                    'window', self.get_window().get_xid())
                vw.configure(stack_mode=_X.Below)
                cw.configure(stack_mode=_X.Above)
                d.sync()
            finally:
                d.close()
        except Exception:
            pass

    def _close_btn_rect(self):
        return (self.w - 34, 8, 26, 26)

    def _hit_close_btn(self, x, y):
        bx, by, bw, bh = self._close_btn_rect()
        return bx <= x < bx + bw and by <= y < by + bh

    def _draw_close_btn(self, ctx):
        bx, by, bw, bh = self._close_btn_rect()
        hover = getattr(self, '_close_hover', False)
        ctx.rectangle(bx, by, bw, bh)
        ctx.set_source_rgba(0.78, 0.16, 0.20, 0.92 if hover else 0.60)
        ctx.fill()
        ctx.set_line_width(2)
        ctx.set_source_rgba(1, 1, 1, 0.95)
        cx, cy = bx + bw / 2.0, by + bh / 2.0
        d = 5
        ctx.move_to(cx - d, cy - d)
        ctx.line_to(cx + d, cy + d)
        ctx.move_to(cx + d, cy - d)
        ctx.line_to(cx - d, cy + d)
        ctx.stroke()

    @staticmethod
    def _ewmh_fullscreen(wid, on):
        """用 EWMH 直接请求全屏(不依赖 wmctrl)"""
        try:
            from Xlib import display as _xd, X
            from Xlib.protocol import event as _xev
            d = _xd.Display()
            root = d.screen().root
            ev = _xev.ClientMessage(
                window=root,
                client_type=d.intern_atom('_NET_WM_STATE'),
                data=(32, [1 if on else 0,
                           d.intern_atom('_NET_WM_STATE_FULLSCREEN'), 0, 1, 0]))
            root.send_event(ev, event_mask=(X.SubstructureRedirectMask |
                                            X.SubstructureNotifyMask))
            d.sync()
            d.close()
            return True
        except Exception as e:
            log('ewmh fullscreen: %s' % e)
            return False

    def _toggle_fullscreen(self):
        if not self.video_path:
            return
        mid = self.get_window().get_xid()
        vid = self.vwin.get_window().get_xid()
        scr = self.get_screen()
        sw, sh = scr.get_width(), scr.get_height()
        if not self.fs_state:
            self.fs_state = True
            # 用 GTK 原生 fullscreen(): 由 WM 正确设置 _NET_WM_STATE_FULLSCREEN,
            # dde-dock 才会把任务栏收起。单纯手发 EWMH 消息不可靠。
            try:
                self.fullscreen()
                self.vwin.fullscreen()
            except Exception:
                pass
            # 兜底: 仍手发一次 EWMH, 应对 WM 忽略 GTK 请求的情况
            self._ewmh_fullscreen(mid, True)
            self._ewmh_fullscreen(vid, True)
            try:
                # 全屏时把两个窗口都提到最前, 保证画面盖住任务栏所在区域
                self.set_keep_above(True)
                self.set_keep_below(False)
                self.vwin.set_keep_below(False)
                self.vwin.set_keep_above(True)
            except Exception:
                pass
            try:
                self.present()
                self.grab_focus()          # 关键: 拿到焦点, Esc/F/空格 才有用
            except Exception:
                pass
            self._raise_control_above_video()
            # 视频子窗口铺满整屏(含任务栏区域)
            try:
                from Xlib import display as _xd
                d = _xd.Display()
                vw = d.create_resource_object('window', vid)
                vw.configure(x=0, y=0, width=sw, height=sh)
                d.sync()
                d.close()
            except Exception:
                pass
            log('全屏')
        else:
            self.fs_state = False
            try:
                self.unfullscreen()
                self.vwin.unfullscreen()
            except Exception:
                pass
            self._ewmh_fullscreen(mid, False)
            self._ewmh_fullscreen(vid, False)
            try:
                # 控制窗回【普通层】(keep_below=False)：普通层恒定在 keep_below
                # 视频窗之上，点击/右键必然落到控制窗。若两边都置底，堆叠顺序
                # 由 WM 自定，视频窗一旦盖住控制窗，挂件就整体“失灵”——
                # 右键菜单弹不出、按钮没反应、电影关不掉，正是之前的根因。
                self.set_keep_above(False)
                self.set_keep_below(False)
                self.vwin.set_keep_above(False)
                self.vwin.set_keep_below(True)
            except Exception:
                pass
            GLib.timeout_add(400, self._sync_vwin)
            log('退出全屏')

    def _poll(self):
        try:
            gw, gh = self.get_size()
            if gw > 0 and gh > 0:
                self.w, self.h = gw, gh
            self._mpv_state()
            if self.playlist and self.video_path:
                eof = self._mpv_cmd(['get_property', 'eof-reached'])
                if eof and eof.get('error') == 'success' and eof.get('data'):
                    self.pl_idx = (self.pl_idx + 1) % len(self.playlist)
                    self._mpv_start(self.playlist[self.pl_idx])
                    self.queue_draw()
                    return True
            if self.fs_state:
                # 全屏中持续把控制窗压在视频窗之上，防止 WM 重新堆叠
                self._raise_control_above_video()
            self._sync_vwin()
            self.queue_draw()
        except Exception:
            pass
        return True

    def _smb_open(self, url):
        try:
            from urllib.parse import urlparse
            pu = urlparse(url)
            host = pu.hostname or ''
            parts = [p for p in pu.path.split('/') if p]
            if not host or not parts:
                log('smb 地址无效: ' + url)
                return
            share = parts[0]
            rel = '/'.join(parts[1:])
            mp = '/run/user/%d/gvfs/smb-share:server=%s,share=%s' % (
                os.getuid(), host, share)
            full = os.path.join(mp, rel) if rel else mp
            if os.path.exists(mp):
                self._open_path(full)
                return
            log('挂载 NAS: smb://%s/%s' % (host, share))
            f = Gio.File.new_for_uri('smb://%s/%s' % (host, share))
            mo = Gtk.MountOperation.new()
            f.mount_enclosing_volume(Gio.MountMountFlags.NONE, mo, None,
                                     self._smb_mount_done, (mp, full))
        except Exception as e:
            log('smb 打开失败: ' + str(e))

    def _smb_mount_done(self, f, res, data):
        mp, full = data
        try:
            f.mount_enclosing_volume_finish(res)
            log('NAS 挂载成功')
        except Exception as e:
            log('NAS 挂载失败: ' + str(e))
            return

        def wait_mount():
            if os.path.exists(mp):
                self._open_path(full)
                return False
            return True
        GLib.timeout_add(500, wait_mount)

    def _scan_folder(self, path, depth=0):
        if depth > 4:
            return []
        vids = []
        try:
            for f in sorted(os.listdir(path)):
                fp = os.path.join(path, f)
                low = f.lower()
                if low.endswith(VIDEO_EXTS):
                    vids.append(fp)
                elif low.endswith(('.nfo', '.jpg', '.jpeg', '.png', '.txt',
                                   '.srt', '.ass', '.lrc', '.db')):
                    continue
                elif os.path.isdir(fp) and not f.startswith('.'):
                    vids.extend(self._scan_folder(fp, depth + 1))
        except Exception:
            pass
        return vids

    def _save_net_history(self, url):
        try:
            hist = self.st.get('net_history') or []
            if url in hist:
                hist.remove(url)
            hist.insert(0, url)
            self.st['net_history'] = hist[:10]
            save_state(self.st)
        except Exception:
            pass

    def _open_path(self, url):
        if not url:
            return
        if url.startswith('smb://'):
            self._smb_open(url)
            return
        if url.startswith(('http://', 'https://', 'rtsp://')):
            self.playlist = []
            self.pl_idx = -1
            self._mpv_start(url)
            return
        if os.path.isdir(url):
            self.scan_state = 'scanning'
            self.queue_draw()
            threading.Thread(target=self._scan_worker, args=(url,),
                             daemon=True).start()
            return
        if os.path.isfile(url):
            self.playlist = []
            self.pl_idx = -1
            self.pl_root = ''
            self._mpv_start(url)

    def _scan_worker(self, path):
        vids = self._scan_folder(path)
        GLib.idle_add(self._scan_done, vids, path)

    def _scan_done(self, vids, path):
        self.scan_state = ''
        self.queue_draw()
        if not vids:
            log('文件夹无视频: ' + path)
            return
        self.playlist = vids
        self.pl_idx = 0
        self.pl_root = path
        self._mpv_start(vids[0])
        log('播放列表 %d 部: ' % len(vids) + path)

    def _show_playlist(self):
        if not self.playlist:
            return
        if self.pl_win is not None:
            try:
                if self.pl_win.get_property('visible'):
                    self.pl_win.destroy()
                    self.pl_win = None
                    return
            except Exception:
                self.pl_win = None
        win = Gtk.Window(title='播放列表 (%d 部)' % len(self.playlist))
        win.set_default_size(340, 520)
        vis = self.get_screen().get_rgba_visual()
        if vis:
            win.set_visual(vis)
        win.set_app_paintable(True)
        css = b'''
        window.plwin { background-color: rgba(22, 22, 28, 0.86); }
        list { background-color: transparent; }
        list row { background-color: transparent; }
        list row:selected { background-color: rgba(60, 130, 90, 0.5); }
        label { background-color: transparent; }
        '''
        prov = Gtk.CssProvider()
        prov.load_from_data(css)
        win.get_style_context().add_provider(
            prov, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
        win.get_style_context().add_class('plwin')
        win.connect('destroy', lambda *a: setattr(self, 'pl_win', None))
        x, y = self.get_position()
        w, h = self.get_size()
        win.move(x + w + 10, y)
        sw = Gtk.ScrolledWindow()
        sw.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
        lb = Gtk.ListBox()
        for i, v in enumerate(self.playlist):
            row = Gtk.ListBoxRow()
            name = os.path.basename(v)
            if self.pl_root and v.startswith(self.pl_root):
                rel = os.path.relpath(v, self.pl_root)
                if rel != name:
                    name = rel
            lab = Gtk.Label(xalign=0, label='%d. %s' % (i + 1, name))
            lab.set_ellipsize(Pango.EllipsizeMode.MIDDLE)
            lab.set_max_width_chars(34)
            lab.set_margin_left(10)
            lab.set_margin_right(10)
            lab.set_margin_top(3)
            lab.set_margin_bottom(3)
            row.add(lab)
            row.idx = i
            if i == self.pl_idx:
                row.get_style_context().add_class('is-selected')
            lb.add(row)
        lb.connect('row-activated', self._on_pl_select)
        sw.add(lb)
        win.add(sw)
        win.show_all()
        self.pl_win = win
        GLib.idle_add(self._pl_scroll_to_current, sw, lb)

    def _pl_scroll_to_current(self, sw, lb):
        try:
            row = lb.get_row_at_index(self.pl_idx)
            if row:
                adj = sw.get_vadjustment()
                if len(self.playlist) > 1:
                    adj.set_value(adj.get_upper() * self.pl_idx / (len(self.playlist) - 1))
        except Exception:
            pass
        return False

    def _on_pl_select(self, box, row):
        i = row.idx
        self.pl_idx = i
        self._mpv_start(self.playlist[i])
        if self.pl_win:
            self.pl_win.destroy()
            self.pl_win = None

    def _hit_pl_btn(self, x, y):
        if self.video_path and self.playlist:
            by = self.h - PAD - BTN_H - 52
            if by - 12 <= y <= by + 14 and x >= self.w - 96:
                return True
        return False

    def _hit_bar(self, x, y):
        if not self.video_path or not self.duration_us:
            return -1
        by = self.h - PAD - BTN_H - 24
        if by - 6 <= y <= by + 10 and PAD <= x <= self.w - PAD:
            return max(0.0, min(1.0, (x - PAD) / float(self.w - 2 * PAD)))
        return -1

    def _open_file(self):
        dlg = Gtk.FileChooserDialog('打开视频', self, Gtk.FileChooserAction.OPEN,
                                    (Gtk.STOCK_CANCEL, Gtk.ResponseType.CANCEL,
                                     Gtk.STOCK_OPEN, Gtk.ResponseType.OK))
        filt = Gtk.FileFilter()
        filt.set_name('视频文件')
        for pat in ('*.mp4', '*.mkv', '*.avi', '*.webm', '*.mov',
                    '*.flv', '*.ts', '*.wmv', '*.m4v', '*.rmvb'):
            filt.add_pattern(pat)
        dlg.add_filter(filt)
        res = dlg.run()
        path = dlg.get_filename() if res == Gtk.ResponseType.OK else None
        dlg.destroy()
        if path:
            self._mpv_start(path)

    def _open_folder(self):
        dlg = Gtk.FileChooserDialog('选择影片文件夹', self, Gtk.FileChooserAction.SELECT_FOLDER,
                                    (Gtk.STOCK_CANCEL, Gtk.ResponseType.CANCEL,
                                     Gtk.STOCK_OPEN, Gtk.ResponseType.OK))
        res = dlg.run()
        path = dlg.get_filename() if res == Gtk.ResponseType.OK else None
        dlg.destroy()
        if path:
            self._open_path(path)

    def _open_url(self):
        dlg = Gtk.Dialog(title='打开网络视频', parent=self, flags=0)
        dlg.add_buttons(Gtk.STOCK_CANCEL, Gtk.ResponseType.CANCEL,
                        Gtk.STOCK_OK, Gtk.ResponseType.OK)
        box = dlg.get_content_area()
        lab = Gtk.Label()
        lab.set_line_wrap(True)
        lab.set_markup(
            '<small>支持: 影片文件/文件夹路径(NAS 挂载路径也可, 自动列播放列表) / '
            'http(s):// 地址\n'
            '提示: smb:// 地址会自动挂载, 下拉框可选择历史地址</small>')
        box.pack_start(lab, True, True, 6)
        combo = Gtk.ComboBoxText.new_with_entry()
        for h in self.st.get('net_history') or []:
            combo.append_text(h)
        entry = combo.get_child()
        entry.set_placeholder_text('例如 smb://192.168.100.3/video/movie 或挂载路径')
        entry.set_activates_default(True)
        box.pack_start(combo, True, True, 6)
        dlg.set_default_response(Gtk.ResponseType.OK)
        dlg.set_default_size(480, -1)
        dlg.show_all()
        res = dlg.run()
        url = combo.get_child().get_text()
        url = (url or '').strip()
        dlg.destroy()
        if res == Gtk.ResponseType.OK and url:
            self._save_net_history(url)
            self._open_path(url)

    def _setup_dnd(self):
        try:
            self.drag_dest_set(Gtk.DestDefaults.ALL, [], Gdk.DragAction.COPY)
            targets = [Gtk.TargetEntry.new('text/uri-list', 0, 0)]
            self.drag_dest_set_target_list(Gtk.TargetList.new(targets))
            self.connect('drag-data-received', self.on_drag_data)
        except Exception:
            pass

    def on_configure(self, widget, event):
        if event.width > 0 and event.height > 0:
            self.w, self.h = event.width, event.height
        return False

    def on_drag_data(self, w, ctx, x, y, data, info, t):
        try:
            uris = data.get_uris()
            if uris:
                path = unquote(uris[0].replace('file://', ''))
                if os.path.exists(path):
                    self._mpv_start(path)
            ctx.finish(True, False, t)
        except Exception:
            try:
                ctx.finish(False, False, t)
            except Exception:
                pass

    # ========== 布局与命中 ==========
    def _btn_rects(self):
        rects = {}
        for row, keys in ((0, ROW1),):
            n = len(keys)
            total = n * BTN_W + (n - 1) * BTN_GAP
            x0 = (self.w - total) // 2
            y = self.h - PAD - BTN_H
            for i, k in enumerate(keys):
                rects[k] = (x0 + i * (BTN_W + BTN_GAP), y, BTN_W, BTN_H)
        return rects

    def _hit_btn(self, x, y):
        for k, (bx, by, bw, bh) in self._btn_rects().items():
            if bx <= x < bx + bw and by <= y < by + bh:
                return k
        return None

    def _hit_corner(self, x, y):
        return (self.w - CORNER <= x <= self.w and
                self.h - CORNER <= y <= self.h)

    def set_hover(self, key):
        if self.hover != key:
            self.hover = key
            self.queue_draw()

    # ========== 交互 ==========
    def on_enter(self, widget, event):
        self.mouse_inside = True
        self.queue_draw()

    def on_leave(self, widget, event):
        self.mouse_inside = False
        self.set_hover(None)
        if self.corner_hover:
            self.corner_hover = False
            self.queue_draw()

    def on_press(self, widget, event):
        x, y = event.x, event.y
        if event.button == 1:
            if self.video_path and self._hit_close_btn(x, y):
                log('click: close(常开关闭钮)')
                self._mpv_quit()
                self.queue_draw()
                return True
            if self._hit_pl_btn(x, y):
                self._show_playlist()
                return True
            frac = self._hit_bar(x, y)
            if frac >= 0:
                self._mpv_cmd(['seek', frac * self.duration_us / 1000000.0,
                               'absolute+exact'])
                log('seek to %.1f%%' % (frac * 100))
                return True
            btn = self._hit_btn(x, y)
            if btn:
                self.set_hover(btn)
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
        if not self.mouse_inside:
            self.mouse_inside = True
            self.queue_draw()
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
            now = time.monotonic()
            if now - self._last_embed_sync > 0.12:
                self._last_embed_sync = now
                self._sync_vwin()
            return True
        corner = self._hit_corner(event.x, event.y)
        if corner != self.corner_hover:
            self.corner_hover = corner
            self.queue_draw()
        self.set_hover(self._hit_btn(event.x, event.y))
        ch = bool(self.video_path) and self._hit_close_btn(event.x, event.y)
        if ch != getattr(self, '_close_hover', False):
            self._close_hover = ch
            self.queue_draw()
        return False

    def on_release(self, widget, event):
        if event.button == 1:
            if self.dragging:
                self.dragging = False
                x, y = self.get_position()
                self.st['x'], self.st['y'] = x, y
                save_state(self.st)
                self._sync_vwin()
                return True
            if self.resizing:
                self.resizing = False
                self.st['w'], self.st['h'] = int(self.w), int(self.h)
                save_state(self.st)
                self._sync_vwin()
                return True
        return False

    def on_key(self, widget, event):
        ctrl = bool(event.state & Gdk.ModifierType.CONTROL_MASK)
        key = Gdk.keyval_name(event.keyval)
        if ctrl and key == 'o':
            self._open_file()
            return True
        if ctrl and key == 'r':
            self.rotate_deg = 0
            self._mpv_cmd(['set_property', 'video-rotate', '0'])
            self._reset_size()
            return True
        if key == 'space':
            if self.video_path:
                self._mpv_cmd(['set_property', 'pause', self.status == 'Playing'])
            return True
        if key == 'f':
            self._toggle_fullscreen()
            return True
        if key == 'Escape':
            if self.fs_state:
                self._toggle_fullscreen()
            elif self.video_path:
                # 非全屏时 Esc = 停止播放：保证任何时候都有关闭手段
                log('Esc 停止播放')
                self._mpv_quit()
                self.queue_draw()
            return True
        if key == 'Left':
            if self.video_path:
                self._mpv_cmd(['seek', -10])
            return True
        if key == 'Right':
            if self.video_path:
                self._mpv_cmd(['seek', 10])
            return True
        if key == 'j':
            if self.video_path:
                self.rotate_deg = (self.rotate_deg + 90) % 360
                self._mpv_cmd(['set_property', 'video-rotate', str(self.rotate_deg)])
            return True
        return False

    def _activate(self, btn):
        log('click: ' + btn)
        if btn == 'prev':
            if self.playlist:
                self.pl_idx = (self.pl_idx - 1) % len(self.playlist)
                self._mpv_start(self.playlist[self.pl_idx])
                if self.pl_win is not None:
                    try:
                        if self.pl_win.get_property('visible'):
                            self._show_playlist()
                    except Exception:
                        self.pl_win = None
            elif not self.video_path:
                self._open_file()
        elif btn == 'toggle':
            if not self.video_path:
                self._open_folder()
            else:
                self._mpv_cmd(['set_property', 'pause', self.status == 'Playing'])
        elif btn == 'next':
            if self.playlist:
                self.pl_idx = (self.pl_idx + 1) % len(self.playlist)
                self._mpv_start(self.playlist[self.pl_idx])
                if self.pl_win is not None:
                    try:
                        if self.pl_win.get_property('visible'):
                            self._show_playlist()
                    except Exception:
                        self.pl_win = None
            elif not self.video_path:
                self._open_url()
        elif btn == 'stop':
            if self.video_path:
                self._mpv_cmd(['seek', 0, 'absolute'])
                self._mpv_cmd(['set_property', 'pause', True])
        elif btn == 'close':
            self._mpv_quit()
        elif btn == 'bwd':
            if self.video_path:
                self._mpv_cmd(['seek', -10])
        elif btn == 'fwd':
            if self.video_path:
                self._mpv_cmd(['seek', 10])
        elif btn == 'fs':
            self._toggle_fullscreen()

    def _scale(self, factor):
        x, y = self.get_position()
        self.w = int(max(MIN_W, min(MAX_W, self.w * factor)))
        self.h = int(max(MIN_H, min(MAX_H, self.h * factor)))
        self.resize(self.w, self.h)
        self.move(x, y)
        self.st['w'], self.st['h'] = self.w, self.h
        save_state(self.st)
        self.queue_draw()
        self._sync_vwin()

    def show_menu(self, event):
        menu = Gtk.Menu()
        size_item = Gtk.MenuItem()
        lab = Gtk.Label()
        lab.set_markup('<span foreground="#b8c4ff">窗口尺寸: %d × %d</span>'
                       % (int(self.w), int(self.h)))
        lab.set_margin_left(10)
        lab.set_margin_right(10)
        size_item.add(lab)
        size_item.set_sensitive(False)
        menu.append(size_item)
        menu.append(Gtk.SeparatorMenuItem())
        lock_item = Gtk.CheckMenuItem.new_with_label('锁定位置')
        lock_item.set_active(self.st['locked'])
        lock_item.connect('toggled', self._toggle_lock)
        menu.append(lock_item)
        fs_item = Gtk.MenuItem.new_with_label('播放器全屏' if not self.fs_state else '退出全屏')
        fs_item.connect('activate', lambda *a: self._toggle_fullscreen())
        menu.append(fs_item)
        z_item = Gtk.MenuItem.new_with_label('放大画面 (+25%)')
        z_item.connect('activate', lambda *a: self._scale(1.25))
        menu.append(z_item)
        zo_item = Gtk.MenuItem.new_with_label('缩小画面 (-20%)')
        zo_item.connect('activate', lambda *a: self._scale(0.8))
        menu.append(zo_item)
        rs_item = Gtk.MenuItem.new_with_label(
            '恢复尺寸 %d × %d' % (int(self.st.get('base_w', DEFAULT_W)),
                            int(self.st.get('base_h', DEFAULT_H))))
        rs_item.connect('activate', lambda *a: self._reset_size())
        menu.append(rs_item)
        sb_item = Gtk.MenuItem.new_with_label('将当前尺寸设为基准')
        sb_item.connect('activate', lambda *a: self._set_base_size())
        menu.append(sb_item)
        dr_item = Gtk.MenuItem.new_with_label('恢复默认大小 %d × %d' % (DEFAULT_W, DEFAULT_H))
        dr_item.connect('activate', lambda *a: self._restore_default())
        menu.append(dr_item)
        menu.append(Gtk.SeparatorMenuItem())
        lp_item = Gtk.MenuItem.new_with_label('打开视频…')
        lp_item.connect('activate', lambda *a: self._open_file())
        menu.append(lp_item)
        pl_item = Gtk.MenuItem.new_with_label('播放列表' if self.playlist else '播放列表(无影片)')
        pl_item.set_sensitive(bool(self.playlist))
        pl_item.connect('activate', lambda *a: self._show_playlist())
        menu.append(pl_item)
        dir_item = Gtk.MenuItem.new_with_label('打开文件夹(播放列表)…')
        dir_item.connect('activate', lambda *a: self._open_folder())
        menu.append(dir_item)
        net_item = Gtk.MenuItem.new_with_label('打开网络视频…')
        net_item.connect('activate', lambda *a: self._open_url())
        menu.append(net_item)
        ref_item = Gtk.MenuItem.new_with_label('刷新画面')
        ref_item.connect('activate', lambda *a: self._do_refresh())
        menu.append(ref_item)

        menu.append(Gtk.SeparatorMenuItem())
        autostart_item = Gtk.CheckMenuItem(label='开机自启')
        autostart_item.set_active(self._autostart_enabled())
        autostart_item.connect('toggled', lambda w: self._toggle_autostart(w.get_active()))
        menu.append(autostart_item)

        quit_item = Gtk.MenuItem.new_with_label('退出')
        quit_item.connect('activate', lambda *a: (self.destroy(), Gtk.main_quit()))
        menu.append(quit_item)
        menu.show_all()
        self._menu = menu          # 持有引用，防止被垃圾回收
        try:
            menu.popup_at_pointer(event)   # GTK 3.22+ 新 API，按指针位置可靠弹出
        except Exception:
            menu.popup(None, None, None, None, event.button, event.time)

    def _toggle_lock(self, item):
        self.st['locked'] = item.get_active()
        save_state(self.st)

    def _reset_size(self):
        self.w = int(self.st.get('base_w', DEFAULT_W))
        self.h = int(self.st.get('base_h', DEFAULT_H))
        self.resize(self.w, self.h)
        self.st['w'], self.st['h'] = self.w, self.h
        save_state(self.st)
        self.queue_draw()
        self._sync_vwin()

    def _set_base_size(self):
        self.st['base_w'], self.st['base_h'] = int(self.w), int(self.h)
        save_state(self.st)

    def _restore_default(self):
        self.w, self.h = DEFAULT_W, DEFAULT_H
        self.resize(self.w, self.h)
        self.st['w'], self.st['h'] = self.w, self.h
        save_state(self.st)
        self.queue_draw()
        self._sync_vwin()

    def _do_refresh(self):
        try:
            self._mpv_state()
            self._sync_vwin()
        except Exception as e:
            log('refresh: ' + str(e))
        self.queue_draw()

    # ========== 绘制 ==========
    def on_draw(self, widget, ctx):
        try:
            self._draw(ctx)
        except Exception as e:
            import traceback
            log('draw error: ' + traceback.format_exc())

    def _fmt_us(self, us):
        s = max(0, int(us // 1000000))
        return '%d:%02d' % (s // 60, s % 60)

    def _draw(self, ctx):
        w, h = self.w, self.h
        ctx.set_operator(cairo.OPERATOR_CLEAR)
        ctx.paint()
        ctx.set_operator(cairo.OPERATOR_OVER)

        # 桌面小窗空闲态(未播放)时, 沿窗口边缘画一圈细蓝色描边, 标示可播放区域。
        # 全屏播放 / 有片源播放时不画。
        if not self.fs_state and not self.video_path:
            self._draw_idle_border(ctx, w, h)

        if not self.video_path and not self.mouse_inside:
            cx, cy = w / 2, h / 2 - 16
            s = min(w, h) * 0.07
            ctx.move_to(cx - s * 0.7 + 1, cy - s + 1)
            ctx.line_to(cx - s * 0.7 + 1, cy + s + 1)
            ctx.line_to(cx + s + 1, cy + 1)
            ctx.close_path()
            ctx.set_source_rgba(0.05, 0.07, 0.10, 0.6)
            ctx.fill()
            ctx.move_to(cx - s * 0.7, cy - s)
            ctx.line_to(cx - s * 0.7, cy + s)
            ctx.line_to(cx + s, cy)
            ctx.close_path()
            ctx.set_source_rgba(1, 1, 1, 0.85)
            ctx.fill()
            return False

        # 播放中常开的关闭钮：不管鼠标在不在窗内都画，保证任何时候点得到
        if self.video_path:
            self._draw_close_btn(ctx)

        if not self.mouse_inside:
            return False

        self._draw_controls(ctx)
        if self.st['locked']:
            ctx.set_font_size(12)
            ctx.move_to(w - PAD - 26, 22)
            ctx.set_source_rgba(0.85, 0.85, 0.90, 0.9)
            ctx.show_text('\u26f8')
        return False

    def _draw_idle_border(self, ctx, w, h):
        """未播放时的细蓝色窗框(内缩 0.5px 保证 1px 线完整可见)"""
        ctx.save()
        ctx.set_line_width(1)
        ctx.set_source_rgba(0.32, 0.60, 0.98, 0.85)
        ctx.rectangle(0.5, 0.5, w - 1, h - 1)
        ctx.stroke()
        ctx.restore()

    def _draw_controls(self, ctx):
        w, h = self.w, self.h
        rects = self._btn_rects()
        btn_y = h - PAD - BTN_H
        ctx.rectangle(0, btn_y - 96, w, h - btn_y + 96)
        ctx.set_source_rgba(0.05, 0.05, 0.08, 0.92)
        ctx.fill()
        bar_y = btn_y - 24
        ctx.rectangle(PAD, bar_y, w - 2 * PAD, 4)
        ctx.set_source_rgba(0.30, 0.30, 0.36, 0.6)
        ctx.fill()
        if self.duration_us and self.video_path:
            frac = max(0.0, min(1.0, self.pos_us / float(self.duration_us)))
            if frac > 0:
                ctx.rectangle(PAD, bar_y, (w - 2 * PAD) * frac, 4)
                ctx.set_source_rgba(0.18, 0.81, 0.44, 0.95)
                ctx.fill()
        elif not self.video_path:
            if self.scan_state == 'scanning':
                self._text(ctx, '正在扫描影片…', 0, bar_y - 26,
                           w, '%s 13' % SYS_FONT, (0.75, 0.75, 0.80))
            else:
                self._text(ctx, '右键打开视频 / 文件夹 / 拖入视频文件', 0, bar_y - 26,
                           w, '%s 13' % SYS_FONT, (0.75, 0.75, 0.80))
        if self.video_path:
            name = os.path.basename(self.video_path)
            if self.playlist:
                name = '%s  (%d/%d)' % (name, self.pl_idx + 1, len(self.playlist))
            self._text(ctx, name, PAD, bar_y - 52, w - 2 * PAD - 96,
                       '%s 11' % SYS_FONT, (0.85, 0.85, 0.90))
            if self.playlist:
                self._text(ctx, '≡ 列表', w - 96, bar_y - 54, 88,
                           '%s Bold 11' % SYS_FONT, (0.45, 0.80, 0.55))
        labels = {
            'prev': '上一部', 'toggle': '播放', 'next': '下一部',
            'stop': '停止', 'close': '关闭',
            'bwd': '快退', 'fwd': '快进',
            'fs': '全屏',
        }
        if not self.video_path:
            labels['prev'] = '打开'
            labels['toggle'] = '文件夹'
            labels['next'] = '网络'
            labels['stop'] = '—'
            labels['bwd'] = '—'
            labels['fwd'] = '—'
            labels['fs'] = '—'
        elif not self.playlist:
            labels['prev'] = '—'
            labels['next'] = '—'
        elif self.status == 'Playing':
            labels['toggle'] = '暂停'
        for k in ROW1:
            self._btn_draw(ctx, k, labels[k], rects[k])
        ctx.move_to(w - CORNER + 5, h - 6)
        ctx.line_to(w - 6, h - CORNER + 5)
        ctx.set_line_width(2)
        ctx.set_source_rgba(0.75, 0.75, 0.80, 0.8 if self.corner_hover else 0.4)
        ctx.stroke()

        if self.st['locked']:
            ctx.set_font_size(11)
            ctx.move_to(w - PAD - 26, 20)
            ctx.set_source_rgba(0.6, 0.6, 0.6, 0.7)
            ctx.show_text('🔒')
        if self.corner_hover:
            ctx.move_to(w - CORNER + 5, h - 6)
            ctx.line_to(w - 6, h - CORNER + 5)
            ctx.set_line_width(2)
            ctx.set_source_rgba(0.7, 0.7, 0.75, 0.5)
            ctx.stroke()
        return False

    def _btn_draw(self, ctx, key, label, rect):
        bx, by, bw, bh = rect
        r, g, b = BTN_COLORS[key]
        if self.hover == key:
            r = min(1.0, r * BTN_HOVER)
            g = min(1.0, g * BTN_HOVER)
            b = min(1.0, b * BTN_HOVER)
        layout = self.create_pango_layout(label)
        layout.set_font_description(Pango.FontDescription.from_string('%s Bold 11' % SYS_FONT))
        lw, lh = layout.get_pixel_size()
        self._text(ctx, label, bx + max(0, (bw - lw) / 2), by + (bh - lh) / 2 + 2,
                   max(bw, lw + 8), '%s Bold 11' % SYS_FONT, (r, g, b))

    def _text(self, ctx, text, x, y, max_w, font_desc, color):
        layout = self.create_pango_layout(text)
        fd = Pango.FontDescription.from_string(font_desc)
        layout.set_font_description(fd)
        layout.set_ellipsize(Pango.EllipsizeMode.END)
        layout.set_width(int(max_w * Pango.SCALE))
        ctx.set_source_rgba(*SHADOW_COLOR, 0.55)
        ctx.move_to(x + 1, y + 1)
        PangoCairo.show_layout(ctx, layout)
        ctx.set_source_rgb(*color)
        ctx.move_to(x, y)
        PangoCairo.show_layout(ctx, layout)


def _is_movie_process(pid):
    try:
        with open('/proc/%d/cmdline' % pid, 'rb') as f:
            return 'desktop-movie.py' in f.read().decode('utf-8', 'replace')
    except Exception:
        return False


def main():
    pid_file = os.path.join(APP_DIR, 'movie.pid')
    try:
        with open(pid_file) as f:
            old = int(f.read().strip() or 0)
        if old and old != os.getpid() and _is_movie_process(old):
            sys.exit(0)
    except Exception:
        pass
    try:
        os.makedirs(APP_DIR, exist_ok=True)
        with open(pid_file, 'w') as f:
            f.write(str(os.getpid()))
    except Exception:
        pass
    w = MovieWidget()

    # SIGTERM/SIGINT 时先干净退出 mpv 再走：否则 pkill / 控制中心“停止”挂件后，
    # mpv 会因 start_new_session 脱离进程组而继续在台上放。
    def _sig_handler(signum, frame):
        try:
            w._mpv_quit()
        except Exception:
            pass
        os._exit(0)

    for _s in (signal.SIGTERM, signal.SIGINT):
        try:
            signal.signal(_s, _sig_handler)
        except Exception:
            pass

    w.show_all()
    if len(sys.argv) > 1:
        GLib.timeout_add(1500, lambda: w._open_path(sys.argv[1]))
    Gtk.main()
    try:
        os.remove(pid_file)
    except Exception:
        pass


if __name__ == '__main__':
    main()