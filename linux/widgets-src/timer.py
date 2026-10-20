#!/usr/bin/env python3
from widget_font import SYS_FONT as _SYS_FONT
# -*- coding: utf-8 -*-
"""桌面倒计时+闹钟挂件 - 透明背景, 倒计时(蓝) + 闹钟(黄), 完成弹窗+声音"""
import json
import os
import sys
import datetime
import subprocess

APP_DIR = os.path.dirname(os.path.abspath(__file__))

import gi
gi.require_version('Gtk', '3.0')
gi.require_version('Pango', '1.0')
from gi.repository import Gtk, Gdk, Pango, GLib
import cairo

USER_DIR = os.path.expanduser('~/.local/share/desktop-widget/')
CONFIG_PATH = os.path.join(USER_DIR, 'timer_alarm.json')
DEFAULT_FONT = _SYS_FONT + ''
SOUNDS = [
    '/usr/share/sounds/deepin/stereo/message.wav',
    '/usr/share/sounds/deepin/stereo/complete-print.wav',
    '/usr/share/sounds/deepin/stereo/dialog-error.wav',
]

DEFAULT_CFG = {
    'x': 60, 'y': 300,
    'font_scale': 1.0,
    'lock_position': False,
    'timer_total': 0,
    'timer_end': None,
    'alarm_time': None,
    'alarm_enabled': False,
    'alarm_done': False,
}


def log(msg):
    os.makedirs(USER_DIR, exist_ok=True)
    with open(os.path.join(USER_DIR, 'timer_alarm.log'), 'a') as f:
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


def fmt_hms(sec):
    sec = max(0, int(sec))
    h, m, s = sec // 3600, sec % 3600 // 60, sec % 60
    return '%02d:%02d:%02d' % (h, m, s)


def play_sound():
    for s in SOUNDS:
        if os.path.exists(s):
            try:
                subprocess.Popen(['paplay', s], stdout=subprocess.DEVNULL,
                                 stderr=subprocess.DEVNULL)
                return
            except Exception:
                pass
    try:
        subprocess.Popen(['aplay', '-q'], stdout=subprocess.DEVNULL,
                         stderr=subprocess.DEVNULL)
    except Exception:
        pass


def play_times(n=3, interval=1.2):
    def _one():
        play_sound()
    _one()
    for i in range(1, n):
        GLib.timeout_add(int(interval * 1000 * i), lambda: (_one(), False)[1])


def parse_hms(text):
    s = text.strip().replace('：', ':')
    parts = s.split(':')
    secs = 0
    try:
        for p in parts:
            secs = secs * 60 + int(p)
    except Exception:
        return None
    if secs <= 0:
        return None
    return secs


