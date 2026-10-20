#!/usr/bin/env python3
from widget_font import SYS_FONT as _SYS_FONT
# -*- coding: utf-8 -*-
"""桌面记事挂件 - 透明背景，可移动/缩放/锁定，按日期切换查看记事"""
import datetime
import json
import os
import sys

import gi
gi.require_version('Gtk', '3.0')
gi.require_version('Pango', '1.0')
from gi.repository import Gtk, Gdk, GLib, Pango, PangoCairo
import subprocess
import cairo

GLib.set_prgname('desktop-notes')

APP_DIR = os.path.dirname(os.path.abspath(__file__))
DATA_PATH = os.path.expanduser('~/.local/share/desktop-notes/notes.json')
STATE_PATH = os.path.expanduser('~/.config/desktop-notes/state.json')
PID_PATH = os.path.expanduser('~/.config/desktop-notes/pid')

def check_single():
    try:
        with open(PID_PATH) as f:
            old = int(f.read().strip())
        if os.path.exists('/proc/%d' % old):
            c = open('/proc/%d/cmdline' % old, 'rb').read().decode(errors='ignore')
            if 'desktop-notes' in c or 'notes.py' in c:
                sys.exit(0)
    except Exception:
        pass
    try:
        os.makedirs(os.path.dirname(PID_PATH), exist_ok=True)
        with open(PID_PATH, 'w') as f:
            f.write(str(os.getpid()))
    except Exception:
        pass

# ---------- 外观 ----------
NOTE_FONT = _SYS_FONT + ''      # 方正宋黑
YELLOW = (1.0, 0.85, 0.12)        # 内容: 黄色
WHITE = (0.95, 0.95, 0.98)         # 内容(交替): 白色
RED = (1.0, 0.30, 0.25)            # 日期: 红色
BLUE = (0.60, 0.80, 1.00)         # 时间戳: 蓝色
BLUE_DIM = (0.42, 0.60, 0.92)     # 按钮: 蓝色
HINT = (0.66, 0.70, 0.80)         # 提示/空日期
SHADOW = (0.05, 0.07, 0.10)

W = 580                            # 宽度(加宽)
MIN_H, MAX_H = 130, 540
PAD = 14
CORNER = 20                        # 缩放柄
BTN_W, BTN_H = 110, 32
BTN_GAP = 6
BTN_FONT = _SYS_FONT + ' Bold 14'
BTN_COLORS = {
    'prev': (0.90, 0.49, 0.20),
    'next': (0.20, 0.57, 0.86),
    'new': (0.18, 0.81, 0.44),
    'refresh': (0.30, 0.80, 0.95),
    'today': (0.85, 0.55, 0.95),
}
HEAD_H = 88                         # 顶部标题行高度（按钮行+日期行+间隔）
FOOT_H = 44                         # 底部按钮行高度


def load_state():
    st = {'x': None, 'y': None, 'w': W, 'h': MIN_H, 'locked': False,
          'date': datetime.date.today().isoformat()}
    try:
        with open(STATE_PATH) as f:
            st.update(json.load(f))
    except Exception:
        pass
    if int(st.get('w', W)) < W:
        st['w'] = W
    return st


def save_state(st):
    try:
        os.makedirs(os.path.dirname(STATE_PATH), exist_ok=True)
        with open(STATE_PATH, 'w') as f:
            json.dump(st, f, indent=2, ensure_ascii=False)
    except Exception:
        pass


def load_notes():
    try:
        with open(DATA_PATH) as f:
            return json.load(f)
    except Exception:
        return {}


def save_notes(notes):
    try:
        os.makedirs(os.path.dirname(DATA_PATH), exist_ok=True)
        with open(DATA_PATH, 'w') as f:
            json.dump(notes, f, indent=1, ensure_ascii=False)
    except Exception:
        pass


def d_add(day, n):
    d = datetime.date.fromisoformat(day)
    return (d + datetime.timedelta(days=n)).isoformat()


