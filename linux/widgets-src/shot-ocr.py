#!/usr/bin/env python3
import os
from widget_font import SYS_FONT
# -*- coding: utf-8 -*-
"""截图 / OCR / 翻译 工具（Deepin）
- 选区截图，可保存到截图文件夹
- 截图后识别文字（tesseract），可复制
- 一键翻译（百度翻译 API / Google 免费接口 / MyMemory 免费接口）
- 托盘图标 + 右键菜单
"""
import hashlib
import json
import os
import random
import re
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import urllib.parse
import urllib.request

import gi
gi.require_version('Gtk', '3.0')
gi.require_version('Gdk', '3.0')
try:
    gi.require_version('AyatanaAppIndicator3', '0.1')
    from gi.repository import AyatanaAppIndicator3 as AppIndicator
    HAVE_TRAY = True
except Exception:
    HAVE_TRAY = False
from gi.repository import Gtk, Gdk, GLib, Pango

APP = 'shot-ocr'
HOME = os.path.expanduser('~')
CONF_DIR = os.path.join(HOME, '.config', APP)
CONF_PATH = os.path.join(CONF_DIR, 'config.json')
# 框选截图期间创建的标记文件：划选工具条看到它就不会弹出来
SHOT_FLAG = '/tmp/shot-ocr.capturing'

DEFAULTS = {
    'shot_dir': os.path.join(HOME, 'Pictures', 'Screenshots'),
    'backend': 'baidu',              # baidu / google / mymemory
    'baidu_appid': '20250205002265891',
    'baidu_key': 'IkK3ngwjhkS4lldTunex',
    'to_lang': 'zh',
    'ocr_lang': 'chi_sim+eng',
    'auto_ocr': True,
    'auto_copy': False,
    'auto_trans': False,
    'auto_save': False,
    'hotkey': 'F1',
    'keep_days': 0,
}


def load_cfg():
    cfg = dict(DEFAULTS)
    try:
        with open(CONF_PATH) as f:
            cfg.update(json.load(f))
    except Exception:
        pass
    # 旧版本默认快捷键是 <Super>a，迁移成现在的默认值（F1 等受支持按键）
    known = globals().get('KEY_CODES') or {}
    hk = str(cfg.get('hotkey') or '').strip()
    if known and hk not in known:
        cfg['hotkey'] = DEFAULTS['hotkey']
    return cfg


def save_cfg(cfg):
    try:
        os.makedirs(CONF_DIR, exist_ok=True)
        with open(CONF_PATH, 'w') as f:
            json.dump(cfg, f, ensure_ascii=False, indent=2)
    except Exception:
        pass


# ─────────────────────────── 翻译 ───────────────────────────

LANG_MAP = {
    'baidu': {'zh': 'zh', 'en': 'en', 'ja': 'jp', 'ko': 'kor', 'fr': 'fra',
              'de': 'de', 'ru': 'ru', 'es': 'spa', 'auto': 'auto'},
    'google': {'zh': 'zh-CN', 'en': 'en', 'ja': 'ja', 'ko': 'ko', 'fr': 'fr',
               'de': 'de', 'ru': 'ru', 'es': 'es', 'auto': 'auto'},
    'mymemory': {'zh': 'zh-CN', 'en': 'en', 'ja': 'ja', 'ko': 'ko', 'fr': 'fr',
                 'de': 'de', 'ru': 'ru', 'es': 'es', 'auto': 'auto'},
}

LANG_NAME = {'zh': '中文', 'en': '英文', 'ja': '日文', 'ko': '韩文',
             'fr': '法文', 'de': '德文', 'ru': '俄文', 'es': '西班牙文'}


def _http(url, data=None, timeout=15):
    req = urllib.request.Request(
        url, data=data,
        headers={'User-Agent': 'Mozilla/5.0 (X11; Linux x86_64) shot-ocr/1.0'})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read().decode('utf-8', 'replace')


def tr_baidu(text, to, cfg):
    appid = cfg.get('baidu_appid', '').strip()
    key = cfg.get('baidu_key', '').strip()
    if not appid or not key:
        raise RuntimeError('未配置百度翻译 APP ID / 密钥')
    salt = str(random.randint(10000, 99999))
    sign = hashlib.md5((appid + text + salt + key).encode('utf-8')).hexdigest()
    params = {'q': text, 'from': 'auto', 'to': LANG_MAP['baidu'].get(to, 'zh'),
              'appid': appid, 'salt': salt, 'sign': sign}
    body = _http('https://fanyi-api.baidu.com/api/trans/vip/translate',
                 urllib.parse.urlencode(params).encode('utf-8')).encode('utf-8')
    js = json.loads(body.decode('utf-8'))
    if 'error_code' in js:
        raise RuntimeError('百度翻译错误 %s: %s' % (js['error_code'], js.get('error_msg', '')))
    return '\n'.join(x['dst'] for x in js.get('trans_result', []))


def tr_google(text, to, cfg):
    q = urllib.parse.quote(text)
    tl = LANG_MAP['google'].get(to, 'zh-CN')
    url = ('https://translate.googleapis.com/translate_a/single'
           '?client=gtx&sl=auto&tl=%s&dt=t&q=%s' % (tl, q))
    js = json.loads(_http(url))
    return ''.join(part[0] for part in js[0] if part and part[0])


