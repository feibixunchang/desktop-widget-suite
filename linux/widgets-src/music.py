#!/usr/bin/env python3
from widget_font import SYS_FONT as _SYS_FONT
# -*- coding: utf-8 -*-
"""桌面音乐挂件 - 通过 MPRIS 控制本机音乐播放器
   - 显示歌曲名/歌手名
   - 上一首/播放暂停/下一首/歌词/停止 按钮
   - 自由拖动/锁定位置/自由缩放
   兼容所有 MPRIS 播放器: VLC, deepin-music, Rhythmbox, Audacious 等
"""
import base64
import json
import os
import re
import shutil
import sqlite3
import subprocess
import sys
import time
import urllib.parse
import urllib.request

import gi
gi.require_version('Gtk', '3.0')
gi.require_version('Pango', '1.0')
from gi.repository import Gtk, Gdk, GLib, Pango, PangoCairo, Gio
import cairo

GLib.set_prgname('desktop-music')   # WM_CLASS 实例名，用于 dock 图标关联

APP_DIR = os.path.dirname(os.path.abspath(__file__))
STATE_PATH = os.path.expanduser('~/.config/desktop-music/state.json')

# 繁体->简体歌词归一化（自带映射，无外部依赖）；缺失时自动降级为原样
if APP_DIR not in sys.path:
    sys.path.insert(0, APP_DIR)
try:
    from trad2simp import to_simplified
except Exception:
    def to_simplified(s):
        return s
DBUS = Gio.BusType.SESSION

# ---------- 外观配置 ----------
TITLE_COLOR = (0.98, 0.98, 1.0)      # 歌名: 亮白
ARTIST_COLOR = (0.55, 0.87, 0.95)    # 歌手: 浅蓝
HINT_COLOR = (0.72, 0.75, 0.82)
SHADOW_COLOR = (0.05, 0.07, 0.10)
LYRIC_CUR = (1.0, 0.85, 0.12)        # 当前行 黄色
LYRIC_OTHER = (0.60, 0.80, 1.0)      # 上/下一行 浅蓝
LYRIC_H = 290                        # 歌词模式窗口高度
TITLE_FONT = _SYS_FONT + ' Bold 21'
ARTIST_FONT = _SYS_FONT + ' 15'
BTN_FONT = _SYS_FONT + ' Bold 11'
BTN_COLORS = {                          # 每个按钮颜色不同
    'prev':   (0.90, 0.49, 0.20),       # 上一首 橙色
    'toggle': (0.18, 0.81, 0.44),       # 播放/暂停 绿色
    'next':   (0.20, 0.57, 0.86),       # 下一首 蓝色
    'fav':    (0.91, 0.24, 0.24),       # 收藏 红色
    'loop':   (0.30, 0.80, 0.95),       # 循环 青色
    'lyrics': (0.95, 0.77, 0.06),       # 歌词 金黄
    'close':  (0.91, 0.24, 0.24),       # 停止 红色
}
BTN_HOVER = 1.25
PAD = 14
BTN_GAP = 10
BTN_W = 84                              # 固定按钮宽度(不随窗口缩放)
BTN_H = 32
CORNER = 20                              # 右下角缩放柄大小
MIN_W, MIN_H = 640, 110
MAX_W, MAX_H = 900, 500
BTN_KEYS = ['prev', 'toggle', 'next', 'fav', 'loop', 'lyrics', 'close']
LOOP_MODES = ('顺序', '单曲', '列表')


def log(msg):
    try:
        with open(os.path.join(APP_DIR, 'music.log'), 'a') as f:
            f.write(time.strftime('%Y-%m-%d %H:%M:%S') + ' ' + str(msg) + '\n')
    except Exception:
        pass


def load_state():
    st = {'x': None, 'y': None, 'w': 640, 'h': 170, 'locked': False,
          'font_scale': 1.0, 'lyr_offset_ms': 0, 'loop_mode': 0}
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


