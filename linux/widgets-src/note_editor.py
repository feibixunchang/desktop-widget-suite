#!/usr/bin/env python3
from widget_font import SYS_FONT as _SYS_FONT
# -*- coding: utf-8 -*-
"""万年历记事编辑器(独立进程)
做成独立应用窗口的原因: 桌面挂件窗口(GTK 弹出层/无任务栏)经常被输入法
判定为"非输入窗口", 表现为只能打英文、切输入法也没用。独立进程 + 普通窗口
后, 输入法与其它应用一致。
用法: note_editor.py --date 2026-09-24 [--x 400 --y 300]
"""
import argparse
import datetime
import json
import os
import sys

import gi
gi.require_version('Gtk', '3.0')
gi.require_version('Pango', '1.0')
from gi.repository import Gtk, Gdk, GLib, Pango
import cairo

APP = 'deepin-note-editor'
USER_DIR = os.path.expanduser('~/.local/share/desktop-widget/')
NOTES_PATH = os.path.join(USER_DIR, 'calendar_notes.json')
CFG_DIR = os.path.expanduser('~/.config/desktop-calendar')
PID_PATH = os.path.join(CFG_DIR, 'editor.pid')
REQ_PATH = os.path.join(CFG_DIR, 'editor.req')
LOG_PATH = os.path.join(CFG_DIR, 'editor.log')
FONT = _SYS_FONT + ''
NOTE_PRESETS = ['#FFFFFF', '#FFEB3B', '#F44336', '#2196F3',
                '#4CAF50', '#FF9800', '#9C27B0', '#FF69B4']
DEFAULT_NOTE_COLOR = '#FFEB3B'


def log(msg):
    try:
        os.makedirs(CFG_DIR, exist_ok=True)
        with open(LOG_PATH, 'a') as f:
            f.write('%s %s\n' % (datetime.datetime.now().strftime('%m-%d %H:%M:%S'),
                                 msg))
    except Exception:
        pass


def esc(s):
    return str(s).replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')


def load_notes():
    try:
        with open(NOTES_PATH) as f:
            return json.load(f)
    except Exception:
        return {}


def save_notes(notes):
    try:
        os.makedirs(USER_DIR, exist_ok=True)
        with open(NOTES_PATH, 'w') as f:
            json.dump(notes, f, ensure_ascii=False, indent=2)
    except Exception as e:
        log('save: ' + str(e))


def get_notes_list(notes, key):
    v = notes.get(key)
    if isinstance(v, list):
        return v
    if isinstance(v, dict):
        return [v]
    if v:
        return [{'t': v}]
    return []


def note_color(notes, key):
    lst = get_notes_list(notes, key)
    if lst and lst[0].get('c'):
        return lst[0]['c']
    return DEFAULT_NOTE_COLOR