def guess_source(text, tl):
    """MyMemory 不接受 auto|xx，需要具体源语言"""
    src = 'zh-CN'
    if not any('\u4e00' <= ch <= '\u9fff' for ch in text):
        src = 'en'
    if src == tl:
        src = 'en' if tl != 'en' else 'zh-CN'
    return src


def tr_mymemory(text, to, cfg):
    tl = LANG_MAP['mymemory'].get(to, 'zh-CN')
    src = guess_source(text, tl)
    url = ('https://api.mymemory.translated.net/get?q=%s&langpair=%s|%s'
           % (urllib.parse.quote(text), src, tl))
    js = json.loads(_http(url))
    out = js.get('responseData', {}).get('translatedText')
    if not out:
        raise RuntimeError('MyMemory 返回为空（可能超出免费额度）')
    return out


def translate(text, to, cfg):
    """按配置的后端翻译；百度失败时自动回退到免费接口"""
    backend = cfg.get('backend', 'baidu')
    order = {'baidu': [tr_baidu, tr_google, tr_mymemory],
             'google': [tr_google, tr_mymemory, tr_baidu],
             'mymemory': [tr_mymemory, tr_google, tr_baidu]}[backend]
    last = None
    for fn in order:
        try:
            return fn(text, to, cfg)
        except Exception as e:
            last = e
    raise RuntimeError('翻译失败：%s' % last)


# ─────────────────────────── OCR ───────────────────────────

def ocr_available():
    return os.path.exists('/usr/local/bin/tesseract') or \
        bool(shutil.which('tesseract'))


def preprocess_png(src_path, dst_path, scale=2.0):
    """OCR 前处理：放大 → 灰度 → 二值化（自动判断是否反色）
    对深色背景上的彩色文字效果提升明显"""
    import cairo
    surf = cairo.ImageSurface.create_from_png(src_path)
    w, h = surf.get_width(), surf.get_height()
    # 大图不放大、均值用抽样统计，避免处理时间过长
    if w * h > 1500000:
        scale = 1.0
    elif w * h > 600000:
        scale = min(scale, 1.5)
    sw, sh = int(w * scale), int(h * scale)
    out = cairo.ImageSurface(cairo.FORMAT_RGB24, sw, sh)
    ctx = cairo.Context(out)
    ctx.scale(scale, scale)
    ctx.set_source_surface(surf, 0, 0)
    ctx.paint()
    out.flush()
    data = bytearray(out.get_data())
    stride = out.get_stride()
    total = n = 0
    step = 3 if sw * sh > 400000 else 1
    for y in range(0, sh, step):
        row = y * stride
        for x in range(0, sw, step):
            o = row + x * 4
            b, g, r = data[o], data[o + 1], data[o + 2]
            total += (r * 299 + g * 587 + b * 114) // 1000
            n += 1
    mean = total / float(n or 1)
    thr = max(60, min(200, mean))
    invert = mean < 128
    for y in range(sh):
        row = y * stride
        for x in range(sw):
            o = row + x * 4
            b, g, r = data[o], data[o + 1], data[o + 2]
            lum = (r * 299 + g * 587 + b * 114) // 1000
            v = 255 if (lum <= thr if invert else lum > thr) else 0
            if invert:
                v = 255 - v
            data[o] = data[o + 1] = data[o + 2] = v
    out.mark_dirty()
    out.write_to_png(dst_path)
    return dst_path


def ocr_image(path, lang='chi_sim+eng'):
    exe = '/usr/local/bin/tesseract'
    if not os.path.exists(exe):
        exe = shutil.which('tesseract')
    if not exe:
        raise RuntimeError('未安装 OCR 引擎（tesseract-ocr）\n'
                           '请执行：sudo apt install tesseract-ocr tesseract-ocr-chi-sim')
    langs = []
    try:
        langs = subprocess.run([exe, '--list-langs'], capture_output=True,
                               text=True, timeout=15).stdout.split()
    except Exception:
        pass
    use = [l for l in lang.split('+') if l in langs] or ['eng']
    cmd = [exe, path, 'stdout', '-l', '+'.join(use)]
    r = subprocess.run(cmd, capture_output=True, text=True, timeout=120)
    text = (r.stdout or '').strip()
    if not text:
        text = (r.stderr or '').strip()
    return text


# ─────────────────────────── 截图选区 ───────────────────────────

import cairo