class MusicWidget(Gtk.Window):
    def _autostart_desktop_name(self):
        return 'desktop-music'

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
        self.set_title('desktop-music')
        self.set_icon_name('desktop-music')
        self.set_keep_below(True)
        self.set_skip_taskbar_hint(True)
        self.set_skip_pager_hint(True)
        self.set_app_paintable(True)
        vis = self.get_screen().get_rgba_visual()
        if vis:
            self.set_visual(vis)

        self.st = load_state()
        self.w, self.h = self.st['w'], self.st['h']
        self.lyrics_visible = bool(self.st.get('lyrics_visible'))
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

        # ---------- MPRIS 状态 ----------
        self.player = None          # 当前播放器总线名
        self.status = 'Stopped'
        self.title = '未检测到播放器'
        self.artist = ''
        self.app_name = ''
        self.duration_us = 0
        self.hover = None           # 悬停按钮
        self.corner_hover = False   # 悬停在缩放角
        self.dragging = False
        self.drag_start = None
        self.drag_orig = None
        self.resizing = False
        self.size_orig = None

        # ---------- 桌面歌词 ----------
        self.lyrics = []            # [(毫秒, 文本)]
        self.lyrics_key = None      # 已抓取的 (title, artist)
        self.lyr_idx = -1           # 当前歌词行索引
        self.lyrics_plain = False   # 是否仅有无时间轴歌词
        self.lyrics_saved_h = self.h
        self._lrc_index = None      # 本地 lrc 头部索引缓存

        # ---------- 播放进度估算 (部分播放器不提供 Position) ----------
        self.pos_ms = 0             # 估计播放位置(毫秒)
        self._pos_off = 0           # 估算基准位置(毫秒)
        self._pos_at = 0.0          # 估算基准时刻 time.monotonic()
        self._pos_track = None      # 估算基准对应的曲目 key
        self._meta = {}             # 最新 Metadata
        self.fav_on = False         # 当前歌曲收藏状态
        self._player_win = None     # 被隐藏的播放器窗口 id
        self._fav_tick = 0          # 收藏状态同步计数
        self._hide_ticks = 0        # 播放器窗口隐藏轮询计数
        self.loop_mode = int(self.st.get('loop_mode', 0)) % 3
        self._prev_status = None    # 上一次 PlaybackStatus(列表循环检测)
        self._loop_seeked = False   # 单曲循环本次已回到开头标志

        GLib.timeout_add(250, self._poll)
        GLib.timeout_add(100, self._karaoke_tick)
        self._poll()

    # ========== MPRIS ==========
    def _discover(self):
        try:
            bus = Gio.bus_get_sync(DBUS, None)
            res = bus.call_sync(
                'org.freedesktop.DBus', '/org/freedesktop/DBus',
                'org.freedesktop.DBus', 'ListNames', None, None,
                Gio.DBusCallFlags.NONE, 1000, None)
            names = res.unpack()[0]
            players = [n for n in names if n.startswith('org.mpris.MediaPlayer2.')]
            if not players:
                self.player = None
                return
            deepin = [n for n in players if 'DeepinMusic' in n]
            players = deepin + players
            old = self.player
            self.player = players[0]
            for n in players:
                try:
                    p = Gio.DBusProxy.new_for_bus_sync(
                        DBUS, Gio.DBusProxyFlags.NONE, None, n,
                        '/org/mpris/MediaPlayer2', 'org.mpris.MediaPlayer2.Player', None)
                    s = self._prop(p, 'PlaybackStatus') or ''
                    if s == 'Playing':
                        self.player = n
                        break
                    if s != 'Stopped' and self.player == players[0]:
                        self.player = n
                except Exception:
                    pass
            if self.player != old:
                self._read_meta()
        except Exception as e:
            log('discover: ' + str(e))

    def _proxy(self, iface):
        if not self.player:
            return None
        try:
            return Gio.DBusProxy.new_for_bus_sync(
                DBUS, Gio.DBusProxyFlags.NONE, None, self.player,
                '/org/mpris/MediaPlayer2', iface, None)
        except Exception:
            return None

    @staticmethod
    def _prop(proxy, name):
        try:
            return proxy.get_cached_property(name)
        except Exception:
            return None

    def _read_meta(self):
        p = self._proxy('org.mpris.MediaPlayer2.Player')
        if not p:
            self.status, self.title, self.artist = 'Stopped', '未检测到播放器', ''
            return
        st = self._prop(p, 'PlaybackStatus')
        self.status = st.unpack() if st else 'Stopped'
        md = self._prop(p, 'Metadata')
        title, artist, dur = '', '', 0
        self._meta = {}
        if md:
            m = md.unpack()
            self._meta = m
            if 'xesam:title' in m:
                title = m['xesam:title']
            if 'xesam:artist' in m:
                artist = ', '.join(str(a) for a in m['xesam:artist'])
            if 'xesam:album' in m and not title:
                title = m['xesam:album']
            if 'xesam:length' in m:
                dur = int(m['xesam:length']) or 0
            if not dur and 'mpris:length' in m:
                # deepin-music 的 mpris:length 单位为毫秒(非标准)
                dur = int(m['mpris:length']) * 1000 or 0
        if not title:
            try:
                q = self._proxy('org.mpris.MediaPlayer2')
                ident = self._prop(q, 'Identity')
                title = ident.unpack() if ident else self.player
            except Exception:
                title = self.player or ''
        self.title = title
        self.artist = artist
        self.duration_us = dur

    # ========== 桌面歌词 ==========
    LYRICS_DIR = os.path.expanduser('~/.cache/deepin/deepin-music/lyrics')
    META_DB = os.path.expanduser('~/.cache/deepin/deepin-music/mediameta.sqlite')
    # 歌词固有提前量(毫秒)。播放器通过 MPRIS 上报的位置普遍滞后于其自身
    # 音频时钟(deepin-music / lx-music 都有 0.5~2 秒上报延迟), 歌词整体偏慢。
    # 该值叠加在用户右键菜单「歌词提前/延后」的微调量之上。
    # 2.9.73 实测用户仍慢约 5 个字(约 1.5~2.5 秒), 故由 1000 提高到 2000。
    LYR_LEAD_MS = 2000
    # LX Music 的歌词存在自己的 SQLite 里(LxDatas/lx.data.db 的 lyric 表,
    # text 为 base64)。优先读它, 不依赖网络 —— lrclib.net 会 429 限流导致
    # 「未找到歌词」。
    LX_DB = os.path.expanduser(
        '~/.config/lx-music-desktop/LxDatas/lx.data.db')

    @staticmethod
    def _strip_lx_wordmarks(text):
        """剥离 LX Music lxlyric 的逐字标记 <起始毫秒,时长>，保留纯文本。
        例：[00:05.44]<0,1090>词<1090,1090>： → [00:05.44]词："""
        return re.sub(r'<\d+,\s*\d+>', '', text)

    @staticmethod
    def _lx_local_lyrics(title, artist):
        """从 LX Music 本地库按歌名/歌手取歌词，取不到返回空串。
        库文件正被 LX Music 占用，故先复制到临时文件再只读打开。"""
        src = MusicWidget.LX_DB
        if not os.path.exists(src) or not title:
            return ''
        tmp = ''
        try:
            tmp = os.path.join('/tmp', 'lx-lyric-%d.db' % os.getpid())
            shutil.copy2(src, tmp)
            con = sqlite3.connect(tmp)
            cur = con.cursor()
            # 用歌名+歌手定位歌曲 id(取并集，任一命中即可)
            ti = title.casefold().strip()
            ar = (artist or '').casefold().strip()
            ids = set()
            for q in ('SELECT id FROM my_list_music_info WHERE LOWER(name)=?',
                      'SELECT id FROM my_list_music_info WHERE LOWER(name)=? '
                      'AND LOWER(singer)=?'):
                try:
                    rows = cur.execute(q, (ti,) if q.count('?') == 1
                                       else (ti, ar)).fetchall()
                    ids.update(r[0] for r in rows)
                except Exception:
                    pass
            if not ids:
                con.close()
                return ''
            # 优先带时间轴的 lxlyric
            for typ in ('lxlyric', 'lyric'):
                for sid in ids:
                    for (txt,) in cur.execute(
                            'SELECT text FROM lyric WHERE id=? AND type=? '
                            'AND LENGTH(text)>0', (sid, typ)).fetchall():
                        try:
                            dec = base64.b64decode(txt).decode('utf-8', 'replace')
                        except Exception:
                            continue
                        if not dec.strip():
                            continue
                        if typ == 'lyric' and not re.search(r'\[\d+:\d+', dec):
                            continue      # 无时间轴的原文，留给网络源
                        con.close()
                        return dec
            con.close()
        except Exception as e:
            log('lx lyric: %s' % e)
        finally:
            if tmp and os.path.exists(tmp):
                try:
                    os.remove(tmp)
                except Exception:
                    pass
        return ''

    @staticmethod
    def _parse_lrc(text):
        out = []
        for line in text.splitlines():
            m = re.search(r'\[(\d+):(\d+[.:]\d+)\]', line)
            if not m:
                continue
            sec = int(m.group(1)) * 60 + float(m.group(2))
            body = MusicWidget._strip_lx_wordmarks(line[m.end():]).strip()
            if not body:
                continue
            out.append((int(sec * 1000), to_simplified(body)))
        out.sort()
        return out

    def _local_lrc_index(self):
        if self._lrc_index is not None:
            return self._lrc_index
        idx = {}
        try:
            for fn in os.listdir(self.LYRICS_DIR):
                if not fn.endswith('.lrc'):
                    continue
                path = os.path.join(self.LYRICS_DIR, fn)
                try:
                    with open(path, encoding='utf-8', errors='replace') as f:
                        head = f.read(4096)
                except Exception:
                    continue
                ti = ar = ''
                for line in head.splitlines()[:12]:
                    m = re.match(r'\s*\[(ti|ar):(.*)\]', line)
                    if m:
                        if m.group(1) == 'ti':
                            ti = m.group(2).strip()
                        else:
                            ar = m.group(2).strip()
                if ti:
                    idx.setdefault((ti.casefold(), ar.casefold()), []).append(path)
                    idx.setdefault((ti.casefold(), ''), []).append(path)
        except Exception:
            pass
        self._lrc_index = idx
        return idx

    def _local_lyrics(self):
        try:
            db = sqlite3.connect('file:' + self.META_DB + '?mode=ro', uri=True)
            cur = db.cursor()
            rows = cur.execute(
                'SELECT hash FROM musicNew WHERE title=? AND artist=? LIMIT 1',
                (self.title, self.artist)).fetchall()
            db.close()
            for (h,) in rows:
                p = os.path.join(self.LYRICS_DIR, h + '.lrc')
                if os.path.exists(p):
                    return open(p, encoding='utf-8', errors='replace').read()
        except Exception:
            pass
        ti = self.title.casefold().strip()
        ar = self.artist.casefold().strip()
        idx = self._local_lrc_index()
        for key in ((ti, ar), (ti, '')):
            for p in idx.get(key) or []:
                try:
                    with open(p, encoding='utf-8', errors='replace') as f:
                        return f.read()
                except Exception:
                    continue
        return ''

    def _fetch_lyrics(self):
        self.lyrics = []
        self.lyrics_plain = False
        self.lyr_idx = -1
        if not self.title or not self.artist:
            return
        local = self._local_lyrics()
        parsed = self._parse_lrc(local)
        if parsed:
            self.lyrics = parsed
            return
        # LX Music 本地库(权威来源, 不受网络限流影响)
        lx = self._lx_local_lyrics(self.title, self.artist)
        parsed = self._parse_lrc(lx)
        if parsed:
            self.lyrics = parsed
            log('歌词来源: lx-music 本地库')
            return
        synced = ''
        plain = ''
        if self.duration_us:
            q = urllib.parse.urlencode({
                'artist_name': self.artist,
                'track_name': self.title,
                'duration': round(self.duration_us / 1000000),
            })
            try:
                with urllib.request.urlopen('https://lrclib.net/api/get?' + q, timeout=6) as r:
                    d = json.loads(r.read())
                synced = d.get('syncedLyrics') or ''
                plain = d.get('plainLyrics') or ''
            except Exception:
                pass
        if not synced:
            try:
                sq = urllib.parse.urlencode({'artist_name': self.artist, 'track_name': self.title})
                with urllib.request.urlopen('https://lrclib.net/api/search?' + sq, timeout=6) as r:
                    lst = json.loads(r.read())
                for it in lst:
                    if it.get('syncedLyrics'):
                        synced = it['syncedLyrics']
                        break
                    if not plain and it.get('plainLyrics'):
                        plain = it['plainLyrics']
            except Exception:
                pass
        if not synced and not plain:
            try:
                sq = urllib.parse.urlencode({'track_name': self.title})
                with urllib.request.urlopen('https://lrclib.net/api/search?' + sq, timeout=6) as r:
                    lst = json.loads(r.read())
                for it in lst:
                    if it.get('syncedLyrics'):
                        synced = it['syncedLyrics']
                        break
                    if not plain and it.get('plainLyrics'):
                        plain = it['plainLyrics']
            except Exception:
                pass
        self.lyrics = self._parse_lrc(synced)
        if not self.lyrics and plain:
            self.lyrics = [(0, to_simplified(plain))]
            self.lyrics_plain = True

    def _update_lyric(self):
        if not self.lyrics:
            return
        pos = self.pos_ms + self.LYR_LEAD_MS + self.st.get('lyr_offset_ms', 0)
        idx = -1
        for i, (t, _) in enumerate(self.lyrics):
            if t <= pos:
                idx = i
            else:
                break
        if idx != self.lyr_idx:
            self.lyr_idx = idx
            self.queue_draw()

    def _karaoke_tick(self):
        if self.player and self.status == 'Playing':
            self.pos_ms = self._pos_off + int((time.monotonic() - self._pos_at) * 1000)
            if self.duration_us and self.pos_ms > self.duration_us // 1000:
                self.pos_ms = self.duration_us // 1000
        if self.lyrics and not self.lyrics_plain:
            self._update_lyric()   # 行切换改为 100ms 粒度(原只在 500ms 轮询里更新, 导致慢半句)
            self.queue_draw()
        return True

    def _poll(self):
        try:
            if self.player:
                try:
                    bus = Gio.bus_get_sync(DBUS, None)
                    res = bus.call_sync(
                        'org.freedesktop.DBus', '/org/freedesktop/DBus',
                        'org.freedesktop.DBus', 'NameHasOwner',
                        GLib.Variant('(s)', (self.player,)), None,
                        Gio.DBusCallFlags.NONE, 1000, None)
                    alive = bool(res.unpack()[0])
                except Exception:
                    alive = False
                if not alive:
                    self.player = None
                    self.status = 'Stopped'
                    self.title = '未检测到播放器'
                    self.artist = ''
                    self.duration_us = 0
                    self.pos_ms = 0
                    self.lyrics = []
                    self.lyrics_key = None
                    self.lyrics_plain = False
                    self.queue_draw()
                    return True
            if not self.player:
                self._discover()
                self.queue_draw()
                return True
            try:
                self._read_meta()
            except Exception:
                self._discover()
            self._fav_tick += 1
            if self._fav_tick % 10 == 0:
                s = self._fav_status()
                if s is not None and s != self.fav_on:
                    self.fav_on = s
            key = (self.title, self.artist)
            if key != self._pos_track:
                self._pos_track = key
                self._pos_off = 0
                self._pos_at = time.monotonic()
                self._loop_seeked = False
            real_pos = self._read_position()
            # D-Bus 同步调用本身有耗时, 取时刻必须在读完之后, 否则外推基准偏早,
            # 会让 pos_ms 少算这段时间(歌词越跑越慢)
            now = time.monotonic()
            if self.status == 'Playing':
                if real_pos is not None and real_pos > 0:
                    self.pos_ms = real_pos // 1000
                    self._pos_off = self.pos_ms
                    self._pos_at = now
                else:
                    self.pos_ms = self._pos_off + int((now - self._pos_at) * 1000)
                if self.duration_us and self.pos_ms > self.duration_us // 1000:
                    self.pos_ms = self.duration_us // 1000
            elif self.status == 'Paused':
                self._pos_off = self.pos_ms
                self._pos_at = now
            else:
                self.pos_ms = 0
            # 单曲循环: 进度接近末尾时回到开头
            dur_ms = int(self.duration_us / 1000)
            if self._loop_seeked and dur_ms and self.pos_ms < dur_ms * 0.5:
                self._loop_seeked = False
            if (self.loop_mode == 1 and self.status == 'Playing'
                    and dur_ms and not self._loop_seeked
                    and self.pos_ms > dur_ms * 0.97):
                self._seek(-self.pos_ms * 1000)
                self._loop_seeked = True
            # 列表循环: 播放结束(Playing->Stopped)时播下一首
            if self.loop_mode == 2 and self._prev_status == 'Playing' \
                    and self.status == 'Stopped':
                self._call('Next')
            self._prev_status = self.status
            if self.lyrics_visible:
                key = (self.title, self.artist)
                if self.lyrics_key != key:
                    self.lyrics_key = key
                    self._fetch_lyrics()
                self._update_lyric()
            self.queue_draw()
        except Exception:
            pass
        return True

    def _read_position(self):
        """读取播放器真实播放位置(微秒),失败返回 None"""
        p = self._proxy('org.freedesktop.DBus.Properties')
        if not p:
            return None
        try:
            res = p.call_sync('Get',
                              GLib.Variant('(ss)', ('org.mpris.MediaPlayer2.Player', 'Position')),
                              Gio.DBusCallFlags.NONE, 800)
            return res.unpack()[0]
        except Exception as e:
            log('PosAttr: %s' % e)
            return None

    def _call(self, method):
        p = self._proxy('org.mpris.MediaPlayer2.Player')
        if not p:
            return
        try:
            p.call_sync(method, GLib.Variant('()', None),
                        Gio.DBusCallFlags.NONE, 800)
        except Exception as e:
            log('call %s: %s' % (method, e))

    def _seek(self, offset_us):
        """Seek 到指定偏移(微秒), 同时重置挂件的位置估算"""
        p = self._proxy('org.mpris.MediaPlayer2.Player')
        if not p:
            return
        try:
            p.call_sync('Seek', GLib.Variant('(x)', (int(offset_us),)),
                        Gio.DBusCallFlags.NONE, 800)
            now = time.monotonic()
            self._pos_off = 0
            self._pos_at = now
            self.pos_ms = 0
        except Exception as e:
            log('seek: %s' % e)

    # ========== 收藏(写入 deepin-music 媒体库) ==========
    def _current_hash(self):
        try:
            url = (getattr(self, '_meta', None) or {}).get('xesam:url', '')
            path = url.replace('file://', '')
            con = sqlite3.connect(MusicWidget.META_DB)
            cur = con.cursor()
            if path:
                cur.execute('SELECT hash FROM musicNew WHERE localpath=? LIMIT 1', (path,))
                row = cur.fetchone()
                if row:
                    con.close()
                    return row[0]
            t, a = self.title, self.artist
            if t:
                if a:
                    cur.execute('SELECT hash FROM musicNew WHERE title=? AND artist=? LIMIT 1', (t, a))
                else:
                    cur.execute('SELECT hash FROM musicNew WHERE title=? LIMIT 1', (t,))
                row = cur.fetchone()
                if row:
                    con.close()
                    return row[0]
            con.close()
            return None
        except Exception as e:
            log('hash: %s' % e)
            return None

    def _fav_status(self):
        h = self._current_hash()
        if not h:
            return None
        try:
            con = sqlite3.connect(MusicWidget.META_DB)
            cur = con.cursor()
            cur.execute('SELECT 1 FROM playlist_fav WHERE music_id=? AND playlist_id=? LIMIT 1', (h, 'fav'))
            row = cur.fetchone()
            con.close()
            return row is not None
        except Exception as e:
            log('fav status: %s' % e)
            return None

    def _toggle_fav(self):
        want = not self.fav_on
        h = self._current_hash()
        if not h:
            log('fav: 未找到当前歌曲记录')
            return
        try:
            self._write_fav_db(h, want)
            self.fav_on = want
            log('fav %s: %s' % ('已收藏' if want else '取消收藏', h))
        except Exception as e:
            log('fav db: %s' % e)
        self.queue_draw()
        self._restart_player(h)

    def _write_fav_db(self, h, want):
        con = sqlite3.connect(MusicWidget.META_DB)
        cur = con.cursor()
        if want:
            cur.execute('INSERT OR IGNORE INTO playlist_fav (music_id, playlist_id, sort_id) VALUES (?, ?, 0)',
                        (h, 'fav'))
        else:
            cur.execute('DELETE FROM playlist_fav WHERE music_id=? AND playlist_id=?', (h, 'fav'))
        con.commit()
        con.close()

    def _restart_player(self, h):
        """重启播放器,使其重新加载收藏列表
        时序:先退出 -> 播放器退出会覆写 DB -> 退出完成后重新写入 -> 再启动"""
        try:
            try:
                p = self._proxy('org.mpris.MediaPlayer2')
                if p:
                    p.call_sync('Quit', GLib.Variant('()', None),
                                Gio.DBusCallFlags.NONE, 800)
            except Exception:
                pass
            bus = Gio.bus_get_sync(DBUS, None)
            for _ in range(20):
                try:
                    res = bus.call_sync(
                        'org.freedesktop.DBus', '/org/freedesktop/DBus',
                        'org.freedesktop.DBus', 'NameHasOwner',
                        GLib.Variant('(s)', ('org.mpris.MediaPlayer2.DeepinMusic',)),
                        None, Gio.DBusCallFlags.NONE, 500, None)
                    if not res.unpack()[0]:
                        break
                except Exception:
                    break
                time.sleep(0.5)
            time.sleep(1.5)
            self._write_fav_db(h, self.fav_on)
            self._launch_player()
            self._hide_ticks = 0
            GLib.timeout_add(150, self._poll_hide_player)
        except Exception as e:
            log('restart player: %s' % e)

    def _poll_hide_player(self):
        """播放器窗口一出现立即隐藏(高速轮询, 尽量不闪现)"""
        self._hide_ticks += 1
        if self._find_main_player_win():
            self._hide_player_window()
            return False
        if self._hide_ticks > 150:
            log('hide: 超时未找到播放器窗口')
            return False
        return True

    def _minimize_player_window(self):
        self._hide_player_window()

    def _call_quit(self):
        p = self._proxy('org.mpris.MediaPlayer2')
        if not p:
            return
        try:
            p.call_sync('Quit', GLib.Variant('()', None),
                        Gio.DBusCallFlags.NONE, 800)
        except Exception as e:
            log('quit: ' + str(e))

    def _stop_playback(self):
        """停止后台音乐播放(不退出挂件窗口)。供「停止」按钮使用"""
        log('stop playback')
        self._call('Stop')
        # 立即给出视觉反馈: 状态置为 Stopped 并清空歌词
        self.status = 'Stopped'
        if self.lyrics:
            self.lyrics = []
        self.queue_draw()

    def _try_launch(self, app_id, bin_path):
        """尝试启动一个播放器: 优先 gtk-launch(遵循 .desktop 与会话环境), 回退直接跑二进制"""
        try:
            subprocess.Popen(['gtk-launch', app_id],
                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                             start_new_session=True)
            log('launch player via gtk-launch: ' + app_id)
            return True
        except Exception as e:
            log('gtk-launch %s: %s' % (app_id, e))
        if os.path.exists(bin_path):
            try:
                subprocess.Popen([bin_path],
                                 stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                 start_new_session=True)
                log('launch player via binary: ' + bin_path)
                return True
            except Exception as e:
                log('launch binary %s: %s' % (bin_path, e))
        return False

    def _launch_player(self):
        """启动本机音乐播放器: 优先 LX Music, 回退 deepin-music, 最后才用 ll-cli"""
        candidates = [
            ('lx-music-desktop', '/opt/lx-music-desktop/lx-music-desktop'),
            ('deepin-music', '/usr/bin/deepin-music'),
        ]
        for app_id, bin_path in candidates:
            if self._try_launch(app_id, bin_path):
                return
        # 最终回退: 原 ll-cli 方式(部分环境仍可用)
        try:
            subprocess.Popen(['ll-cli', 'run', 'org.deepin.music'],
                             stdout=subprocess.DEVNULL,
                             stderr=subprocess.DEVNULL,
                             start_new_session=True)
            log('launch player (ll-cli): ok')
        except Exception as e:
            log('launch fallback: ' + str(e))

    def _raise_player(self):
        self._unhide_player_window()
        p = self._proxy('org.mpris.MediaPlayer2')
        if not p:
            return
        try:
            p.call_sync('Raise', GLib.Variant('()', None),
                        Gio.DBusCallFlags.NONE, 800)
        except Exception as e:
            log('raise: ' + str(e))

    def _find_main_player_win(self):
        try:
            out = subprocess.check_output(['xdotool', 'search', '--name', '音乐'],
                                          stderr=subprocess.DEVNULL, timeout=5)
            best = None
            best_area = 0
            for i in out.split():
                wid = i.decode()
                try:
                    geo = subprocess.check_output(
                        ['xdotool', 'getwindowgeometry', wid],
                        stderr=subprocess.DEVNULL, timeout=3).decode()
                    m = re.search(r'Geometry:\s*(\d+)x(\d+)', geo)
                    if not m:
                        continue
                    area = int(m.group(1)) * int(m.group(2))
                    if area > best_area:
                        best_area = area
                        best = wid
                except Exception:
                    continue
            return best
        except Exception:
            return None

    def _hide_player_window(self):
        wid = self._find_main_player_win()
        if wid:
            try:
                subprocess.run(['xdotool', 'windowunmap', wid], timeout=3)
                self._player_win = wid
                log('hide player window: %s' % wid)
            except Exception as e:
                log('hide: %s' % e)
        else:
            log('hide: 未找到播放器窗口')
        return False

    def _unhide_player_window(self):
        if self._player_win:
            try:
                subprocess.run(['xdotool', 'windowmap', self._player_win], timeout=3)
                subprocess.run(['xdotool', 'windowactivate', self._player_win], timeout=3)
                self._player_win = None
                log('show player window')
            except Exception as e:
                log('show: %s' % e)

    # ========== 布局与命中 ==========
    def _btn_rects(self):
        n = len(BTN_KEYS)
        total = n * BTN_W + (n - 1) * BTN_GAP
        x0 = (self.w - total) // 2
        y = self.h - PAD - BTN_H
        rects = {}
        for i, k in enumerate(BTN_KEYS):
            rects[k] = (x0 + i * (BTN_W + BTN_GAP), y, BTN_W, BTN_H)
        return rects

    def _hit_btn(self, x, y):
        for k, (bx, by, bw, bh) in self._btn_rects().items():
            if bx <= x < bx + bw and by <= y < by + bh:
                return k
        return None

    def set_hover(self, key):
        if self.hover != key:
            self.hover = key
            self.queue_draw()

    def _hit_corner(self, x, y):
        return (self.w - CORNER <= x <= self.w and
                self.h - CORNER <= y <= self.h)

    # ========== 交互 ==========
    def on_leave(self, widget, event):
        self.set_hover(None)
        if self.corner_hover:
            self.corner_hover = False
            self.queue_draw()

    def on_press(self, widget, event):
        x, y = event.x, event.y
        if event.button == 1:
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
                x, y = self.get_position()
                self.st['x'], self.st['y'] = x, y
                save_state(self.st)
                return True
            if self.resizing:
                self.resizing = False
                self.st['w'], self.st['h'] = int(self.w), int(self.h)
                save_state(self.st)
                return True
        return False

    def _activate(self, btn):
        if btn == 'prev':
            self._call('Previous')
        elif btn == 'toggle':
            if not self.player:
                self._launch_player()
            else:
                self._call('PlayPause')
        elif btn == 'next':
            self._call('Next')
        elif btn == 'fav':
            self._toggle_fav()
        elif btn == 'loop':
            self.loop_mode = (self.loop_mode + 1) % 3
            self.st['loop_mode'] = self.loop_mode
            save_state(self.st)
            self.queue_draw()
        elif btn == 'lyrics':
            self.lyrics_visible = not self.lyrics_visible
            if self.lyrics_visible:
                self.lyrics_saved_h = self.h
                self.h = LYRIC_H
                self.resize(self.w, LYRIC_H)
                self.lyrics_key = None
                self._fetch_lyrics()
                self._update_lyric()
            else:
                self.h = self.lyrics_saved_h
                self.resize(self.w, self.lyrics_saved_h)
            self.st['h'] = int(self.h)
            self.st['lyrics_visible'] = self.lyrics_visible
            save_state(self.st)
            self.queue_draw()
        elif btn == 'close':
            self._stop_playback()

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
        ref_item = Gtk.MenuItem.new_with_label('刷新')
        ref_item.connect('activate', lambda *a: self._do_refresh())
        lyric_item = Gtk.MenuItem.new_with_label('重新抓取歌词')
        lyric_item.connect('activate', lambda *a: self._refetch_lyrics())
        font_up = Gtk.MenuItem.new_with_label('增大字号')
        font_up.connect('activate', lambda *a: self._adj_font(0.1))
        font_down = Gtk.MenuItem.new_with_label('减小字号')
        font_down.connect('activate', lambda *a: self._adj_font(-0.1))
        # 注意方向: pos = pos_ms + LYR_LEAD_MS + lyr_offset_ms, 该值越大歌词越超前,
        # 所以「提前」必须传正值, 「延后」传负值(2.9.72 及之前这里方向是反的)。
        ly_early = Gtk.MenuItem.new_with_label('歌词提前 0.5 秒')
        ly_early.connect('activate', lambda *a: self._adj_lyr(500))
        ly_late = Gtk.MenuItem.new_with_label('歌词延后 0.5 秒')
        ly_late.connect('activate', lambda *a: self._adj_lyr(-500))
        ly_early2 = Gtk.MenuItem.new_with_label('歌词提前 2 秒（快速校准）')
        ly_early2.connect('activate', lambda *a: self._adj_lyr(2000))
        ly_late2 = Gtk.MenuItem.new_with_label('歌词延后 2 秒')
        ly_late2.connect('activate', lambda *a: self._adj_lyr(-2000))
        cur_off = int(self.st.get('lyr_offset_ms', 0) or 0)
        ly_show = Gtk.MenuItem.new_with_label(
            '当前微调: %+.1f 秒' % (cur_off / 1000.0))
        ly_show.set_sensitive(False)
        ly_reset = Gtk.MenuItem.new_with_label('歌词偏移清零')
        ly_reset.connect('activate', lambda *a: self._adj_lyr(-cur_off))
        lock_item = Gtk.CheckMenuItem.new_with_label('锁定位置')
        lock_item.set_active(self.st['locked'])
        lock_item.connect('toggled', self._toggle_lock)
        quit_item = Gtk.MenuItem.new_with_label('退出')
        quit_item.connect('activate', lambda *a: (self.destroy(), Gtk.main_quit()))
        menu.append(ref_item)
        menu.append(lyric_item)
        menu.append(Gtk.SeparatorMenuItem())
        menu.append(font_up)
        menu.append(font_down)
        menu.append(Gtk.SeparatorMenuItem())
        menu.append(ly_early)
        menu.append(ly_late)
        menu.append(ly_early2)
        menu.append(ly_late2)
        menu.append(ly_show)
        menu.append(ly_reset)
        menu.append(Gtk.SeparatorMenuItem())
        menu.append(lock_item)
        menu.append(Gtk.SeparatorMenuItem())
        autostart_item = Gtk.CheckMenuItem(label='开机自启')
        autostart_item.set_active(self._autostart_enabled())
        autostart_item.connect('toggled', lambda w: self._toggle_autostart(w.get_active()))
        menu.append(autostart_item)
        menu.append(quit_item)
        menu.show_all()
        menu.popup(None, None, None, None, event.button, event.time)

    def _do_refresh(self):
        try:
            self._discover()
            if self.player:
                self._read_meta()
                self.lyrics_key = None
                if self.lyrics_visible:
                    self._fetch_lyrics()
                self._update_lyric()
        except Exception as e:
            log('refresh: %s' % e)
        self.queue_draw()

    def _refetch_lyrics(self):
        self.lyrics_key = None
        self._fetch_lyrics()
        self._update_lyric()
        self.queue_draw()

    def _adj_font(self, delta):
        self.st['font_scale'] = min(1.8, max(0.7, self.st.get('font_scale', 1.0) + delta))
        save_state(self.st)
        self.queue_draw()

    def _adj_lyr(self, delta_ms):
        self.st['lyr_offset_ms'] = min(10000, max(-10000,
            self.st.get('lyr_offset_ms', 0) + delta_ms))
        save_state(self.st)
        if self.lyrics_visible:
            self._update_lyric()
            self.queue_draw()

    def _toggle_lock(self, item):
        self.st['locked'] = item.get_active()
        save_state(self.st)

    # ========== 绘制 ==========
    def _rounded(self, ctx, x, y, w, h, r):
        ctx.new_path()
        ctx.arc(x + r, y + r, r, 3.1416, 4.7124)
        ctx.arc(x + w - r, y + r, r, 4.7124, 6.2832)
        ctx.arc(x + w - r, y + h - r, r, 0, 1.5708)
        ctx.arc(x + r, y + h - r, r, 1.5708, 3.1416)
        ctx.close_path()

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

        playing = self.status == 'Playing'
        text_w = w - 2 * PAD
        self._text(ctx, self.title, PAD, 16, text_w, (_SYS_FONT + ' Bold %d') % self.fs(21), TITLE_COLOR)
        if self.lyrics_visible:
            if self.artist:
                self._text(ctx, self.artist, PAD, 70, text_w, (_SYS_FONT + ' %d') % self.fs(13), ARTIST_COLOR)
            self._lyrics_panel(ctx)
        elif self.artist:
            self._text(ctx, self.artist, PAD, 68, text_w, (_SYS_FONT + ' %d') % self.fs(15), ARTIST_COLOR)
        elif not self.player:
            self._text(ctx, '音乐未运行 · 点击「打开音乐」按钮启动',
                       PAD, 68, text_w, (_SYS_FONT + ' %d') % self.fs(12), HINT_COLOR)

        rects = self._btn_rects()
        self._btn_draw(ctx, 'prev', '上一首', rects['prev'])
        if not self.player:
            toggle_label = '打开音乐'
        elif playing:
            toggle_label = '暂停'
        else:
            toggle_label = '播放'
        self._btn_draw(ctx, 'toggle', toggle_label, rects['toggle'])
        self._btn_draw(ctx, 'next', '下一首', rects['next'])
        self._btn_draw(ctx, 'fav', '已收藏' if self.fav_on else '收藏', rects['fav'])
        self._btn_draw(ctx, 'loop', LOOP_MODES[self.loop_mode], rects['loop'])
        self._btn_draw(ctx, 'lyrics', '歌词', rects['lyrics'])
        self._btn_draw(ctx, 'close', '停止', rects['close'])

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

    def _lyrics_panel(self, ctx):
        w = self.w

        def one_line(t):
            return ' '.join(t.splitlines()) if t else t

        if not self.lyrics:
            self._text(ctx, '未找到歌词', PAD, 150, w - 2 * PAD, (_SYS_FONT + ' %d') % self.fs(15), HINT_COLOR)
            return
        i = self.lyr_idx if self.lyr_idx >= 0 else 0
        if self.lyrics_plain:
            rows = [('(未同步歌词)', 0.75),
                    (one_line(self.lyrics[0][1]), 1.0),
                    ('', 0.75)]
        else:
            rows = [(one_line(self.lyrics[i - 1][1]) if i > 0 else '', 0.75),
                    (one_line(self.lyrics[i][1]) if i < len(self.lyrics) else '', 1.0),
                    (one_line(self.lyrics[i + 1][1]) if i + 1 < len(self.lyrics) else '', 0.75)]
        for k, (txt, alpha) in enumerate(rows):
            if not txt:
                continue
            font = (_SYS_FONT + ' Bold %d') % self.fs(18) if k == 1 else (_SYS_FONT + ' %d') % self.fs(14)
            color = LYRIC_CUR if k == 1 else LYRIC_OTHER
            layout = self.create_pango_layout(txt)
            fd = Pango.FontDescription.from_string(font)
            layout.set_font_description(fd)
            layout.set_ellipsize(Pango.EllipsizeMode.END)
            layout.set_width(int((w - 2 * PAD) * Pango.SCALE))
            lw, lh = layout.get_pixel_size()
            y = (106, 146, 194)[k]
            x0 = PAD + (w - 2 * PAD - lw) / 2
            ctx.set_source_rgba(*SHADOW_COLOR, alpha * 0.5)
            ctx.move_to(x0 + 1, y + 1)
            PangoCairo.show_layout(ctx, layout)
            if k == 1 and not self.lyrics_plain and lw > 0:
                pos = self.pos_ms + self.LYR_LEAD_MS + self.st.get('lyr_offset_ms', 0)
                t0 = self.lyrics[i][0]
                t1 = self.lyrics[i + 1][0] if i + 1 < len(self.lyrics) else t0 + 5000
                span = float(t1 - t0) if t1 > t0 else 5000.0
                ahead = min(2.0 / max(1, len(txt)), 0.3)
                prog = min(1.0, max(0.0, (pos - t0) / span + ahead))
                ctx.set_source_rgba(*LYRIC_OTHER, alpha)
                ctx.move_to(x0, y)
                PangoCairo.show_layout(ctx, layout)
                if prog > 0.02:
                    ctx.save()
                    ctx.rectangle(x0, y - 2, lw * prog, lh + 6)
                    ctx.clip()
                    ctx.set_source_rgba(*LYRIC_CUR, alpha)
                    ctx.move_to(x0, y)
                    PangoCairo.show_layout(ctx, layout)
                    ctx.restore()
            else:
                ctx.set_source_rgba(color[0], color[1], color[2], alpha)
                ctx.move_to(x0, y)
                PangoCairo.show_layout(ctx, layout)

    def fs(self, px):
        """按 font_scale 缩放字号"""
        return max(8, int(round(px * float(self.st.get('font_scale', 1.0)))))

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


def main():
    pid_file = os.path.join(APP_DIR, 'music.pid')
    try:
        with open(pid_file) as f:
            old = int(f.read().strip() or 0)
        if old and old != os.getpid() and os.path.exists('/proc/%d' % old):
            sys.exit(0)
    except Exception:
        pass
    try:
        os.makedirs(APP_DIR, exist_ok=True)
        with open(pid_file, 'w') as f:
            f.write(str(os.getpid()))
    except Exception:
        pass
    w = MusicWidget()
    w.show_all()
    GLib.timeout_add(800, w._all_desktops)
    GLib.timeout_add(5000, w._all_desktops)
    Gtk.main()
    try:
        os.remove(pid_file)
    except Exception:
        pass


if __name__ == '__main__':
    main()