class TimerAlarmWidget(Gtk.Window):
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

        self.alert_window = None
        self.dragging = False
        self.drag_start = None
        self.drag_orig = None
        self.timer_running = False
        self.alarm_pending = False

        end = self.cfg.get('timer_end')
        if end:
            try:
                end = datetime.datetime.fromisoformat(end)
                if end > datetime.datetime.now():
                    self.timer_running = True
                else:
                    self.cfg['timer_end'] = None
            except Exception:
                self.cfg['timer_end'] = None
        at = self.cfg.get('alarm_time')
        if at:
            try:
                if datetime.datetime.fromisoformat(at) < datetime.datetime.now() \
                        and not self.cfg.get('alarm_done'):
                    self.cfg['alarm_enabled'] = False
                    self.cfg['alarm_done'] = True
            except Exception:
                pass

        self.main_vbox = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6)
        self.add(self.main_vbox)
        self.build_rows()

        self.connect('draw', self.on_draw)
        self.connect('button-press-event', self.on_button_press)
        self.connect('motion-notify-event', self.on_motion)
        self.connect('button-release-event', self.on_button_release)
        self.connect('scroll-event', self.on_scroll)
        self.apply_style()
        self.show_all()
        self.move(self.cfg['x'], self.cfg['y'])
        GLib.timeout_add(800, self._all_desktops)
        GLib.timeout_add(5000, self._all_desktops)
        self.tick()
        GLib.timeout_add_seconds(1, self.tick)

    def fs(self, px):
        return max(8, int(px * self.cfg.get('font_scale', 1.0)))

    def apply_style(self):
        css = b'''
        window, box, grid, eventbox, label, entry, dialog, menubar, headerbar {
            background: transparent; }
        button { background: rgba(30,30,30,0.30); color: #FFFFFF; border: none;
                 border-radius: 6px; padding: 2px 8px; }
        button:hover { background: rgba(90,90,90,0.7); }
        entry { background: rgba(30,30,30,0.35); color: #FFFFFF;
                border: 1px solid rgba(255,255,255,0.35); border-radius: 6px; }
        menu { background: rgba(255,255,255,0.97); color: #000000; }
        menuitem { color: #000000; }
        menuitem:hover { background: rgba(230,230,230,0.95); color: #000000; }
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

    def mk_button(self, label, cb, size=13):
        b = Gtk.Button(label=label)
        b.modify_font(Pango.FontDescription('%s %d' % (DEFAULT_FONT, self.fs(size))))
        b.connect('clicked', cb)
        return b

    def build_rows(self):
        self.timer_label = Gtk.Label()
        self.timer_label.set_halign(Gtk.Align.START)
        self.main_vbox.pack_start(self.timer_label, False, False, 0)

        timer_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        timer_box.set_halign(Gtk.Align.CENTER)
        self.timer_start_btn = self.mk_button('开始', self.on_timer_start)
        self.timer_set_btn = self.mk_button('设置', self.on_timer_set)
        self.timer_reset_btn = self.mk_button('重置', self.on_timer_reset)
        timer_box.pack_start(self.timer_start_btn, False, False, 0)
        timer_box.pack_start(self.timer_set_btn, False, False, 0)
        timer_box.pack_start(self.timer_reset_btn, False, False, 0)
        self.main_vbox.pack_start(timer_box, False, False, 0)

        quick_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        quick_box.set_halign(Gtk.Align.CENTER)
        for mins in (1, 5, 15, 30):
            b = self.mk_button('%d分' % mins, lambda w, m=mins: self.quick_set(m * 60), 12)
            quick_box.pack_start(b, False, False, 0)
        self.main_vbox.pack_start(quick_box, False, False, 0)

        alarm_label = Gtk.Label()
        self.alarm_label = alarm_label
        self.main_vbox.pack_start(self.alarm_label, False, False, 0)

        alarm_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        alarm_box.set_halign(Gtk.Align.CENTER)
        self.alarm_toggle_btn = self.mk_button('开启', self.on_alarm_toggle)
        self.alarm_set_btn = self.mk_button('设置', self.on_alarm_set)
        alarm_box.pack_start(self.alarm_toggle_btn, False, False, 0)
        alarm_box.pack_start(self.alarm_set_btn, False, False, 0)
        self.main_vbox.pack_start(alarm_box, False, False, 0)
        self.update_row_buttons()

    def update_row_buttons(self):
        if self.timer_running:
            self.timer_start_btn.set_label('暂停')
        else:
            self.timer_start_btn.set_label('开始')
        if self.cfg.get('alarm_enabled'):
            self.alarm_toggle_btn.set_label('关闭')
        else:
            self.alarm_toggle_btn.set_label('开启')

    def tick(self):
        now = datetime.datetime.now()
        total = int(self.cfg.get('timer_total', 0))
        end = self.cfg.get('timer_end')
        if end:
            try:
                end = datetime.datetime.fromisoformat(end)
            except Exception:
                end = None
        left = 0
        if end:
            left = int((end - now).total_seconds())
            if left <= 0 and self.timer_running:
                self.timer_running = False
                self.cfg['timer_end'] = None
                self.cfg['timer_total'] = 0
                save_cfg(self.cfg)
                self.show_alert('倒计时结束！', '时间是 ' + fmt_hms(total) + ' 已到')
                play_times(4)
        self.timer_label.set_markup(
            '<span foreground="#42A5F5" font="%s %d" line_height="1.05"><b>倒计时</b>  <b>%s</b></span>'
            % (DEFAULT_FONT, self.fs(18),
               fmt_hms(left if end else 0) if total else '—— —— ——'))

        at = self.cfg.get('alarm_time')
        if at and self.cfg.get('alarm_enabled'):
            try:
                at = datetime.datetime.fromisoformat(at)
                if not self.alarm_pending:
                    fmt = at.strftime('%H:%M:%S')
                    self.alarm_label.set_markup(
                        '<span foreground="#FFEB3B" font="%s %d" line_height="1.05"><b>闹钟</b>  <b>%s</b></span>'
                        % (DEFAULT_FONT, self.fs(18), fmt))
                if not self.cfg.get('alarm_done') and now >= at:
                    self.alarm_pending = True
                    self.cfg['alarm_done'] = True
                    save_cfg(self.cfg)
                    self.show_alert('闹钟时间到！', at.strftime('%H:%M:%S') + ' 到了')
                    play_times(4)
            except Exception:
                pass
        elif self.alarm_label is not None:
            self.alarm_label.set_markup(
                '<span foreground="%s" font="%s %d" line_height="1.05"><b>闹钟</b>  <b>%s</b></span>'
                % ('#FFEB3B' if self.cfg.get('alarm_time') else '#78909C',
                   DEFAULT_FONT, self.fs(18),
                   fmt_hms(int((datetime.datetime.fromisoformat(self.cfg['alarm_time'])
                                - datetime.datetime.now()).total_seconds()))
                   if self.cfg.get('alarm_time') and not self.cfg.get('alarm_done')
                   else '未设置'))
        self.update_row_buttons()
        return True

    def quick_set(self, secs):
        self.timer_running = True
        self.cfg['timer_total'] = secs
        self.cfg['timer_end'] = (datetime.datetime.now()
                                 + datetime.timedelta(seconds=secs)).isoformat()
        save_cfg(self.cfg)
        log('quick timer %d' % secs)
        self.tick()

    def on_timer_start(self, w):
        total = int(self.cfg.get('timer_total', 0))
        if total <= 0:
            self.ask_timer()
            return
        self.timer_running = not self.timer_running
        if self.timer_running:
            self.cfg['timer_end'] = (datetime.datetime.now()
                                     + datetime.timedelta(seconds=total)).isoformat()
        else:
            end = self.cfg.get('timer_end')
            if end:
                try:
                    endt = datetime.datetime.fromisoformat(end)
                    self.cfg['timer_total'] = max(0, int((endt - datetime.datetime.now()).total_seconds()))
                except Exception:
                    pass
            self.cfg['timer_end'] = None
        log('timer running=%s total=%s' % (self.timer_running, self.cfg['timer_total']))
        save_cfg(self.cfg)
        self.tick()

    def on_timer_set(self, w):
        self.ask_timer()

    def on_timer_reset(self, w):
        self.timer_running = False
        self.cfg['timer_end'] = None
        save_cfg(self.cfg)
        self.tick()

    def on_alarm_toggle(self, w):
        if not self.cfg.get('alarm_time'):
            self.ask_alarm()
            return
        if self.cfg.get('alarm_enabled'):
            self.cfg['alarm_enabled'] = False
            self.cfg['alarm_done'] = True
        else:
            now = datetime.datetime.now()
            try:
                at = datetime.datetime.fromisoformat(self.cfg['alarm_time'])
                if at < now:
                    self.ask_alarm()
                    return
            except Exception:
                pass
            self.cfg['alarm_enabled'] = True
            self.cfg['alarm_done'] = False
            self.alarm_pending = False
        save_cfg(self.cfg)
        self.tick()

    def on_alarm_set(self, w):
        self.ask_alarm()

    def ask_timer(self):
        dlg = Gtk.Dialog(title='设置倒计时', transient_for=None)
        dlg.set_decorated(False)
        dlg.set_type_hint(Gdk.WindowTypeHint.DIALOG)
        visual = self.get_screen().get_rgba_visual()
        if visual is not None:
            dlg.set_visual(visual)
        dlg.set_app_paintable(True)
        dlg.add_button('取消', Gtk.ResponseType.CANCEL)
        dlg.add_button('确定', Gtk.ResponseType.OK)
        box = dlg.get_content_area()
        box.set_spacing(8)
        box.set_margin_start(14)
        box.set_margin_end(14)
        box.set_margin_top(12)
        box.set_margin_bottom(12)
        note = Gtk.Label()
        note.set_markup('<span foreground="#FFFFFF" font="%s 13">输入倒计时时长(秒数, 如 300 或 5:00)</span>'
                        % DEFAULT_FONT)
        box.pack_start(note, False, False, 0)
        entry = Gtk.Entry()
        entry.set_width_chars(12)
        entry.modify_font(Pango.FontDescription('%s 14' % DEFAULT_FONT))
        entry.set_text(fmt_hms(int(self.cfg.get('timer_total', 0))) if self.cfg.get('timer_total') else '300')
        entry.set_can_focus(True)
        box.pack_start(entry, False, False, 0)
        dlg.connect('draw', self.dlg_draw)
        dlg.show_all()
        x, y = self.get_position()
        w, h = self.get_size()
        dlg.move(x, y + h + 8)
        entry.grab_focus()
        resp = dlg.run()
        if resp == Gtk.ResponseType.OK:
            secs = parse_hms(entry.get_text())
            if secs:
                self.timer_running = False
                self.cfg['timer_total'] = secs
                self.cfg['timer_end'] = None
                save_cfg(self.cfg)
                self.tick()
        dlg.destroy()

    def ask_alarm(self):
        dlg = Gtk.Dialog(title='设置闹钟', transient_for=None)
        dlg.set_decorated(False)
        dlg.set_type_hint(Gdk.WindowTypeHint.DIALOG)
        visual = self.get_screen().get_rgba_visual()
        if visual is not None:
            dlg.set_visual(visual)
        dlg.set_app_paintable(True)
        dlg.add_button('取消', Gtk.ResponseType.CANCEL)
        dlg.add_button('确定', Gtk.ResponseType.OK)
        box = dlg.get_content_area()
        box.set_spacing(8)
        box.set_margin_start(14)
        box.set_margin_end(14)
        box.set_margin_top(12)
        box.set_margin_bottom(12)
        note = Gtk.Label()
        note.set_markup('<span foreground="#FFFFFF" font="%s 13">输入今天的时间 HH:MM:SS (如 20:30:00)</span>'
                        % DEFAULT_FONT)
        box.pack_start(note, False, False, 0)
        entry = Gtk.Entry()
        entry.set_width_chars(12)
        entry.modify_font(Pango.FontDescription('%s 14' % DEFAULT_FONT))
        entry.set_text('20:00:00')
        entry.set_can_focus(True)
        box.pack_start(entry, False, False, 0)
        dlg.connect('draw', self.dlg_draw)
        dlg.show_all()
        x, y = self.get_position()
        w, h = self.get_size()
        dlg.move(x, y + h + 8)
        entry.grab_focus()
        resp = dlg.run()
        if resp == Gtk.ResponseType.OK:
            t = entry.get_text().strip().replace('：', ':')
            parts = t.split(':')
            try:
                h, m, s = [int(p) for p in parts + ['0', '0'][len(parts):]]
                if len(parts) > 3 or h > 23 or m > 59 or s > 59:
                    raise ValueError
                at = datetime.datetime.now().replace(hour=h, minute=m, second=s, microsecond=0)
            except Exception:
                dlg.destroy()
                return
            if at <= datetime.datetime.now():
                at += datetime.timedelta(days=1)
            self.cfg['alarm_time'] = at.isoformat()
            self.cfg['alarm_enabled'] = True
            self.cfg['alarm_done'] = False
            self.alarm_pending = False
            save_cfg(self.cfg)
            log('alarm set %s' % at)
            self.tick()
        dlg.destroy()

    def dlg_draw(self, widget, cr):
        cr.set_operator(cairo.OPERATOR_SOURCE)
        cr.set_source_rgba(0.08, 0.08, 0.08, 0.55)
        cr.paint()
        return False

    def show_alert(self, title, msg):
        if self.alert_window is not None:
            try:
                self.alert_window.destroy()
            except Exception:
                pass
        dlg = Gtk.Window(Gtk.WindowType.TOPLEVEL)
        dlg.set_decorated(False)
        dlg.set_title(title)
        dlg.set_type_hint(Gdk.WindowTypeHint.NOTIFICATION)
        visual = self.get_screen().get_rgba_visual()
        if visual is not None:
            dlg.set_visual(visual)
        dlg.set_app_paintable(True)
        dlg.set_keep_above(True)
        dlg.set_keep_below(False)
        vbox = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
        vbox.set_margin_start(20)
        vbox.set_margin_end(20)
        vbox.set_margin_top(16)
        vbox.set_margin_bottom(14)
        dlg.add(vbox)
        vbox.add_events(Gdk.EventMask.POINTER_MOTION_MASK |
                        Gdk.EventMask.BUTTON_PRESS_MASK |
                        Gdk.EventMask.BUTTON_RELEASE_MASK)
        vbox.drag_state = None
        vbox.connect('button-press-event', self.dlg_drag_start, dlg)
        vbox.connect('motion-notify-event', self.dlg_drag_move, dlg)
        vbox.connect('button-release-event', self.dlg_drag_end, dlg)
        head = Gtk.Label()
        head.set_markup('<span foreground="#FF5252" font="%s %d" line_height="0.75"><b>%s</b></span>'
                        % (DEFAULT_FONT, self.fs(24), title))
        vbox.pack_start(head, False, False, 0)
        sub = Gtk.Label()
        sub.set_markup('<span foreground="#FFFFFF" font="%s %d" line_height="0.75">%s</span>'
                       % (DEFAULT_FONT, self.fs(16), msg))
        vbox.pack_start(sub, False, False, 0)
        ok = Gtk.Button(label='知道了')
        ok.modify_font(Pango.FontDescription('%s %d' % (DEFAULT_FONT, self.fs(14))))
        ok.set_halign(Gtk.Align.CENTER)
        ok.connect('clicked', lambda w: dlg.destroy())
        vbox.pack_start(ok, False, False, 0)
        dlg.connect('draw', self.dlg_draw)
        x, y = self.get_position()
        w, h = self.get_size()
        dlg.move(x + w // 2 - 120, y + h + 8)
        dlg.resize(240, 150)
        dlg.show_all()
        self.alert_window = dlg

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

    def on_scroll(self, widget, event):
        if self.cfg.get('lock_position', False):
            return True
        if event.direction == Gdk.ScrollDirection.UP:
            self.scale_by(0.1)
        elif event.direction == Gdk.ScrollDirection.DOWN:
            self.scale_by(-0.1)
        return True

    def scale_by(self, delta):
        self.cfg['font_scale'] = max(0.6, min(2.5, self.cfg.get('font_scale', 1.0) + delta))
        save_cfg(self.cfg)
        self.refresh_all()

    def refresh_all(self):
        for lbl in (self.timer_label, self.alarm_label):
            pass
        for b in (self.timer_start_btn, self.timer_set_btn, self.timer_reset_btn,
                  self.alarm_toggle_btn, self.alarm_set_btn):
            b.modify_font(Pango.FontDescription('%s %d' % (DEFAULT_FONT, self.fs(13))))
        self.tick()

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
        lock_item = Gtk.CheckMenuItem(label='锁定位置')
        lock_item.set_active(bool(self.cfg.get('lock_position', False)))
        lock_item.connect('toggled', self.on_lock_toggle)
        menu.append(lock_item)
        menu.append(Gtk.SeparatorMenuItem())
        add('设置倒计时', lambda w: self.ask_timer())
        add('设置闹钟', lambda w: self.ask_alarm())
        menu.append(Gtk.SeparatorMenuItem())
        
        autostart_item = Gtk.CheckMenuItem(label='开机自启')
        autostart_item.set_active(self._autostart_enabled())
        autostart_item.connect('toggled', lambda w: self._toggle_autostart(w.get_active()))
        menu.append(autostart_item)

        menu.append(Gtk.SeparatorMenuItem())
        add('退出', lambda w: Gtk.main_quit())
        menu.show_all()
        menu.popup(None, None, None, None, event.button, event.time)

    def on_lock_toggle(self, item):
        self.cfg['lock_position'] = item.get_active()
        if item.get_active():
            x, y = self.get_position()
            self.cfg['x'], self.cfg['y'] = x, y
        save_cfg(self.cfg)


def main():
    log('timer_alarm widget start')
    win = TimerAlarmWidget()
    Gtk.main()


if __name__ == '__main__':
    main()