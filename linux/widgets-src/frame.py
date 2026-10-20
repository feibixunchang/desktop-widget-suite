#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""桌面相框挂件 - 背景透明, 幻灯片播放图片文件夹"""
import os
from widget_font import SYS_FONT as _SYS_FONT
import datetime
import json
import os
import sys

import gi
gi.require_version('Gtk', '3.0')
from gi.repository import Gtk, Gdk, GLib, Pango, PangoCairo, GdkPixbuf
import subprocess
import cairo

GLib.set_prgname('desktop-frame')

APP_DIR = os.path.dirname(os.path.abspath(__file__))
STATE_PATH = os.path.expanduser('~/.config/desktop-frame/state.json')
PID_PATH = os.path.expanduser('~/.config/desktop-frame/pid')

EXTS = ('.jpg', '.jpeg', '.png', '.gif', '.bmp', '.webp')
MIN_W, MIN_H = 380, 300
MAX_W, MAX_H = 3000, 1900
PAD = 12
CORNER = 26
HEAD_H = 34
NAV_W, NAV_H = 52, 52
TEXT = (0.92, 0.93, 0.97)
TEXT_DIM = (0.66, 0.70, 0.80)


def check_single():
    try:
        with open(PID_PATH) as f:
            old = int(f.read().strip())
        if os.path.exists('/proc/%d' % old):
            c = open('/proc/%d/cmdline' % old, 'rb').read().decode(errors='ignore')
            if 'desktop-frame' in c or 'frame.py' in c:
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
    st = {'x': None, 'y': None, 'w': 640, 'h': 500, 'locked': False,
          'dir': None, 'slide': True, 'interval': 10, 'ratio': True, 'idx': 0}
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


def default_dir():
    for d in ('~/图片', '~/Pictures'):
        p = os.path.expanduser(d)
        if os.path.isdir(p):
            return p
    return os.path.expanduser('~/图片')


