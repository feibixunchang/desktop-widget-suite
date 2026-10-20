#!/usr/bin/env python3
from widget_font import SYS_FONT as _SYS_FONT
# -*- coding: utf-8 -*-
"""桌面万年历挂件 - 透明背景, 公历/农历/周数, 点击记事"""
import calendar
import json
import os
import subprocess
import sys
import datetime

import gi
gi.require_version('Gtk', '3.0')
gi.require_version('Pango', '1.0')
from gi.repository import Gtk, Gdk, Pango, GLib
import cairo

APP_DIR = os.path.dirname(os.path.abspath(__file__))
if APP_DIR not in sys.path:
    sys.path.insert(0, APP_DIR)

from lunar_python import Solar, Lunar
USER_DIR = os.path.expanduser('~/.local/share/desktop-widget/')
CONFIG_PATH = os.path.join(USER_DIR, 'calendar.json')
NOTES_PATH = os.path.join(USER_DIR, 'calendar_notes.json')
DEFAULT_FONT = _SYS_FONT + ''

DEFAULT_CFG = {
    'x': 60, 'y': 60,
    'font_scale': 1.0,
    'lock_position': False,
    'wd': '#FFFFFF',
    'we': '#FFEB3B',
    'today': '#42A5F5',
    'lunar': '#B0BEC5',
    'title': '#87CEEB',
    'week': '#FFEB3B',
    'note': '#FFFFFF',
    'alt_a': '#FFFFFF',      # 记事交替色 A(单数条 1,3,5…)
    'alt_b': '#FFEB3B',      # 记事交替色 B(双数条 2,4,6…)
}

DEFAULT_NOTE_COLOR = '#FFEB3B'      # 记事圆点默认颜色(黄)
NOTE_PRESETS = ['#FFFFFF', '#FFEB3B', '#F44336', '#2196F3',
                '#4CAF50', '#FF9800', '#9C27B0', '#FF69B4']