class ShotOverlay(Gtk.Window):
    """全屏覆盖层：拖拽框选，松开后回调 (x, y, w, h)"""

    def __init__(self, on_done):
        super().__init__(type=Gtk.WindowType.TOPLEVEL)
        self.on_done = on_done
        self.set_decorated(False)
        self.set_app_paintable(True)
        self.set_keep_above(True)
        self.set_skip_taskbar_hint(True)
        self.set_skip_pager_hint(True)
        self.set_accept_focus(True)
        try:
            self.set_type_hint(Gdk.WindowTypeHint.DOCK)
        except Exception:
            pass
        scr = self.get_screen()
        vis = scr.get_rgba_visual()
        if vis:
            self.set_visual(vis)
        self.set_default_size(scr.get_width(), scr.get_height())
        self.add_events(Gdk.EventMask.BUTTON_PRESS_MASK |
                        Gdk.EventMask.BUTTON_RELEASE_MASK |
                        Gdk.EventMask.POINTER_MOTION_MASK |
                        Gdk.EventMask.KEY_PRESS_MASK)
        self.connect('draw', self.on_draw)
        self.connect('button-press-event', self.on_press)
        self.connect('motion-notify-event', self.on_motion)
        self.connect('button-release-event', self.on_release)
        self.connect('key-press-event', self.on_key)
        self.start = None
        self.end = None
        try:
            open(SHOT_FLAG, 'w').close()      # 告诉划选工具条：正在框选
        except OSError:
            pass
        self.connect('destroy', self._clear_flag)
        self.show_all()
        self.move(0, 0)
        self.grab_focus()

    def _clear_flag(self, w=None):
        try:
            os.unlink(SHOT_FLAG)
        except OSError:
            pass

    def on_key(self, w, e):
        if e.keyval == Gdk.KEY_Escape:
            self.destroy()
            self.on_done(None)
            return True
        return False

    def on_draw(self, w, cr):
        cr.set_operator(cairo.OPERATOR_SOURCE)
        cr.set_source_rgba(0, 0, 0, 0.35)
        cr.paint()
        cr.set_operator(cairo.OPERATOR_OVER)
        if self.start and self.end:
            x0, y0, x1, y1 = self._rect()
            cr.set_source_rgba(1, 1, 1, 0.12)
            cr.rectangle(x0, y0, x1 - x0, y1 - y0)
            cr.fill()
            cr.set_source_rgba(0.2, 0.65, 1.0, 1)
            cr.set_line_width(2)
            cr.rectangle(x0 + .5, y0 + .5, x1 - x0, y1 - y0)
            cr.stroke()
            label = '%d × %d' % (x1 - x0, y1 - y0)
            cr.set_source_rgba(0, 0, 0, 0.7)
            cr.rectangle(x0, max(0, y0 - 26), 96, 24)
            cr.fill()
            cr.set_source_rgba(1, 1, 1, 1)
            cr.select_font_face(SYS_FONT, cairo.FONT_SLANT_NORMAL,
                                cairo.FONT_WEIGHT_BOLD)
            cr.set_font_size(15)
            cr.move_to(x0 + 8, max(16, y0 - 9))
            cr.show_text(label)

    def _rect(self):
        x0 = min(self.start[0], self.end[0])
        y0 = min(self.start[1], self.end[1])
        x1 = max(self.start[0], self.end[0])
        y1 = max(self.start[1], self.end[1])
        return x0, y0, x1, y1

    def on_press(self, w, e):
        self.start = (e.x, e.y)
        self.end = (e.x, e.y)
        self.queue_draw()
        return True

    def on_motion(self, w, e):
        if self.start:
            self.end = (e.x, e.y)
            self.queue_draw()
        return True

    def on_release(self, w, e):
        if not self.start:
            return True
        self.end = (e.x, e.y)
        x0, y0, x1, y1 = self._rect()
        self.hide()
        if x1 - x0 < 6 or y1 - y0 < 6:
            self.destroy()
            self.on_done(None)
            return True
        GLib.timeout_add(220, self._finish, x0, y0, x1 - x0, y1 - y0)
        return True

    def _finish(self, x, y, w, h):
        try:
            root = Gdk.get_default_root_window()
            pb = Gdk.pixbuf_get_from_window(root, x, y, w, h)
        except Exception:
            pb = None
        self.destroy()
        self.on_done(pb)
        return False


# ─────────────────────────── 结果窗口 ───────────────────────────

