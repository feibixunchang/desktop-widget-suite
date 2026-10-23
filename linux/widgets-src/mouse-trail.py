#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
多彩粒子鼠标拖尾 · Linux 版（GTK3 / cairo / Xlib）
==================================================
对应 Windows 端「炫彩鼠标尾巴 (NeonMouseTrail)」挂件，重写为多特效粒子引擎。

特效（托盘菜单可任意组合，默认全开）：
    颗粒   —— 高密度小尘粒，快速散射
    烟雾   —— 柔软膨胀的雾团，缓缓上浮
    星星   —— 四角闪光的星芒，闪烁漂移
    气泡   —— 半透明泡泡，摇晃上浮
    小方块 —— 旋转飞舞的彩色方块
    碎彩带 —— 翻转飘落的彩纸屑
    萤火虫 —— 脉动发光的小光点，缓慢游荡
    流星   —— 高速划过的光迹

交互：
    滑动指针 → 粒子从指针飘散而出（移速越快越密）
    点击     → 崩裂：冲击波环 + 白光闪 + 碎片四射 + 烟雾
    按住不放 → 持续喷洒碎片

颜色：彩虹（色相随时间流转）/ 炫彩（糖果色板）/ 单色，托盘可切。

实现要点（踩坑记录，改动前必读）：
- SDL/pygame 在 X11 下拿不到 alpha visual（surface 有 alpha 但窗口不透明 →
  全屏叠加层整块黑屏），必须 GTK3 + RGBA visual。
- cairo 必须 `import cairo`（pycairo）：gi.repository.cairo 没有 OPERATOR_CLEAR
  等常量；且 GTK 的 draw 回调会吞掉异常 → 画面全空还看不到报错。
- 全局坐标与按键必须用 Xlib root.query_pointer()：Gdk.Device.get_position()
  返回 GTK 内部缓存旧值（拖尾所有点会重叠在同一位置）。按键取 mask 的
  Button1/2/3Mask 位（叠加层已 input_shape 点击穿透，收不到 GTK 事件）。
- 脏区重绘：粒子新旧包围盒并集 queue_draw_area；draw 里 OPERATOR_CLEAR
  清屏 + 全量重画所有粒子（GTK 只会把 clip 区合并得更大，不会更小）。
- APP 名随安装位置自动切换：装进 /usr/lib/mouse-trail/ 且名为 main.py 时
  → mouse-trail（与控制中心/守护/开机自启/.off 对齐，可无缝替换套件旧拖尾）；
  独立测试 → neon-trail。

依赖：python3, python3-gi, gir1.2-gtk-3.0, python3-cairo, python3-xlib
用法：neon-trail [--color RRGGBB] [--color-mode rainbow|candy|mono]
                  [--density flow|normal|feast] [--fps N]
                  [--effects grit,smoke,star,bubble,square,confetti,firefly,comet]
                  [--set KEY=VAL ...]     # 运行时改设置（托盘失灵时的备用通道）

托盘菜单点不开时的备用手段（不依赖图形栈，瞬间生效）：
    neon-trail --set density=feast            # 密度：流畅/标准/盛宴
    neon-trail --set color_mode=candy          # 颜色：rainbow/candy/mono
    neon-trail --set color=ff00aa             # 单色模式的颜色
    neon-trail --set effects=grit,smoke,star  # 只保留列出的特效
    neon-trail --set enabled=false             # 临时关掉
    neon-trail --set fps=40                   # 帧率（15~120）