def log(msg):
    os.makedirs(USER_DIR, exist_ok=True)
    with open(os.path.join(USER_DIR, 'calendar.log'), 'a') as f:
        f.write(datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S') + ' ' + str(msg) + '\n')


def load_cfg():
    cfg = dict(DEFAULT_CFG)
    try:
        with open(CONFIG_PATH) as f:
            cfg.update(json.load(f))
    except Exception:
        pass
    return cfg


def save_cfg(cfg):
    try:
        os.makedirs(USER_DIR, exist_ok=True)
        with open(CONFIG_PATH, 'w') as f:
            json.dump(cfg, f, ensure_ascii=False, indent=2)
    except Exception as e:
        log('cfg save: ' + str(e))


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
        log('notes save: ' + str(e))


def esc(s):
    return str(s).replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')


def get_notes_list(notes, d):
    """返回某日期的记事列表, 兼容旧格式(字符串/单条dict)"""
    v = notes.get(d.isoformat())
    if isinstance(v, list):
        return v
    if isinstance(v, dict):
        return [v]
    if v:
        return [{'t': v}]
    return []


def note_text(notes, d):
    lst = get_notes_list(notes, d)
    return ' / '.join(str(n.get('t', '')) for n in lst)


def note_color(notes, d):
    """某日期记事圆点颜色(取第一条)"""
    lst = get_notes_list(notes, d)
    if lst and lst[0].get('c'):
        return lst[0]['c']
    return DEFAULT_NOTE_COLOR


def has_note(notes, d):
    return len(get_notes_list(notes, d)) > 0


def lunar_day(d):
    l = Solar.fromYmd(d.year, d.month, d.day).getLunar()
    jq = l.getJieQi()
    if jq:
        return jq
    return l.getDayInChinese()


def lunar_title(year, month):
    """取显示月内天数最多的农历月份(公历月初可能仍在上个农历月)"""
    counts = {}
    last = calendar.monthrange(year, month)[1]
    best_l = None
    best_key = None
    best_n = -1
    for day in range(1, last + 1):
        l = Solar.fromYmd(year, month, day).getLunar()
        key = (l.getYear(), l.getMonth())
        n = counts.get(key, 0) + 1
        counts[key] = n
        if n > best_n:
            best_n = n
            best_key = key
            best_l = l
    if best_l is None:
        best_l = Lunar.fromYmd(year, month, 1)
    return '%s年 %s月' % (best_l.getYearInGanZhi() + best_l.getYearShengXiao(),
                          best_l.getMonthInChinese())


WEEK_HEADS = ['周一', '周二', '周三', '周四', '周五', '周六', '周日']


class CalendarWidget(Gtk.Window):
    def __init__(self):
        super().__init__(type=Gtk.WindowType.TOPLEVEL)
        visual = self.get_screen().get_rgba_visual()
        if visual is not None:
            self.set_visual(visual)
        self.cfg = load_cfg()
        self.set_keep_below(True)
        self.set_skip_taskbar_hint(True)
        self.set_skip_pager_hint(True)
        self.set_app_paintable(True)
        self.set_decorated(False)
        self.set_accept_focus(False)
        self.set_can_focus(False)

        today = datetime.date.today()
        self.year = today.year
        self.month = today.month
        self.selected = today
        self._today = today
        self.notes = load_notes()

        self.dragging = False
        self.drag_start = None
        self.drag_orig = None
        self._fixed_w = None

        self.main_vbox = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
        self.main_vbox.set_margin_top(6)
        self.add(self.main_vbox)

        self.cal_grid = None
        self.build_header()
        self.build_calendar()

        self.note_header = Gtk.Label()
        self.note_header.set_halign(Gtk.Align.START)
        self.main_vbox.pack_start(self.note_header, False, False, 0)

        self.note_scroll = Gtk.ScrolledWindow()
        self.note_scroll.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
        self.note_scroll.set_min_content_height(30)
        self.note_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
        self.note_scroll.add(self.note_box)
        self.main_vbox.pack_start(self.note_scroll, True, True, 0)
        self._note_rows_data = []
        self._collapsed = {}

        self.connect('draw', self.on_draw)
        self.connect('button-press-event', self.on_button_press)
        self.connect('motion-notify-event', self.on_motion)
        self.connect('button-release-event', self.on_button_release)
        self.connect('scroll-event', self.on_scroll)
        self.apply_style()
        self.show_all()
        self.update_notes()
        GLib.timeout_add(800, self._all_desktops)
        try:
            self._notes_mtime = os.path.getmtime(NOTES_PATH)
        except Exception:
            self._notes_mtime = 0
        GLib.timeout_add(1500, self._watch_notes)
        self._start_day_watch()

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
        try:
            self.move(self.cfg['x'], self.cfg['y'])
        except Exception:
            pass
        return False

    def _start_day_watch(self):
        """每分钟检查是否跨天, 更新'今天'高亮"""
        GLib.timeout_add_seconds(60, self._check_date)

    def _check_date(self):
        """跨天后更新'今天'高亮"""
        t = datetime.date.today()
        if t != self._today:
            old = self._today
            self._today = t
            if self.year == old.year and self.month == old.month:
                self.selected = t
                self.refresh_view()
        return True

    def fs(self, px):
        return max(8, int(px * self.cfg.get('font_scale', 1.0)))

    def note_disp_color(self, idx, note):
        """记事显示颜色: 优先自身颜色, 否则按序号交替(单数A/双数B)"""
        if note.get('c'):
            return note['c']
        return self.cfg['alt_a'] if idx % 2 == 0 else self.cfg['alt_b']

    def apply_style(self):
        css = b'''
        window, box, grid, eventbox, label, fixed, scrolledwindow {
            background: transparent; }
        .note-arrow { margin-top: -5px; }
        .note-dot { margin-top: 5px; }
        button { background: rgba(30,30,30,0.30); color: #FFFFFF; border: none; }
        button:hover { background: rgba(90,90,90,0.7); }
        menu { background: #FFFFFF; color: #000000; }
        menuitem { color: #000000; }
        menuitem:hover { background: #E8E8E8; }
        checkmenuitem { color: #000000; }
        '''
        prov = Gtk.CssProvider()
        prov.load_from_data(css)
        Gtk.StyleContext.add_provider_for_screen(
            self.get_screen(), prov,
            Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)

    def on_draw(self, widget, cr):
        cr.set_operator(cairo.OPERATOR_SOURCE)
        cr.set_source_rgba(0, 0, 0, 0)
        cr.paint()
        return False

    def build_header(self):
        bar = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=4)
        self.week_label = Gtk.Label()
        self.week_label.set_valign(Gtk.Align.CENTER)
        bar.pack_start(self.week_label, False, False, 0)

        self.title_label = Gtk.Label()
        self.title_label.set_valign(Gtk.Align.CENTER)
        self.title_label.set_halign(Gtk.Align.CENTER)
        bar.pack_start(self.title_label, True, True, 0)
        self.update_title()

        btn_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=2)
        prev_btn = Gtk.Button(label='◀')
        prev_btn.set_valign(Gtk.Align.CENTER)
        prev_btn.set_size_request(34, 30)
        prev_btn.modify_font(Pango.FontDescription('%s %d' % (DEFAULT_FONT, self.fs(12))))
        prev_btn.connect('clicked', lambda w: self.shift_month(-1))
        btn_box.pack_start(prev_btn, False, False, 0)

        today_btn = Gtk.Button()
        dot_lbl = Gtk.Label(label='●')
        dot_lbl.set_margin_top(6)
        dot_lbl.set_valign(Gtk.Align.CENTER)
        today_btn.add(dot_lbl)
        today_btn.set_valign(Gtk.Align.CENTER)
        today_btn.set_size_request(34, 30)
        today_btn.modify_font(Pango.FontDescription('%s %d' % (DEFAULT_FONT, self.fs(12))))
        today_btn.connect('clicked', lambda w: self.goto_today())
        btn_box.pack_start(today_btn, False, False, 0)

        next_btn = Gtk.Button(label='▶')
        next_btn.set_valign(Gtk.Align.CENTER)
        next_btn.set_size_request(34, 30)
        next_btn.modify_font(Pango.FontDescription('%s %d' % (DEFAULT_FONT, self.fs(12))))
        next_btn.connect('clicked', lambda w: self.shift_month(1))
        btn_box.pack_start(next_btn, False, False, 0)

        bar.pack_end(btn_box, False, False, 0)
        self.main_vbox.pack_start(bar, False, False, 0)

        pad_prov = Gtk.CssProvider()
        pad_prov.load_from_data(b'''
        button {
            padding: 1px 3px;
            min-width: 8px;
            min-height: 0;
        }
        ''')
        for b in (prev_btn, today_btn, next_btn):
            b.get_style_context().add_provider(
                pad_prov, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)

    def update_title(self):
        line1 = '{0}年{1}月 {2}'.format(self.year, self.month, lunar_title(self.year, self.month))
        self.title_label.set_markup(
            '<span foreground="%s" font="%s %d" line_height="1.0">%s</span>'
            % (self.cfg['title'], DEFAULT_FONT, self.fs(15), esc(line1)))
        self.week_label.set_markup(
            '<span foreground="%s" font="%s %d" line_height="0.75">第%d周</span>'
            % (self.cfg['week'], DEFAULT_FONT, self.fs(12),
               datetime.date(self.year, self.month, 1).isocalendar()[1]))

    def build_calendar(self):
        if self.cal_grid is not None:
            self.main_vbox.remove(self.cal_grid)
        grid = Gtk.Grid()
        grid.set_column_homogeneous(False)
        grid.set_row_spacing(self.fs(12))
        grid.set_column_spacing(self.fs(12))
        sg = Gtk.SizeGroup(Gtk.SizeGroupMode.HORIZONTAL)

        for i, h in enumerate(WEEK_HEADS):
            lbl = Gtk.Label()
            if i < 5:
                head_c = '#90CAF9'
            elif i == 5:
                head_c = self.cfg['week']
            else:
                head_c = '#FF0000'
            lbl.set_markup('<span foreground="%s" font="%s %d" line_height="0.75">%s</span>'
                           % (head_c, DEFAULT_FONT, self.fs(15), h))
            sg.add_widget(lbl)
            grid.attach(lbl, i + 1, 0, 1, 1)

        weeks = self.month_weeks()
        for r, w in enumerate(weeks):
            wl = Gtk.Label()
            wl.set_markup('<span foreground="%s" font="%s %d" line_height="0.75">%02d</span>'
                          % (self.cfg['week'], DEFAULT_FONT, self.fs(14), w[0]))
            wl.set_halign(Gtk.Align.START)
            grid.attach(wl, 0, r + 1, 1, 1)
            for c, d in enumerate(w[1]):
                if d is None:
                    grid.attach(Gtk.Label(), c + 1, r + 1, 1, 1)
                    continue
                self.attach_day_cell(grid, d, c + 1, r + 1, sg)

        self.main_vbox.pack_start(grid, False, False, 0)
        self.main_vbox.reorder_child(grid, 1)
        grid.set_halign(Gtk.Align.FILL)
        self.cal_grid = grid
        self.show_all()

    def attach_day_cell(self, grid, d, col, row, sg=None):
        is_today = d == datetime.date.today()
        if is_today:
            main_c = self.cfg['today']
        elif d.weekday() == 6:
            main_c = '#FF0000'
        elif d.weekday() == 5:
            main_c = self.cfg['we']
        else:
            main_c = self.cfg['wd']
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=3)
        num = Gtk.Label()
        num.set_markup('<span foreground="%s" font="%s %d" line_height="0.75">%d</span>'
                       % (main_c, DEFAULT_FONT, self.fs(23), d.day))
        box.pack_start(num, False, False, 0)
        ln = Gtk.Label()
        ln.set_markup('<span foreground="%s" font="%s %d" line_height="0.75">%s</span>'
                      % (self.cfg['lunar'], DEFAULT_FONT, self.fs(13), esc(lunar_day(d))))
        box.pack_start(ln, False, False, 0)
        if has_note(self.notes, d):
            ln.set_markup('<span foreground="%s" font="%s %d" line_height="0.75">%s</span>'
                          % (note_color(self.notes, d), DEFAULT_FONT, self.fs(13), esc(lunar_day(d))))

        eb = Gtk.EventBox()
        eb.add(box)
        if sg is not None:
            sg.add_widget(eb)
        eb.connect('button-press-event', self.on_day_click, d)
        grid.attach(eb, col, row, 1, 1)

    def month_weeks(self):
        cal = calendar.Calendar(firstweekday=0)
        days = list(cal.itermonthdates(self.year, self.month))
        weeks = []
        for i in range(0, len(days), 7):
            chunk = days[i:i + 7]
            chunk = [(d if d.month == self.month else None) for d in chunk]
            dates = [d for d in chunk if d is not None]
            w = dates[0].isocalendar()[1]
            weeks.append((w, chunk))
        return weeks

    def on_day_click(self, widget, event, d):
        self.selected = d
        self.update_notes()
        if event.button == 3:
            self.show_note_dialog(d, int(event.x_root), int(event.y_root))
        return True

    def show_note_dialog(self, d, px=None, py=None):
        # 用独立进程的记事编辑器(独立普通窗口, 输入法才会正常接管)
        try:
            cmd = [sys.executable,
                   os.path.join(APP_DIR, 'note_editor.py'),
                   '--date', d.isoformat()]
            if px is not None:
                cmd += ['--x', str(px)]
            if py is not None:
                cmd += ['--y', str(py)]
            _p = subprocess.Popen(cmd, stdout=subprocess.DEVNULL,
                                  stderr=subprocess.DEVNULL,
                                  start_new_session=True)
            # 编辑器退出后回收子进程, 避免留下僵尸
            GLib.timeout_add(2000, lambda: _p.poll() is None)
            return
        except Exception as e:
            log('editor launch: %s' % e)
        self._show_note_dialog_builtin(d, px, py)
        return

    def _show_note_dialog_builtin(self, d, px=None, py=None):
        # 已经开着记事窗口时, 只切换到新日期(避免弹出多个窗口互相重叠)
        dlg_old = getattr(self, '_note_dlg', None)
        if dlg_old is not None:
            try:
                if dlg_old.get_visible():
                    self._retarget_note_dlg(d, px, py)
                    return
            except Exception:
                pass
            self._note_dlg = None
        key = d.isoformat()
        self._dlg_date = d
        dlg = Gtk.Window(Gtk.WindowType.TOPLEVEL)
        dlg.set_decorated(False)
        dlg.set_title('%s 记事' % key)
        try:
            dlg.set_type_hint(Gdk.WindowTypeHint.NORMAL)
        except Exception:
            pass
        dlg.set_skip_taskbar_hint(False)
        dlg.set_skip_pager_hint(False)
        try:
            dlg.set_wmclass('deepin-note-editor', 'DeepinNoteEditor')
        except Exception:
            pass
        visual = self.get_screen().get_rgba_visual()
        if visual is not None:
            dlg.set_visual(visual)
        dlg.set_app_paintable(True)
        cw = self.get_size()[0]
        scr = dlg.get_screen()
        scr_h = scr.get_height()
        dlg_w = max(cw, 560)
        dlg_h = min(840, max(420, scr_h - self.cfg.get('y', 60) - 130))
        dlg.set_default_size(dlg_w, dlg_h)
        dlg.set_keep_above(True)

        vbox = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
        vbox.set_margin_start(14)
        vbox.set_margin_end(14)
        vbox.set_margin_top(12)
        vbox.set_margin_bottom(12)
        dlg.add(vbox)
        vbox.add_events(Gdk.EventMask.POINTER_MOTION_MASK |
                        Gdk.EventMask.BUTTON_PRESS_MASK |
                        Gdk.EventMask.BUTTON_RELEASE_MASK)
        vbox.drag_state = None
        vbox.connect('button-press-event', self.dlg_drag_start, dlg)
        vbox.connect('motion-notify-event', self.dlg_drag_move, dlg)
        vbox.connect('button-release-event', self.dlg_drag_end, dlg)

        head = Gtk.Label()
        head.set_halign(Gtk.Align.START)
        head.set_markup('<span foreground="%s" font="%s %d">%s 记事</span>'
                        % (self.cfg['week'], DEFAULT_FONT, self.fs(15), key))
        vbox.pack_start(head, False, False, 0)
        self._dlg_head = head
        self._note_dlg = dlg
        dlg.connect('destroy', self._on_note_dlg_destroy)

        # 一行输入框: 敲回车直接添加并保存(不用先点"添加"再点"保存")
        self._dlg_entry = Gtk.Entry()
        self._dlg_entry.set_placeholder_text('在这里写记事, 回车直接添加')
        self._dlg_entry.modify_font(
            Pango.FontDescription('%s %d' % (DEFAULT_FONT, self.fs(12))))
        self._dlg_entry.connect('activate', lambda w: self._dlg_quick_add())
        eprov = Gtk.CssProvider()
        eprov.load_from_data(b'''
        entry, entry:focus, entry:selected {
            background-color: rgba(0, 0, 0, 0.55);
            background-image: none;
            color: #FFFFFF;
            border: 1px solid rgba(255,255,255,0.45);
            border-radius: 6px;
            padding: 4px 8px;
        }
        ''')
        self._dlg_entry.get_style_context().add_provider(
            eprov, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
        vbox.pack_start(self._dlg_entry, False, False, 0)
        tip = Gtk.Label()
        tip.set_markup('<span foreground="#9AA4B8" font="%s %d">'
                       '输入拼音后按空格上屏；若无中文，按 Ctrl+空格 切到搜狗再试</span>'
                       % (DEFAULT_FONT, self.fs(9)))
        tip.set_halign(Gtk.Align.START)
        vbox.pack_start(tip, False, False, 0)
        self._dlg_edit_idx = None
        head.add_events(Gdk.EventMask.POINTER_MOTION_MASK |
                        Gdk.EventMask.BUTTON_PRESS_MASK |
                        Gdk.EventMask.BUTTON_RELEASE_MASK)
        head.drag_state = None
        head.connect('button-press-event', self.dlg_drag_start, dlg)
        head.connect('motion-notify-event', self.dlg_drag_move, dlg)
        head.connect('button-release-event', self.dlg_drag_end, dlg)

        self._dlg_rows = get_notes_list(self.notes, d)
        self._dlg_scroll = Gtk.ScrolledWindow()
        self._dlg_scroll.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
        self._dlg_scroll.set_min_content_height(240)
        self._dlg_list = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
        self._dlg_scroll.add(self._dlg_list)
        vbox.pack_start(self._dlg_scroll, True, True, 0)
        self._render_dlg_rows()

        self._dlg_edit_idx = None

        self._dlg_color = None
        self._dlg_colors = {}
        cbox = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6)
        cbox.set_halign(Gtk.Align.START)
        clabel = Gtk.Label(label='新记事颜色:')
        clabel.modify_font(Pango.FontDescription('%s 13' % DEFAULT_FONT))
        clabel.modify_fg(Gtk.StateFlags.NORMAL, Gdk.Color(255, 255, 255))
        cbox.pack_start(clabel, False, False, 0)
        for pc in NOTE_PRESETS:
            tb = Gtk.ToggleButton()
            lbl = Gtk.Label()
            lbl.set_markup('<span foreground="%s" font="%s 15" % _SYS_FONT>●</span>' % pc)
            tb.add(lbl)
            tb.set_active(pc == note_color(self.notes, d))
            tb.connect('toggled', self._on_pick_color, pc)
            self._dlg_colors[pc] = tb
            cbox.pack_start(tb, False, False, 0)
        vbox.pack_start(cbox, False, False, 0)

        add_btn = Gtk.Button(label='＋ 添加')
        add_btn.modify_font(Pango.FontDescription('%s 13' % DEFAULT_FONT))
        add_btn.connect('clicked', lambda w: self._dlg_quick_add())
        vbox.pack_start(add_btn, False, False, 0)
        self._dlg_add_btn = add_btn
        hbox = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        hbox.set_halign(Gtk.Align.END)
        cancel_btn = Gtk.Button(label='关闭(已自动保存)')
        cancel_btn.modify_font(Pango.FontDescription('%s 13' % DEFAULT_FONT))
        cancel_btn.connect('clicked', lambda w: dlg.destroy())
        hbox.pack_start(cancel_btn, False, False, 0)
        ok_btn = cancel_btn
        vbox.pack_start(hbox, False, False, 0)

        dlg.connect('draw', self.dlg_draw)
        dprov = Gtk.CssProvider()
        dprov.load_from_data(b'''
        label, button, checkbutton { color: #FFFFFF; }
        button { background-image: none; background-color: rgba(255,255,255,0.08); }
        button:hover { background-color: rgba(90,160,255,0.35); }
        ''')
        dlg.get_style_context().add_provider(
            dprov, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
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
        for b in (add_btn, cancel_btn):
            b.get_style_context().add_provider(
                bprov, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
        self._place_note_dlg(px, py)
        dlg.show_all()
        try:
            ph = dlg.get_preferred_height()[1]
            dlg.resize(dlg_w, int(min(scr_h - 120, max(300, ph + 10))))
        except Exception:
            pass
        self._place_note_dlg(px, py)
        # 关键: 强制激活(等价于 xdotool windowactivate), 否则输入法不会接管这个窗口
        self._activate_note_dlg()
        if getattr(self, '_dlg_entry', None) is not None:
            self._dlg_entry.grab_focus()
        else:
            tv.grab_focus()

    def _render_dlg_rows(self):
        """重建对话框记事列表"""
        for ch in self._dlg_list.get_children():
            self._dlg_list.remove(ch)
        if not hasattr(self, '_dlg_expanded'):
            self._dlg_expanded = set()
        for idx, nt in enumerate(self._dlg_rows):
            row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            color = self.note_disp_color(idx, nt)
            time_txt = nt.get('time') or '--:--'
            tmark = Gtk.Label()
            tmark.set_markup('<span foreground="%s" font="%s %d">%s</span>'
                             % (color, DEFAULT_FONT, self.fs(10), esc(time_txt)))
            tmark.set_halign(Gtk.Align.START)
            row.pack_start(tmark, False, False, 0)
            raw = str(nt.get('t', ''))
            expanded = idx in self._dlg_expanded
            if len(raw) > 60 and not expanded:
                show = raw[:60] + ' …(' + str(len(raw)) + '字)'
                fold = True
            else:
                show = raw
                fold = False
            body = Gtk.Label()
            body.set_markup('<span foreground="%s" font="%s %d" line_height="1.0">%s</span>'
                            % (color, DEFAULT_FONT, self.fs(10), esc(show)))
            body.set_halign(Gtk.Align.START)
            body.set_line_wrap(True)
            body.set_xalign(0.0)
            row.pack_start(body, True, True, 0)
            if len(raw) > 60:
                fb = Gtk.Button(label=('收起' if expanded else '展开'))
                fb.modify_font(Pango.FontDescription('%s 11' % DEFAULT_FONT))
                fb.connect('clicked', lambda w, i=idx: self._dlg_fold(i))
                row.pack_start(fb, False, False, 0)
            edit_btn = Gtk.Button(label='编辑')
            edit_btn.modify_font(Pango.FontDescription('%s 12' % DEFAULT_FONT))
            edit_btn.connect('clicked', lambda w, i=idx: self._dlg_edit(i))
            row.pack_start(edit_btn, False, False, 0)
            del_btn = Gtk.Button(label='✕')
            del_btn.modify_font(Pango.FontDescription('%s 11' % DEFAULT_FONT))
            del_btn.connect('clicked', lambda w, i=idx: self._dlg_del(i))
            row.pack_start(del_btn, False, False, 0)
            self._dlg_list.pack_start(row, False, False, 0)
        self._dlg_list.show_all()

    def _dlg_fold(self, idx):
        """展开/收起长记事"""
        if not hasattr(self, '_dlg_expanded'):
            self._dlg_expanded = set()
        if idx in self._dlg_expanded:
            self._dlg_expanded.discard(idx)
        else:
            self._dlg_expanded.add(idx)
        self._render_dlg_rows()

    def _on_pick_color(self, btn, pc):
        """新记事颜色单选"""
        if btn.get_active():
            box = btn.get_parent()
            for ch in box.get_children():
                if ch is not btn and isinstance(ch, Gtk.ToggleButton):
                    ch.set_active(False)
            self._dlg_color = pc

    def _dlg_edit(self, idx):
        """把已有记事填入输入框, 保存时覆盖该条"""
        if not (0 <= idx < len(self._dlg_rows)):
            return
        self._dlg_edit_idx = idx
        nt = self._dlg_rows[idx]
        try:
            self._dlg_entry.set_text(nt.get('t', ''))
            self._dlg_entry.grab_focus()
        except Exception:
            pass
        self._dlg_color = nt.get('c', '') or None
        if hasattr(self, '_dlg_add_btn'):
            self._dlg_add_btn.set_label('保存修改')
        self._dlg_edit_idx = idx

    def _activate_note_dlg(self):
        """用 EWMH _NET_ACTIVE_WINDOW 强制激活记事窗口(输入法据此接管输入)"""
        try:
            dlg = self._note_dlg
            dlg.set_focus_on_map(True)
            dlg.set_accept_focus(True)
            dlg.set_can_focus(True)
            dlg.present()
            dlg.grab_focus()
            try:
                self._dlg_entry.grab_focus()
            except Exception:
                pass
            win = dlg.get_window()
            if win is not None:
                from Xlib import display as _xd, X
                from Xlib.protocol import event as _xev
                d = _xd.Display()
                root = d.screen().root
                ev = _xev.ClientMessage(
                    window=win.get_xid(),
                    client_type=d.intern_atom('_NET_ACTIVE_WINDOW'),
                    data=(32, [2, X.CurrentTime, 0, 0, 0]))
                root.send_event(ev, event_mask=(X.SubstructureRedirectMask |
                                                X.SubstructureNotifyMask))
                d.sync()
                d.close()
        except Exception as e:
            pass

    def _on_note_dlg_destroy(self, *a):
        self._note_dlg = None

    def _retarget_note_dlg(self, d, px=None, py=None):
        """记事窗口已打开时, 切换到新日期(不新开窗口)"""
        self._dlg_date = d
        key = d.isoformat()
        try:
            self._note_dlg.set_title('%s 记事' % key)
        except Exception:
            pass
        try:
            self._dlg_head.set_markup(
                '<span foreground="%s" font="%s %d">%s 记事</span>'
                % (self.cfg['week'], DEFAULT_FONT, self.fs(15), key))
        except Exception:
            pass
        self._dlg_rows = get_notes_list(self.notes, d)
        self._dlg_edit_idx = None
        self._dlg_color = None
        try:
            self._dlg_entry.set_text('')
        except Exception:
            pass
        cur = note_color(self.notes, d)
        for pc, tb in getattr(self, '_dlg_colors', {}).items():
            tb.set_active(pc == cur)
        self._render_dlg_rows()
        try:
            self._place_note_dlg(px, py)
            self._activate_note_dlg()
        except Exception:
            pass

    def _dlg_persist(self):
        """把当前编辑结果立刻写入磁盘(不用再点保存)"""
        d = getattr(self, '_dlg_date', None) or self.selected
        key = d.isoformat()
        rows = []
        for nt in self._dlg_rows:
            t = str(nt.get('t', '')).strip()
            if t:
                rows.append({'t': t,
                             'time': nt.get('time') or
                             datetime.datetime.now().strftime('%H:%M'),
                             'c': nt.get('c', '')})
        if rows:
            self.notes[key] = rows
        else:
            self.notes.pop(key, None)
        save_notes(self.notes)
        self.update_notes()
        self.build_calendar()

    def _dlg_quick_add(self):
        """输入框: 回车或点"添加"都立即保存"""
        e = getattr(self, '_dlg_entry', None)
        if e is None:
            return
        txt = e.get_text().strip()
        if not txt:
            return
        if getattr(self, '_dlg_edit_idx', None) is not None:
            ei = self._dlg_edit_idx
            if 0 <= ei < len(self._dlg_rows):
                self._dlg_rows[ei]['t'] = txt
                if self._dlg_color:
                    self._dlg_rows[ei]['c'] = self._dlg_color
            self._dlg_edit_idx = None
            if hasattr(self, '_dlg_add_btn'):
                self._dlg_add_btn.set_label('＋ 添加')
        else:
            self._dlg_rows.append(
                {'t': txt,
                 'time': datetime.datetime.now().strftime('%H:%M:%S'),
                 'c': self._dlg_color or ''})
        e.set_text('')
        self._render_dlg_rows()
        self._dlg_persist()

    def _dlg_add(self, tv=None, add_btn=None):
        self._dlg_quick_add()

    def _dlg_del(self, idx):
        if 0 <= idx < len(self._dlg_rows):
            del self._dlg_rows[idx]
            self._render_dlg_rows()
            self._dlg_persist()

    def dlg_draw(self, widget, cr):
        """对话框背景: 深色卡片(保证白字看得清)"""
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
            cr.set_source_rgba(0.06, 0.07, 0.10, 0.95)
            cr.fill_preserve()
            cr.set_source_rgba(1, 1, 1, 0.16)
            cr.set_line_width(1)
            cr.stroke()
        except Exception:
            pass
        return False

    def _place_note_dlg(self, px=None, py=None):
        """把记事窗口锚定在被点的那一天旁边(越界时自动换边/夹住)"""
        if self._note_dlg is None:
            return
        scr = self._note_dlg.get_screen()
        scr_w, scr_h = scr.get_width(), scr.get_height()
        dw, dh = self._note_dlg.get_size()
        if px is None or py is None:
            x, y = self.get_position()
            px, py = x + self.get_size()[0] + 12, y + 20
        dx = px + 18
        if dx + dw > scr_w - 6:
            dx = px - dw - 18
        dx = max(6, min(dx, scr_w - dw - 6))
        dy = py - 24
        dy = max(6, min(dy, scr_h - dh - 90))
        self._note_dlg.move(int(dx), int(dy))

    def dlg_drag_start(self, widget, event, dlg):
        if event.button == 1:
            widget.drag_state = (event.x_root, event.y_root,
                                 dlg.get_position()[0], dlg.get_position()[1])
            widget.dragging = True
        return False

    def dlg_drag_move(self, widget, event, dlg):
        st = getattr(widget, 'drag_state', None)
        if st and getattr(widget, 'dragging', False):
            dlg.move(st[2] + event.x_root - st[0],
                     st[3] + event.y_root - st[1])
        return False

    def dlg_drag_end(self, widget, event, dlg):
        widget.drag_state = None
        widget.dragging = False
        return False

    def save_note(self, dlg, key):
        self._dlg_persist()
        try:
            dlg.destroy()
        except Exception:
            pass
        return
        rows = []
        for nt in self._dlg_rows:
            t = str(nt.get('t', '')).strip()
            if t:
                rows.append({'t': t,
                             'time': nt.get('time') or datetime.datetime.now().strftime('%H:%M'),
                             'c': nt.get('c', '')})
        if rows:
            self.notes[key] = rows
        else:
            self.notes.pop(key, None)
        save_notes(self.notes)
        self.update_notes()
        self.build_calendar()
        dlg.destroy()

    def _watch_notes(self):
        """记事文件被编辑后立即刷新(编辑器是独立进程)"""
        try:
            m = os.path.getmtime(NOTES_PATH)
        except Exception:
            m = 0
        if m != getattr(self, '_notes_mtime', 0):
            self._notes_mtime = m
            self.notes = load_notes()
            self.update_notes()
            self.build_calendar()
        return True

    def update_notes(self):
        self._note_rows_data = get_notes_list(self.notes, self.selected)
        header = '<span foreground="%s" font="%s %d" line_height="0.75">%s 记事：</span>' % (
            self.cfg['week'], DEFAULT_FONT, self.fs(16),
            '%d月%d日' % (self.selected.month, self.selected.day))
        self.note_header.set_markup(header)
        self._collapsed = {}
        self._render_note_rows()
        self.note_scroll.show_all()
        self.resize_by_content()

    def _render_note_rows(self):
        for ch in self.note_box.get_children():
            self.note_box.remove(ch)
        if not self._note_rows_data:
            hint = Gtk.Label()
            hint.set_markup('<span foreground="%s" font="%s %d" line_height="0.75">（无，右键日期可添加）</span>'
                            % (self.cfg['lunar'], DEFAULT_FONT, self.fs(12)))
            hint.set_halign(Gtk.Align.START)
            self.note_box.pack_start(hint, False, False, 0)
        else:
            for idx, nt in enumerate(self._note_rows_data):
                color = self.note_disp_color(idx, nt)
                time_txt = nt.get('time') or '--:--'
                row = self._build_note_row(idx, color, time_txt,
                                           str(nt.get('t', '')))
                self.note_box.pack_start(row, False, False, 0)
        self.note_scroll.show_all()

    def _is_collapsed(self, idx, txt):
        return self._collapsed.get(idx, len(txt) > 100)

    def _build_note_row(self, idx, color, time_txt, txt):
        eb = Gtk.EventBox()
        row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6)
        coll = self._is_collapsed(idx, txt)
        show_txt = txt
        if len(txt) > 100:
            ar = Gtk.Label()
            ar.set_markup('<span foreground="%s" font="%s 11">%s</span>'
                          % (color, DEFAULT_FONT, '▶' if coll else '▼'))
            ar_eb = Gtk.EventBox()
            ar_eb.set_valign(Gtk.Align.START)
            ar_eb.add(ar)
            ar_eb.connect('button-press-event', self._on_arrow_click, idx)
            ar_eb.get_style_context().add_class('note-arrow')
            row.pack_start(ar_eb, False, False, 0)
            if coll:
                show_txt = txt[:100] + '……'
        dot = Gtk.Label()
        dot.set_markup('<span foreground="%s" font="%s 12">●</span>'
                       % (color, DEFAULT_FONT))
        dot.set_valign(Gtk.Align.START)
        dot.get_style_context().add_class('note-dot')
        row.pack_start(dot, False, False, 0)
        tm = Gtk.Label()
        tm.set_markup('<span foreground="%s" font="%s 12">%s</span>'
                      % (color, DEFAULT_FONT, esc(time_txt)))
        tm.set_valign(Gtk.Align.START)
        row.pack_start(tm, False, False, 0)
        body = Gtk.Label()
        body.set_markup('<span foreground="%s" font="%s 12" line_height="1.1">%s</span>'
                        % (color, DEFAULT_FONT, esc(show_txt)))
        body.set_halign(Gtk.Align.START)
        body.set_valign(Gtk.Align.START)
        body.set_line_wrap(True)
        body.set_line_wrap_mode(Pango.WrapMode.WORD_CHAR)
        body.set_xalign(0.0)
        row.pack_start(body, True, True, 0)
        eb.add(row)
        eb.connect('button-press-event', self._on_note_click, idx)
        return eb

    def _on_note_click(self, widget, event, idx):
        if event.button == 3:
            self._show_note_menu(event, idx)
            return True
        return False

    def _show_note_menu(self, event, idx):
        m = Gtk.Menu()
        mi_edit = Gtk.MenuItem(label='编辑')
        mi_edit.connect('activate', lambda w: self._widget_edit_note(idx))
        m.append(mi_edit)
        mi_del = Gtk.MenuItem(label='删除')
        mi_del.connect('activate', lambda w: self._widget_del_note(idx))
        m.append(mi_del)
        txt = str(self._note_rows_data[idx].get('t', ''))
        if len(txt) > 100:
            mi_tg = Gtk.MenuItem(
                label='展开' if self._is_collapsed(idx, txt) else '折叠')
            mi_tg.connect('activate', lambda w: self._toggle_collapse(idx))
            m.append(mi_tg)
        m.show_all()
        m.popup_at_pointer(event)

    def _on_arrow_click(self, widget, event, idx):
        if event.button == 1:
            self._toggle_collapse(idx)
            return True
        return False

    def _toggle_collapse(self, idx):
        txt = str(self._note_rows_data[idx].get('t', ''))
        self._collapsed[idx] = not self._is_collapsed(idx, txt)
        self._render_note_rows()
        self.resize_by_content()

    def _widget_edit_note(self, idx):
        self.show_note_dialog(self.selected)
        self._dlg_edit(idx)

    def _widget_del_note(self, idx):
        key = self.selected.isoformat()
        lst = self.notes.get(key, [])
        if not (0 <= idx < len(lst)):
            return
        del lst[idx]
        if lst:
            self.notes[key] = lst
        else:
            self.notes.pop(key, None)
        save_notes(self.notes)
        self.update_notes()
        self.build_calendar()

    def resize_by_content(self):
        self.queue_resize()
        while Gtk.events_pending():
            Gtk.main_iteration_do(False)
        nat = self.get_preferred_size()
        w = min(max(nat[1].width, 560), 680)
        if self._fixed_w is None:
            self._fixed_w = w
        scr = self.get_screen()
        max_h = scr.get_height() - self.cfg.get('y', 60) - 110
        max_h = max(300, max_h)
        ns_cur = self.note_scroll.get_preferred_size()[1].height
        rest = nat[1].height - ns_cur
        # 窗口宽度与万年历网格同宽
        grid_w = (self.cal_grid.get_preferred_size()[1].width
                  if self.cal_grid else int(self._fixed_w))
        win_w = int(grid_w) + 8
        view_w = max(300, win_w - 145)
        box_nat = self.note_box.get_preferred_height_for_width(view_w)[1]
        avail = max(170, max_h - rest - 40)
        ns_min = max(30, min(box_nat, avail))
        self.note_scroll.set_min_content_height(ns_min)
        h = rest + ns_min
        self.set_resizable(False)
        self.set_size_request(win_w, int(h) + 20)

    def goto_today(self):
        t = datetime.date.today()
        self.year, self.month, self.selected = t.year, t.month, t
        self.refresh_view()

    def shift_month(self, delta):
        m = self.month + delta
        y = self.year + (m - 1) // 12
        self.year, self.month = y, (m - 1) % 12 + 1
        days = calendar.monthrange(self.year, self.month)[1]
        if self.selected.day > days:
            self.selected = self.selected.replace(year=self.year, month=self.month, day=days)
        else:
            self.selected = self.selected.replace(year=self.year, month=self.month)
        self.refresh_view()

    def refresh_view(self):
        self.update_title()
        self.build_calendar()
        self.update_notes()

    def on_scroll(self, widget, event):
        # 指针在记事滚动区上时交给滚动条, 不触发缩放
        try:
            alloc = self.note_scroll.get_allocation()
            px, py = self.get_pointer()
            if alloc.y - 6 <= py <= alloc.y + alloc.height + 6:
                return True
        except Exception:
            pass
        direction = event.direction
        if direction == Gdk.ScrollDirection.UP:
            self.scale_by(0.1)
        elif direction == Gdk.ScrollDirection.DOWN:
            self.scale_by(-0.1)
        return True

    def scale_by(self, delta):
        self.cfg['font_scale'] = max(0.6, min(2.5, self.cfg.get('font_scale', 1.0) + delta))
        save_cfg(self.cfg)
        self.refresh_view()

    def on_button_press(self, widget, event):
        if self.cfg.get('lock_position', False):
            if event.button == 3:
                self.show_menu(event)
            return True
        if event.button == 1:
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
            self.move(self.drag_orig[0] + event.x_root - self.drag_start[0],
                      self.drag_orig[1] + event.y_root - self.drag_start[1])
            return True
        return False

    def on_button_release(self, widget, event):
        if event.button == 1:
            if self.dragging:
                x, y = self.get_position()
                self.cfg['x'], self.cfg['y'] = x, y
                save_cfg(self.cfg)
            self.dragging = False
        return False

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

        def add(label, cb):
            item = Gtk.MenuItem(label=label)
            item.connect('activate', cb)
            menu.append(item)

        add('增大字号', lambda w: self.scale_by(0.1))
        add('减小字号', lambda w: self.scale_by(-0.1))
        add('刷新', lambda w: self.refresh_view())

        color_item = Gtk.MenuItem(label='记事颜色')
        color_sub = Gtk.Menu()
        for label, key in (('单数条(1,3,5…)', 'alt_a'), ('双数条(2,4,6…)', 'alt_b')):
            it = Gtk.MenuItem(label=label)
            sub = Gtk.Menu()
            for pc in NOTE_PRESETS:
                ci = Gtk.CheckMenuItem(label='● ' + pc)
                ci.set_active(self.cfg.get(key, '#FFFFFF') == pc)
                ci.connect('activate', lambda w, k=key, c=pc: self._set_alt(k, c))
                sub.append(ci)
            it.set_submenu(sub)
            color_sub.append(it)
        color_item.set_submenu(color_sub)
        menu.append(color_item)

        lock_item = Gtk.CheckMenuItem(label='锁定位置')
        lock_item.set_active(bool(self.cfg.get('lock_position', False)))
        lock_item.connect('toggled', self.on_lock_toggle)
        menu.append(lock_item)
        menu.append(Gtk.SeparatorMenuItem())
        add('回到今天', lambda w: self.goto_today())
        add('设置颜色', lambda w: self.show_color_dialog())
        menu.append(Gtk.SeparatorMenuItem())
        
        autostart_item = Gtk.CheckMenuItem(label='开机自启')
        autostart_item.set_active(self._autostart_enabled())
        autostart_item.connect('toggled', lambda w: self._toggle_autostart(w.get_active()))
        menu.append(autostart_item)

        menu.append(Gtk.SeparatorMenuItem())
        add('退出', lambda w: Gtk.main_quit())
        menu.show_all()
        menu.popup(None, None, None, None, event.button, event.time)

    def _set_alt(self, key, color):
        self.cfg[key] = color
        save_cfg(self.cfg)
        self.refresh_view()

    def on_lock_toggle(self, item):
        self.cfg['lock_position'] = item.get_active()
        if item.get_active():
            x, y = self.get_position()
            self.cfg['x'], self.cfg['y'] = x, y
        save_cfg(self.cfg)

    def show_color_dialog(self):
        dlg = Gtk.Dialog(title='设置颜色', transient_for=None)
        dlg.add_button('取消', Gtk.ResponseType.CANCEL)
        dlg.add_button('保存', Gtk.ResponseType.OK)
        box = dlg.get_content_area()
        box.set_spacing(6)
        box.set_margin_start(12)
        box.set_margin_end(12)
        box.set_margin_top(12)
        box.set_margin_bottom(12)

        def row(label, key):
            hb = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
            lb = Gtk.Label(label=label)
            lb.set_halign(Gtk.Align.START)
            btn = Gtk.ColorButton()
            btn.set_rgba(Gdk.RGBA(0, 0, 0, 1))
            try:
                col = self.cfg.get(key, '#FFFFFF').lstrip('#')
                r = int(col[0:2], 16) / 255.0
                g = int(col[2:4], 16) / 255.0
                b = int(col[4:6], 16) / 255.0
                btn.set_rgba(Gdk.RGBA(r, g, b, 1))
            except Exception:
                pass
            hb.pack_start(lb, True, True, 0)
            hb.pack_start(btn, False, False, 0)
            box.pack_start(hb, False, False, 0)
            return key, btn

        items = [row('标题颜色', 'title'),
                 row('工作日颜色', 'wd'),
                 row('周末颜色', 'we'),
                 row('今天颜色', 'today'),
                 row('农历颜色', 'lunar'),
                 row('记事颜色', 'note')]
        dlg.show_all()
        resp = dlg.run()
        if resp == Gtk.ResponseType.OK:
            for key, btn in items:
                rgba = btn.get_rgba()
                self.cfg[key] = '#%02X%02X%02X' % (
                    int(rgba.red * 255), int(rgba.green * 255), int(rgba.blue * 255))
            save_cfg(self.cfg)
            self.refresh_view()
        dlg.destroy()


def main():
    log('calendar widget start')
    win = CalendarWidget()
    Gtk.main()


if __name__ == '__main__':
    main()