class ResultWindow(Gtk.Window):
    def __init__(self, cfg, app=None):
        super().__init__(title='截图 / OCR / 翻译')
        self.cfg = cfg
        self.app = app
        self.pixbuf = None
        self.shot_path = None
        self.set_default_size(780, 640)
        self.set_icon_name(APP)
        self.set_position(Gtk.WindowPosition.CENTER)
        self.connect('delete-event', self.on_delete)
        self.connect('key-press-event', self.on_key)
        self._build()

    def on_key(self, w, e):
        """按 ESC 隐藏面板"""
        if e.keyval == Gdk.KEY_Escape:
            self.hide()
            return True
        return False

    def on_delete(self, w, e):
        self.hide()
        return True

    def _build(self):
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
        box.set_border_width(10)
        self.add(box)

        self.img = Gtk.Image()
        self.img.set_halign(Gtk.Align.CENTER)
        frame = Gtk.Frame()
        frame.add(self.img)
        box.pack_start(frame, False, False, 0)

        nb = Gtk.Notebook()
        box.pack_start(nb, True, True, 0)

        sw1 = Gtk.ScrolledWindow()
        self.tv_src = Gtk.TextView()
        self.tv_src.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
        self.tv_src.override_font(Pango.FontDescription(SYS_FONT + ' 12'))
        sw1.add(self.tv_src)
        nb.append_page(sw1, Gtk.Label(label='识别文字（原文）'))

        sw2 = Gtk.ScrolledWindow()
        self.tv_dst = Gtk.TextView()
        self.tv_dst.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
        self.tv_dst.override_font(Pango.FontDescription(SYS_FONT + ' 12'))
        sw2.add(self.tv_dst)
        nb.append_page(sw2, Gtk.Label(label='翻译结果'))
        self.nb = nb

        row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6)
        box.pack_start(row, False, False, 0)

        def btn(text, cb, tip=None):
            b = Gtk.Button(label=text)
            if tip:
                b.set_tooltip_text(tip)
            b.connect('clicked', cb)
            row.pack_start(b, False, False, 0)
            return b

        btn('🔍 识别文字', lambda w: self.do_ocr())
        btn('🌐 翻译', lambda w: self.do_translate())
        btn('📋 复制原文', lambda w: self.copy(self.get_text(self.tv_src), '原文'))
        btn('📋 复制译文', lambda w: self.copy(self.get_text(self.tv_dst), '译文'))
        btn('💾 保存图片', lambda w: self.save_image())
        btn('📄 保存文字', lambda w: self.save_text())
        btn('📂 截图文件夹', lambda w: self.open_shot_dir())
        btn('⚙ 设置', lambda w: self.on_settings())

        self.lang_btn = Gtk.ComboBoxText()
        for code, name in LANG_NAME.items():
            self.lang_btn.append(code, '译为%s' % name)
        self.lang_btn.set_active_id(self.cfg.get('to_lang', 'zh'))
        self.lang_btn.connect('changed', self.on_lang)
        row.pack_end(self.lang_btn, False, False, 0)

        self.status = Gtk.Label(label='就绪')
        self.status.set_xalign(0)
        self.status.set_ellipsize(3)
        box.pack_start(self.status, False, False, 0)

    def on_settings(self):
        if self.app:
            self.app.settings()
        else:
            d = SettingsDialog(self, self.cfg)
            if d.run() == Gtk.ResponseType.OK:
                d.apply()
            d.destroy()

    # ---------- 基础操作 ----------

    def get_text(self, tv):
        b = tv.get_buffer()
        return b.get_text(b.get_start_iter(), b.get_end_iter(), False)

    def set_text(self, tv, text):
        tv.get_buffer().set_text(text or '')

    def copy(self, text, what='内容'):
        if not text.strip():
            self.set_status('没有可复制的%s' % what)
            return
        cb = Gtk.Clipboard.get(Gdk.SELECTION_CLIPBOARD)
        cb.set_text(text, -1)
        cb.store()
        self.set_status('已复制%s（%d 字）' % (what, len(text)))

    def on_lang(self, w):
        self.cfg['to_lang'] = self.lang_btn.get_active_id() or 'zh'
        save_cfg(self.cfg)

    def set_status(self, text):
        GLib.idle_add(self.status.set_text, text)

    def open_shot_dir(self):
        """打开截图文件夹（依次尝试系统里的文件管理器，失败时提示路径）"""
        d = self.cfg.get('shot_dir') or DEFAULTS['shot_dir']
        try:
            os.makedirs(d, exist_ok=True)
        except Exception:
            pass
        for cmd in (['dde-file-manager', d], ['xdg-open', d],
                    ['nemo', d], ['thunar', d], ['pcmanfm', d]):
            if shutil.which(cmd[0]):
                try:
                    subprocess.Popen(cmd, stdout=subprocess.DEVNULL,
                                     stderr=subprocess.DEVNULL)
                    self.set_status('已打开截图文件夹：%s' % d)
                    return
                except Exception:
                    continue
        self.set_status('截图文件夹：%s（未找到文件管理器，可手动打开）' % d)

    # ---------- 图片 ----------

    def set_image(self, pb, path=None):
        self.pixbuf = pb
        self.shot_path = path
        w, h = pb.get_width(), pb.get_height()
        max_w, max_h = 740, 300
        if w > max_w or h > max_h:
            scale = min(max_w / float(w), max_h / float(h))
            pb2 = pb.scale_simple(int(w * scale), int(h * scale), 2)
        else:
            pb2 = pb
        self.img.set_from_pixbuf(pb2)
        self.set_status('截图 %d × %d' % (w, h))

    def save_image(self):
        if not self.pixbuf:
            self.set_status('还没有截图')
            return None
        d = self.cfg.get('shot_dir')
        os.makedirs(d, exist_ok=True)
        name = '截图_%s.png' % time.strftime('%Y%m%d_%H%M%S')
        path = os.path.join(d, name)
        try:
            self.pixbuf.savev(path, 'png', [], [])
        except Exception as e:
            self.set_status('保存失败：%s' % e)
            return None
        self.shot_path = path
        self.set_status('图片已保存到 %s' % path)
        return path

    def save_text(self):
        text = self.get_text(self.tv_dst) or self.get_text(self.tv_src)
        if not text.strip():
            self.set_status('没有可保存的文字')
            return
        d = self.cfg.get('shot_dir')
        os.makedirs(d, exist_ok=True)
        path = os.path.join(d, '文字_%s.txt' % time.strftime('%Y%m%d_%H%M%S'))
        with open(path, 'w', encoding='utf-8') as f:
            f.write(text)
        self.set_status('文字已保存到 %s' % path)

    # ---------- OCR / 翻译 ----------

    def do_ocr(self):
        if not self.pixbuf:
            self.set_status('请先截图（托盘菜单 → 截图）')
            return
        self.set_status('正在识别文字…')

        def work():
            try:
                fd, tmp = tempfile.mkstemp(suffix='.png', prefix='shot-ocr-')
                os.close(fd)
                self.pixbuf.savev(tmp, 'png', [], [])
                src = tmp
                try:
                    src = preprocess_png(tmp, tmp + '.pre.png')
                except Exception:
                    src = tmp
                text = ocr_image(src, self.cfg.get('ocr_lang', 'chi_sim+eng'))
                for p in (tmp, tmp + '.pre.png'):
                    try:
                        os.unlink(p)
                    except OSError:
                        pass
            except Exception as e:
                self.set_status('识别失败：%s' % e)
                return
            GLib.idle_add(self._ocr_done, text)

        threading.Thread(target=work, daemon=True).start()

    def _ocr_done(self, text):
        self.set_text(self.tv_src, text)
        self.set_status('识别完成（%d 字）' % len(text.strip()))
        if self.cfg.get('auto_copy') and text.strip():
            self.copy(text, '原文')
        if self.cfg.get('auto_trans') and text.strip():
            self.do_translate()

    def do_translate(self):
        text = self.get_text(self.tv_src).strip()
        if not text:
            self.set_status('没有可翻译的文字（先识别）')
            return
        to = self.cfg.get('to_lang', 'zh')
        cfg = dict(self.cfg)
        self.set_status('正在翻译（%s → %s）…'
                        % (cfg.get('backend'), LANG_NAME.get(to, to)))

        def work():
            try:
                out = translate(text, to, cfg)
            except Exception as e:
                GLib.idle_add(self.set_status, '翻译失败：%s' % e)
                return
            GLib.idle_add(self._trans_done, out)

        threading.Thread(target=work, daemon=True).start()

    def _trans_done(self, out):
        self.set_text(self.tv_dst, out)
        self.nb.set_current_page(1)
        self.set_status('翻译完成（%d 字）' % len(out.strip()))