class NotesWidget(Gtk.Window):
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
        self.set_title('desktop-notes')
        self.set_icon_name('desktop-notes')
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
        self.w = int(self.st['w'])
        scr = self.get_screen()
        sh = scr.get_height()
        self.max_h = min(MAX_H, int(sh * 0.6))
        self.h = min(self.max_h, max(MIN_H, 460))
        self.x = self.st['x']
        self.y = self.st['y']
        if self.x is None:
            self.x = scr.get_width() - self.w
            self.y = 0
        self.cur_date = datetime.date.today().isoformat()
        self.notes = load_notes()
        self.hover = None
        self.corner_hover = False
        self.dragging = False
        self.drag_start = None
        self.drag_orig = None
        self.resizing = False
        self.size_orig = None
        self.scroll = 0
        self.expand = {}

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

        self.move(self.x, self.y)
        self._allow_shrink = False
        self.resize(self.w, self.h)
        self.show_all()
        GLib.timeout_add(800, self._all_desktops)
        GLib.timeout_add(5000, self._all_desktops)
        GLib.timeout_add(60000, self._day_watch)

    # 跨天自动切到今天(今天19日就显示19日, 明天20日自动变20日)
    def _day_watch(self):
        today = datetime.date.today().isoformat()
        if self.cur_date == d_add(today, -1):
            self.cur_date = today
            self._save()
            self._autofit()
            self.queue_draw()
        return True

    # ---------- 数据 ----------
    def _notes_of(self, day):
        return self.notes.get(day, [])

    def _save(self):
        save_notes(self.notes)
        self.st['date'] = self.cur_date
        self.st['w'], self.st['h'] = self.w, self.h
        save_state(self.st)

    # ---------- 高度自适应 ----------
    def _content_h(self):
        h = HEAD_H
        for i, nt in enumerate(self._notes_of(self.cur_date)):
            txt = nt.get('txt', '')
            _, _, _, disp = self._note_meta(i, txt)
            h += max(28, disp + 7)
        if not self._notes_of(self.cur_date):
            h += 40
        return h + FOOT_H

    def _text_h(self, txt, font, maxw):
        layout = self.create_pango_layout(txt)
        layout.set_font_description(Pango.FontDescription.from_string(font))
        layout.set_wrap(Pango.WrapMode.WORD_CHAR)
        layout.set_width(int(maxw * Pango.SCALE))
        _, h = layout.get_pixel_size()
        return h

    LINE_H = 17                       # 12 号单行行高基准
    FOLD_LINES = 2                    # 折叠时显示行数

    def _fs(self):
        return int(self.st.get('font_size', 14))

    def _note_meta(self, i, txt):
        fs = self._fs()
        maxw = self.w - 2 * PAD - 78
        line_h = int(self.LINE_H * fs / 12.0)
        full = self._text_h(txt, NOTE_FONT + ' %d' % fs, maxw)
        is_long = full > self.FOLD_LINES * line_h + 6
        open_ = self.expand.get((self.cur_date, i), False)
        disp = full if (open_ or not is_long) else self.FOLD_LINES * line_h
        return full, is_long, open_, disp

    def _note_color(self, i):
        scheme = self.st.get('scheme', 'mixed')
        if scheme == 'yellow':
            return YELLOW
        if scheme == 'white':
            return WHITE
        return YELLOW if i % 2 == 0 else WHITE

    def _autofit(self, force=False):
        need = max(MIN_H, min(self.max_h, self._content_h()))
        if force or self.h < need:
            self.h = need
            self.st['h'] = need
            self.resize(self.w, need)

    # ---------- 按钮 ----------
    def _btn_rects(self):
        y = 4
        keys = ('prev', 'next', 'today', 'new', 'refresh')
        bw = (self.w - 2 * PAD - (len(keys) - 1) * BTN_GAP) / float(len(keys))
        x = PAD
        return {k: (x + i * (bw + BTN_GAP), y, bw, BTN_H)
                for i, k in enumerate(keys)}
    def _hit_btn(self, x, y):
        for k, (bx, by, bw, bh) in self._btn_rects().items():
            if bx <= x <= bx + bw and by <= y <= by + bh:
                return k
        return None

    # ---------- 事件 ----------
    def on_press(self, wdg, ev):
        if ev.button == 3:
            self._menu_at(ev)
            return True
        btn = self._hit_btn(ev.x, ev.y)
        if btn:
            self._activate(btn)
            return True
        if ev.button == 1:
            idx = self._hit_note(ev.y)
            if idx is not None:
                txt = self._notes_of(self.cur_date)[idx].get('txt', '')
                full, is_long, open_, _ = self._note_meta(idx, txt)
                if is_long:
                    self._toggle_note(idx)
                    return True
        if self.st['locked']:
            return False
        if (self.w - CORNER <= ev.x <= self.w and
                self.h - CORNER <= ev.y <= self.h):
            self.resizing = True
            self.size_orig = (self.w, self.h, ev.x_root, ev.y_root)
            return True
        self.dragging = True
        self.drag_start = (ev.x_root, ev.y_root)
        self.drag_orig = (self.x or 0, self.y or 0)
        return True

    def on_motion(self, wdg, ev):
        if self.resizing:
            nw = max(self.w, self.size_orig[0] + ev.x_root - self.size_orig[2])
            nh = max(MIN_H, self.size_orig[1] + ev.y_root - self.size_orig[3])
            nw = min(nw, 1200)
            nh = min(nh, self.max_h)
            self.resize(nw, nh)
            self.w, self.h = nw, nh
            self._save()
            self.queue_draw()
            return
        if self.dragging and not self.st['locked']:
            dx = ev.x_root - self.drag_start[0]
            dy = ev.y_root - self.drag_start[1]
            self.move(self.drag_orig[0] + dx, self.drag_orig[1] + dy)
            return
        old = self.hover
        self.hover = self._hit_btn(ev.x, ev.y)
        ch = (not self.st['locked'] and
              self.w - CORNER <= ev.x <= self.w and self.h - CORNER <= ev.y <= self.h)
        if ch != self.corner_hover or old != self.hover:
            self.corner_hover = ch
            self.queue_draw()

    def on_release(self, wdg, ev):
        if self.dragging:
            self.x, self.y = self.get_position()
            self.st['x'], self.st['y'] = self.x, self.y
            self._save()
        if self.resizing:
            self.st['w'], self.st['h'] = self.w, self.h
            self._allow_shrink = False
            self._save()
        self.dragging = False
        self.resizing = False

    def on_leave(self, wdg, ev):
        self.hover = None
        self.corner_hover = False
        self.queue_draw()

    def on_scroll(self, wdg, ev):
        ct = getattr(self, '_content_end', HEAD_H) - HEAD_H
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

    def _menu_at(self, ev):
        idx = self._hit_note(ev.y)
        items = self._notes_of(self.cur_date)
        fs = self._fs()
        menu = Gtk.Menu()

        def add(label, cb):
            it = Gtk.MenuItem(label=label)
            it.connect('activate', cb)
            menu.append(it)

        if idx is not None and 0 <= idx < len(items):
            txt = items[idx].get('txt', '')
            full, is_long, open_, _ = self._note_meta(idx, txt)
            if is_long:
                add('收起' if open_ else '展开这条记事',
                    lambda w: self._toggle_note(idx))
            add('编辑这条记事', lambda w: self._edit_note(idx))
            add('删除这条记事', lambda w: self._del_note(idx))
            menu.append(Gtk.SeparatorMenuItem())

        add('新建记事', lambda w: self._new_note())
        if items:
            add('清空今日记事', lambda w: self._clear_today())
        menu.append(Gtk.SeparatorMenuItem())

        color_item = Gtk.MenuItem(label='配色')
        color_sub = Gtk.Menu()
        for label, key in (('黄白交替', 'mixed'), ('全黄', 'yellow'), ('全白', 'white')):
            it = Gtk.CheckMenuItem(label=label)
            it.set_active(self.st.get('scheme', 'mixed') == key)
            it.connect('activate', lambda w, k=key: self._set_scheme(k))
            color_sub.append(it)
        color_item.set_submenu(color_sub)
        menu.append(color_item)

        font_item = Gtk.MenuItem(label='字号')
        font_sub = Gtk.Menu()
        for sz in (10, 11, 12, 13, 14, 16, 18):
            it = Gtk.CheckMenuItem(label='%d 号' % sz)
            it.set_active(fs == sz)
            it.connect('activate', lambda w, s=sz: self._set_font(s))
            font_sub.append(it)
        font_item.set_submenu(font_sub)
        menu.append(font_item)

        menu.append(Gtk.SeparatorMenuItem())
        lock_item = Gtk.CheckMenuItem(label='锁定位置')
        lock_item.set_active(bool(self.st['locked']))
        lock_item.connect('activate', self._on_lock_toggle)
        menu.append(lock_item)
        menu.append(Gtk.SeparatorMenuItem())
        
        autostart_item = Gtk.CheckMenuItem(label='开机自启')
        autostart_item.set_active(self._autostart_enabled())
        autostart_item.connect('toggled', lambda w: self._toggle_autostart(w.get_active()))
        menu.append(autostart_item)

        menu.append(Gtk.SeparatorMenuItem())
        add('退出', lambda w: Gtk.main_quit())

        menu.show_all()
        menu.popup(None, None, None, None, ev.button, ev.time)

    def _toggle_note(self, idx):
        txt = self._notes_of(self.cur_date)[idx].get('txt', '')
        full, is_long, open_, _ = self._note_meta(idx, txt)
        if not is_long:
            return
        self.expand[(self.cur_date, idx)] = not open_
        self.scroll = 0
        self._autofit(force=True)
        self.queue_draw()

    def _clear_today(self):
        dlg = Gtk.MessageDialog(
            transient_for=self, flags=0,
            message_type=Gtk.MessageType.WARNING,
            buttons=Gtk.ButtonsType.OK_CANCEL,
            text='清空 %s 的全部记事？此操作不可恢复。' % self.cur_date)
        resp = dlg.run()
        dlg.destroy()
        if resp == Gtk.ResponseType.OK:
            if self.cur_date in self.notes:
                del self.notes[self.cur_date]
            self._save()
            self._autofit(force=True)
            self.queue_draw()

    def _on_lock_toggle(self, item):
        self.st['locked'] = item.get_active()
        self._save()
        self.queue_draw()

    def _set_scheme(self, key):
        self.st['scheme'] = key
        self._save()
        self._autofit(force=True)
        self.queue_draw()

    def _set_font(self, sz):
        self.st['font_size'] = sz
        self._save()
        self._autofit(force=True)
        self.queue_draw()

    def _hit_note(self, y):
        y0 = HEAD_H - self.scroll
        for i, nt in enumerate(self._notes_of(self.cur_date)):
            _, _, _, disp = self._note_meta(i, nt.get('txt', ''))
            lh = max(28, disp + 7)
            if y0 <= y < y0 + lh:
                return i
            y0 += lh
        return None

    def _activate(self, btn):
        if btn == 'prev':
            self.cur_date = d_add(self.cur_date, -1)
        elif btn == 'next':
            self.cur_date = d_add(self.cur_date, 1)
        elif btn == 'new':
            self._new_note()
        elif btn == 'today':
            self.cur_date = datetime.date.today().isoformat()
        elif btn == 'refresh':
            self._reload()
        self.scroll = 0
        self._save()
        self._autofit()
        self.queue_draw()

    def _reload(self):
        """重新加载记事数据文件(外部修改后立即刷新)"""
        self.notes = load_notes()
        self.expand = {k: v for k, v in self.expand.items() if k[0] == self.cur_date}

    def _style_tv(self, tv, frame):
        css = b'''
        textview {
            background-color: rgba(0, 0, 0, 0);
            color: #FFFFFF;
        }
        textview text {
            background-color: rgba(0, 0, 0, 0);
            color: #FFFFFF;
        }
        frame {
            background-color: rgba(0, 0, 0, 0);
            border: 1px solid rgba(255,255,255,0.6);
            border-radius: 6px;
        }
        button {
            background-color: rgba(0,0,0,0);
            background-image: none;
            border: none;
            box-shadow: none;
            color: #FFFFFF;
        }
        button:hover { color: #B0BEC5; }
        '''
        prov = Gtk.CssProvider()
        prov.load_from_data(css)
        for w in (tv, frame):
            w.get_style_context().add_provider(
                prov, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)

    def _dlg_transparent(self, widget, cr):
        cr.set_operator(cairo.OPERATOR_SOURCE)
        cr.set_source_rgba(0, 0, 0, 0)
        cr.paint()
        return False

    def _make_note_dlg(self, title, prefill=''):
        """倒计时同款对话框: 透明窗口 + 全透明输入框 + 细线边框 + 无背景按钮"""
        dlg = Gtk.Dialog(title=title, transient_for=self)
        dlg.set_decorated(False)
        dlg.set_type_hint(Gdk.WindowTypeHint.DIALOG)
        visual = self.get_screen().get_rgba_visual()
        if visual is not None:
            dlg.set_visual(visual)
        dlg.set_app_paintable(True)
        dlg.set_default_size(self.w, -1)
        box = dlg.get_content_area()
        box.set_spacing(8)
        box.set_margin_start(14)
        box.set_margin_end(14)
        box.set_margin_top(12)
        box.set_margin_bottom(12)
        frame = Gtk.Frame()
        tv = Gtk.TextView()
        tv.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
        tv.set_size_request(self.w - 20, 240)
        tv.set_editable(True)
        tv.set_can_focus(True)
        if prefill:
            tv.get_buffer().set_text(prefill)
        frame.add(tv)
        box.pack_start(frame, True, True, 0)
        b1 = dlg.add_button('取消', Gtk.ResponseType.CANCEL)
        b2 = dlg.add_button('保存', Gtk.ResponseType.OK)
        self._style_tv(tv, frame)
        bprov = Gtk.CssProvider()
        bprov.load_from_data(b'''
        button {
            background-color: rgba(0,0,0,0);
            background-image: none;
            border: none;
            box-shadow: none;
            color: #FFFFFF;
        }
        button:hover { color: #B0BEC5; }
        ''')
        for b in (b1, b2):
            b.get_style_context().add_provider(
                bprov, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
        dlg.connect('draw', self._dlg_transparent)
        box.show_all()
        tv.grab_focus()
        return dlg, tv

    def _place_dlg(self, dlg):
        px = self.x if self.x is not None else 200
        py = self.y if self.y is not None else 200
        scr_w = self.get_screen().get_width()
        scr_h = self.get_screen().get_height()
        px = max(0, min(px, scr_w - 560))
        ty = py + self.h + 10
        if ty + 300 > scr_h:
            ty = max(0, scr_h - 300)
        dlg.move(px, ty)

    def _edit_note(self, idx):
        d = datetime.date.fromisoformat(self.cur_date)
        lst = self.notes.get(self.cur_date, [])
        if not (0 <= idx < len(lst)):
            return
        self._autofit(force=True)
        dlg, tv = self._make_note_dlg(
            '编辑记事 (%d.%02d.%02d)' % (d.year, d.month, d.day),
            lst[idx].get('txt', ''))
        self._place_dlg(dlg)
        while True:
            resp = dlg.run()
            if resp != Gtk.ResponseType.OK:
                break
            buf = tv.get_buffer()
            txt = buf.get_text(buf.get_start_iter(), buf.get_end_iter(), True).strip()
            if txt:
                lst[idx]['txt'] = txt
                self._save()
                self._autofit()
                self.queue_draw()
                break
        dlg.destroy()

    def _del_note(self, idx):
        day = self.cur_date
        lst = self.notes.get(day, [])
        if 0 <= idx < len(lst):
            del lst[idx]
            self._save()
            self._autofit()
            self.queue_draw()

    def _new_note(self):
        d = datetime.date.fromisoformat(self.cur_date)
        self._autofit(force=True)
        dlg, tv = self._make_note_dlg(
            '新建记事 (%d.%02d.%02d)' % (d.year, d.month, d.day))
        self._place_dlg(dlg)
        while True:
            resp = dlg.run()
            if resp != Gtk.ResponseType.OK:
                break
            buf = tv.get_buffer()
            txt = buf.get_text(buf.get_start_iter(), buf.get_end_iter(), True).strip()
            if txt:
                now = datetime.datetime.now().strftime('%H:%M:%S')
                self.notes.setdefault(self.cur_date, []).append({'t': now, 'txt': txt})
                self._save()
                self._autofit()
                self.queue_draw()
                break
        dlg.destroy()

    def _snap_dlg(self, dlg, px, ty):
        ax, ay = dlg.get_position()
        if (ax, ay) != (0, 0) and (ax, ay) != (px, ty):
            dlg.move(px + px - ax, ty + ty - ay)

    # ---------- 绘制 ----------
    def _rounded(self, ctx, x, y, w, h, r):
        ctx.new_path()
        ctx.arc(x + r, y + r, r, 3.1416, 1.5 * 3.1416)
        ctx.arc(x + w - r, y + r, r, 1.5 * 3.1416, 2 * 3.1416)
        ctx.arc(x + w - r, y + h - r, r, 0, 0.5 * 3.1416)
        ctx.arc(x + r, y + h - r, r, 0.5 * 3.1416, 3.1416)
        ctx.close_path()

    def _text(self, ctx, txt, x, y, maxw, font, color, alpha=1.0, wrap=False):
        layout = self.create_pango_layout(txt)
        layout.set_font_description(Pango.FontDescription.from_string(font))
        if wrap:
            layout.set_wrap(Pango.WrapMode.WORD_CHAR)
            layout.set_width(int(maxw * Pango.SCALE))
        else:
            layout.set_ellipsize(Pango.EllipsizeMode.END)
            layout.set_width(int(maxw * Pango.SCALE))
        ctx.set_source_rgba(*SHADOW, alpha * 0.5)
        ctx.move_to(x + 1, y + 1)
        PangoCairo.show_layout(ctx, layout)
        ctx.set_source_rgba(color[0], color[1], color[2], alpha)
        ctx.move_to(x, y)
        PangoCairo.show_layout(ctx, layout)
        return layout.get_pixel_size()[1]

    def _btn(self, ctx, label, rect, key):
        bx, by, bw, bh = rect
        r, g, b = BTN_COLORS[key]
        if self.hover == key:
            r, g, b = min(1.0, r * 1.35), min(1.0, g * 1.35), min(1.0, b * 1.35)
        layout = self.create_pango_layout(label)
        layout.set_font_description(Pango.FontDescription.from_string(BTN_FONT))
        lw, lh = layout.get_pixel_size()
        self._text(ctx, label, bx + (bw - lw) / 2, by + (bh - lh) / 2 + 1,
                   bw, BTN_FONT, (r, g, b))

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

        # 顶部: 日期
        d = datetime.date.fromisoformat(self.cur_date)
        week = '一二三四五六日'[d.weekday()]
        n = len(self._notes_of(self.cur_date))
        date_txt = '%d.%02d.%02d 周%s · %d 条' % (d.year, d.month, d.day, week, n)
        self._text(ctx, date_txt, PAD, 44, w - 2 * PAD, NOTE_FONT + ' 14', WHITE)

        # 顶部按钮
        for k, rect in self._btn_rects().items():
            label = {'prev': '上一个', 'next': '下一个', 'today': '今天',
                     'new': '添加', 'refresh': '刷新'}[k]
            self._btn(ctx, label, rect, k)

        # 内容区（滚动裁剪）
        ctx.save()
        ctx.rectangle(0, HEAD_H, w, self.h - HEAD_H - FOOT_H)
        ctx.clip()
        y = HEAD_H - self.scroll
        rows = self._notes_of(self.cur_date)
        if not rows:
            self._text(ctx, '这一天没有记事 · 点 ＋ 添加', PAD, y + 6,
                       w - 2 * PAD, NOTE_FONT + ' 14', HINT, alpha=0.8)
            y += 44
        else:
            tw = 72
            fs = self._fs()
            fn = NOTE_FONT + ' %d' % fs
            per_char = 13.5 * fs / 12.0
            for i, nt in enumerate(rows):
                if not hasattr(self, '_dbg2'):
                    pass
                t = nt.get('t', '')[:5]
                txt = nt.get('txt', '')
                color = self._note_color(i)
                full, is_long, open_, disp = self._note_meta(i, txt)
                if is_long and not open_:
                    maxw = w - 2 * PAD - tw - 6
                    per_line = max(2, int(maxw / per_char))
                    cut = txt[:per_line * self.FOLD_LINES] + '…'
                    while self._text_h(cut, fn, maxw) > disp and len(cut) > 8:
                        cut = cut[:-2] + '…'
                    self._text(ctx, cut, PAD + tw + 6, y, maxw,
                               fn, color, wrap=True)
                    self._text(ctx, '▾ 展开', w - PAD - 52, y + disp - 14, 52,
                               NOTE_FONT + ' 11', HINT, alpha=0.8)
                else:
                    self._text(ctx, txt, PAD + tw + 6, y, w - 2 * PAD - tw - 6,
                               fn, color, wrap=True)
                    if is_long:
                        self._text(ctx, '▴ 收起', w - PAD - 52, y + disp - 14, 52,
                                   NOTE_FONT + ' 11', HINT, alpha=0.8)
                self._text(ctx, t, PAD, y, tw, fn, BLUE)
                y += max(28, disp + 7)
        ctx.restore()
        self._content_end = y + self.scroll

        # 滚动条（细轨道 + 活动块）
        ct = self._content_end - HEAD_H
        vh = self.h - HEAD_H - FOOT_H
        if ct > vh:
            tr_y, tr_h = HEAD_H + 2, vh - 4
            self._rounded(ctx, self.w - 12, tr_y, 5, tr_h, 2)
            ctx.set_source_rgba(0.7, 0.7, 0.8, 0.30)
            ctx.fill()
            frac = min(1.0, (self.scroll + vh) / max(1, ct))
            bar_h = max(22, int(tr_h * vh / max(1, ct)))
            self._rounded(ctx, self.w - 12, tr_y + (tr_h - bar_h) * frac, 5, bar_h, 2)
            ctx.set_source_rgba(0.7, 0.7, 0.8, 0.65)
            ctx.fill()

        # 底部提示 + 缩放柄
        if not self.st['locked']:
            self._text(ctx, '右键菜单 · 拖拽移动 · 右下角缩放', PAD, self.h - 26,
                       w - 2 * PAD - CORNER, NOTE_FONT + ' 11', HINT, alpha=0.55)
        if self.corner_hover:
            ctx.move_to(w - CORNER + 5, self.h - 6)
            ctx.line_to(w - 6, self.h - CORNER + 5)
            ctx.set_line_width(2)
            ctx.set_source_rgba(0.7, 0.7, 0.8, 0.5)
            ctx.stroke()

        # 高度自适应: 内容超出则增高; 首次帧精确贴合
        need = max(MIN_H, min(self.max_h, self._content_end + FOOT_H))
        if self._allow_shrink or self.h < need:
            if self.h != need:
                self.h = need
                self.st['h'] = need
                self.resize(self.w, need)


if __name__ == '__main__':
    check_single()
    NotesWidget()
    Gtk.main()