多个 --set 一起写；改完会自动通知正在运行的实例重载（SIGHUP）。
"""
import argparse
import fcntl
import json
import math
import os
import random
import signal
import sys
import time

# ───────── 先处理纯配置类命令，再导入图形栈 ─────────
# --set 只是读写 JSON + 发 SIGHUP，不需要 GTK/cairo/X/DBus：
# 若先 import gi，启动要 0.3~1.5 秒、刷一屏 atk/dbind 警告，
# 在没有 X 的环境（ssh、脚本、开机自启）里还会直接崩掉。
# 这里自包含地处理完就退出（路径/特效表在这里重新算一遍）。
if '--set' in sys.argv:
    _pairs = []
    _seen = False
    for _a in sys.argv[1:]:
        if _a == '--set':
            _seen = True
            continue
        if _seen and _a.startswith('--'):
            break
        if _seen:
            _pairs.append(_a)
    if not _pairs:
        sys.stderr.write('--set 需要至少一个 KEY=VAL\n')
        sys.exit(2)

    _HERE0 = os.path.dirname(os.path.abspath(__file__))
    _APP0 = ('mouse-trail' if (os.path.basename(_HERE0) == 'mouse-trail'
                               and os.path.basename(__file__).startswith('main'))
             else 'neon-trail')
    _HOME0 = os.path.expanduser('~')
    _KEYS0 = ['grit', 'smoke', 'star', 'bubble', 'square', 'confetti',
              'firefly', 'comet']
    _DENS0 = ('flow', 'normal', 'feast')
    _DEFAULT0 = dict(enabled=True, effects={k: True for k in _KEYS0},
                     color_mode='rainbow', color='00e5ff',
                     density='normal', fps=60)

    def _cfg_path0():
        d = os.path.join(_HOME0, '.config', _APP0)
        try:
            os.makedirs(d, exist_ok=True)
            if not os.access(d, os.W_OK):
                raise OSError('not writable')
            return os.path.join(d, 'config.json')
        except Exception:
            d = os.path.join(_HOME0, '.config', '%s-uid%d' % (_APP0, os.getuid()))
            os.makedirs(d, exist_ok=True)
            return os.path.join(d, 'config.json')

    def _load0():
        cfg = dict(_DEFAULT0)
        cfg['effects'] = dict(_DEFAULT0['effects'])
        try:
            with open(_cfg_path0()) as f:
                data = json.load(f)
            if isinstance(data, dict):
                for k in ('enabled', 'color_mode', 'color', 'density', 'fps'):
                    if k in data:
                        cfg[k] = data[k]
                eff = data.get('effects')
                if isinstance(eff, dict):
                    for k in _KEYS0:
                        if k in eff:
                            cfg['effects'][k] = bool(eff[k])
        except Exception:
            pass
        return cfg

    def _save0(cfg):
        p = _cfg_path0()
        tmp = p + '.tmp'
        with open(tmp, 'w') as f:
            json.dump(cfg, f, ensure_ascii=False, indent=2)
        os.replace(tmp, p)

    def _pid0():
        try:
            d = os.path.dirname(_cfg_path0())
            with open(os.path.join(d, '%s.pid' % _APP0)) as f:
                pid = int(f.read().strip())
            os.kill(pid, 0)
            return pid
        except Exception:
            return None

    _cfg = _load0()
    _changed = False
    for _kv in _pairs:
        if '=' not in _kv:
            continue
        _k, _v = _kv.split('=', 1)
        _k, _v = _k.strip(), _v.strip()
        if _k == 'enabled':
            _cfg['enabled'] = _v.lower() not in ('0', 'false', 'off', 'no')
            _changed = True
        elif _k == 'color_mode' and _v in ('rainbow', 'candy', 'mono'):
            _cfg['color_mode'] = _v
            _changed = True
        elif _k == 'density' and _v in _DENS0:
            _cfg['density'] = _v
            _changed = True
        elif _k == 'color' and len(_v) == 6:
            try:
                int(_v, 16)
                _cfg['color'] = _v
                _changed = True
            except ValueError:
                print('无效颜色: %s（要 6 位十六进制，如 ff00aa）' % _v,
                      file=sys.stderr)
        elif _k == 'fps':
            try:
                _cfg['fps'] = max(15, min(120, int(_v)))
                _changed = True
            except ValueError:
                print('无效帧率: %s' % _v, file=sys.stderr)
        elif _k == 'effects':
            _sel = [s.strip() for s in _v.split(',') if s.strip() in _KEYS0]
            if _sel:
                _cfg['effects'] = {x: (x in _sel) for x in _KEYS0}
                _changed = True
            else:
                print('没有有效的特效名（可选：%s）' % ','.join(_KEYS0),
                      file=sys.stderr)
        else:
            print('未知设置项: %s' % _k, file=sys.stderr)
    if not _changed:
        print('没有可应用的设置')
        sys.exit(1)
    _save0(_cfg)
    _pid = _pid0()
    if _pid:
        try:
            os.kill(_pid, signal.SIGHUP)
            print('已通知运行中的实例 (pid %d) 重载配置' % _pid)
        except Exception as e:
            print('已保存配置，但通知运行中实例失败: %s' % e, file=sys.stderr)
    else:
        print('已保存配置（当前没有运行中的实例，下次启动生效）')
    sys.exit(0)

# ───────── 身份与路径 ─────────
_HERE = os.path.dirname(os.path.abspath(__file__))
APP = 'mouse-trail' if (os.path.basename(_HERE) == 'mouse-trail'
                        and os.path.basename(__file__).startswith('main')) \
    else 'neon-trail'
# 图形栈：必须在 --set 短路之后才导入（见上面的说明）
import cairo
import gi
gi.require_version('Gtk', '3.0')
gi.require_version('Gdk', '3.0')
from gi.repository import Gtk, Gdk, GLib, Gio
# DBus application id / 托盘 indicator id：必须是反向域名形式。
# 用裸名（'neon-trail'）会注册成标准 SNI bus name，Deepin 面板不认那条路径，
# 图标能画出来但右键菜单弹不出来。
APP_ID = ('com.deepin.DesktopWidgetsMouseTrail' if APP == 'mouse-trail'
          else 'com.deepin.NeonTrail')

HOME = os.path.expanduser('~')
CONFIG_DIR = os.path.join(HOME, '.config', APP)
CONFIG_PATH = os.path.join(CONFIG_DIR, 'config.json')
AUTOSTART_PATH = os.path.join(HOME, '.config', 'autostart', '%s.desktop' % APP)
LOCK_PATH = os.path.join(CONFIG_DIR, 'instance.lock')

# X11 全局指针（实时查询，非 GTK 缓存）
_XROOT = None
try:
    from Xlib import display as _xdisplay
    _XROOT = _xdisplay.Display().screen().root
except Exception:
    _XROOT = None

# ───────── 特效定义 ─────────
# (key, 中文名, 备注)
EFFECTS = [
    ('grit',     '颗粒',   '高密度小尘粒，快速散射'),
    ('smoke',    '烟雾',   '柔软雾团，缓缓上浮'),
    ('star',     '星星',   '四角星芒，闪烁漂移'),
    ('bubble',   '气泡',   '半透明泡泡，摇晃上浮'),
    ('square',   '小方块', '旋转飞舞的彩色方块'),
    ('confetti', '碎彩带', '翻转飘落的彩纸屑'),
    ('firefly',  '萤火虫', '脉动光点，缓慢游荡'),
    ('comet',    '流星',   '高速划过的光迹'),
]
EFFECT_KEYS = [k for k, _, _ in EFFECTS]

# 物理参数：rate=每像素移动生成率, life/size=随机区间, grav=重力(负=上浮),
#           drag=阻尼, spd=初速区间, inherit=指针速度继承系数
KIND_SPEC = {
    'grit':     (0.22, (0.35, 0.80), (1.0, 2.6),  320, 2.6, (20, 110),  0.26),
    'smoke':    (0.009, (1.20, 2.20), (6.0, 13.), -14, 1.1, (6, 30),    0.06),
    'star':     (0.058, (0.50, 1.10), (2.6, 5.2),   50, 1.4, (15, 80),   0.18),
    'bubble':   (0.039, (1.40, 2.80), (3.0, 9.0),  -26, 0.9, (8, 40),    0.08),
    'square':   (0.032, (0.60, 1.20), (2.6, 5.5),  280, 1.6, (15, 90),   0.20),
    'confetti': (0.097, (0.80, 1.60), (3.0, 5.0),  190, 1.1, (25, 120),  0.22),
    'firefly':  (0.008, (1.50, 3.00), (2.0, 3.6),   0, 0.5, (5, 25),    0.05),
    'comet':    (0.019, (0.22, 0.42), (1.4, 2.6),   60, 0.4, (320, 700), 0.55),
}

DENSITY = {'flow': 0.55, 'normal': 1.0, 'feast': 1.9}
DENSITY_LABEL = {'flow': '流畅', 'normal': '标准', 'feast': '盛宴'}
# 上限下调：在「绚丽」与「低 CPU」之间取平衡（4K 全屏软件渲染下每帧粒子越多越吃 CPU）
DENSITY_CAP = {'flow': 240, 'normal': 460, 'feast': 820}
DENSITY_FRAME_CAP = {'flow': 9, 'normal': 16, 'feast': 28}

CANDY_PALETTE = [
    (1.00, 0.18, 0.47),   # 玫红
    (1.00, 0.47, 0.10),   # 橙
    (1.00, 0.84, 0.10),   # 金黄
    (0.35, 0.86, 0.35),   # 草绿
    (0.16, 0.78, 1.00),   # 天蓝
    (0.51, 0.35, 1.00),   # 紫
    (1.00, 0.35, 0.85),   # 粉
    (0.00, 0.90, 1.00),   # 青
]

DEFAULTS = dict(
    enabled=True,
    effects={k: True for k in EFFECT_KEYS},
    color_mode='rainbow',
    color='00e5ff',
    density='normal',
    fps=60,
)


def hsv(h, s, v):
    """h∈[0,1) → (r,g,b) 各通道 [0,1]"""
    h = h % 1.0
    i = int(h * 6.0)
    f = h * 6.0 - i
    p = v * (1.0 - s)
    q = v * (1.0 - f * s)
    t = v * (1.0 - (1.0 - f) * s)
    i = i % 6
    if i == 0:
        return (v, t, p)
    if i == 1:
        return (q, v, p)
    if i == 2:
        return (p, v, t)
    if i == 3:
        return (p, q, v)
    if i == 4:
        return (t, p, v)
    return (v, p, q)


def parse_color(s):
    s = (s or '').strip().lstrip('#')
    if len(s) != 6:
        raise ValueError(s)
    return tuple(int(s[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def load_config():
    cfg = dict(DEFAULTS)
    cfg['effects'] = dict(DEFAULTS['effects'])
    # 主路径读不到时，尝试降级目录（见 save_config 的降级逻辑）
    candidates = [CONFIG_PATH,
                  os.path.join(HOME, '.config', '%s-uid%d' % (APP, os.getuid()),
                               'config.json')]
    for path in candidates:
        try:
            with open(path) as f:
                data = json.load(f)
            break
        except Exception:
            data = None
    if isinstance(data, dict):
        for k in ('enabled', 'color_mode', 'color', 'density', 'fps'):
            if k in data:
                cfg[k] = data[k]
        eff = data.get('effects')
        if isinstance(eff, dict):
            for k in EFFECT_KEYS:
                if k in eff:
                    cfg['effects'][k] = bool(eff[k])
    if cfg['color_mode'] not in ('rainbow', 'candy', 'mono'):
        cfg['color_mode'] = 'rainbow'
    if cfg['density'] not in DENSITY:
        cfg['density'] = 'normal'
    try:
        cfg['fps'] = max(15, min(120, int(cfg['fps'])))
    except Exception:
        cfg['fps'] = DEFAULTS['fps']
    return cfg


def save_config(cfg):
    """保存配置。

    踩坑：早期用 root 跑测试，把 ~/.config/neon-trail/ 留成 root 属主，
    普通用户再启动就写不进去了（save_config 静默失败 → 托盘里改了设置
    但重启后没生效，看起来像「菜单点不动」）。这里在写之前先确保目录
    对当前用户可写，不可写就降级到 ~/.config/<APP>-user-<uid>/。
    """
    try:
        os.makedirs(CONFIG_DIR, exist_ok=True)
        # 目录存在但不属于当前用户 → 换路径，避免静默丢配置
        if not os.access(CONFIG_DIR, os.W_OK):
            raise PermissionError('config dir not writable: %s' % CONFIG_DIR)
        tmp = CONFIG_PATH + '.tmp'
        with open(tmp, 'w') as f:
            json.dump(cfg, f, ensure_ascii=False, indent=2)
        os.replace(tmp, CONFIG_PATH)
    except Exception as e:
        # 降级：写到用户专属目录
        try:
            alt_dir = os.path.join(HOME, '.config', '%s-uid%d' % (APP, os.getuid()))
            os.makedirs(alt_dir, exist_ok=True)
            alt_path = os.path.join(alt_dir, 'config.json')
            with open(alt_path, 'w') as f:
                json.dump(cfg, f, ensure_ascii=False, indent=2)
            print('[warn] 主配置目录不可写，已改存 %s（%s）' % (alt_path, e),
                  file=sys.stderr)
        except Exception as e2:
            print('[warn] 配置保存失败:', e2, file=sys.stderr)


def autostart_enabled():
    return os.path.exists(AUTOSTART_PATH)


def pid_path():
    """pid 文件：与配置同目录的降级目录，保证任何用户组合下都可写。"""
    d = CONFIG_DIR
    try:
        os.makedirs(d, exist_ok=True)
        if not os.access(d, os.W_OK):
            raise PermissionError(dir)
    except Exception:
        d = '/tmp'
    return os.path.join(d, '%s.pid' % APP)


def write_pid():
    try:
        p = pid_path()
        with open(p, 'w') as f:
            f.write('%d\n' % os.getpid())
        return p
    except Exception:
        return None


def running_pid():
    """读 pid 文件里的实例 PID（若存活）。"""
    try:
        with open(pid_path()) as f:
            pid = int(f.read().strip())
    except Exception:
        return None
    try:
        os.kill(pid, 0)
        return pid
    except Exception:
        return None


def set_autostart(enable):
    """开机自启：优先软链到系统菜单目录的 .desktop，没有就自己写一份。"""
    try:
        if enable:
            os.makedirs(os.path.dirname(AUTOSTART_PATH), exist_ok=True)
            src = '/usr/share/applications/%s.desktop' % APP
            if not os.path.exists(src):
                src = '/usr/local/share/applications/%s.desktop' % APP
            if os.path.exists(src):
                if os.path.islink(AUTOSTART_PATH) or os.path.exists(AUTOSTART_PATH):
                    os.remove(AUTOSTART_PATH)
                os.symlink(src, AUTOSTART_PATH)
                return True
            exe = '/usr/bin/%s' % APP
            if not os.path.exists(exe):
                exe = '%s %s' % (sys.executable, os.path.abspath(__file__))
            content = ('[Desktop Entry]\n'
                       'Type=Application\n'
                       'Name=多彩粒子鼠标拖尾\n'
                       'Comment=颗粒/烟雾/星星/气泡/小方块/碎彩带，点击崩裂\n'
                       'Exec=%s\n'
                       'Terminal=false\n'
                       'X-GNOME-Autostart-enabled=true\n'
                       'Categories=X-Windows;Utility;\n') % exe
            with open(AUTOSTART_PATH, 'w') as f:
                f.write(content)
        else:
            if os.path.islink(AUTOSTART_PATH) or os.path.exists(AUTOSTART_PATH):
                os.remove(AUTOSTART_PATH)
        return True
    except Exception:
        return False


def acquire_lock():
    """单实例：flock 锁文件，进程死亡自动释放，不会留死锁。

    健壮性（两次踩坑）：
    1. 若锁目录被别的用户（如 root）占用导致无写权限，不要误判成
       “已有一个实例”而直接退出，而是降级到 /tmp 锁继续运行。
    2. 降级锁同样必须用 flock！早期 /tmp 锁只是 open() 不加锁，导致
       root 实例和用户实例能同时运行、同时往托盘注册同一个 indicator，
       面板只显示其中一个 → 用户右键托盘时菜单弹在另一个进程上，
       表现为「点托盘完全没反应」。
    """
    def _try_lock(path):
        f = open(path, 'w')
        try:
            fcntl.flock(f, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            f.close()
            return None
        f.write('%d\n' % os.getpid())
        f.flush()
        return f

    try:
        os.makedirs(CONFIG_DIR, exist_ok=True)
        return _try_lock(LOCK_PATH)
    except Exception:
        pass
    # 主锁目录不可写 → 降级到 /tmp（同样带 flock）
    for alt in ('/tmp/%s.%s.lock' % (APP, os.getuid()),
                '/tmp/%s.lock' % APP):
        try:
            return _try_lock(alt)
        except Exception:
            continue
    return None


class Particle(object):
    __slots__ = ('kind', 'x', 'y', 'vx', 'vy', 'age', 'life', 'size',
                 'r', 'g', 'b', 'a', 'rot', 'vr', 'phase', 'drag', 'grav',
                 'w', 'h', 'flip', 'tw')


class SettingsWindow(Gtk.Window):
    """独立设置窗口：托盘菜单点不开时的可靠兜底（双击托盘图标打开）。

    用 Gtk.Window 而非 Gtk.ApplicationWindow：不需要进 application 的窗口列表，
    免得和全屏叠加层抢焦点。
    """

    def __init__(self, app):
        Gtk.Window.__init__(self, title='多彩粒子鼠标拖尾 · 设置')
        self.app = app
        self.set_default_size(380, 460)
        self.set_position(Gtk.WindowPosition.CENTER)
        self.connect('delete-event', self.on_delete)
        # 置顶：全屏叠加层是 keep_above 的，普通窗口容易被挡在下面
        self.set_keep_above(True)

        root = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
        root.set_border_width(14)
        self.add(root)

        # 启用开关
        row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        self.sw_enable = Gtk.Switch()
        self.sw_enable.set_active(bool(app.cfg['enabled']))
        self.sw_enable.connect('notify::active', self.on_enable)
        row.pack_start(Gtk.Label(label='启用拖尾', xalign=0), True, True, 0)
        row.pack_end(self.sw_enable, False, False, 0)
        root.pack_start(row, False, False, 0)

        # 密度
        root.pack_start(Gtk.Label(label='密度', xalign=0), False, False, 0)
        self.cb_dens = Gtk.ComboBoxText()
        for mode in ('flow', 'normal', 'feast'):
            self.cb_dens.append(mode, DENSITY_LABEL[mode])
        self.cb_dens.set_active_id(app.cfg['density'])
        self.cb_dens.connect('changed', self.on_density)
        root.pack_start(self.cb_dens, False, False, 0)

        # 颜色模式
        root.pack_start(Gtk.Label(label='颜色', xalign=0), False, False, 0)
        self.cb_color = Gtk.ComboBoxText()
        for mode, name in (('rainbow', '彩虹（流转）'), ('candy', '炫彩（糖果）'),
                           ('mono', '单色')):
            self.cb_color.append(mode, name)
        self.cb_color.set_active_id(app.cfg['color_mode'])
        self.cb_color.connect('changed', self.on_color)
        root.pack_start(self.cb_color, False, False, 0)

        # 单色
        self.btn_color = Gtk.ColorButton()
        try:
            r, g, b = app.mono_rgb
            self.btn_color.set_rgba(Gdk.RGBA(r, g, b, 1.0))
        except Exception:
            pass
        self.btn_color.connect('color-set', self.on_pick_color)
        row2 = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        row2.pack_start(Gtk.Label(label='单色颜色', xalign=0), True, True, 0)
        row2.pack_end(self.btn_color, False, False, 0)
        root.pack_start(row2, False, False, 0)

        # 特效多选
        root.pack_start(Gtk.Label(label='特效（可多选）', xalign=0), False, False, 0)
        grid = Gtk.Grid(column_spacing=6, row_spacing=4)
        self.cb_eff = {}
        for i, (key, name, _desc) in enumerate(EFFECTS):
            cb = Gtk.CheckButton(label=name)
            cb.set_active(bool(app.cfg['effects'].get(key)))
            cb.connect('toggled', self.on_effect, key)
            grid.attach(cb, i % 2, i // 2, 1, 1)
            self.cb_eff[key] = cb
        root.pack_start(grid, False, False, 0)

        # 帧率
        row3 = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        row3.pack_start(Gtk.Label(label='帧率', xalign=0), True, True, 0)
        self.spin_fps = Gtk.SpinButton.new_with_range(15, 120, 5)
        try:
            self.spin_fps.set_value(int(app.cfg['fps']))
        except Exception:
            self.spin_fps.set_value(60)
        self.spin_fps.connect('value-changed', self.on_fps)
        row3.pack_end(self.spin_fps, False, False, 0)
        root.pack_start(row3, False, False, 0)

        # 开机自启
        self.sw_auto = Gtk.Switch()
        self.sw_auto.set_active(autostart_enabled())
        self.sw_auto.connect('notify::active', self.on_autostart)
        row4 = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        row4.pack_start(Gtk.Label(label='开机自启', xalign=0), True, True, 0)
        row4.pack_end(self.sw_auto, False, False, 0)
        root.pack_start(row4, False, False, 0)

        # 按钮
        btns = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        btns.set_halign(Gtk.Align.END)
        b_close = Gtk.Button(label='关闭')
        b_close.connect('clicked', lambda *a: self.hide())
        b_quit = Gtk.Button(label='退出拖尾')
        b_quit.connect('clicked', lambda *a: self.app.quit())
        btns.pack_start(b_quit, False, False, 0)
        btns.pack_start(b_close, False, False, 0)
        root.pack_start(btns, False, False, 0)

    def on_delete(self, *_a):
        self.hide()
        return True

    def show_now(self):
        # hide→show 让 WM 重新计算层级，配合 keep_above 保证不被全屏叠加层盖住
        self.hide()
        self.show_all()
        self.present()
        try:
            self.set_keep_above(True)
        except Exception:
            pass

    # ─── 回调：改内存 + 落盘 + 刷新托盘勾选态 ───
    def _apply(self):
        save_config(self.app.cfg)
        try:
            self.app.tray.refresh_checks()
        except Exception:
            pass

    def on_enable(self, sw, _p):
        self.app.cfg['enabled'] = bool(sw.get_active())
        if not self.app.cfg['enabled']:
            self.app.particles = []
        self._apply()

    def on_density(self, cb):
        mode = cb.get_active_id()
        if mode:
            self.app.cfg['density'] = mode
            self.app.cap = DENSITY_CAP[mode]
            self._apply()

    def on_color(self, cb):
        mode = cb.get_active_id()
        if mode:
            self.app.cfg['color_mode'] = mode
            self._apply()

    def on_pick_color(self, btn):
        rgba = btn.get_rgba()
        self.app.cfg['color'] = '%02x%02x%02x' % (
            int(rgba.red * 255), int(rgba.green * 255), int(rgba.blue * 255))
        self.app.mono_rgb = parse_color(self.app.cfg['color'])
        self._apply()

    def on_effect(self, cb, key):
        on = bool(cb.get_active())
        if not on and sum(1 for k in self.cb_eff
                          if k != key and self.cb_eff[k].get_active()) == 0:
            cb.set_active(True)       # 至少留一个特效
            return
        self.app.cfg['effects'][key] = on
        self._apply()

    def on_fps(self, spin):
        try:
            self.app.cfg['fps'] = int(spin.get_value())
        except Exception:
            return
        self.app.interval = max(8, int(1000.0 / max(1, self.app.cfg['fps'])))
        self._apply()

    def on_autostart(self, sw, _p):
        set_autostart(bool(sw.get_active()))
        sw.set_active(autostart_enabled())


class Overlay(Gtk.Window):
    """全屏点击穿透覆盖层。POPUP 类型让 WM 不管理它：不抢焦点、不进任务栏。"""

    def __init__(self, app):
        Gtk.Window.__init__(self, type=Gtk.WindowType.POPUP)
        self.app = app
        self.set_decorated(False)
        self.set_app_paintable(True)
        self.set_skip_taskbar_hint(True)
        self.set_skip_pager_hint(True)
        self.set_accept_focus(False)
        self.set_can_focus(False)

        scr = Gdk.Screen.get_default()
        self.sw, self.sh = scr.get_width(), scr.get_height()
        # 透明的关键：必须拿到 RGBA visual
        vis = self.get_screen().get_rgba_visual()
        if vis:
            self.set_visual(vis)

        self.da = Gtk.DrawingArea()
        self.da.set_size_request(self.sw, self.sh)
        self.da.set_events(0)
        self.da.connect('draw', self.on_draw)
        self.add(self.da)
        self.set_size_request(self.sw, self.sh)
        self.resize(self.sw, self.sh)   # POPUP 窗口必须显式撑满，否则可能只按子控件尺寸
        self.move(0, 0)
        self.show_all()
        self.make_click_through()
        try:
            self.stick()                 # 跨所有工作区显示
        except Exception:
            pass
        self.set_keep_above(True)

    def make_click_through(self):
        """清空输入形状 → 所有鼠标事件直接穿透到下层窗口。"""
        try:
            win = self.get_window()
            if win:
                region = cairo.Region()
                # set_* 是直接替换语义（空 region = 全窗口无输入区 → 穿透）；
                # 旧 input_shape_combine_region 是 union 语义，在「无遮罩」基础上
                # union 空集会退化为整窗接收输入，导致穿透失败、菜单点不动。
                if hasattr(win, 'set_input_shape_combine_region'):
                    win.set_input_shape_combine_region(region)
                else:
                    win.input_shape_combine_region(region, 0, 0)
        except Exception as e:
            print('[warn] 点击穿透设置失败:', e, file=sys.stderr)

    def on_draw(self, da, cr):
        self.app.draw(cr)
        return False


class SniServer(object):
    """自实现标准 StatusNotifierItem（含 Activate/ContextMenu + dbusmenu）。

    为什么不用 AyatanaAppIndicator3：实测（Deepin 25 / dde-shell）Ayatana
    导出的 org.kde.StatusNotifierItem 接口只有 SecondaryActivate 和 Scroll，
    **没有 Activate（左键）也没有 ContextMenu（右键）**。Deepin 面板是纯
    SNI 客户端，只能靠这两个方法把点击传给应用——所以用 Ayatana 时托盘
    图标"点了完全没反应"（同环境控制中心也点不动，可作对照）。
    这里自己按 SNI 规范导出完整接口，点击才能到达程序。
    """

    def __init__(self, tray, menu):
        self.tray = tray
        self.app = tray.app
        self.menu = menu
        self._ids = {}          # id -> Gtk.MenuItem
        self._next_id = 1
        self.conn = Gio.bus_get_sync(Gio.BusType.SESSION, None)

        self.item_iface = Gio.DBusNodeInfo.new_for_xml('''
<node>
  <interface name="org.kde.StatusNotifierItem">
    <method name="Activate">
      <arg name="x" direction="in" type="i"/>
      <arg name="y" direction="in" type="i"/>
    </method>
    <method name="SecondaryActivate">
      <arg name="x" direction="in" type="i"/>
      <arg name="y" direction="in" type="i"/>
    </method>
    <method name="ContextMenu">
      <arg name="x" direction="in" type="i"/>
      <arg name="y" direction="in" type="i"/>
    </method>
    <method name="Scroll">
      <arg name="delta" direction="in" type="i"/>
      <arg name="orientation" direction="in" type="s"/>
    </method>
    <property name="Id" type="s" access="read"/>
    <property name="Category" type="s" access="read"/>
    <property name="Status" type="s" access="read"/>
    <property name="Title" type="s" access="read"/>
    <property name="IconName" type="s" access="read"/>
    <property name="IconThemePath" type="s" access="read"/>
    <property name="Menu" type="o" access="read"/>
  </interface>
</node>''').interfaces[0]

        self.menu_iface = Gio.DBusNodeInfo.new_for_xml('''
<node>
  <interface name="com.canonical.dbusmenu">
    <method name="GetLayout">
      <arg name="parentId" direction="in" type="i"/>
      <arg name="recursionDepth" direction="in" type="i"/>
      <arg name="propertyNames" direction="in" type="as"/>
      <arg name="revision" direction="out" type="u"/>
      <arg name="layout" direction="out" type="(ia{sv}av)"/>
    </method>
    <method name="GetGroupProperties">
      <arg name="ids" direction="in" type="ai"/>
      <arg name="propertyNames" direction="in" type="as"/>
      <arg name="ret" direction="out" type="a(ia{sv})"/>
    </method>
    <method name="AboutToShow">
      <arg name="id" direction="in" type="i"/>
      <arg name="needUpdate" direction="out" type="b"/>
    </method>
    <method name="Event">
      <arg name="id" direction="in" type="i"/>
      <arg name="eventId" direction="in" type="s"/>
      <arg name="data" direction="in" type="v"/>
      <arg name="timestamp" direction="in" type="u"/>
    </method>
    <property name="Version" type="u" access="read"/>
    <property name="Status" type="s" access="read"/>
  </interface>
</node>''').interfaces[0]

        self._own_and_register()

    # ───────── dbus 注册 ─────────
    def _own_and_register(self):
        conn = self.conn
        self.item_path = '/StatusNotifierItem'
        self.menu_path = '/StatusNotifierItem/Menu'

        def on_owned(conn, name):
            try:
                conn.register_object(self.item_path, self.item_iface,
                                     self._on_item_call, self._on_item_get,
                                     None)
                conn.register_object(self.menu_path, self.menu_iface,
                                     self._on_menu_call, self._on_menu_get,
                                     None)
                conn.call_sync('org.kde.StatusNotifierWatcher',
                               '/StatusNotifierWatcher',
                               'org.kde.StatusNotifierWatcher',
                               'RegisterStatusNotifierItem',
                               GLib.Variant('(s)', (name,)),
                               None, Gio.DBusCallFlags.NONE, 3000, None)
                print('[tray] SNI 已注册 (%s)' % name, file=sys.stderr)
            except Exception as e:
                print('[warn] SNI 注册失败:', e, file=sys.stderr)

        Gio.bus_own_name_on_connection(conn, APP_ID,
                                       Gio.BusNameOwnerFlags.NONE,
                                       on_owned, None)

    # ───────── org.kde.StatusNotifierItem ─────────
    _PROPS = {
        'Id': None, 'Category': 'ApplicationStatus', 'Status': 'Active',
        'Title': '多彩粒子鼠标拖尾', 'IconName': None,
        'IconThemePath': '', 'Menu': '/StatusNotifierItem/Menu',
    }

    # 注意：PyGObject 的 register_object 不把 user_data 转发给回调，
    # get_property 实参只有 (conn, sender, path, iface, name) 5 个。
    def _on_item_get(self, conn, sender, path, iface, name):
        if name == 'Id':
            return GLib.Variant('s', APP_ID)
        if name == 'IconName':
            return GLib.Variant('s', APP)
        v = self._PROPS.get(name)
        if v is None:
            return None
        return GLib.Variant('s', v) if isinstance(v, str) else v

    def _on_item_call(self, conn, sender, path, iface, method, params, inv,
                      user_data=None):
        try:
            args = params.unpack()
        except Exception:
            args = ()
        if method == 'Activate':
            # 左键：打开设置窗口（比右键菜单更可靠的主入口）
            GLib.idle_add(self.app.open_settings)
        elif method == 'ContextMenu':
            # 右键：弹托盘菜单。必须 idle：dbus 回调里直接 popup 会进
            # GTK grab 递归，阻塞 dbus 处理导致调用方超时。
            if args:
                GLib.idle_add(self._popup_at, args[0], args[1])
        elif method == 'SecondaryActivate':
            if args:
                GLib.idle_add(self._popup_at, args[0], args[1])
        elif method == 'Scroll':
            pass
        inv.return_value(None)

    def _popup_at(self, x, y):
        self.menu.show_all()

        def pos(*_a):
            return (x, y, True)
        self.menu.popup(None, None, pos, None, 3, Gdk.CURRENT_TIME)
        return False

    # ───────── com.canonical.dbusmenu ─────────
    def _on_menu_get(self, conn, sender, path, iface, name):
        if name == 'Version':
            return GLib.Variant('u', 3)
        if name == 'Status':
            return GLib.Variant('s', 'normal')
        return None

    def _on_menu_call(self, conn, sender, path, iface, method, params, inv,
                      user_data=None):
        try:
            args = params.unpack()
        except Exception:
            args = ()
        print('[dbg] menu call %s args=%r' % (method, args),
              file=sys.stderr)
        if method == 'GetLayout':
            parent_id = args[0] if args else 0
            root = self._menu_root(parent_id)
            inv.return_value(GLib.Variant('(u(ia{sv}av))', (1, root)))
        elif method == 'AboutToShow':
            inv.return_value(GLib.Variant('(b)', (False,)))
        elif method == 'GetGroupProperties':
            inv.return_value(GLib.Variant('(a(ia{sv}))', ([],)))
        elif method == 'Event':
            if args:
                self._fire_event(args[0], args[1] if len(args) > 1 else '')
            inv.return_value(None)
        else:
            inv.return_dbus_error('UnknownMethod', method)

    # ───────── 菜单模型（从 Gtk.Menu 实时生成） ─────────
    def _assign_ids(self, items):
        """给 Gtk.MenuItem 分配稳定 id（首次访问时分配）。"""
        for it in items:
            if id(it) not in self._ids:
                self._ids[id(it)] = self._next_id
                self._next_id += 1

    def _id_of(self, widget):
        self._assign_ids([widget])
        return self._ids[id(widget)]

    def _props_of(self, it):
        p = {}
        if isinstance(it, Gtk.SeparatorMenuItem):
            p['type'] = GLib.Variant('s', 'separator')
            return p
        p['visible'] = GLib.Variant('b', True)
        p['enabled'] = GLib.Variant('b', it.get_sensitive())
        label = it.get_label() if hasattr(it, 'get_label') else ''
        if label:
            p['label'] = GLib.Variant('s', label)
        if isinstance(it, Gtk.CheckMenuItem):
            p['toggle-type'] = GLib.Variant('s', 'checkmark')
            p['toggle-state'] = GLib.Variant('i', 1 if it.get_active() else 0)
        if it.get_submenu() is not None:
            p['children-display'] = GLib.Variant('s', 'submenu')
        return p

    def _node(self, it):
        sub = it.get_submenu()
        kids = []
        if sub is not None:
            for c in sub.get_children():
                kids.append(self._node(c))
        # av 的每个元素必须是 Variant，不能塞裸 tuple
        return GLib.Variant('(ia{sv}av)',
                            (self._id_of(it), self._props_of(it), kids))

    def _menu_root(self, parent_id):
        """parent_id=0 → 根菜单；否则对应项的子菜单。"""
        if parent_id == 0:
            items = self.menu.get_children()
        else:
            w = self._widget_for_id(parent_id)
            sub = w.get_submenu() if w is not None else None
            items = sub.get_children() if sub is not None else []
        self._assign_ids(items)
        kids = [self._node(it) for it in items]
        # 注意：外层构造时 a{sv} 字段要传纯 dict（_create 会把 Variant
        # 当容器迭代而 KeyError），av 的元素才必须是 Variant。
        return (parent_id, {}, kids)

    def _fire_event(self, nid, event_id):
        """面板点击菜单项 → 触发对应 Gtk.MenuItem.activate()
        （CheckMenuItem 会自动 toggle 并 emit activate → 复用现有回调）"""
        print('[dbg] fire_event nid=%r event=%r' % (nid, event_id),
              file=sys.stderr)
        if event_id not in ('clicked', ''):
            return
        w = self._widget_for_id(nid)
        print('[dbg] widget for %r -> %r' % (nid, w), file=sys.stderr)
        if w is not None:
            GLib.idle_add(lambda: (w.activate(), False)[1])

    def _widget_for_id(self, nid):
        # _ids 是 id(widget)->nid，需要反查；直接遍历菜单树更稳
        stack = list(self.menu.get_children())
        while stack:
            it = stack.pop(0)
            if self._id_of(it) == nid:
                return it
            sub = it.get_submenu()
            if sub is not None:
                stack.extend(sub.get_children())
        return None

    def set_status(self, active):
        pass


class Tray(object):
    """托盘图标：自实现标准 SNI（含 Activate/ContextMenu + dbusmenu）。

    踩坑记录（Deepin 25 / dde-shell 实测）：
    1. AyatanaAppIndicator3 的 org.kde.StatusNotifierItem 只导出
       SecondaryActivate/Scroll，没有 Activate/ContextMenu → 面板无法
       把左右键点击传给应用 → 图标显示正常但"点了完全没反应"
       （控制中心 com.deepin.DesktopWidgetsHub 同样点不动，可作对照）。
    2. 因此这里用 SniServer 自己按 SNI 规范导出完整接口。
    3. dbusmenu 的 Event 经 Gtk.MenuItem.activate() 落到既有回调，
       勾选态/设置保存全部复用同一套逻辑。
    """

    def __init__(self, app):
        self.app = app
        self.ind = None
        self.icon = None
        self._suppress = False
        self.menu = self.build_menu()
        # 自实现标准 SNI（主路径）
        try:
            self.sni = SniServer(self, self.menu)
            return
        except Exception as e:
            print('[warn] SNI 自实现失败，回退 AppIndicator:', e,
                  file=sys.stderr)
            self.sni = None
        # 回退 1：Ayatana AppIndicator3
        try:
            gi.require_version('AyatanaAppIndicator3', '0.1')
            from gi.repository import AyatanaAppIndicator3 as AI
            self.ind = AI.Indicator.new(
                APP_ID, APP, AI.IndicatorCategory.APPLICATION_STATUS)
            self.ind.set_status(AI.IndicatorStatus.ACTIVE)
            self.ind.set_title('多彩粒子鼠标拖尾')
            self.ind.set_menu(self.menu)
            return
        except Exception as e:
            print('[warn] AppIndicator 不可用，回退 StatusIcon:', e,
                  file=sys.stderr)
            self.ind = None
        # 回退 2：Gtk.StatusIcon（XEmbed）
        try:
            self.icon = Gtk.StatusIcon()
            self.icon.set_from_icon_name(APP)
            self.icon.set_tooltip_text('多彩粒子鼠标拖尾')
            self.icon.set_visible(True)
            self.icon.connect('popup-menu', self.on_popup)
            self.icon.connect('activate', lambda *a: self.app.open_settings())
        except Exception as e:
            print('[warn] 托盘不可用:', e, file=sys.stderr)

    def on_popup(self, icon, button, t):
        self.menu.popup(None, None, Gtk.StatusIcon.position_menu,
                        icon, button, t)

    def refresh_checks(self):
        """把当前配置同步到菜单勾选态（外部改配置后调用）。"""
        app = self.app
        try:
            self._set_active_quiet(self.it_enable, bool(app.cfg['enabled']))
            for key, it in self.it_eff.items():
                self._set_active_quiet(it, bool(app.cfg['effects'].get(key)))
            for mode, it in self.it_color.items():
                self._set_active_quiet(it, app.cfg['color_mode'] == mode)
            for mode, it in self.it_dens.items():
                self._set_active_quiet(it, app.cfg['density'] == mode)
            self._set_active_quiet(self.it_auto, autostart_enabled())
        except Exception as e:
            print('[warn] 菜单状态同步失败:', e, file=sys.stderr)

    def build_menu(self):
        app = self.app
        m = Gtk.Menu()

        self.it_enable = Gtk.CheckMenuItem(label='启用拖尾')
        self.it_enable.set_active(bool(app.cfg['enabled']))
        self.it_enable.connect('activate', self.on_toggle_enable)
        m.append(self.it_enable)
        m.append(Gtk.SeparatorMenuItem())

        # 特效选择（可多选）
        eff_sub = Gtk.Menu()
        self.it_eff = {}
        for key, name, _desc in EFFECTS:
            it = Gtk.CheckMenuItem(label=name)
            it.set_active(bool(app.cfg['effects'].get(key)))
            it.connect('activate', self.on_effect, key)
            eff_sub.append(it)
            self.it_eff[key] = it
        eff_item = Gtk.MenuItem(label='特效选择')
        eff_item.set_submenu(eff_sub)
        m.append(eff_item)

        # 颜色模式
        color_sub = Gtk.Menu()
        self.it_color = {}
        for mode, name in (('rainbow', '彩虹（流转）'),
                           ('candy', '炫彩（糖果）'),
                           ('mono', '单色')):
            it = Gtk.CheckMenuItem(label=name)
            it.set_active(app.cfg['color_mode'] == mode)
            it.connect('activate', self.on_color_mode, mode)
            color_sub.append(it)
            self.it_color[mode] = it
        color_item = Gtk.MenuItem(label='颜色')
        color_item.set_submenu(color_sub)
        m.append(color_item)

        # 密度
        dens_sub = Gtk.Menu()
        self.it_dens = {}
        for mode, name in DENSITY_LABEL.items():
            it = Gtk.CheckMenuItem(label=name)
            it.set_active(app.cfg['density'] == mode)
            it.connect('activate', self.on_density, mode)
            dens_sub.append(it)
            self.it_dens[mode] = it
        dens_item = Gtk.MenuItem(label='密度')
        dens_item.set_submenu(dens_sub)
        m.append(dens_item)

        m.append(Gtk.SeparatorMenuItem())
        self.it_auto = Gtk.CheckMenuItem(label='开机自启')
        self.it_auto.set_active(autostart_enabled())
        self.it_auto.connect('activate', self.on_autostart)
        m.append(self.it_auto)

        m.append(Gtk.SeparatorMenuItem())
        it = Gtk.MenuItem(label='设置…')
        it.connect('activate', lambda *a: app.open_settings())
        m.append(it)
        it = Gtk.MenuItem(label='退出')
        it.connect('activate', lambda *a: app.quit())
        m.append(it)

        m.show_all()
        return m

    # ───────── 菜单回调 ─────────
    def _set_active_quiet(self, it, val):
        """设置勾选态但不触发其 activate 回调（避免循环递归）。"""
        self._suppress = True
        try:
            it.set_active(val)
        finally:
            self._suppress = False

    def on_toggle_enable(self, it):
        if self._suppress:
            return
        self.app.cfg['enabled'] = bool(it.get_active())
        save_config(self.app.cfg)
        if not self.app.cfg['enabled']:
            self.app.particles = []

    def on_effect(self, it, key):
        if self._suppress:
            return
        on = bool(it.get_active())
        self.app.cfg['effects'][key] = on
        if not any(self.app.cfg['effects'].values()):
            # 至少保留一个特效
            self.app.cfg['effects'][key] = True
            self._set_active_quiet(it, True)
        save_config(self.app.cfg)

    def on_color_mode(self, it, mode):
        if self._suppress:
            return
        self.app.cfg['color_mode'] = mode
        save_config(self.app.cfg)
        for m2, it2 in self.it_color.items():
            self._set_active_quiet(it2, m2 == mode)

    def on_density(self, it, mode):
        if self._suppress:
            return
        self.app.cfg['density'] = mode
        self.app.cap = DENSITY_CAP[mode]
        save_config(self.app.cfg)
        for m2, it2 in self.it_dens.items():
            self._set_active_quiet(it2, m2 == mode)

    def on_autostart(self, it):
        if self._suppress:
            return
        set_autostart(bool(it.get_active()))
        self._set_active_quiet(self.it_auto, autostart_enabled())


class App(object):
    def __init__(self, cli):
        self.cfg = load_config()
        for k, v in (cli or {}).items():
            if v is not None:
                self.cfg[k] = v
        self.cfg['effects'] = {k: bool(self.cfg['effects'].get(k, True))
                               for k in EFFECT_KEYS}
        if self.cfg['color_mode'] not in ('rainbow', 'candy', 'mono'):
            self.cfg['color_mode'] = 'rainbow'
        if self.cfg['density'] not in DENSITY:
            self.cfg['density'] = 'normal'
        try:
            self.mono_rgb = parse_color(self.cfg['color'])
        except Exception:
            self.mono_rgb = parse_color(DEFAULTS['color'])

        self.cap = DENSITY_CAP[self.cfg['density']]
        self.particles = []
        self.acc = dict((k, 0.0) for k in EFFECT_KEYS)
        self.held_acc = 0.0
        self.prev_pos = None
        self.pvx = 0.0
        self.pvy = 0.0
        self.btn_down = False
        self.base_hue = random.random()
        self.prev_boxes = []
        self.last_t = time.monotonic()
        self._glow_surf = self._make_glow_sprite()
        self.running = True

        self.overlay = Overlay(self)
        self._shown = True
        self.sw = self.overlay.sw
        self.sh = self.overlay.sh
        self.tray = Tray(self)
        # 独立设置窗口：托盘菜单失灵时的可靠入口（托盘左键 / 菜单「设置…」）
        self.settings = None
        self.interval = max(8, int(1000.0 / max(1, int(self.cfg['fps']))))
        # 空闲降频：没有粒子时把轮询间隔拉长到 IDLE_INTERVAL，鼠标一动立刻
        # 恢复全帧率。60fps 空转 query_pointer 本身只有 ~0.08ms/次（≈0.5% CPU），
        # 但叠加 4K 透明叠加层的 GTK 主循环唤醒，实测能到 10% 上下；
        # 空闲降到 ~16Hz 可压到 1% 以内，且拖尾起始延迟 ≤60ms 察觉不到。
        self.idle_interval = 60
        self._timer_id = None
        self._active = False
        self._reschedule(self.interval)
        self.pidfile = write_pid()

    # ───────── 运行时改配置（供 SIGHUP / 命令行控制用） ─────────
    def open_settings(self):
        """打开独立设置窗口（托盘左键 / 菜单「设置…」都走这里）。"""
        try:
            if self.settings is None:
                self.settings = SettingsWindow(self)
            self.settings.show_now()
            return True
        except Exception as e:
            print('[warn] 设置窗口打开失败:', e, file=sys.stderr)
            return False

    def reload_config(self):
        """重新读取配置文件并应用，托盘失灵时也能靠命令行改设置。"""
        try:
            cfg = load_config()
        except Exception:
            return False
        self.cfg['enabled'] = bool(cfg['enabled'])
        self.cfg['color_mode'] = cfg['color_mode']
        self.cfg['color'] = cfg['color']
        self.cfg['density'] = cfg['density']
        try:
            self.cfg['fps'] = max(15, min(120, int(cfg['fps'])))
        except Exception:
            pass
        self.cfg['effects'] = cfg['effects']
        try:
            self.mono_rgb = parse_color(self.cfg['color'])
        except Exception:
            pass
        self.cap = DENSITY_CAP.get(self.cfg['density'], DENSITY_CAP['normal'])
        self.interval = max(8, int(1000.0 / max(1, int(self.cfg['fps']))))
        if not self.cfg['enabled']:
            self.particles = []
        # 托盘菜单的勾选态同步刷新（失败也没关系）
        try:
            self.tray.refresh_checks()
        except Exception:
            pass
        return True

    def _reschedule(self, interval):
        """重建定时器：GLib.timeout_add 的间隔不能改，只能移除后重加。"""
        if self._timer_id is not None:
            try:
                GLib.source_remove(self._timer_id)
            except Exception:
                pass
        self._timer_id = GLib.timeout_add(interval, self._on_timer)
        self._cur_interval = interval

    def _on_timer(self):
        self._timer_id = None
        if not self.running:
            return False
        self.tick()
        # 动态调频：有粒子/刚有移动 → 全速；彻底空闲 → 降频
        want = (self.interval if (self.particles or self._active)
                else self.idle_interval)
        if want != getattr(self, '_cur_interval', None):
            self._reschedule(want)
        else:
            # 间隔没变 → 直接续期，不做 remove+add（省掉每帧的定时器重建开销）
            self._timer_id = GLib.timeout_add(want, self._on_timer)
        return False

    # ───────── 鼠标状态（Xlib 实时查询） ─────────
    def mouse_state(self):
        if _XROOT is None:
            return None
        try:
            ptr = _XROOT.query_pointer()
            return ptr.root_x, ptr.root_y, ptr.mask & 0x700
        except Exception:
            return None

    # ───────── 生成粒子 ─────────
    def make_room(self, extra):
        over = len(self.particles) + extra - self.cap
        if over > 0:
            del self.particles[:over]

    def _pick_color(self, kind):
        mode = self.cfg['color_mode']
        if mode == 'candy':
            return random.choice(CANDY_PALETTE)
        if mode == 'mono':
            return self.mono_rgb
        sat = {'smoke': 0.18, 'firefly': 0.55}.get(kind, 0.85)
        h = (self.base_hue + random.uniform(-0.05, 0.05)) % 1.0
        return hsv(h, sat, 1.0)

    def _spawn(self, kind, x, y, vx=None, vy=None, life_mul=1.0,
               life_r=None, size_r=None):
        if len(self.particles) >= self.cap:
            return
        _rate, lr, sr, grav, drag, spd_r, inherit = KIND_SPEC[kind]
        life_r = lr if life_r is None else life_r
        size_r = sr if size_r is None else size_r
        p = Particle()
        p.kind = kind
        p.x = x + random.uniform(-2.0, 2.0)
        p.y = y + random.uniform(-2.0, 2.0)
        if vx is None:
            if kind == 'comet':
                if abs(self.pvx) + abs(self.pvy) > 40.0:
                    base = math.atan2(self.pvy, self.pvx)
                else:
                    base = random.uniform(0.0, 2.0 * math.pi)
                ang = base + random.uniform(-0.35, 0.35)
                sp = random.uniform(*spd_r)
                vx = math.cos(ang) * sp
                vy = math.sin(ang) * sp
            else:
                ang = random.uniform(0.0, 2.0 * math.pi)
                sp = random.uniform(*spd_r)
                vx = math.cos(ang) * sp + self.pvx * inherit
                vy = math.sin(ang) * sp + self.pvy * inherit
        p.vx = vx
        p.vy = vy
        p.age = 0.0
        p.life = random.uniform(*life_r) * life_mul
        p.size = random.uniform(*size_r)
        p.r, p.g, p.b = self._pick_color(kind)
        p.a = 1.0
        p.rot = random.uniform(0.0, 2.0 * math.pi)
        p.vr = random.uniform(-6.0, 6.0)
        p.phase = random.uniform(0.0, 2.0 * math.pi)
        p.drag = drag
        p.grav = grav
        if kind == 'confetti':
            p.w = p.size * 0.5
            p.h = p.size
        else:
            p.w = p.size
            p.h = p.size
        p.flip = random.uniform(8.0, 16.0)
        p.tw = 1.0
        self.particles.append(p)

    def _spawn_effect(self, kind, x, y, life, size, r, g, b):
        """ring / flash 等无位移特效。"""
        p = Particle()
        p.kind = kind
        p.x = x
        p.y = y
        p.vx = p.vy = 0.0
        p.age = 0.0
        p.life = life
        p.size = size
        p.r, p.g, p.b = r, g, b
        p.a = 1.0
        p.rot = p.vr = p.phase = 0.0
        p.drag = p.grav = 0.0
        p.w = p.h = size
        p.flip = p.tw = 1.0
        self.particles.append(p)

    def spawn_segment(self, a, b, dist):
        """沿指针移动线段撒粒子（滑动飘散）。"""
        eff = self.cfg['effects']
        mult = DENSITY[self.cfg['density']]
        budget = DENSITY_FRAME_CAP[self.cfg['density']]
        ax, ay = a
        bx, by = b
        for k in EFFECT_KEYS:
            if not eff.get(k):
                self.acc[k] = 0.0
                continue
            self.acc[k] += KIND_SPEC[k][0] * dist * mult
            while self.acc[k] >= 1.0:
                self.acc[k] -= 1.0
                if budget <= 0:
                    break
                budget -= 1
                t = random.random()
                x = ax + (bx - ax) * t + random.uniform(-2.0, 2.0)
                y = ay + (by - ay) * t + random.uniform(-2.0, 2.0)
                self._spawn(k, x, y)
        for k in EFFECT_KEYS:
            if self.acc[k] > 2.0:
                self.acc[k] = 2.0

    def burst(self, x, y):
        """点击崩裂：冲击波 + 白光 + 碎片四射 + 烟雾。"""
        eff = self.cfg['effects']
        mult = DENSITY[self.cfg['density']]
        self.make_room(70)
        # 冲击波环
        if self.cfg['color_mode'] == 'mono':
            rr, gg, bb = self.mono_rgb
        elif self.cfg['color_mode'] == 'candy':
            rr, gg, bb = random.choice(CANDY_PALETTE)
        else:
            rr, gg, bb = hsv(self.base_hue, 0.5, 1.0)
        self._spawn_effect('ring', x, y, 0.5, random.uniform(90.0, 170.0),
                           rr, gg, bb)
        self._spawn_effect('flash', x, y, 0.14, 34.0, 1.0, 1.0, 1.0)
        # 碎片四射
        pool = [k for k in ('confetti', 'square', 'star', 'grit') if eff.get(k)]
        if not pool:
            pool = ['grit']
        w = {'confetti': 0.34, 'square': 0.24, 'star': 0.22, 'grit': 0.20}
        n = int(28 + 14 * mult)
        for _ in range(n):
            kind = random.choices(pool, weights=[w[k] for k in pool])[0]
            ang = random.uniform(0.0, 2.0 * math.pi)
            sp = 160.0 + 560.0 * random.random() ** 0.6
            self._spawn(kind, x, y,
                        vx=math.cos(ang) * sp,
                        vy=math.sin(ang) * sp - 50.0,
                        life_mul=1.25)
        # 高速火花
        for _ in range(10):
            ang = random.uniform(0.0, 2.0 * math.pi)
            sp = random.uniform(380.0, 900.0)
            self._spawn('grit', x, y, vx=math.cos(ang) * sp,
                        vy=math.sin(ang) * sp,
                        life_r=(0.20, 0.40), size_r=(0.8, 1.8))
        # 爆开烟雾
        for _ in range(4):
            ang = random.uniform(0.0, 2.0 * math.pi)
            sp = random.uniform(30.0, 110.0)
            self._spawn('smoke', x, y, vx=math.cos(ang) * sp,
                        vy=math.sin(ang) * sp - 25.0,
                        life_r=(0.9, 1.6), size_r=(5.0, 11.0))

    def held_spray(self, dt, x, y):
        """按住不放 → 持续喷洒碎片。"""
        eff = self.cfg['effects']
        pool = [k for k in ('confetti', 'square', 'star', 'grit') if eff.get(k)]
        if not pool:
            return
        self.held_acc += dt
        while self.held_acc >= 0.045:
            self.held_acc -= 0.045
            for _ in range(2):
                kind = random.choice(pool)
                ang = random.uniform(0.0, 2.0 * math.pi)
                sp = random.uniform(120.0, 420.0)
                self._spawn(kind, x + random.uniform(-3.0, 3.0),
                            y + random.uniform(-3.0, 3.0),
                            vx=math.cos(ang) * sp,
                            vy=math.sin(ang) * sp - 40.0,
                            life_mul=0.9)

    def release_puff(self, x, y):
        """松开按钮 → 一小团烟雾 + 慢速尘埃。"""
        for _ in range(3):
            ang = random.uniform(0.0, 2.0 * math.pi)
            sp = random.uniform(20.0, 70.0)
            self._spawn('smoke', x, y, vx=math.cos(ang) * sp,
                        vy=math.sin(ang) * sp - 15.0,
                        life_r=(0.7, 1.2), size_r=(4.0, 8.0))
        for _ in range(6):
            ang = random.uniform(0.0, 2.0 * math.pi)
            sp = random.uniform(60.0, 160.0)
            self._spawn('grit', x, y, vx=math.cos(ang) * sp,
                        vy=math.sin(ang) * sp, life_r=(0.3, 0.6))

    # ───────── 物理推进 ─────────
    def update_particles(self, dt):
        alive = []
        sh = self.sh
        sw = self.sw
        for p in self.particles:
            p.age += dt
            if p.age >= p.life:
                continue
            if p.kind in ('ring', 'flash'):
                alive.append(p)
                continue
            # 分种类的漂浮/摇摆
            if p.kind == 'bubble':
                p.x += math.sin(p.age * 3.2 + p.phase) * 9.0 * dt
                p.vy -= 20.0 * dt
                if p.vy < -55.0:
                    p.vy = -55.0
            elif p.kind == 'confetti':
                p.vx += math.sin(p.age * 11.0 + p.phase) * 26.0 * dt
            elif p.kind == 'firefly':
                p.vx += math.cos(p.age * 2.1 + p.phase) * 13.0 * dt
                p.vy += math.sin(p.age * 1.6 + p.phase * 1.4) * 13.0 * dt
            elif p.kind == 'star':
                p.vx += math.cos(p.age * 6.0 + p.phase) * 9.0 * dt
                p.vy += math.sin(p.age * 5.3 + p.phase) * 9.0 * dt
            elif p.kind == 'smoke':
                p.vy -= 10.0 * dt
            dmp = math.exp(-p.drag * dt)
            p.vx *= dmp
            p.vy *= dmp
            p.vy += p.grav * dt
            p.x += p.vx * dt
            p.y += p.vy * dt
            p.rot += p.vr * dt
            if p.y > sh + 80 or p.y < -140 or p.x < -180 or p.x > sw + 180:
                continue
            alive.append(p)
        self.particles = alive

    # ───────── 脏区计算 ─────────
    def boxes(self):
        res = []
        for p in self.particles:
            t = p.age / p.life
            k = p.kind
            if k == 'smoke':
                rad = p.size * (1.0 + 1.1 * t) * 1.4
            elif k == 'ring':
                rad = p.size * (1.0 - (1.0 - t) ** 3) + 10.0
            elif k == 'flash':
                rad = p.size * 1.15
            elif k == 'firefly':
                rad = p.size * 3.2
            elif k == 'comet':
                rad = p.size + 5.0 + (abs(p.vx) + abs(p.vy)) * 0.032
            else:
                rad = p.size * 1.7 + 5.0
            res.append((p.x - rad, p.y - rad, p.x + rad, p.y + rad))
        return res

    def queue_redraw(self):
        cur = self.boxes()
        allb = cur + self.prev_boxes
        self.prev_boxes = cur
        if not allb:
            return
        x0 = min(b[0] for b in allb)
        y0 = min(b[1] for b in allb)
        x1 = max(b[2] for b in allb)
        y1 = max(b[3] for b in allb)
        x0 = max(0, int(x0 - 2))
        y0 = max(0, int(y0 - 2))
        x1 = min(self.sw, int(math.ceil(x1 + 2)))
        y1 = min(self.sh, int(math.ceil(y1 + 2)))
        w = max(1, x1 - x0)
        h = max(1, y1 - y0)
        if w * h >= 0.7 * self.sw * self.sh:
            x0, y0, w, h = 0, 0, self.sw, self.sh
        self.overlay.da.queue_draw_area(x0, y0, w, h)

    # ───────── 绘制 ─────────
    # 共享的白色径向辉光精灵（一次性创建，所有粒子复用）。
    # 绘制时通过 mask_surface 就地着色，避免每帧按颜色 new 表面/渐变 → 内存恒定、CPU 大降。
    def _make_glow_sprite(self):
        px = 64
        surf = cairo.ImageSurface(cairo.FORMAT_ARGB32, px, px)
        c = cairo.Context(surf)
        cx = px / 2.0
        grad = cairo.RadialGradient(cx, cx, 1.0, cx, cx, cx - 1.0)
        grad.add_color_stop_rgba(0.0, 1.0, 1.0, 1.0, 1.0)
        grad.add_color_stop_rgba(0.18, 1.0, 1.0, 1.0, 0.72)
        grad.add_color_stop_rgba(0.50, 1.0, 1.0, 1.0, 0.22)
        grad.add_color_stop_rgba(1.0, 1.0, 1.0, 1.0, 0.0)
        c.set_source(grad)
        c.paint()
        return surf

    def _draw_glow(self, cr, x, y, size, r, g, b, alpha):
        if alpha <= 0.004:
            return
        surf = self._glow_surf
        cr.save()
        cr.translate(x, y)
        s = size / 64.0
        cr.scale(s, s)
        # 用精灵的 alpha 作遮罩，把纯色 (r,g,b) 染上去 → 任意颜色零额外分配
        cr.set_source_rgba(r, g, b, alpha)
        cr.mask_surface(surf, -32.0, -32.0)
        cr.restore()

    def draw(self, cr):
        cr.set_operator(cairo.OPERATOR_CLEAR)
        cr.paint()
        cr.set_operator(cairo.OPERATOR_OVER)
        cr.set_line_cap(cairo.LINE_CAP_ROUND)
        cr.set_line_join(cairo.LINE_JOIN_ROUND)
        if not self.particles:
            return
        # 烟雾垫底，环/闪光最上
        order = ('smoke', 'bubble', 'square', 'confetti',
                 'firefly', 'grit', 'star', 'comet', 'flash', 'ring')
        for kind in order:
            for p in self.particles:
                if p.kind == kind:
                    self.draw_particle(cr, p)

    def draw_particle(self, cr, p):
        t = p.age / p.life
        # 生命末尾几乎不可见 → 跳过绘制，降低渐隐期开销
        if (1.0 - t) < 0.03:
            return
        k = p.kind
        if k == 'smoke':
            a = 0.16 * (1.0 - t) * min(1.0, t * 8.0)
            if a <= 0.004:
                return
            rad = p.size * (1.0 + 1.1 * t)
            self._draw_glow(cr, p.x, p.y, rad * 2.6, p.r, p.g, p.b, a)
        elif k == 'firefly':
            a = (1.0 - t) * (0.45 + 0.55 * math.sin(p.age * 8.0 + p.phase))
            if a <= 0.01:
                return
            self._draw_glow(cr, p.x, p.y, p.size * 7.0, p.r, p.g, p.b, a * 0.5)
            cr.set_source_rgba(p.r, p.g, p.b, min(1.0, a * 1.6))
            cr.arc(p.x, p.y, p.size * 0.55, 0, 2.0 * math.pi)
            cr.fill()
        elif k == 'star':
            tw = 0.72 + 0.5 * math.sin(p.age * 20.0 + p.phase)
            a = (1.0 - t) ** 1.1
            r = p.size * tw
            if r < 0.4:
                return
            ir = r * 0.34
            cr.move_to(p.x, p.y - r)
            cr.line_to(p.x + ir, p.y - ir)
            cr.line_to(p.x + r, p.y)
            cr.line_to(p.x + ir, p.y + ir)
            cr.line_to(p.x, p.y + r)
            cr.line_to(p.x - ir, p.y + ir)
            cr.line_to(p.x - r, p.y)
            cr.line_to(p.x - ir, p.y - ir)
            cr.close_path()
            cr.set_source_rgba(p.r, p.g, p.b, a)
            cr.fill()
            cr.arc(p.x, p.y, max(0.5, ir * 0.45), 0, 2.0 * math.pi)
            cr.set_source_rgba(1.0, 1.0, 1.0, a * 0.9)
            cr.fill()
        elif k == 'bubble':
            r = p.size * (1.0 + 0.10 * math.sin(p.age * 5.0 + p.phase))
            a = 0.9 * (1.0 - t) ** 0.7
            cr.set_source_rgba(p.r, p.g, p.b, a * 0.16)
            cr.arc(p.x, p.y, r, 0, 2.0 * math.pi)
            cr.fill()
            cr.set_line_width(1.0)
            cr.set_source_rgba(1.0, 1.0, 1.0, a * 0.55)
            cr.arc(p.x, p.y, r, 0, 2.0 * math.pi)
            cr.stroke()
            cr.set_source_rgba(1.0, 1.0, 1.0, a * 0.85)
            cr.arc(p.x - r * 0.34, p.y - r * 0.36,
                   max(0.4, r * 0.16), 0, 2.0 * math.pi)
            cr.fill()
        elif k == 'square':
            a = 0.95 * (1.0 - t) ** 0.9
            cr.save()
            cr.translate(p.x, p.y)
            cr.rotate(p.rot)
            cr.rectangle(-p.size / 2.0, -p.size / 2.0, p.size, p.size)
            cr.set_source_rgba(p.r, p.g, p.b, a)
            cr.fill()
            cr.set_line_width(0.8)
            cr.set_source_rgba(1.0, 1.0, 1.0, a * 0.5)
            cr.rectangle(-p.size / 2.0, -p.size / 2.0, p.size, p.size)
            cr.stroke()
            cr.restore()
        elif k == 'confetti':
            a = 0.95 * (1.0 - t) ** 0.8
            sx = max(0.16, abs(math.sin(p.age * p.flip + p.phase)))
            cr.save()
            cr.translate(p.x, p.y)
            cr.rotate(p.rot)
            cr.scale(sx, 1.0)
            cr.rectangle(-p.w / 2.0, -p.h / 2.0, p.w, p.h)
            cr.set_source_rgba(p.r, p.g, p.b, a)
            cr.fill()
            cr.restore()
        elif k == 'grit':
            a = 1.0 - t
            if a <= 0.02:
                return
            cr.set_source_rgba(p.r, p.g, p.b, a)
            if p.size < 1.5:
                cr.rectangle(p.x - p.size, p.y - p.size,
                             p.size * 2.0, p.size * 2.0)
            else:
                cr.arc(p.x, p.y, p.size, 0, 2.0 * math.pi)
            cr.fill()
        elif k == 'comet':
            a = 1.0 - t
            tx = p.x - p.vx * 0.030
            ty = p.y - p.vy * 0.030
            lg = cairo.LinearGradient(tx, ty, p.x, p.y)
            lg.add_color_stop_rgba(0.0, p.r, p.g, p.b, 0.0)
            lg.add_color_stop_rgba(1.0, p.r, p.g, p.b, a)
            cr.set_line_width(p.size)
            cr.set_source(lg)
            cr.move_to(tx, ty)
            cr.line_to(p.x, p.y)
            cr.stroke()
            cr.set_source_rgba(1.0, 1.0, 1.0, a * 0.9)
            cr.arc(p.x, p.y, p.size * 0.5, 0, 2.0 * math.pi)
            cr.fill()
        elif k == 'ring':
            a = 0.85 * (1.0 - t) ** 1.5
            r = p.size * (1.0 - (1.0 - t) ** 3)
            cr.set_line_width(max(1.0, 6.0 * (1.0 - t)))
            cr.set_source_rgba(p.r, p.g, p.b, a)
            cr.arc(p.x, p.y, r, 0, 2.0 * math.pi)
            cr.stroke()
            cr.set_line_width(max(0.8, 2.0 * (1.0 - t)))
            cr.set_source_rgba(1.0, 1.0, 1.0, a * 0.8)
            cr.arc(p.x, p.y, r, 0, 2.0 * math.pi)
            cr.stroke()
        elif k == 'flash':
            a = (1.0 - t) ** 1.2 * 0.9
            r = p.size * (0.25 + 0.75 * t)
            cr.set_source_rgba(1.0, 1.0, 1.0, a)
            cr.arc(p.x, p.y, r, 0, 2.0 * math.pi)
            cr.fill()

    # ───────── 主循环 ─────────
    def tick(self):
        if not self.running:
            return False
        now = time.monotonic()
        dt = now - self.last_t
        self.last_t = now
        if dt <= 0.0:
            dt = 0.016
        elif dt > 0.05:
            dt = 0.05
        self.base_hue = (self.base_hue + dt * 0.10) % 1.0

        st = self.mouse_state()
        moved = False
        if st:
            x, y, btn = st
            if self.prev_pos is not None:
                dx = x - self.prev_pos[0]
                dy = y - self.prev_pos[1]
                dist = math.hypot(dx, dy)
                # 死区：指针亚像素抖动(<2.5px)不生成粒子，避免静止时持续喷粒子吃 CPU
                if dist > 2.5:
                    moved = True
                    if dt > 0.0:
                        self.pvx = self.pvx * 0.55 + (dx / dt) * 0.45
                        self.pvy = self.pvy * 0.55 + (dy / dt) * 0.45
                    if self.cfg['enabled']:
                        if dist > 110.0:
                            # 指针跳变（切桌面/瞬移）：只在落点撒，不拉一条长线
                            self.spawn_segment((x, y), (x, y),
                                               min(dist, 40.0))
                        else:
                            self.spawn_segment(self.prev_pos, (x, y), dist)
            self.prev_pos = (x, y)
            pressed = btn != 0
            if self.cfg['enabled']:
                if pressed and not self.btn_down:
                    self.burst(x, y)
                    moved = True
                elif pressed and self.btn_down:
                    self.held_spray(dt, x, y)
                    moved = True
                elif not pressed and self.btn_down:
                    self.release_puff(x, y)
                    moved = True
            self.btn_down = pressed
        # 刚有移动 → 保持全帧率（且下一轮立即恢复到 interval）
        self._active = moved

        self.update_particles(dt)
        # 空闲（无粒子）时卸载全屏透明叠加层，避免合成器持续合成 → 空闲 CPU 归零
        if self.particles:
            if not self._shown:
                self.overlay.show()
                self._shown = True
            self.queue_redraw()
        elif self._shown:
            self.overlay.hide()
            self._shown = False
        return True

    def quit(self):
        self.running = False
        if self._timer_id is not None:
            try:
                GLib.source_remove(self._timer_id)
            except Exception:
                pass
            self._timer_id = None
        save_config(self.cfg)
        try:
            if self.pidfile and os.path.exists(self.pidfile):
                os.remove(self.pidfile)
        except Exception:
            pass
        # Gtk.Application 模式下用 quit() 结束整个应用
        try:
            self._gtk_app.quit()
            return
        except Exception:
            pass
        Gtk.main_quit()


def main():
    ap = argparse.ArgumentParser(description='多彩粒子鼠标拖尾（GTK3）')
    ap.add_argument('--color', help='十六进制 RGB，如 ff00aa（单色模式）')
    ap.add_argument('--color-mode', choices=['rainbow', 'candy', 'mono'])
    ap.add_argument('--density', choices=['flow', 'normal', 'feast'])
    ap.add_argument('--fps', type=int)
    ap.add_argument('--effects',
                    help='逗号分隔的特效列表，如 star,confetti,bubble')
    ap.add_argument('--set', nargs='+', metavar='KEY=VAL',
                    help='运行时改设置：density=feast / enabled=false / '
                         'color_mode=candy / effects=grit,smoke / fps=40')
    args, _unknown = ap.parse_known_args()

    # 注意：--set 在模块顶部（图形栈导入之前）已经处理并退出了，
    # 走到这里说明没有 --set，用不到 args.set。
    lock = acquire_lock()
    if lock is None:
        print('已有一个实例在运行，本次退出', file=sys.stderr)
        return

    cli = {}
    if args.color:
        cli['color'] = args.color
        if not args.color_mode:
            cli['color_mode'] = 'mono'
    if args.color_mode:
        cli['color_mode'] = args.color_mode
    if args.density:
        cli['density'] = args.density
    if args.fps:
        cli['fps'] = args.fps
    if args.effects:
        keys = [s.strip() for s in args.effects.split(',')
                if s.strip() in EFFECT_KEYS]
        if keys:
            cli['effects'] = {k: (k in keys) for k in EFFECT_KEYS}

    app = App(cli)

    def on_sig(signum, frame):
        app.quit()

    def on_hup(signum, frame):
        # 配置热重载：Python 信号处理器只能做最小工作，重载丢到主循环里做
        GLib.idle_add(lambda: (app.reload_config(), False)[1])

    signal.signal(signal.SIGTERM, on_sig)
    signal.signal(signal.SIGINT, on_sig)
    try:
        signal.signal(signal.SIGHUP, on_hup)
    except Exception:
        pass

    # 踩坑记录：必须用 Gtk.Application + application_id 运行，不能裸 Gtk.main()。
    # 原因：AppIndicator 的 dbusmenu 导出与面板的菜单接管依赖「这个进程在 DBus
    # 上拥有一个 application bus name」。用 Gtk.main() 时进程没有 bus name，
    # Deepin 面板点图标会毫无反应（tooltip 变了但菜单不弹）——
    # 桌面挂件控制中心（com.deepin.DesktopWidgetsHub）用 Gtk.Application 就正常。
    from gi.repository import Gio

    class TrailApplication(Gtk.Application):
        def __init__(self, application_id, app_obj):
            super().__init__(application_id=application_id,
                             flags=Gio.ApplicationFlags.FLAGS_NONE)
            self._app = app_obj

        def do_activate(self):
            # 没有任何窗口，activate 后必须 hold() 保持进程存活，
            # 否则 Gtk.Application 会在没有窗口时立刻退出主循环。
            self.hold()

    gtk_app = TrailApplication(APP_ID, app)
    app._gtk_app = gtk_app
    gtk_app.run(None)


if __name__ == '__main__':
    main()