class FrameWidget(Gtk.Window):
    def _autostart_desktop_name(self):
        return 'desktop-frame'

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
        self.set_title('desktop-frame')
        self.set_icon_name('desktop-frame')
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
        self.w, self.h = self.st['w'], self.st['h']
        self.folder = self.st.get('dir') or default_dir()
        self.locked = bool(self.st['locked'])
        self.slide = bool(self.st.get('slide', True))
        self.interval = max(3, int(self.st.get('interval', 10)))
        self.ratio = bool(self.st.get('ratio', True))
        self.idx = int(self.st.get('idx', 0))
        self.files = self._scan()
        self.pix = None
        self.hover = None
        self.inside = False
        self.corner_hover = False
        self.dragging = False
        self.drag_start = None
        self.drag_orig = None
        self.resizing = False
        self.size_orig = None

        if self.st['x'] is not None:
            self.move(self.st['x'], self.st['y'])
        else:
            scr = self.get_screen()
            self.move(max(0, scr.get_width() - self.w - 40), 120)
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

        self._load_img()
        if self.slide:
            GLib.timeout_add_seconds(self.interval, self._slide_tick)
        self.show_all()
        GLib.timeout_add(800, self._all_desktops)
        GLib.timeout_add(5000, self._all_desktops)

    def log(self, msg):
        try:
            with open(os.path.join(APP_DIR, 'frame.log'), 'a') as f:
                f.write(datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S') + ' ' + str(msg) + '\n')
        except Exception:
            pass

    # ---------- 图片 ----------
    def _scan(self):
        try:
            files = []
            for root, dirs, fs in os.walk(self.folder):
                dirs.sort()
                for f in sorted(fs):
                    if f.lower().endswith(EXTS):
                        files.append(os.path.join(root, f))
                if len(files) >= 500:
                    break
            return files
        except Exception as e:
            self.log('scan: %s' % e)
            return []

    def _load_img(self):
        self.pix = None
        if not self.files:
            self.queue_draw()
            return
        self.idx = self.idx % len(self.files)
        f = self.files[self.idx]
        try:
            # 限制最长边, 省内存
            self.pix = GdkPixbuf.Pixbuf.new_from_file_at_scale(f, 1600, 1600, True)
            self.st['idx'] = self.idx
        except Exception as e:
            self.log('load img: %s' % e)
            self.pix = None
        if self.pix and self.ratio and not self.locked:
            self._adapt_size()
        save_state(self.st)
        self.queue_draw()

    def _adapt_size(self):
        pw, ph = self.pix.get_width(), self.pix.get_height()
        if not pw or not ph:
            return
        avail_w = self.w - 2 * PAD
        nh = int((self.w - 2 * PAD) * ph / pw) + HEAD_H
        nh = max(MIN_H, min(MAX_H, nh))
        if nh != self.h:
            self.h = nh
            self.resize(self.w, nh)
        self.st['h'] = self.h

    def _next(self, d):
        if not self.files:
            return
        self.idx = (self.idx + d) % len(self.files)
        self._load_img()

    def _slide_tick(self):
        if self.slide and len(self.files) > 1:
            self._next(1)
        return self.slide

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
        cho = Gtk.MenuItem.new_with_label('选择图片文件夹…')
        cho.connect('activate', lambda *a: self._choose_folder())
        slide_item = Gtk.CheckMenuItem.new_with_label('幻灯片播放')
        slide_item.set_active(self.slide)
        slide_item.connect('toggled', self._toggle_slide)
        ratio_item = Gtk.CheckMenuItem.new_with_label('窗口随图片比例调整')
        ratio_item.set_active(self.ratio)
        ratio_item.connect('toggled', self._toggle_ratio)
        lock_item = Gtk.CheckMenuItem.new_with_label('锁定位置')
        lock_item.set_active(self.locked)
        lock_item.connect('toggled', self._toggle_lock)
        quit_item = Gtk.MenuItem.new_with_label('退出')
        quit_item.connect('activate', lambda *a: (self.destroy(), Gtk.main_quit()))
        menu.append(cho)
        menu.append(Gtk.SeparatorMenuItem())
        menu.append(slide_item)
        int_item = Gtk.MenuItem.new_with_label('幻灯片间隔')
        int_sub = Gtk.Menu()
        for s in (3, 5, 10, 15, 30, 60, 120):
            it = Gtk.CheckMenuItem.new_with_label('%d 秒' % s)
            it.set_active(self.interval == s)
            it.connect('activate', lambda w, v=s: self._set_interval(v))
            int_sub.append(it)
        int_item.set_submenu(int_sub)
        menu.append(int_item)
        menu.append(ratio_item)
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

    def _choose_folder(self):
        dlg = Gtk.FileChooserDialog(
            title='选择图片文件夹', transient_for=self,
            action=Gtk.FileChooserAction.SELECT_FOLDER)
        dlg.add_button('取消', Gtk.ResponseType.CANCEL)
        dlg.add_button('选择', Gtk.ResponseType.OK)
        dlg.set_current_folder(self.folder)
        if dlg.run() == Gtk.ResponseType.OK:
            self.folder = dlg.get_filename()
            self.st['dir'] = self.folder
            self.idx = 0
            self.files = self._scan()
            self._load_img()
            self.queue_draw()
        dlg.destroy()

    def _toggle_slide(self, item):
        self.slide = item.get_active()
        self.st['slide'] = self.slide
        save_state(self.st)
        if self.slide:
            GLib.timeout_add_seconds(self.interval, self._slide_tick)

    def _set_interval(self, v):
        self.interval = v
        self.st['interval'] = v
        save_state(self.st)

    def _toggle_ratio(self, item):
        self.ratio = item.get_active()
        self.st['ratio'] = self.ratio
        save_state(self.st)
        if self.ratio:
            self._adapt_size()

    def _toggle_lock(self, item):
        self.locked = item.get_active()
        self.st['locked'] = self.locked
        save_state(self.st)

    # ---------- 事件 ----------
    def _nav_rects(self):
        """窗口左右边缘的《》按钮区域(符号本体贴边, 垂直居中于图片区)"""
        if not self.pix:
            return None
        sw, _ = self._measure('《', (_SYS_FONT + ' Bold 30'))
        sh = 34
        y = HEAD_H + (self.h - HEAD_H - sh) / 2
        y = max(HEAD_H + 2, min(self.h - sh - 2, y))
        return ((4, y, sw, sh), (self.w - sw - 4, y, sw, sh))

    def on_press(self, wdg, ev):
        if ev.button == 3:
            self.show_menu(ev)
            return True
        if ev.button == 1:
            if self.pix and HEAD_H <= ev.y < self.h - 4:
                nb = self._nav_rects()
                if nb and nb[0][0] <= ev.x <= nb[0][0] + nb[0][2] and nb[0][1] <= ev.y <= nb[0][1] + nb[0][3]:
                    self._next(-1)
                    return True
                if nb and nb[1][0] <= ev.x <= nb[1][0] + nb[1][2] and nb[1][1] <= ev.y <= nb[1][1] + nb[1][3]:
                    self._next(1)
                    return True
            # 角部缩放: 即使"锁定位置"也允许(锁定只锁移动)
            if (self.w - CORNER <= ev.x <= self.w and
                    self.h - CORNER <= ev.y <= self.h):
                self.resizing = True
                self.size_orig = (self.w, self.h, ev.x_root, ev.y_root)
                self._set_cursor('nwse-resize')
                return True
            if not self.locked:
                self.dragging = True
                self.drag_start = (ev.x_root, ev.y_root)
                self.drag_orig = (self.x if self.x is not None else 0,
                                  self.y if self.y is not None else 0)
                return True
        return False

    def _set_cursor(self, name=None):
        try:
            win = self.get_window()
            if win is None:
                return
            if name:
                cur = Gdk.Cursor.new_from_name(self.get_display(), name)
            else:
                cur = None
            win.set_cursor(cur)
        except Exception:
            pass

    def on_motion(self, wdg, ev):
        if not self.resizing:
            in_corner = (self.w - CORNER <= ev.x <= self.w and
                         self.h - CORNER <= ev.y <= self.h)
            if in_corner != getattr(self, '_corner_hover', False):
                self._corner_hover = in_corner
                self._set_cursor('nwse-resize' if in_corner else None)
                self.queue_draw()
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
        inside = (HEAD_H <= ev.y < self.h - 4)
        if inside != self.inside:
            self.inside = inside
            self.queue_draw()
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
        if self.inside:
            self.inside = False
            self.queue_draw()

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

    def _rounded(self, ctx, x, y, w, h, r):
        ctx.new_path()
        ctx.arc(x + r, y + r, r, 3.1416, 1.5 * 3.1416)
        ctx.arc(x + w - r, y + r, r, 1.5 * 3.1416, 2 * 3.1416)
        ctx.arc(x + w - r, y + h - r, r, 0, 0.5 * 3.1416)
        ctx.arc(x + r, y + h - r, r, 0.5 * 3.1416, 3.1416)
        ctx.close_path()

    def _nav_symbol(self, ctx, ch, x, y):
        layout = self.create_pango_layout(ch)
        layout.set_font_description(Pango.FontDescription.from_string((_SYS_FONT + ' Bold 30')))
        ctx.move_to(x, y)
        PangoCairo.layout_path(ctx, layout)
        ctx.set_line_width(2.5)
        ctx.set_source_rgba(0, 0, 0, 0.85)
        ctx.stroke_preserve()
        ctx.set_source_rgba(1, 1, 1, 0.95)
        ctx.fill()

    def on_draw(self, wdg, ctx):
        try:
            self._draw(ctx)
        except Exception:
            pass
        return False

    def _draw(self, ctx):
        w = self.w
        ctx.set_operator(cairo.OPERATOR_CLEAR)
        ctx.paint()
        ctx.set_operator(cairo.OPERATOR_OVER)

        # 顶部: 文件夹名
        name = os.path.basename(self.folder) if self.folder else '未选择文件夹'
        self._text(ctx, '文件夹: %s' % name, PAD, 8, w - 2 * PAD,
                   (_SYS_FONT + ' 13'), TEXT_DIM, 0.85)

        # 图片区
        area_y = HEAD_H
        area_h = self.h - HEAD_H
        if self.pix:
            pw, ph = self.pix.get_width(), self.pix.get_height()
            aw, ah = w - 2 * PAD, area_h - 4
            k = min(aw / pw, ah / ph)
            dw, dh = pw * k, ph * k
            dx = (w - dw) / 2
            dy = area_y + (area_h - dh) / 2
            ctx.save()
            ctx.translate(dx, dy)
            ctx.scale(k, k)
            Gdk.cairo_set_source_pixbuf(ctx, self.pix, 0, 0)
            ctx.get_source().set_filter(cairo.Filter.BILINEAR)
            ctx.paint()
            ctx.restore()
            # 《》按钮: 悬停时显示在图片左右(无背景, 白字黑描边, 深浅图都可见)
            if self.inside:
                nbs = self._nav_rects()
                if nbs:
                    for i, (bx, by, bw, bh) in enumerate(nbs):
                        self._nav_symbol(ctx, '《' if i == 0 else '》', bx, by)
        else:
            msg = '此文件夹没有图片' if self.files is not None else '请右键选择图片文件夹'
            self._text(ctx, msg, PAD, area_y + area_h / 2 - 10, w - 2 * PAD,
                       (_SYS_FONT + ' 14'), TEXT_DIM, 0.7)

        # 缩放柄
        if self.corner_hover:
            ctx.move_to(w - CORNER + 9, self.h - 6)
            ctx.line_to(w - 6, self.h - CORNER + 9)
            ctx.set_line_width(2)
            ctx.set_source_rgba(0.7, 0.7, 0.8, 0.5)
            ctx.stroke()


if __name__ == '__main__':
    check_single()
    FrameWidget()
    Gtk.main()