class NoteEditor(Gtk.Window):
    def __init__(self, key, px=None, py=None):
        super().__init__(type=Gtk.WindowType.TOPLEVEL)
        self.set_title('%s 记事' % key)
        self.set_decorated(False)
        self.set_resizable(False)
        try:
            self.set_wmclass(APP, 'DeepinNoteEditor')
        except Exception:
            pass
        self.set_skip_taskbar_hint(False)
        self.set_skip_pager_hint(False)
        self.set_keep_above(True)
        self.set_focus_on_map(True)
        self.set_accept_focus(True)
        self.set_app_paintable(True)
        vis = self.get_screen().get_rgba_visual()
        if vis is not None:
            self.set_visual(vis)
        self.key = key
        self.notes = load_notes()
        self.rows = get_notes_list(self.notes, key)
        self.color = None
        self.expanded = set()
        self.edit_idx = None
        self._drag = None
        self._req_mtime = 0
        self.connect('draw', self.on_draw)
        self.connect('key-press-event', self.on_key)
        self.connect('destroy', Gtk.main_quit)
        self.add_events(Gdk.EventMask.BUTTON_PRESS_MASK |
                        Gdk.EventMask.BUTTON_RELEASE_MASK |
                        Gdk.EventMask.POINTER_MOTION_MASK |
                        Gdk.EventMask.KEY_PRESS_MASK)
        self._build()
        self._place(px, py)
        self.show_all()
        try:
            ph = self.get_preferred_height()[1]
            self.resize(560, int(min(self.get_screen().get_height() - 160,
                                     max(340, ph + 16))))
            self._place(px, py)
        except Exception:
            pass
        self._activate()
        GLib.timeout_add(400, self._watch_req)

    # ── 构建界面 ──
    def _build(self):
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
        box.set_margin_start(14)
        box.set_margin_end(14)
        box.set_margin_top(12)
        box.set_margin_bottom(12)
        self.add(box)
        box.add_events(Gdk.EventMask.BUTTON_PRESS_MASK |
                       Gdk.EventMask.BUTTON_RELEASE_MASK |
                       Gdk.EventMask.POINTER_MOTION_MASK)
        box.drag_state = None
        box.connect('button-press-event', self.drag_start)
        box.connect('motion-notify-event', self.drag_move)
        box.connect('button-release-event', self.drag_end)

        self.head = Gtk.Label()
        self.head.set_halign(Gtk.Align.START)
        self.head.set_markup(self._head_markup())
        box.pack_start(self.head, False, False, 0)
        self.head.add_events(Gdk.EventMask.BUTTON_PRESS_MASK |
                             Gdk.EventMask.BUTTON_RELEASE_MASK |
                             Gdk.EventMask.POINTER_MOTION_MASK)
        self.head.drag_state = None
        self.head.connect('button-press-event', self.drag_start)
        self.head.connect('motion-notify-event', self.drag_move)
        self.head.connect('button-release-event', self.drag_end)

        self.entry = Gtk.Entry()
        self.entry.set_placeholder_text('在这里写记事（打完拼音按空格上屏），回车保存')
        self.entry.modify_font(Pango.FontDescription('%s 13' % FONT))
        self.entry.connect('activate', lambda w: self.quick_add())
        # 直接覆盖颜色: 保证深底白字(不依赖 CSS 作用域)
        self.entry.override_background_color(
            Gtk.StateFlags.NORMAL, Gdk.RGBA(0.07, 0.08, 0.11, 0.96))
        self.entry.override_background_color(
            Gtk.StateFlags.FOCUSED, Gdk.RGBA(0.07, 0.08, 0.11, 0.96))
        self.entry.override_color(Gtk.StateFlags.NORMAL, Gdk.RGBA(1, 1, 1, 1))
        self.entry.override_color(Gtk.StateFlags.FOCUSED, Gdk.RGBA(1, 1, 1, 1))
        self.entry.override_cursor(Gdk.RGBA(1, 1, 1, 1), Gdk.RGBA(1, 1, 1, 1))
        if os.environ.get('NOTE_DEBUG_TEXT'):
            self.entry.set_text(os.environ['NOTE_DEBUG_TEXT'])
        box.pack_start(self.entry, False, False, 0)

        tip = Gtk.Label()
        tip.set_markup('<span foreground="#9AA4B8" font="%s 10">'
                       '中文输入：点这里 → 打拼音 → 按空格上屏 → 回车保存</span>' % FONT)
        tip.set_halign(Gtk.Align.START)
        box.pack_start(tip, False, False, 0)

        sc = Gtk.ScrolledWindow()
        sc.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
        sc.set_min_content_height(220)
        self.listbox = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
        sc.add(self.listbox)
        box.pack_start(sc, True, True, 0)

        cbox = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6)
        cl = Gtk.Label(label='新记事颜色:')
        cl.modify_font(Pango.FontDescription('%s 12' % FONT))
        cbox.pack_start(cl, False, False, 0)
        self.cbtns = {}
        for pc in NOTE_PRESETS:
            tb = Gtk.ToggleButton()
            lb = Gtk.Label()
            lb.set_markup('<span foreground="%s" font="%s">●</span>'
                          % (pc, _SYS_FONT + ' 14'))
            tb.add(lb)
            tb.connect('toggled', self.on_color, pc)
            cbox.pack_start(tb, False, False, 0)
            self.cbtns[pc] = tb
        box.pack_start(cbox, False, False, 0)

        hb = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        hb.set_halign(Gtk.Align.END)
        self.add_btn = Gtk.Button(label='＋ 添加')
        self.add_btn.connect('clicked', lambda w: self.quick_add())
        hb.pack_start(self.add_btn, False, False, 0)
        close = Gtk.Button(label='关闭(已自动保存)')
        close.connect('clicked', lambda w: self.close_editor())
        hb.pack_start(close, False, False, 0)
        box.pack_start(hb, False, False, 0)

        css = b'''
        window { background: transparent; }
        label { color: #FFFFFF; }
        button { color: #FFFFFF; background-image: none;
                 background-color: rgba(255,255,255,0.10); border-radius: 6px;
                 padding: 3px 10px; border: none; }
        button:hover { background-color: rgba(90,160,255,0.35); }
        entry, entry:focus, entry:selected, entry:selected:focus {
            background-color: rgba(0,0,0,0.62);
            background-image: none;
            color: #FFFFFF;
            border: 1px solid rgba(255,255,255,0.5);
            border-radius: 6px;
            padding: 4px 8px;
        }
        entry text, entry:selected text, entry:focus text {
            background-color: rgba(0,0,0,0.62);
            background-image: none;
            color: #FFFFFF;
        }
        entry selection { background-color: rgba(64,140,255,0.45); color: #FFFFFF; }
        box.noterow { background-color: rgba(255,255,255,0.055);
                      border-radius: 6px; padding: 2px 6px; }
        '''
        prov = Gtk.CssProvider()
        prov.load_from_data(css)
        # 注意: 必须加到 screen, 加到窗口的 style context 对子控件不生效
        Gtk.StyleContext.add_provider_for_screen(
            self.get_screen(), prov, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
        for pc, tb in self.cbtns.items():
            tb.set_active(pc == note_color(self.notes, self.key))
        self.render_rows()

    def _head_markup(self):
        try:
            d = datetime.date.fromisoformat(self.key)
            wd = '一二三四五六日'[d.weekday()]
            return ('<span foreground="#FFEB3B" font="%s 16">记事</span>'
                    '<span foreground="#FFFFFF" font="%s 15">  %d年%d月%d日 '
                    '星期%s</span>' % (FONT, FONT, d.year, d.month, d.day, wd))
        except Exception:
            return ('<span foreground="#FFFFFF" font="%s 15">%s</span>'
                    % (FONT, self.key))

    def render_rows(self):
        for ch in self.listbox.get_children():
            self.listbox.remove(ch)
        if not self.rows:
            lb = Gtk.Label()
            lb.set_markup('<span foreground="#9AA4B8" font="%s 12">这一天还没有记事</span>'
                          % FONT)
            lb.set_halign(Gtk.Align.START)
            self.listbox.pack_start(lb, False, False, 0)
            self.listbox.show_all()
            return
        for idx, nt in enumerate(self.rows):
            row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            row.set_margin_top(2)
            row.set_margin_bottom(2)
            row.get_style_context().add_class('noterow')
            col = nt.get('c') or DEFAULT_NOTE_COLOR
            tm = Gtk.Label()
            tm.set_markup('<span foreground="%s" font="%s 11">%s</span>'
                          % (col, FONT, esc(nt.get('time') or '--:--')))
            row.pack_start(tm, False, False, 0)
            raw = str(nt.get('t', ''))
            exp = idx in self.expanded
            show = raw if (len(raw) <= 60 or exp) else \
                raw[:60] + ' …(' + str(len(raw)) + '字)'
            body = Gtk.Label()
            body.set_markup('<span foreground="%s" font="%s 13">%s</span>'
                            % (col, FONT, esc(show)))
            body.set_line_wrap(True)
            body.set_xalign(0.0)
            body.set_halign(Gtk.Align.START)
            row.pack_start(body, True, True, 0)
            if len(raw) > 60:
                fb = Gtk.Button(label=('收起' if exp else '展开'))
                fb.connect('clicked', lambda w, i=idx: self.toggle_fold(i))
                row.pack_start(fb, False, False, 0)
            eb = Gtk.Button(label='编辑')
            eb.connect('clicked', lambda w, i=idx: self.start_edit(i))
            row.pack_start(eb, False, False, 0)
            xb = Gtk.Button(label='✕')
            xb.connect('clicked', lambda w, i=idx: self.delete_row(i))
            row.pack_start(xb, False, False, 0)
            self.listbox.pack_start(row, False, False, 0)
        self.listbox.show_all()

    # ── 逻辑 ──
    def persist(self):
        rows = []
        for nt in self.rows:
            t = str(nt.get('t', '')).strip()
            if t:
                rows.append({'t': t,
                             'time': nt.get('time') or
                             datetime.datetime.now().strftime('%H:%M'),
                             'c': nt.get('c', '')})
        if rows:
            self.notes[self.key] = rows
        else:
            self.notes.pop(self.key, None)
        save_notes(self.notes)

    def quick_add(self):
        txt = self.entry.get_text().strip()
        if not txt:
            return
        if self.edit_idx is not None and 0 <= self.edit_idx < len(self.rows):
            self.rows[self.edit_idx]['t'] = txt
            if self.color:
                self.rows[self.edit_idx]['c'] = self.color
            self.edit_idx = None
            self.add_btn.set_label('＋ 添加')
        else:
            self.rows.append({'t': txt,
                              'time': datetime.datetime.now().strftime('%H:%M:%S'),
                              'c': self.color or ''})
        self.entry.set_text('')
        self.render_rows()
        self.persist()

    def start_edit(self, idx):
        if 0 <= idx < len(self.rows):
            self.edit_idx = idx
            self.entry.set_text(str(self.rows[idx].get('t', '')))
            self.entry.grab_focus()
            self.add_btn.set_label('保存修改')

    def delete_row(self, idx):
        if 0 <= idx < len(self.rows):
            del self.rows[idx]
            self.edit_idx = None
            self.render_rows()
            self.persist()

    def toggle_fold(self, idx):
        if idx in self.expanded:
            self.expanded.discard(idx)
        else:
            self.expanded.add(idx)
        self.render_rows()

    def on_color(self, btn, pc):
        self.color = pc if btn.get_active() else None

    def close_editor(self):
        try:
            os.remove(PID_PATH)
        except Exception:
            pass
        Gtk.main_quit()

    def on_key(self, w, ev):
        if ev.keyval == Gdk.KEY_Escape:
            self.close_editor()
            return True
        return False

    # ── 拖动 ──
    def drag_start(self, w, ev):
        if ev.button == 1:
            w.drag_state = (ev.x_root, ev.y_root, self.get_position()[0],
                            self.get_position()[1])
            w.dragging = True
        return False

    def drag_move(self, w, ev):
        st = getattr(w, 'drag_state', None)
        if st and getattr(w, 'dragging', False):
            self.move(int(st[2] + ev.x_root - st[0]),
                      int(st[3] + ev.y_root - st[1]))
        return False

    def drag_end(self, w, ev):
        w.drag_state = None
        w.dragging = False
        return False

    # ── 背景/定位/激活 ──
    def on_draw(self, widget, cr):
        try:
            cr.set_operator(cairo.OPERATOR_CLEAR)
            cr.paint()
            cr.set_operator(cairo.OPERATOR_OVER)
            W = widget.get_allocated_width()
            H = widget.get_allocated_height()
            r = 12.0
            cr.new_path()
            cr.arc(r, r, r, 3.1416, 1.5 * 3.1416)
            cr.arc(W - r, r, r, 1.5 * 3.1416, 2 * 3.1416)
            cr.arc(W - r, H - r, r, 0, 0.5 * 3.1416)
            cr.arc(r, H - r, r, 0.5 * 3.1416, 3.1416)
            cr.close_path()
            cr.set_source_rgba(0.06, 0.07, 0.10, 0.96)
            cr.fill_preserve()
            cr.set_source_rgba(1, 1, 1, 0.16)
            cr.set_line_width(1)
            cr.stroke()
        except Exception:
            pass
        return False

    def _place(self, px=None, py=None):
        scr = self.get_screen()
        sw, sh = scr.get_width(), scr.get_height()
        if px is None or py is None:
            px, py = sw // 2, sh // 3
        w, h = 560, 520
        dx = px + 18
        if dx + w > sw - 6:
            dx = px - w - 18
        dx = max(6, min(dx, sw - w - 6))
        dy = max(6, min(py - 24, sh - h - 90))
        self.resize(w, h)
        self.move(int(dx), int(dy))

    def _activate(self):
        """强制激活(输入法据此接管输入)"""
        try:
            self.present()
            self.entry.grab_focus()
            from Xlib import display as _xd, X
            from Xlib.protocol import event as _xev
            d = _xd.Display()
            root = d.screen().root
            ev = _xev.ClientMessage(window=self.get_window().get_xid(),
                                 client_type=d.intern_atom('_NET_ACTIVE_WINDOW'),
                                 data=(32, [2, X.CurrentTime, 0, 0, 0]))
            root.send_event(ev, event_mask=(X.SubstructureRedirectMask |
                                            X.SubstructureNotifyMask))
            d.sync()
            d.close()
        except Exception as e:
            import traceback
            log('activate: %s | %s' % (e, traceback.format_exc().replace(chr(10), ' ; ')))

    def _watch_req(self):
        """另一个实例请求切换日期时, 在本窗口内切换(不新开窗口)"""
        try:
            if os.path.exists(REQ_PATH):
                m = os.path.getmtime(REQ_PATH)
                if m != self._req_mtime:
                    self._req_mtime = m
                    req = json.load(open(REQ_PATH))
                    os.remove(REQ_PATH)
                    if req.get('date') and req['date'] != self.key:
                        self.set_date(req['date'], req.get('x'), req.get('y'))
        except Exception:
            pass
        return True

    def set_date(self, key, px=None, py=None):
        self.key = key
        self.notes = load_notes()
        self.rows = get_notes_list(self.notes, key)
        self.expanded = set()
        self.edit_idx = None
        self.entry.set_text('')
        self.head.set_markup(self._head_markup())
        cur = note_color(self.notes, key)
        for pc, tb in self.cbtns.items():
            tb.set_active(pc == cur)
        self.color = None
        self.render_rows()
        if px is not None and py is not None:
            self._place(px, py)
        self._activate()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--date', required=True)
    ap.add_argument('--x', type=int, default=None)
    ap.add_argument('--y', type=int, default=None)
    a = ap.parse_args()
    os.makedirs(CFG_DIR, exist_ok=True)
    # 单实例: 已在运行则只请求切换日期
    old = None
    try:
        old = int(open(PID_PATH).read().strip())
    except Exception:
        old = None
    if old and os.path.exists('/proc/%d' % old):
        try:
            cmd = open('/proc/%d/cmdline' % old, 'rb').read().decode(
                errors='ignore')
        except Exception:
            cmd = ''
        if 'note_editor' in cmd:
            try:
                json.dump({'date': a.date, 'x': a.x, 'y': a.y},
                          open(REQ_PATH, 'w'))
            except Exception:
                pass
            return
    try:
        open(PID_PATH, 'w').write(str(os.getpid()))
    except Exception:
        pass
    NoteEditor(a.date, a.x, a.y)
    Gtk.main()


if __name__ == '__main__':
    main()