# ─────────────────────────── 设置 ───────────────────────────

class SettingsDialog(Gtk.Dialog):
    def __init__(self, parent, cfg):
        super().__init__(title='截图 OCR 翻译 · 设置', transient_for=parent, modal=True)
        self.cfg = cfg
        self.set_default_size(620, 460)
        self.add_button('取消', Gtk.ResponseType.CANCEL)
        self.add_button('保存', Gtk.ResponseType.OK)
        box = self.get_content_area()
        box.set_spacing(8)
        box.set_border_width(12)

        grid = Gtk.Grid(column_spacing=10, row_spacing=8)
        box.pack_start(grid, False, False, 0)
        r = 0

        def row(label, widget):
            nonlocal r
            lab = Gtk.Label(label=label)
            lab.set_xalign(0)
            grid.attach(lab, 0, r, 1, 1)
            grid.attach(widget, 1, r, 1, 1)
            r += 1

        self.appid = Gtk.Entry()
        self.appid.set_text(cfg.get('baidu_appid', ''))
        row('百度翻译 APP ID', self.appid)
        self.key = Gtk.Entry()
        self.key.set_text(cfg.get('baidu_key', ''))
        row('百度翻译密钥', self.key)

        self.backend = Gtk.ComboBoxText()
        for k, n in (('baidu', '百度翻译（推荐）'), ('google', 'Google 免费接口'),
                     ('mymemory', 'MyMemory 免费接口')):
            self.backend.append(k, n)
        self.backend.set_active_id(cfg.get('backend', 'baidu'))
        row('翻译引擎', self.backend)

        self.to_lang = Gtk.ComboBoxText()
        for k, n in LANG_NAME.items():
            self.to_lang.append(k, n)
        self.to_lang.set_active_id(cfg.get('to_lang', 'zh'))
        row('翻译目标语言', self.to_lang)

        self.ocr_lang = Gtk.ComboBoxText()
        for k, n in (('chi_sim+eng', '中英混合'), ('chi_sim', '仅中文'),
                     ('eng', '仅英文')):
            self.ocr_lang.append(k, n)
        self.ocr_lang.set_active_id(cfg.get('ocr_lang', 'chi_sim+eng'))
        row('识别语言', self.ocr_lang)

        dbox = Gtk.Box(spacing=6)
        self.dir_entry = Gtk.Entry()
        self.dir_entry.set_text(cfg.get('shot_dir', ''))
        dbox.pack_start(self.dir_entry, True, True, 0)
        pick = Gtk.Button(label='选择…')
        pick.connect('clicked', self.pick_dir)
        dbox.pack_start(pick, False, False, 0)
        row('截图保存位置', dbox)

        self._new_hotkey = None
        self.hotkey_btn = Gtk.Button(label=cfg.get('hotkey', 'F1'))
        self.hotkey_btn.connect('clicked', self.capture_hotkey)
        row('触发截图快捷键（点它再按新键）', self.hotkey_btn)

        self.auto_ocr = Gtk.CheckButton(label='截图后自动识别文字')
        self.auto_ocr.set_active(bool(cfg.get('auto_ocr', True)))
        box.pack_start(self.auto_ocr, False, False, 0)
        self.auto_trans = Gtk.CheckButton(label='识别后自动翻译')
        self.auto_trans.set_active(bool(cfg.get('auto_trans', False)))
        box.pack_start(self.auto_trans, False, False, 0)
        self.auto_copy = Gtk.CheckButton(label='识别后自动复制文字到剪贴板')
        self.auto_copy.set_active(bool(cfg.get('auto_copy', False)))
        box.pack_start(self.auto_copy, False, False, 0)
        self.auto_save = Gtk.CheckButton(label='每次截图都自动保存到截图文件夹')
        self.auto_save.set_active(bool(cfg.get('auto_save', False)))
        box.pack_start(self.auto_save, False, False, 0)

        tip = Gtk.Label(label='提示：命令行参数 --shot（截图保存）/ --ocr（截图识别）/ '
                              '--translate（截图翻译），可在系统设置里绑定快捷键；\n'
                              '当前默认快捷键 %s' % cfg.get('hotkey', '<Super>a'))
        tip.set_xalign(0)
        tip.set_line_wrap(True)
        box.pack_start(tip, False, False, 6)
        self.show_all()

    def capture_hotkey(self, w):
        """弹出小窗口，按下哪个键就用哪个键"""
        d = Gtk.Dialog(title='按下要绑定的键', transient_for=self, modal=True)
        d.set_default_size(360, 110)
        lab = Gtk.Label(label='请按下新的快捷键（例如 F1 / F8 / Print / Pause）')
        d.get_content_area().add(lab)
        d.show_all()

        def on_key(widget, event):
            name = norm_key(Gdk.keyval_name(event.keyval))
            if not name:
                return True
            if name in KEY_CODES:
                self._new_hotkey = name
                self.hotkey_btn.set_label(name)
                d.destroy()
            else:
                self.hotkey_btn.set_label('%s 不支持' % name)
            return True

        d.connect('key-press-event', on_key)
        d.run()
        d.destroy()

    def pick_dir(self, w):
        d = Gtk.FileChooserDialog(title='选择截图保存目录', parent=self,
                                  action=Gtk.FileChooserAction.SELECT_FOLDER)
        d.add_button('取消', Gtk.ResponseType.CANCEL)
        d.add_button('确定', Gtk.ResponseType.OK)
        if d.run() == Gtk.ResponseType.OK:
            self.dir_entry.set_text(d.get_filename())
        d.destroy()

    def apply(self):
        c = self.cfg
        if self._new_hotkey:
            c['hotkey'] = self._new_hotkey
        c['baidu_appid'] = self.appid.get_text().strip()
        c['baidu_key'] = self.key.get_text().strip()
        c['backend'] = self.backend.get_active_id() or 'baidu'
        c['to_lang'] = self.to_lang.get_active_id() or 'zh'
        c['ocr_lang'] = self.ocr_lang.get_active_id() or 'chi_sim+eng'
        c['shot_dir'] = self.dir_entry.get_text().strip() or DEFAULTS['shot_dir']
        c['auto_ocr'] = self.auto_ocr.get_active()
        c['auto_trans'] = self.auto_trans.get_active()
        c['auto_copy'] = self.auto_copy.get_active()
        c['auto_save'] = self.auto_save.get_active()
        save_cfg(c)


# ─────────────────────── 全局快捷键（evdev 键盘） ───────────────────────

KEY_CODES = {
    'F1': 59, 'F2': 60, 'F3': 61, 'F4': 62, 'F5': 63, 'F6': 64,
    'F7': 65, 'F8': 66, 'F9': 67, 'F10': 68, 'F11': 87, 'F12': 88,
    'Print': 99, 'SysRq': 99, 'ScrollLock': 70, 'Pause': 119,
    'Insert': 110, 'Home': 102, 'End': 107, 'PageUp': 104, 'PageDown': 109,
    'Space': 57, 'Tab': 15, 'Grave': 41, 'Delete': 111,
    'A': 30, 'B': 48, 'C': 46, 'D': 32, 'E': 18, 'F': 33, 'G': 34,
    'H': 35, 'I': 23, 'J': 36, 'K': 37, 'L': 38, 'M': 50, 'N': 49,
    'O': 24, 'P': 25, 'Q': 16, 'R': 19, 'S': 31, 'T': 20, 'U': 22,
    'V': 47, 'W': 17, 'X': 45, 'Y': 21, 'Z': 44,
    '1': 2, '2': 3, '3': 4, '4': 5, '5': 6, '6': 7, '7': 8, '8': 9,
    '9': 10, '0': 11,
}


def norm_key(name):
    """把 GTK 键名规范成配置里保存的名字"""
    if not name:
        return None
    n = name.strip()
    if len(n) == 1:
        return n.upper()
    low = n.lower()
    if low.startswith('f') and low[1:].isdigit():
        return 'F%d' % int(low[1:])
    table = {'space': 'Space', 'pause': 'Pause', 'print': 'Print',
             'insert': 'Insert', 'home': 'Home', 'end': 'End', 'tab': 'Tab',
             'grave': 'Grave', 'delete': 'Delete', 'page_up': 'PageUp',
             'page_down': 'PageDown', 'scroll_lock': 'ScrollLock',
             'sys_req': 'Print'}
    return table.get(low, n)


class HotkeyWatcher:
    """用 evdev 只读监听键盘（不抓取、不拦截），按下快捷键就触发回调"""

    def __init__(self, cfg, on_hotkey, on_escape=None):
        import glob as _glob
        self._glob = _glob
        self.cfg = cfg
        self.on_hotkey = on_hotkey
        self.on_escape = on_escape or (lambda: False)
        self.fds = []
        self._last = 0.0
        self._last_scan = time.time()
        self.set_hotkey(cfg.get('hotkey', 'F1'))
        self._rescan()
        GLib.timeout_add(20, self._poll)

    def set_hotkey(self, name):
        self.code = KEY_CODES.get(norm_key(name) or '', 59)

    def _rescan(self):
        have = {p for _, p in self.fds}
        for path in sorted(self._glob.glob('/dev/input/event*')):
            if path in have:
                continue
            try:
                self.fds.append((os.open(path, os.O_RDONLY | os.O_NONBLOCK), path))
            except Exception:
                pass

    def _poll(self):
        import struct as _struct
        import time as _time
        now = _time.time()
        for fd, path in list(self.fds):
            try:
                while True:
                    data = os.read(fd, 4096)
                    if not data:
                        break
                    buf = data
                    while len(buf) >= 24:
                        _s, _u, etype, code, value = _struct.unpack('llHHi', buf[:24])
                        buf = buf[24:]
                        if etype == 1 and value == 1:
                            # ESC 取消当前框选。全屏覆盖层在 DDE/KWin 下
                            # 常常拿不到键盘焦点，GTK 的 key-press-event
                            # 收不到 ESC，所以统一在 evdev 这条路上处理。
                            #
                            # 这里不能走 GLib.idle_add：覆盖层存活期间 GTK
                            # 主循环持续处理鼠标事件，idle 源优先级低会被
                            # 一直压后，表现为按 ESC 没反应。直接调用
                            # 销毁动作，保证同步生效。
                            if code == 1:
                                try:
                                    self.on_escape()
                                except Exception:
                                    pass
                            elif (code == self.code
                                    and now - self._last > 0.35):
                                self._last = now
                                GLib.idle_add(self.on_hotkey)
            except BlockingIOError:
                continue
            except OSError:
                try:
                    os.close(fd)
                except Exception:
                    pass
                self.fds = [(f, p) for f, p in self.fds if f != fd]
            except Exception:
                pass
        # 键盘设备可能在程序启动之后才插上（无线接收器/USB 键盘延迟枚举），
        # 只在 __init__ 扫一次会永远漏掉后来出现的设备，导致快捷键无反应。
        if now - self._last_scan > 5.0:
            self._last_scan = now
            self._rescan()
        return True


# ─────────────────────────── 主程序 ───────────────────────────

import socket

SOCK_PATH = os.path.join(tempfile.gettempdir(), 'shot-ocr.sock')
ICON_PATH = '/usr/share/icons/hicolor/scalable/apps/shot-ocr.svg'


class ShotApp:
    def __init__(self, cfg):
        self.cfg = cfg
        self.win = ResultWindow(cfg, self)
        self.overlay = None
        self.tray = None
        self.hotkey = HotkeyWatcher(cfg, self.on_hotkey, self.on_escape)
        self._make_indicator()
        self.srv = None
        self._start_server()

    def on_hotkey(self):
        """按下设定的快捷键（默认 F1）→ 截图并识别"""
        self.shot('ocr')
        return False

    def on_escape(self):
        """ESC 取消正在进行的框选（同步执行，不排队）。

        全屏覆盖层拿不到键盘焦点时 GTK 收不到 ESC，所以统一由
        HotkeyWatcher 在 evdev 层拦截并直接派发到这里。
        """
        ov = self.overlay
        if ov is None:
            return False
        # 先摘掉引用，避免 destroy 触发的回调里再次进入
        self.overlay = None
        try:
            if ov.get_visible():
                ov.destroy()
        except Exception:
            pass
        try:
            ov.on_done(None)
        except Exception:
            pass
        return False

    # ---------- 托盘 ----------
    def _make_indicator(self):
        """用 Gtk.StatusIcon（与鼠标炫彩拖尾一致）：
        菜单由本进程渲染 → 字号正常、白底黑字，且支持悬停提示"""
        self.tray = None
        menu = Gtk.Menu()
        head = Gtk.MenuItem(label='截图 OCR 翻译（快捷键 %s）'
                            % self.cfg.get('hotkey', 'F1'))
        head.set_sensitive(False)
        menu.append(head)
        menu.append(Gtk.SeparatorMenuItem())

        def add(label, cb):
            it = Gtk.MenuItem(label=label)
            it.connect('activate', cb)
            menu.append(it)

        add('📷 截图并识别文字', lambda w: self.shot('ocr'))
        add('🌐 截图并翻译', lambda w: self.shot('translate'))
        add('💾 截图并保存', lambda w: self.shot('save'))
        menu.append(Gtk.SeparatorMenuItem())
        add('🪟 显示结果窗口', lambda w: self.show_window())
        add('📂 打开截图文件夹', lambda w: self.open_dir())
        add('⚙ 设置', lambda w: self.settings())
        add('ℹ 关于', lambda w: self.about())
        menu.append(Gtk.SeparatorMenuItem())
        add('🚪 退出', lambda w: self.quit())
        menu.show_all()
        self.menu = menu
        try:
            icon = Gtk.StatusIcon()
            icon.set_from_icon_name(APP)
            if not icon.get_icon_name():
                icon.set_from_file(ICON_PATH)
            icon.set_tooltip_text('截图 OCR 翻译（快捷键 %s）— 右键打开菜单'
                                  % self.cfg.get('hotkey', 'F1'))
            icon.connect('popup-menu', self._on_tray_popup)
            icon.connect('activate', lambda i: self.show_window())
            icon.set_visible(True)
            self.tray = icon
        except Exception:
            self.tray = None

    def _on_tray_popup(self, icon, button, activate_time):
        self.menu.popup(None, None, Gtk.StatusIcon.position_menu, icon,
                        button, activate_time)

    # ---------- 单实例通信 ----------
    def _start_server(self):
        try:
            if os.path.exists(SOCK_PATH):
                os.unlink(SOCK_PATH)
            s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            s.bind(SOCK_PATH)
            s.listen(4)
            s.setblocking(False)
            self.srv = s
            GLib.io_add_watch(s.fileno(), GLib.IO_IN, self._on_conn)
        except Exception:
            self.srv = None

    def _on_conn(self, fd, cond):
        try:
            conn, _ = self.srv.accept()
            data = conn.recv(256).decode('utf-8', 'replace').strip()
            conn.close()
        except Exception:
            return True
        cmd = data or 'open'
        GLib.idle_add(self.handle, cmd)
        return True

    def handle(self, cmd):
        if cmd in ('save', 'shot'):
            self.shot('save')
        elif cmd == 'ocr':
            self.shot('ocr')
        elif cmd == 'translate':
            self.shot('translate')
        else:
            self.show_window()
        return False

    # ---------- 动作 ----------
    def startup(self, mode, autostart=False):
        if mode:
            self.handle(mode)
        elif not autostart:
            self.show_window()

    def shot(self, mode='ocr'):
        if self.overlay is not None:
            return
        self.overlay = ShotOverlay(lambda pb: self._shot_done(pb, mode))

    def _shot_done(self, pb, mode):
        self.overlay = None
        if pb is None:
            return
        self.win.set_image(pb)
        self.win.show_all()
        self.win.present()
        if mode == 'save' or self.cfg.get('auto_save'):
            self.win.save_image()
        if mode in ('ocr', 'translate'):
            if mode == 'translate':
                self.cfg['auto_trans'] = True
            self.win.do_ocr()

    def show_window(self):
        self.win.show_all()
        self.win.present()

    def open_dir(self):
        self.win.open_shot_dir()

    def settings(self):
        d = SettingsDialog(self.win, self.cfg)
        if d.run() == Gtk.ResponseType.OK:
            d.apply()
            self.win.lang_btn.set_active_id(self.cfg.get('to_lang', 'zh'))
            self.hotkey.set_hotkey(self.cfg.get('hotkey', 'F1'))
            self.win.set_status('设置已保存（截图快捷键 %s）' % self.cfg.get('hotkey'))
        d.destroy()

    def about(self):
        d = Gtk.MessageDialog(transient_for=self.win, modal=True,
                              message_type=Gtk.MessageType.INFO,
                              buttons=Gtk.ButtonsType.OK,
                              text='截图 OCR 翻译 1.0.0')
        d.format_secondary_text(
            '截图 → 识别文字 → 翻译\n'
            'OCR 引擎：tesseract（需安装 tesseract-ocr / tesseract-ocr-chi-sim）\n'
            '翻译引擎：百度翻译 API / Google 免费接口 / MyMemory 免费接口\n'
            '命令行：--shot 截图保存，--ocr 截图识别，--translate 截图翻译\n'
            '可把上述命令绑定到系统快捷键。')
        d.run()
        d.destroy()

    def quit(self):
        try:
            if self.srv:
                self.srv.close()
            if os.path.exists(SOCK_PATH):
                os.unlink(SOCK_PATH)
        except Exception:
            pass
        Gtk.main_quit()


def main():
    mode = None
    autostart = False
    for a in sys.argv[1:]:
        if a in ('--shot', '--save'):
            mode = 'save'
        elif a == '--ocr':
            mode = 'ocr'
        elif a == '--translate':
            mode = 'translate'
        elif a in ('--open', '--show'):
            mode = 'open'
        elif a == '--autostart':
            autostart = True
    if mode and os.path.exists(SOCK_PATH):
        try:
            c = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            c.connect(SOCK_PATH)
            c.sendall((mode + '\n').encode())
            c.close()
            return
        except Exception:
            pass
    cfg = load_cfg()
    app = ShotApp(cfg)

    def boot():
        app.startup(mode, autostart)
        return False

    GLib.timeout_add(500, boot)
    Gtk.main()


if __name__ == '__main__':
    main()
