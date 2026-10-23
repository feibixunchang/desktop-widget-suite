#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""任务栏信息条：固定显示在任务栏最左侧，单行显示
   顺序：年月日 星期 | 时间 | CPU 占用 | 内存占用 | 上行网速 | 下行网速
   每个栏目宽度在启动时按“最宽可能的文本”预先固定，数值刷新时
   标签与各栏位置完全不动，只有数值本身在栏内变化。
   右键菜单：调整字号 / 显示项开关 / 退出。
"""
import datetime
import glob
import json
import os
import re
import signal
import subprocess
import sys
import threading
import time
import urllib.parse
import urllib.request

import gi

gi.require_version('Gtk', '3.0')
gi.require_version('Gdk', '3.0')
gi.require_version('Pango', '1.0')
gi.require_version('PangoCairo', '1.0')
from gi.repository import Gtk, Gdk, Pango, PangoCairo, GLib
import cairo

APP = 'taskbar-info'
CONFIG_DIR = os.path.expanduser('~/.config/%s' % APP)
CONFIG_PATH = os.path.join(CONFIG_DIR, 'config.json')
LOCK_PATH = os.path.expanduser('~/.cache/%s.lock' % APP)

# 字体候选：换系统后只要命中任意一个就能正常显示
# （中文字形缺失时 Pango 会自动回退，因此这里只影响观感）
FONT_CANDIDATES = [
    '方正宋黑_GBK',
    'FZSongHei-B07',
    'Noto Sans CJK SC',
    'Source Han Sans SC',
    'WenQuanYi Micro Hei',
    'Microsoft YaHei',
    'DejaVu Sans Mono',
    'Sans',
]

TIME_SIZE = 24.0
DATE_SIZE = 17.0
INFO_SIZE = 18.0
# 日期允许的负字距范围（Pango 单位，1024 = 1px），用于在字号不缩小的前提下压窄到时间列宽
DATE_SPACING_STEPS = (0, -512, -1024, -1536, -2048, -2560, -3072)
FALLBACK_DOCK_H = 72

COLOR_TIME = (1.0, 0.82, 0.0, 1.0)
COLOR_DATE = (0.53, 0.81, 0.98, 1.0)
COLOR_WEATHER = (0.66, 0.93, 0.72, 1.0)
COLOR_LABEL = (0.60, 0.63, 0.70, 1.0)
COLOR_VALUE = (0.95, 0.96, 0.98, 1.0)
COLOR_UP = (0.45, 0.92, 0.60, 1.0)
COLOR_DOWN = (0.45, 0.76, 1.0, 1.0)

WEEK = ['周一', '周二', '周三', '周四', '周五', '周六', '周日']

DISK_NAME_MAP = {
    'SATA SSD': 'SATA',
    'USB Flash Drive': 'LKS',
    'D85C-EB30': 'LKS',
}

# 占位模板：用“最宽可能出现的文本”预先算出每栏宽度，保证永不抖动
T_TIME = '88:88:88'
T_DATE = '88/88/88 周三'
T_CPU_LABEL = 'CPU'
T_CPU_VALUE = '100%'
T_MEM_LABEL = '内存'
T_MEM_VALUE = '100%'
T_UP_LABEL = '↑'
T_UP_VALUE = '999.9 MB/s'
T_DOWN_LABEL = '↓'
T_DOWN_VALUE = '999.9 MB/s'
T_WEATHER = '城市 天气'

PAD_LEFT = 14
PAD_RIGHT = 18
GAP = 6           # 栏目之间的空隙（留一点点，尽量省任务栏空间）
LABEL_GAP = 4     # 同一栏内标签与数值之间的空隙（只留一点点）

DEFAULT_CFG = {
    'font_scale': 0.8,   # 默认字号倍率（按使用者喜好定稿；右键菜单可随时增减）
    'compact': True,     # 紧凑排版（False = 固定宽度，数值变化时位置不动）
    'show_cpu': True,
    'show_mem': True,
    'show_net': True,
    'show_weather': True,
    'weather_display': '北京市',       # 显示用的地点名
    'weather_location': '北京市',  # 查询用位置（城市名或 纬度,经度；Open-Meteo 公共接口，支持村级）
    'weather_refresh': 900,            # 刷新间隔（秒）
    # 任务栏内水平偏移(像素)。0 = 紧贴任务栏最左端; 调大可让信息条避开
    # dde-dock 的启动器/搜索图标。60 为实测合适的默认位置。
    'x_offset': 60,
}


def load_config():
    cfg = dict(DEFAULT_CFG)
    try:
        with open(CONFIG_PATH) as f:
            cfg.update(json.load(f))
    except Exception:
        pass
    return cfg


def save_config(cfg):
    try:
        os.makedirs(CONFIG_DIR, exist_ok=True)
        with open(CONFIG_PATH, 'w') as f:
            json.dump(cfg, f, ensure_ascii=False, indent=2)
    except Exception:
        pass


def _kill_stale(pid):
    """结束上一次遗留的挂件进程（确认确实是本程序再动手）"""
    try:
        with open('/proc/%d/cmdline' % pid, 'rb') as f:
            cmd = f.read().decode('utf-8', 'replace')
    except Exception:
        return
    if 'taskbar-info.py' not in cmd:
        return
    try:
        os.kill(pid, signal.SIGTERM)
    except Exception:
        return
    for _ in range(30):
        time.sleep(0.05)
        if not os.path.exists('/proc/%d' % pid):
            return


def single_instance():
    """保证同一用户只有一个实例；若已有旧实例，先结束它再接管，
       这样重装、重复点击启动菜单都不会出现“装好了却看不到”的情况。"""
    try:
        import fcntl
        os.makedirs(os.path.dirname(LOCK_PATH), exist_ok=True)
        f = open(LOCK_PATH, 'a+')
    except Exception:
        return None
    try:
        fcntl.flock(f, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except Exception:
        try:
            f.seek(0)
            old = f.read().strip()
            if old.isdigit() and int(old) != os.getpid():
                _kill_stale(int(old))
        except Exception:
            pass
        try:
            fcntl.flock(f, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except Exception:
            return None
    try:
        f.seek(0)
        f.truncate()
        f.write(str(os.getpid()))
        f.flush()
    except Exception:
        pass
    return f


def list_families():
    fams = set()
    try:
        for fam in PangoCairo.FontMap.get_default().list_families():
            fams.add(fam.get_name())
    except Exception:
        pass
    return fams


def pick_font():
    fams = list_families()
    for name in FONT_CANDIDATES:
        if name in fams:
            return name
    return 'Sans'


class CpuSampler:
    def __init__(self):
        self.prev = self.read()

    @staticmethod
    def read():
        try:
            with open('/proc/stat') as f:
                parts = f.readline().split()
            v = [int(x) for x in parts[1:]]
            idle = v[3] + (v[4] if len(v) > 4 else 0)
            return sum(v), idle
        except Exception:
            return None

    def usage(self):
        cur = self.read()
        if cur is None:
            return None
        old, self.prev = self.prev, cur
        if old is None:
            return None
        dt = cur[0] - old[0]
        di = cur[1] - old[1]
        if dt <= 0:
            return None
        return max(0.0, min(100.0, 100.0 * (dt - di) / dt))


def mem_usage():
    total = avail = free = buffers = cached = None
    try:
        with open('/proc/meminfo') as f:
            for line in f:
                parts = line.split()
                if len(parts) < 2:
                    continue
                key, val = parts[0], float(parts[1])
                if key == 'MemTotal:':
                    total = val
                elif key == 'MemAvailable:':
                    avail = val
                elif key == 'MemFree:':
                    free = val
                elif key == 'Buffers:':
                    buffers = val
                elif key == 'Cached:':
                    cached = val
    except Exception:
        return None
    if not total:
        return None
    if avail is None:
        # 老内核没有 MemAvailable 时近似估算
        if free is None:
            return None
        avail = free + (buffers or 0) + (cached or 0)
    return max(0.0, min(100.0, 100.0 * (total - avail) / total))


def default_route_iface():
    """取默认路由所在的网卡名，避免把 docker/veth 等虚拟网卡的流量算进来"""
    try:
        with open('/proc/net/route') as f:
            next(f, None)
            for line in f:
                cols = line.split()
                if len(cols) < 4:
                    continue
                if cols[1] == '00000000' and (int(cols[3], 16) & 0x1):
                    return cols[0]
    except Exception:
        pass
    return None


VIRTUAL_PREFIX = ('lo', 'docker', 'veth', 'br-', 'virbr', 'vmnet', 'tun', 'tap',
                  'vboxnet', 'wg', 'zt')


class NetSampler:
    def __init__(self):
        self.prev = self.read()
        self.t_prev = time.time()

    @staticmethod
    def read():
        iface = default_route_iface()
        rx = tx = 0
        try:
            with open('/proc/net/dev') as f:
                for line in f:
                    if ':' not in line:
                        continue
                    name, rest = line.split(':', 1)
                    name = name.strip()
                    if iface is not None:
                        if name != iface:
                            continue
                    else:
                        if name.startswith(VIRTUAL_PREFIX):
                            continue
                    cols = rest.split()
                    if len(cols) < 9:
                        continue
                    rx += int(cols[0])
                    tx += int(cols[8])
            return rx, tx
        except Exception:
            return None

    def rates(self):
        """返回 (上行 B/s, 下行 B/s)"""
        cur = self.read()
        t = time.time()
        if cur is None:
            return 0.0, 0.0
        old, t0 = self.prev, self.t_prev
        self.prev, self.t_prev = cur, t
        if old is None:
            return 0.0, 0.0
        dt = t - t0
        if dt <= 0.01:
            dt = 1.0
        up = max(0.0, (cur[1] - old[1]) / dt)
        down = max(0.0, (cur[0] - old[0]) / dt)
        return up, down


def fmt_speed(bps):
    if bps < 1024:
        return '%d B/s' % int(bps)
    kb = bps / 1024.0
    if kb < 1024:
        return '%.1f KB/s' % kb
    mb = kb / 1024.0
    if mb < 1024:
        return '%.1f MB/s' % mb
    return '%.1f GB/s' % (mb / 1024.0)


class WeatherFetcher:
    WMO = {
        0: '晴', 1: '大部晴朗', 2: '多云', 3: '阴', 45: '雾', 48: '雾凇',
        51: '毛毛雨', 53: '毛毛雨', 55: '毛毛雨', 56: '冻雨', 57: '冻雨',
        61: '小雨', 63: '中雨', 65: '大雨', 66: '冻雨', 67: '冻雨',
        71: '小雪', 73: '中雪', 75: '大雪', 77: '雪粒', 80: '阵雨',
        81: '阵雨', 82: '强阵雨', 85: '阵雪', 86: '阵雪', 95: '雷阵雨',
        96: '雷阵雨', 99: '雷阵雨',
    }
    DIRS = ['北', '东北偏北', '东北', '东北偏东', '东', '东南偏东', '东南',
            '东南偏南', '南', '西南偏南', '西南', '西南偏西', '西', '西北偏西',
            '西北', '西北偏北']

    def __init__(self, cfg):
        self.cfg = cfg
        self.details = {}
        self.forecast = []
        self.last_ok = 0.0
        self.last_try = 0.0
        self.source = ''
        self._lock = threading.Lock()

    @staticmethod
    def _json(url, timeout=15):
        req = urllib.request.Request(url, headers={
            'User-Agent': 'Mozilla/5.0 (X11; Linux x86_64) taskbar-info/1.0',
            'Accept': 'application/json'})
        with urllib.request.urlopen(req, timeout=timeout) as response:
            return json.loads(response.read().decode('utf-8', 'replace'))

    @classmethod
    def _direction(cls, value):
        try:
            return cls.DIRS[int((float(value) % 360) / 22.5) % 16]
        except Exception:
            return '--'

    @staticmethod
    def _strip_suffix(name):
        """GeoNames 对「北京市」这类带行政后缀的词条常返回空，剥离后再试"""
        for suf in ('特别行政区', '自治区', '地区', '盟', '自治州', '市', '县', '区'):
            if name.endswith(suf) and len(name) > len(suf):
                return name[:-len(suf)]
        return name

    def _geo(self):
        loc = str(self.cfg.get('weather_location', '北京市')).strip()
        if ',' in loc:
            a, b = loc.split(',', 1)
            return float(a), float(b)
        result = {}
        for name in (loc, self._strip_suffix(loc)):    # 原样 -> 剥离行政后缀
            url = ('https://geocoding-api.open-meteo.com/v1/search?name=%s&count=1&language=zh'
                   % urllib.parse.quote(name))
            data = self._json(url)
            rs = data.get('results') or []
            if rs:
                result = rs[0]
                break
        if not result:
            raise ValueError('未找到地点: %s' % loc)
        return float(result['latitude']), float(result['longitude'])

    @staticmethod
    def _value(value, digits=1, suffix=''):
        if value is None or value == '':
            return '--'
        try:
            return ('%.*f%s' % (digits, float(value), suffix))
        except Exception:
            return str(value) + suffix

    def fetch(self):
        self.last_try = time.time()
        try:
            lat, lon = self._geo()
            url = (
                'https://api.open-meteo.com/v1/forecast?latitude=%s&longitude=%s'
                '&current=temperature_2m,relative_humidity_2m,apparent_temperature,'
                'is_day,precipitation,rain,showers,snowfall,weather_code,cloud_cover,'
                'pressure_msl,surface_pressure,wind_speed_10m,wind_direction_10m,'
                'wind_gusts_10m,uv_index'
                '&daily=weather_code,temperature_2m_max,temperature_2m_min,'
                'apparent_temperature_max,apparent_temperature_min,sunrise,sunset,'
                'uv_index_max,precipitation_sum,precipitation_probability_max,'
                'wind_speed_10m_max,wind_direction_10m_dominant&forecast_days=7'
                '&timezone=auto' % (lat, lon))
            data = self._json(url)
            current = data.get('current') or {}
            daily = data.get('daily') or {}
            details = {
                'city': self.cfg.get('weather_display', '当前位置'),
                'text': self.WMO.get(current.get('weather_code'), '未知'),
                'temp': current.get('temperature_2m'),
                'feels': current.get('apparent_temperature'),
                'humidity': current.get('relative_humidity_2m'),
                'precipitation': current.get('precipitation'),
                'rain': current.get('rain'),
                'showers': current.get('showers'),
                'snowfall': current.get('snowfall'),
                'cloud_cover': current.get('cloud_cover'),
                'pressure': current.get('pressure_msl') or current.get('surface_pressure'),
                'wind_speed': current.get('wind_speed_10m'),
                'wind_dir': self._direction(current.get('wind_direction_10m')),
                'wind_gust': current.get('wind_gusts_10m'),
                'uv': current.get('uv_index'),
                'is_day': current.get('is_day', 1),
                'time': current.get('time', '')[-5:],
            }
            forecast = []
            for i, date in enumerate(daily.get('time', [])[:7]):
                forecast.append({
                    'date': date,
                    'text': self.WMO.get((daily.get('weather_code') or [None] * 7)[i], '未知'),
                    'tmax': (daily.get('temperature_2m_max') or [None] * 7)[i],
                    'tmin': (daily.get('temperature_2m_min') or [None] * 7)[i],
                    'feels_max': (daily.get('apparent_temperature_max') or [None] * 7)[i],
                    'feels_min': (daily.get('apparent_temperature_min') or [None] * 7)[i],
                    'sunrise': ((daily.get('sunrise') or [''] * 7)[i] or '')[-5:],
                    'sunset': ((daily.get('sunset') or [''] * 7)[i] or '')[-5:],
                    'uv': (daily.get('uv_index_max') or [None] * 7)[i],
                    'precip': (daily.get('precipitation_sum') or [None] * 7)[i],
                    'precip_prob': (daily.get('precipitation_probability_max') or [None] * 7)[i],
                    'wind': (daily.get('wind_speed_10m_max') or [None] * 7)[i],
                    'wind_dir': self._direction((daily.get('wind_direction_10m_dominant') or [None] * 7)[i]),
                })
            with self._lock:
                self.details = details
                self.forecast = forecast
                self.source = 'Open-Meteo'
                self.last_ok = time.time()
            return details
        except Exception:
            return None

    def get_text(self):
        with self._lock:
            d = dict(self.details)
        if not d:
            return '%s --' % self.cfg.get('weather_display', '当前位置')
        return '%s %s℃ %s' % (d.get('city', ''), self._value(d.get('temp'), 1), d.get('text', '--'))

    def get_details(self):
        with self._lock:
            return dict(self.details), [dict(x) for x in self.forecast], self.source

    def maybe_refresh(self, force=False):
        now = time.time()
        interval = float(self.cfg.get('weather_refresh', 900) or 900)
        if not force and self.last_ok and now - self.last_ok < interval:
            return
        if not force and now - self.last_try < 60:
            return
        self.last_try = now
        threading.Thread(target=self.fetch, daemon=True).start()


def fmt_bytes(value):
    if value is None:
        return '--'
    value = float(value)
    for unit in ('B', 'KB', 'MB', 'GB', 'TB', 'PB'):
        if abs(value) < 1024.0:
            return '%.1f%s' % (value, unit)
        value /= 1024.0
    return '%.1fEB' % value


def run_command(args, timeout=5):
    try:
        return subprocess.run(args, capture_output=True, text=True,
                              timeout=timeout, check=False).stdout.strip()
    except Exception:
        return ''


class SystemDetails:
    @staticmethod
    def _read(path):
        try:
            with open(path) as f:
                return f.read().strip()
        except Exception:
            return ''

    def addresses(self):
        result = []
        raw = run_command(['ip', '-j', 'addr', 'show'])
        if raw:
            try:
                for link in json.loads(raw):
                    iface = link.get('ifname', '')
                    for addr in link.get('addr_info', []):
                        family = addr.get('family')
                        if family not in ('inet', 'inet6'):
                            continue
                        address = addr.get('local', '')
                        if (addr.get('scope') == 'link' or address.startswith('fe80:') or
                                iface == 'lo' or address.startswith('127.') or address == '::1'):
                            continue
                        result.append((family, iface, address))
            except Exception:
                pass
        if not result:
            for address in run_command(['hostname', '-I']).split():
                result.append(('inet', '', address))
        return result

    def gpu(self):
        result = {'name': '--', 'usage': None, 'temperature': None,
                  'memory_used': None, 'memory_total': None}
        for card in glob.glob('/sys/class/drm/card[0-9]*'):
            device = os.path.join(card, 'device')
            busy = self._read(os.path.join(device, 'gpu_busy_percent'))
            if busy:
                result['name'] = os.path.basename(card)
                try:
                    result['usage'] = float(busy)
                except Exception:
                    pass
                for hwmon in glob.glob(os.path.join(device, 'hwmon', 'hwmon*')):
                    temp = self._read(os.path.join(hwmon, 'temp1_input'))
                    if temp:
                        try:
                            result['temperature'] = float(temp) / 1000.0
                        except Exception:
                            pass
                        break
                for key, target in (('mem_info_vram_used', 'memory_used'),
                                    ('mem_info_vram_total', 'memory_total')):
                    value = self._read(os.path.join(device, key))
                    if value:
                        try:
                            result[target] = float(value)
                        except Exception:
                            pass
                break
        if result['usage'] is None:
            raw = run_command(['nvidia-smi', '--query-gpu=utilization.gpu,temperature.gpu,memory.used,memory.total',
                               '--format=csv,noheader,nounits'])
            if raw:
                parts = [x.strip() for x in raw.splitlines()[0].split(',')]
                if len(parts) >= 4:
                    result['name'] = 'NVIDIA GPU'
                    try:
                        result['usage'] = float(parts[0])
                        result['temperature'] = float(parts[1])
                        result['memory_used'] = float(parts[2]) * 1024 * 1024
                        result['memory_total'] = float(parts[3]) * 1024 * 1024
                    except Exception:
                        pass
        return result

    def hardware(self):
        temperatures = []
        fans = []
        for path in glob.glob('/sys/class/hwmon/hwmon*'):
            name = self._read(os.path.join(path, 'name'))
            for item in glob.glob(os.path.join(path, 'temp*_input')):
                value = self._read(item)
                if not value:
                    continue
                base = item[:-5]
                label = self._read(base + 'label') or os.path.basename(base)
                try:
                    temperatures.append((name, label, float(value) / 1000.0))
                except Exception:
                    pass
            for item in glob.glob(os.path.join(path, 'fan*_input')):
                value = self._read(item)
                if not value:
                    continue
                base = item[:-5]
                label = self._read(base + 'label') or os.path.basename(base)
                try:
                    fans.append((name, label, float(value)))
                except Exception:
                    pass
        freqs = []
        for line in self._read('/proc/cpuinfo').splitlines():
            if line.lower().startswith('cpu mhz'):
                try:
                    freqs.append(float(line.split(':', 1)[1]))
                except Exception:
                    pass
        battery = []
        for path in glob.glob('/sys/class/power_supply/*'):
            kind = self._read(os.path.join(path, 'type'))
            if kind == 'Battery':
                capacity = self._read(os.path.join(path, 'capacity'))
                status = self._read(os.path.join(path, 'status'))
                if capacity:
                    battery.append('%s%% %s' % (capacity, status or ''))
        try:
            load = os.getloadavg()
        except Exception:
            load = (0.0, 0.0, 0.0)
        return {
            'cpu_freq': sum(freqs) / len(freqs) if freqs else None,
            'load': load,
            'uptime': self._read('/proc/uptime').split()[0] if self._read('/proc/uptime') else '--',
            'kernel': os.uname().release,
            'host': os.uname().nodename,
            'temperatures': temperatures,
            'fans': fans,
            'battery': battery,
            'gpu': self.gpu(),
        }

    @staticmethod
    def fmt_uptime(seconds):
        try:
            seconds = int(float(seconds))
            days, seconds = divmod(seconds, 86400)
            hours, seconds = divmod(seconds, 3600)
            minutes = seconds // 60
            return '%d天 %02d:%02d' % (days, hours, minutes)
        except Exception:
            return '--'


class DiskSampler:
    def __init__(self):
        self.previous = {}
        self.previous_time = 0.0

    @staticmethod
    def _walk(devices):
        for device in devices or []:
            yield device
            yield from DiskSampler._walk(device.get('children'))

    def _devices(self):
        raw = run_command(['lsblk', '-J', '-b', '-o',
                           'NAME,TYPE,FSTYPE,TRAN,PKNAME,MODEL,SIZE,ROTA'])
        if not raw:
            return {}
        try:
            data = json.loads(raw)
            return {d.get('name', ''): d for d in self._walk(data.get('blockdevices'))}
        except Exception:
            return {}

    def _diskstats(self):
        result = {}
        try:
            with open('/proc/diskstats') as f:
                for line in f:
                    parts = line.split()
                    if len(parts) < 10:
                        continue
                    result[parts[2]] = (int(parts[5]), int(parts[9]))
        except Exception:
            pass
        return result

    def sample(self):
        devices = self._devices()
        stats = self._diskstats()
        now = time.time()
        elapsed = max(0.1, now - self.previous_time) if self.previous_time else 0
        rates = {}
        for name, value in stats.items():
            old = self.previous.get(name)
            if old and elapsed:
                rates[name] = (max(0, value[0] - old[0]) * 512.0 / elapsed,
                               max(0, value[1] - old[1]) * 512.0 / elapsed)
            else:
                rates[name] = (0.0, 0.0)
        self.previous = stats
        self.previous_time = now
        mounts = []
        try:
            with open('/proc/mounts') as f:
                for line in f:
                    parts = line.split()
                    if len(parts) < 2 or not parts[0].startswith('/dev/'):
                        continue
                    source, mount = parts[0], parts[1].replace('\\040', ' ')
                    if mount.startswith('/boot'):
                        continue
                    mounts.append((source, mount))
        except Exception:
            pass
        used_names = set()
        result = []
        for source, mount in mounts:
            name = os.path.basename(os.path.realpath(source))
            if name in used_names:
                continue
            device = devices.get(name, {})
            parent = device.get('pkname') or ''
            parent_device = devices.get(parent, {})
            fstype = (device.get('fstype') or '').lower()
            transport = (parent_device.get('tran') or device.get('tran') or '').lower()
            if fstype in ('ntfs', 'vfat', 'exfat', 'msdos', 'fuseblk') and transport != 'usb':
                continue
            try:
                stat = os.statvfs(mount)
                total = stat.f_frsize * stat.f_blocks
                available = stat.f_frsize * stat.f_bavail
                free = stat.f_frsize * stat.f_bfree
            except Exception:
                continue
            used = max(0, total - free)
            rate = rates.get(name) or rates.get(parent) or (0.0, 0.0)
            result.append({
                'mount': mount, 'device': source, 'fstype': fstype or '--',
                'model': (parent_device.get('model') or device.get('model') or '').strip() or name,
                'transport': transport or '--', 'rotational': parent_device.get('rota'),
                'total': total, 'used': used, 'available': available,
                'percent': 100.0 * used / total if total else 0,
                'read': rate[0], 'write': rate[1],
            })
            used_names.add(name)
        return result


class InfoPanel(Gtk.Window):
    BG_TOP = (0.015, 0.025, 0.045, 0.94)
    BG_BOTTOM = (0.0, 0.0, 0.0, 0.72)
    TITLE = (0.88, 0.93, 1.0, 1.0)
    LABEL = (0.63, 0.70, 0.80, 1.0)
    VALUE = (0.96, 0.97, 1.0, 1.0)
    ACCENT = (0.36, 0.72, 1.0, 1.0)
    GOOD = (0.40, 0.92, 0.65, 1.0)

    def __init__(self, owner, disk_sampler=None):
        super().__init__(type=Gtk.WindowType.POPUP)
        self.owner = owner
        self.system = SystemDetails()
        self.disks = disk_sampler or DiskSampler()
        self.data = {}
        self.panel_rect = (0, 0, 0, 0)
        self.timeout_id = None
        self.grabbed = False
        self.seat = Gdk.Display.get_default().get_default_seat()
        self.set_title('任务栏信息')
        self.set_decorated(False)
        self.set_resizable(False)
        self.set_app_paintable(True)
        self.set_skip_taskbar_hint(True)
        self.set_skip_pager_hint(True)
        self.set_keep_above(True)
        self.set_accept_focus(True)
        visual = self.get_screen().get_rgba_visual()
        if visual is not None:
            self.set_visual(visual)
        self.da = Gtk.DrawingArea()
        self.da.set_can_focus(True)
        self.da.connect('draw', self.on_draw)
        self.add(self.da)
        self.connect('draw', lambda w, cr: False)
        self.add_events(Gdk.EventMask.BUTTON_PRESS_MASK | Gdk.EventMask.KEY_PRESS_MASK)
        self.connect('button-press-event', self.on_button_press)
        self.connect('key-press-event', self.on_key_press)
        self.set_size_request(900, 850)

    def _font(self, size, bold=False):
        font = Pango.FontDescription()
        font.set_family(self.owner.font)
        font.set_size(int(round(size * Pango.SCALE)))
        if bold:
            font.set_weight(Pango.Weight.BOLD)
        return font

    def _layout(self, text, size, bold=False):
        layout = Pango.Layout(self.da.get_pango_context())
        layout.set_font_description(self._font(size, bold))
        layout.set_text(str(text), -1)
        return layout

    def _text(self, cr, text, x, y, size=12, color=None, bold=False):
        cr.set_source_rgba(*(color or self.VALUE))
        layout = self._layout(text, size, bold)
        cr.move_to(x, y)
        PangoCairo.show_layout(cr, layout)
        return layout.get_pixel_size()[0]

    def _value(self, cr, label, value, x, y, width, size=12):
        self._text(cr, label, x, y, size, self.LABEL)
        value = str(value)
        while size > 12 and self._layout(value, size).get_pixel_size()[0] > width - 150:
            size -= 0.5
        vw = self._layout(value, size).get_pixel_size()[0]
        self._text(cr, value, x + width - vw, y, size, self.VALUE)

    def _section(self, cr, title, x, y, width, center=False):
        title_x = x
        if center:
            title_width = self._layout(title, 14, True).get_pixel_size()[0]
            title_x = x + (width - title_width) / 2.0
        self._text(cr, title, title_x, y, 14, self.ACCENT, True)
        cr.set_source_rgba(*self.ACCENT)
        cr.set_line_width(1)
        cr.move_to(x, y + 34)
        cr.line_to(x + width, y + 34)
        cr.stroke()
        return y + 52

    def _rounded_background(self, cr, width, height):
        cr.set_operator(cairo.OPERATOR_SOURCE)
        cr.set_source_rgba(0, 0, 0, 0)
        cr.paint()
        cr.set_operator(cairo.OPERATOR_OVER)
        cr.save()
        cr.move_to(16, 0)
        cr.line_to(width - 16, 0)
        cr.arc(width - 16, 16, 16, -1.5707963268, 0)
        cr.line_to(width, height - 16)
        cr.arc(width - 16, height - 16, 16, 0, 1.5707963268)
        cr.line_to(16, height)
        cr.arc(16, height - 16, 16, 1.5707963268, 3.1415926536)
        cr.line_to(0, 16)
        cr.arc(16, 16, 16, 3.1415926536, 4.7123889804)
        cr.close_path()
        gradient = cairo.LinearGradient(0, 0, 0, height)
        gradient.add_color_stop_rgba(0, *self.BG_TOP)
        gradient.add_color_stop_rgba(1, *self.BG_BOTTOM)
        cr.set_source(gradient)
        cr.fill_preserve()
        cr.set_source_rgba(0.35, 0.65, 0.95, 0.45)
        cr.set_line_width(1)
        cr.stroke()
        cr.restore()

    def _weather_rows(self, cr, x, y, width, details, forecast):
        y = self._section(cr, '今日天气', x, y, width)
        rows = [
            ('天气', '%s · %s' % (details.get('city', '--'), details.get('text', '--'))),
            ('温度 / 体感', '%s℃ / %s℃' % (details.get('temp', '--'), details.get('feels', '--'))),
            ('湿度 / 云量', '%s%% / %s%%' % (details.get('humidity', '--'), details.get('cloud_cover', '--'))),
            ('风向 / 风速', '%s %s km/h' % (details.get('wind_dir', '--'), details.get('wind_speed', '--'))),
            ('阵风', '%s km/h' % details.get('wind_gust', '--')),
            ('降水 / 雨雪', '%s mm / %s mm' % (details.get('precipitation', '--'), details.get('snowfall', '--'))),
            ('气压', '%s hPa' % details.get('pressure', '--')),
            ('紫外线', 'UV %s' % details.get('uv', '--')),
            ('日出 / 日落', '%s / %s' % (forecast[0].get('sunrise', '--') if forecast else '--', forecast[0].get('sunset', '--') if forecast else '--')),
        ]
        for label, value in rows:
            self._value(cr, label, value, x, y, width)
            y += 32
        y += 10
        y = self._section(cr, '未来 7 天', x, y, width)
        week = '一二三四五六日'
        for item in forecast[:7]:
            try:
                day = '周' + week[datetime.datetime.strptime(item['date'], '%Y-%m-%d').weekday()]
            except Exception:
                day = '--'
            date = item.get('date', '')[5:]
            self._text(cr, day, x, y, 12, self.LABEL, True)
            self._text(cr, date, x + 52, y, 12, self.LABEL)
            self._text(cr, item.get('text', '--'), x + 132, y, 12, self.VALUE)
            self._text(cr, '↑%s°' % item.get('tmax', '--'), x + 292, y, 12, self.VALUE)
            self._text(cr, '↓%s°' % item.get('tmin', '--'), x + 370, y, 12, self.VALUE)
            self._text(cr, '雨%s%%' % item.get('precip_prob', '--'), x + 458, y, 12, self.VALUE)
            y += 30
        return y

    def _system_rows(self, cr, x, y, width, owner, hardware, addresses):
        y = self._section(cr, '系统状态', x, y, width)
        load = hardware.get('load', (0, 0, 0))
        gpu = hardware.get('gpu', {})
        gpu_usage = '未检测到' if gpu.get('usage') is None else '%.0f%%' % gpu['usage']
        gpu_temp = '未检测到' if gpu.get('temperature') is None else '%.1f℃' % gpu['temperature']
        gpu_memory = '未检测到'
        if gpu.get('memory_total'):
            gpu_memory = '%s / %s' % (fmt_bytes(gpu.get('memory_used')), fmt_bytes(gpu.get('memory_total')))
        rows = [
            ('CPU 使用率', '%s' % owner._cpu_text),
            ('内存使用率', '%s' % owner._mem_text),
            ('GPU 使用率', gpu_usage),
            ('GPU 温度', gpu_temp),
            ('GPU 显存', gpu_memory),
            ('平均负载', '%.2f  %.2f  %.2f' % load),
            ('CPU 频率', '%s MHz' % (self._num(hardware.get('cpu_freq'), 0) if hardware.get('cpu_freq') is not None else '--')),
            ('运行时间', SystemDetails.fmt_uptime(hardware.get('uptime'))),
            ('主机 / 内核', '%s / %s' % (hardware.get('host', '--'), hardware.get('kernel', '--'))),
        ]
        for label, value in rows:
            self._value(cr, label, value, x, y, width)
            y += 32
        y += 8
        y = self._section(cr, '硬件监控', x, y, width)
        temps = hardware.get('temperatures', [])
        fans = hardware.get('fans', [])
        preferred = ('k10temp', 'coretemp', 'zenpower', 'cpu_thermal')
        selected = None
        for name in preferred:
            for item in temps:
                if item[0].lower() == name:
                    selected = item
                    break
            if selected:
                break
        if selected is None and temps:
            selected = temps[0]
        temp_text = '%s %.1f℃' % (selected[1], selected[2]) if selected else '未检测到温度传感器'
        fan_text = '、'.join('%s %.0f RPM' % (label, value) for _, label, value in fans[:3]) or '系统未暴露 fan 传感器'
        self._value(cr, 'CPU 温度', temp_text, x, y, width, 12)
        y += 32
        self._value(cr, '风扇转速', fan_text, x, y, width, 12)
        y += 32
        battery = '、'.join(hardware.get('battery', [])) or '未检测到电池'
        self._value(cr, '电池', battery, x, y, width, 12)
        y += 32
        y = self._section(cr, '网络地址', x, y, width)
        inet = next((address for family, iface, address in addresses if family == 'inet'), '未获取')
        inet6 = next((address for family, iface, address in addresses if family == 'inet6'), '未获取')
        self._value(cr, 'IPv4', inet, x, y, width, 12)
        y += 32
        self._value(cr, 'IPv6', inet6, x, y, width, 12)
        y += 32
        return y

    def _disk_rows(self, cr, x, y, width):
        disks = self.data.get('disks', [])
        y = self._section(cr, '硬盘与实时读写', x, y, width, True)
        if not disks:
            self._text(cr, '未检测到已挂载磁盘', x + width / 2.0, y, 12, self.LABEL)
            return y + 32
        columns = 2 if len(disks) > 1 else 1
        gap = 36
        card_width = (width - gap * (columns - 1)) / float(columns)
        card_height = 116
        rows = (len(disks) + columns - 1) // columns
        for row in range(rows):
            row_disks = disks[row * columns:(row + 1) * columns]
            row_width = len(row_disks) * card_width + (len(row_disks) - 1) * gap
            row_x = x + (width - row_width) / 2.0
            for column, disk in enumerate(row_disks):
                card_x = row_x + column * (card_width + gap)
                card_y = y + row * (card_height + 22)
                tag = '外接' if disk.get('transport') == 'usb' else '系统'
                mount = disk.get('mount', '--')
                base = os.path.basename(mount) or mount
                model = disk.get('model', '') or '--'
                custom = None
                if model in DISK_NAME_MAP:
                    custom = DISK_NAME_MAP[model]
                elif base in DISK_NAME_MAP:
                    custom = DISK_NAME_MAP[base]
                if custom:
                    title = '%s  [%s]' % (custom, tag)
                elif len(base) >= 12 and re.match(r'^[0-9A-Fa-f-]+$', base):
                    title = '%s  [%s]' % (model, tag)
                else:
                    title = '%s  [%s]  %s' % (base, tag, model)
                self._text(cr, title, card_x, card_y, 12, self.GOOD, True)
                if disk.get('total'):
                    usage = '已用 %s  可用 %s  总量 %s  %s%%' % (
                        fmt_bytes(disk.get('used')), fmt_bytes(disk.get('available')),
                        fmt_bytes(disk.get('total')), self._num(disk.get('percent'), 0))
                else:
                    usage = '未挂载 · 总量 %s' % fmt_bytes(disk.get('total'))
                self._value(cr, '容量', usage, card_x, card_y + 30, card_width, 12)
                self._value(cr, '速度', '读 %s/s  写 %s/s  %s' % (
                    fmt_speed(disk.get('read', 0)), fmt_speed(disk.get('write', 0)),
                    '机械盘' if disk.get('rotational') == '1' else '固态/未知'),
                    card_x, card_y + 64, card_width, 12)
        return y + rows * (card_height + 22)

    @staticmethod
    def _num(value, digits=1):
        if value is None:
            return '--'
        try:
            return ('%.*f' % (digits, float(value)))
        except Exception:
            return str(value)

    def on_draw(self, widget, cr):
        current_disks = self.disks.sample()
        self.data['disks'] = current_disks
        disk_count = len(current_disks)
        if disk_count != getattr(self, '_geometry_disk_count', -1):
            self._geometry_disk_count = disk_count
            self._update_geometry()
        panel_x, panel_y, panel_width, panel_height = self.panel_rect
        if panel_width <= 0 or panel_height <= 0:
            return False
        cr.save()
        cr.translate(panel_x, panel_y)
        self._rounded_background(cr, panel_width, panel_height)
        details, forecast, source = self.data.get('weather', ({}, [], ''))
        hardware = self.data.get('hardware', {})
        addresses = self.data.get('addresses', [])
        title = '任务栏信息  ·  %s' % datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
        self._text(cr, title, 28, 20, 16, self.TITLE, True)
        self._text(cr, '数据源 %s  ·  点击外部区域关闭' % (source or '等待联网'), 28, 51, 12, self.LABEL)
        column_gap = 36
        column_width = (panel_width - 56 - column_gap) / 2.0
        right_x = 28 + column_width + column_gap
        left_y = self._weather_rows(cr, 28, 86, column_width, details, forecast)
        right_y = self._system_rows(cr, right_x, 86, column_width, self.owner, hardware, addresses)
        self._disk_rows(cr, 28, max(left_y, right_y) + 18, panel_width - 56)
        cr.restore()
        return False

    def _update_geometry(self):
        screen = self.get_screen()
        screen_w = screen.get_width()
        screen_h = screen.get_height()
        width = min(1200, screen_w - 24)
        disk_rows = (len(self.data.get('disks', [])) + 1) // 2
        content_height = 900 + max(0, disk_rows - 1) * 138
        available_height = screen_h - self.owner.dock_h - 24
        height = min(1200, max(820, min(content_height, available_height)))
        dock_top, dock_h = self.owner.detect_dock()
        try:
            owner_x = self.owner.win.get_position()[0]
        except Exception:
            owner_x = 0
        x = max(12, min(owner_x, screen_w - width - 12))
        if self.owner.panel_at_top:
            y = dock_top + dock_h + 8
        else:
            y = dock_top - height - 8
        y = max(8, min(y, screen_h - height - 8))
        self.panel_rect = (x, y, width, height)
        self.da.queue_draw()
        self.queue_draw()

    def refresh(self, force=False):
        weather = self.owner.weather
        if force:
            weather.maybe_refresh(force=True)
        details, forecast, source = weather.get_details()
        self.data = {
            'weather': (details, forecast, source),
            'hardware': self.system.hardware(),
            'addresses': self.system.addresses(),
            'disks': self.disks.sample(),
        }
        self.da.queue_draw()
        self.queue_draw()
        self._update_geometry()
        return True

    def open(self):
        self.refresh(force=True)
        screen = self.get_screen()
        screen_w = screen.get_width()
        screen_h = screen.get_height()
        self.set_size_request(screen_w, screen_h)
        self.move(0, 0)
        self.show_all()
        self.present()
        self._update_geometry()
        try:
            status = self.seat.grab(self.get_window(), Gdk.SeatCapabilities.ALL,
                                    True, None, None, None, None)
            self.grabbed = status == Gdk.GrabStatus.SUCCESS
        except Exception:
            self.grabbed = False
        GLib.idle_add(self.da.grab_focus)

    def close(self):
        if self.grabbed:
            try:
                self.seat.ungrab()
            except Exception:
                pass
            self.grabbed = False
        self.hide()

    def on_button_press(self, widget, event):
        x, y, width, height = self.panel_rect
        if (event.x_root < x or event.x_root >= x + width or
                event.y_root < y or event.y_root >= y + height):
            self.close()
        return True

    def on_key_press(self, widget, event):
        if event.keyval == Gdk.KEY_Escape:
            self.close()
            return True
        return False


class TimeBar:
    def __init__(self):
        self.cfg = load_config()

        scr = Gdk.Screen.get_default()
        self.screen_w = scr.get_width()
        self.screen_h = scr.get_height()
        self.screen_bottom = self.screen_h
        self.dock_h = FALLBACK_DOCK_H
        self.dock_top = self.screen_h - self.dock_h
        self._asc_cache = {}
        self.win_w, self.win_h = 420, FALLBACK_DOCK_H
        self._req_size = None
        self.panel_at_top = False
        self.y_row1, self.y_row2 = 8, 40
        self.font = pick_font()
        self.font_scale = float(self.cfg.get('font_scale', 1.0))
        self.cpu = CpuSampler()
        self.net = NetSampler()
        self.weather = WeatherFetcher(self.cfg)
        self.disk_sampler = DiskSampler()
        self.panel = None

        self.win = Gtk.Window()
        self.win.set_title(APP)
        self.win.set_decorated(False)
        self.win.set_resizable(False)
        self.win.set_app_paintable(True)
        self.win.set_skip_taskbar_hint(True)
        self.win.set_skip_pager_hint(True)
        self.win.set_accept_focus(False)
        self.win.set_keep_above(True)
        screen = self.win.get_screen()
        visual = screen.get_rgba_visual()
        if visual:
            self.win.set_visual(visual)

        self.da = Gtk.DrawingArea()
        self.da.connect('draw', self.on_draw)
        self.win.add(self.da)

        self.win.connect('destroy', self.on_quit)
        self.win.connect('button-press-event', self.on_press)
        for sig in (signal.SIGTERM, signal.SIGINT, signal.SIGHUP):
            try:
                GLib.unix_signal_add(GLib.PRIORITY_DEFAULT, sig, self.on_quit)
            except Exception:
                pass

        self._sample()
        self.win.show_all()
        self.relayout()
        if not os.path.exists(CONFIG_PATH):
            save_config(self.cfg)
        GLib.timeout_add(1000, self.tick)

    # ---------- 字体与测量 ----------

    def _fd(self, size):
        fd = Pango.FontDescription()
        fd.set_family(self.font)
        fd.set_weight(Pango.Weight.BOLD)
        fd.set_absolute_size(int(size * Pango.SCALE * 96.0 / 72.0))
        return fd

    def _layout(self, ctx, text, size):
        l = Pango.Layout(ctx)
        l.set_font_description(self._fd(size))
        l.set_text(text, -1)
        return l

    def _measure(self, ctx, text, size):
        return self._layout(ctx, text, size).get_pixel_size()

    def _ascent(self, ctx, size):
        """该字号的基线位置(像素)，用于让同一行不同字号的文字坐在同一条基线上"""
        key = round(size, 1)
        v = self._asc_cache.get(key)
        if v is None:
            v = self._layout(ctx, '8', size).get_baseline() / float(Pango.SCALE)
            self._asc_cache[key] = v
        return v

    def _char_pitch(self, ctx, size):
        """等宽字距：数字与冒号中较宽者，用于让时间每秒刷新时宽度恒定"""
        return max(self._measure(ctx, '8', size)[0],
                   self._measure(ctx, ':', size)[0])

    def _draw_mono(self, cr, ctx, text, x, y, size):
        pitch = self._char_pitch(ctx, size)
        for i, ch in enumerate(text):
            l = self._layout(ctx, ch, size)
            w = l.get_pixel_size()[0]
            cr.move_to(x + i * pitch + (pitch - w) / 2.0, y)
            PangoCairo.show_layout(cr, l)

    # ---------- 任务栏几何 ----------

    def detect_dock(self):
        """推算任务栏(面板)的位置与高度：返回 (任务栏顶边 y, 任务栏高度)
           支持任务栏在屏幕底部或顶部，探测失败时用默认值（贴底）"""
        self.panel_at_top = False
        try:
            scr = Gdk.Screen.get_default()
            mon = None
            gw = self.win.get_window()
            if gw is not None:
                mon = scr.get_monitor_at_window(gw)
            if mon is None or mon < 0:
                mon = scr.get_primary_monitor()
            if mon is None or mon < 0:
                mon = 0
            geo = scr.get_monitor_geometry(mon)
            wa = scr.get_monitor_workarea(mon)
            self.screen_bottom = geo.y + geo.height
            top_gap = wa.y - geo.y
            bottom_gap = self.screen_bottom - (wa.y + wa.height)
            if bottom_gap >= 16 and bottom_gap <= 240:
                return self.screen_bottom - bottom_gap, bottom_gap
            if top_gap >= 16 and top_gap <= 240:
                self.panel_at_top = True
                return geo.y, top_gap
        except Exception:
            pass
        return self.screen_bottom - self.dock_h, self.dock_h

    # ---------- 布局：按最宽模板固定每栏宽度 ----------

    def _cell_w(self, ctx, label, value, size):
        """一栏内“标签 + 间隔 + 数值”的预留宽度（按最宽模板取值）"""
        return (self._measure(ctx, label, size)[0] + LABEL_GAP +
                self._measure(ctx, value, size)[0])

    def _measure_sp(self, ctx, text, size, spacing):
        """带字距的宽度"""
        l = self._layout(ctx, text, size)
        if spacing:
            attrs = Pango.AttrList()
            attrs.insert(Pango.attr_letter_spacing_new(spacing))
            l.set_attributes(attrs)
        return l.get_pixel_size()

    def _fit_date(self, ctx, max_w, size):
        """返回 (字号, 字距)：先用负字距压窄，仍放不下才缩字号。
           目标是不超过时间列宽，保证下行不长于上行、两行左沿对齐"""
        for sp in DATE_SPACING_STEPS:
            if self._measure_sp(ctx, T_DATE, size, sp)[0] <= max_w:
                return size, sp
        while size > 8.0:
            size -= 0.5
            if self._measure_sp(ctx, T_DATE, size, 0)[0] <= max_w:
                return size, 0
        return 8.0, 0

    def relayout(self):
        ctx = self.da.get_pango_context()
        s = self.font_scale
        self.show_cpu = bool(self.cfg.get('show_cpu', True))
        self.show_mem = bool(self.cfg.get('show_mem', True))
        self.show_net = bool(self.cfg.get('show_net', True))
        self.show_weather = bool(self.cfg.get('show_weather', True))

        # 先探测任务栏尺寸，后面一切以它为准
        self.dock_top, self.dock_h = self.detect_dock()

        # 若尚未采样过（例如刚启动），先用模板占位，避免缺属性
        for _attr, _tpl in (('_cpu_text', T_CPU_VALUE), ('_mem_text', T_MEM_VALUE),
                            ('_up_text', T_UP_VALUE), ('_down_text', T_DOWN_VALUE),
                            ('_weather_text', T_WEATHER)):
            if not getattr(self, _attr, None):
                setattr(self, _attr, _tpl)

        size_time = TIME_SIZE * s
        size_date = DATE_SIZE * s
        size_info = INFO_SIZE * s

        # 单行显示：全部栏目统一字号（单行高度富余，字号可以更大一些）
        size = TIME_SIZE * s
        self.size_line = size
        self.size_time = size
        self.size_date = size
        self.size_info = size
        self.date_spacing = 0
        h1 = h2 = 0

        # 文字(含上下留白)必须装进任务栏高度，装不下就整体缩小
        while size > 10.0:
            if self._measure(ctx, T_TIME, size)[1] + 12 <= self.dock_h:
                break
            size -= 0.5
        self.size_line = self.size_time = self.size_date = self.size_info = size

        # 栏宽策略：
        #   紧凑(compact): 按当前实际文本测宽 → 无空隙, 但数值位数变化时整行会伸缩
        #   固定(fixed)  : 按最宽模板测宽   → 位置纹丝不动, 但数值短时右侧会留空隙
        self.compact = bool(self.cfg.get('compact', True))
        _now = datetime.datetime.now()
        self._date_text = '%s %s' % (_now.strftime('%y/%m/%d'), WEEK[_now.weekday()])
        self.w_date = self._measure(ctx, self._date_text, size)[0]
        w_txt = self._weather_text if (self.compact and self._weather_text) else T_WEATHER
        self.w_weather = self._measure(ctx, w_txt, size)[0]
        self.w_time = self._char_pitch(ctx, size) * len(T_TIME)
        cpu_txt = self._cpu_text if self.compact else T_CPU_VALUE
        mem_txt = self._mem_text if self.compact else T_MEM_VALUE
        up_txt = self._up_text if self.compact else T_UP_VALUE
        down_txt = self._down_text if self.compact else T_DOWN_VALUE
        self.lab_cpu = self._measure(ctx, T_CPU_LABEL, size)[0]
        self.w_cpu = self.lab_cpu + LABEL_GAP + self._measure(ctx, cpu_txt, size)[0]
        self.lab_mem = self._measure(ctx, T_MEM_LABEL, size)[0]
        self.w_mem = self.lab_mem + LABEL_GAP + self._measure(ctx, mem_txt, size)[0]
        self.lab_up = self._measure(ctx, T_UP_LABEL, size)[0]
        self.w_up = self.lab_up + LABEL_GAP + self._measure(ctx, up_txt, size)[0]
        self.lab_down = self._measure(ctx, T_DOWN_LABEL, size)[0]
        self.w_down = self.lab_down + LABEL_GAP + self._measure(ctx, down_txt, size)[0]

        # 列顺序：年月日 星期 → 天气 → 时间 → CPU → 内存 → 上行 → 下行
        self.widths = [self.w_date]
        self.label_areas = [0.0]
        if self.show_weather:
            self.widths.append(self.w_weather)
            self.label_areas.append(0.0)
        self.widths.append(self.w_time)
        self.label_areas.append(0.0)
        if self.show_cpu:
            self.widths.append(self.w_cpu)
            self.label_areas.append(self.lab_cpu)
        if self.show_mem:
            self.widths.append(self.w_mem)
            self.label_areas.append(self.lab_mem)
        if self.show_net:
            self.widths.append(self.w_up)
            self.label_areas.append(self.lab_up)
            self.widths.append(self.w_down)
            self.label_areas.append(self.lab_down)

        self.cols = []
        x = PAD_LEFT
        for w in self.widths:
            self.cols.append(x)
            x += w + GAP
        self.win_w = int(x - GAP + PAD_RIGHT)

        self.win_h = int(self.dock_h)
        line_h = self._measure(ctx, T_TIME, size)[1]
        self.y_line = int((self.win_h - line_h) / 2)
        # 只有一个字号，按基线垂直居中
        self.base = self.y_line + self._ascent(ctx, size)
        self.y_row1 = self.y_row2 = self.y_line
        self.base1 = self.base2 = self.base

        if (self.win_w, self.win_h) != self._req_size:
            self._req_size = (self.win_w, self.win_h)
            self.win.set_size_request(self.win_w, self.win_h)
            self.da.set_size_request(self.win_w, self.win_h)
        # 与任务栏同高时正好占满任务栏
        if self.panel_at_top:
            y = self.dock_top
        else:
            y = self.dock_top + self.dock_h - self.win_h
        x = int(self.cfg.get('x_offset', 0) or 0)
        self.win.move(max(0, x), max(0, y))

    def _cur_size(self):
        return self._req_size or (0, 0)

    # ---------- 数据 ----------

    def build_line(self):
        """返回单行的单元格；顺序必须与 relayout 里的宽度数组一致"""
        cells = [(None, self._date_text, None, COLOR_DATE, self.size_date, False, 0)]
        if self.show_weather:
            cells.append((None, self._weather_text, None, COLOR_WEATHER,
                          self.size_info, False, 0))
        cells.append((None, datetime.datetime.now().strftime('%H:%M:%S'), None,
                      COLOR_TIME, self.size_time, True, 0))
        if self.show_cpu:
            cells.append((T_CPU_LABEL, self._cpu_text, COLOR_LABEL, COLOR_VALUE,
                          self.size_info, False, 0))
        if self.show_mem:
            cells.append((T_MEM_LABEL, self._mem_text, COLOR_LABEL, COLOR_VALUE,
                          self.size_info, False, 0))
        if self.show_net:
            cells.append((T_UP_LABEL, self._up_text, COLOR_UP, COLOR_UP,
                          self.size_info, False, 0))
            cells.append((T_DOWN_LABEL, self._down_text, COLOR_DOWN, COLOR_DOWN,
                          self.size_info, False, 0))
        return cells

    # ---------- 绘制 ----------

    def on_draw(self, wd, cr):
        cr.set_operator(cairo.OPERATOR_SOURCE)
        cr.set_source_rgba(0, 0, 0, 0)
        cr.paint()
        cr.set_operator(cairo.OPERATOR_OVER)
        ctx = wd.get_pango_context()
        cells = self.build_line()
        self.draw_grid(cr, ctx, cells, self.base)
        return False

    def draw_grid(self, cr, ctx, cells, base):
        """按固定列绘制：标签左对齐贴列左沿；同一行按基线对齐"""
        for i, cell in enumerate(cells):
            if i >= len(self.cols):
                break
            label, value, c_label, c_value, size, mono, spacing = cell
            x = self.cols[i]
            w = self.widths[i]
            y = base - self._ascent(ctx, size)
            if not label:
                cr.set_source_rgba(*c_value)
                if mono:
                    self._draw_mono(cr, ctx, value, x, y, size)
                else:
                    l = self._layout(ctx, value, size)
                    if spacing:
                        attrs = Pango.AttrList()
                        attrs.insert(Pango.attr_letter_spacing_new(spacing))
                        l.set_attributes(attrs)
                    cr.move_to(x, y)
                    PangoCairo.show_layout(cr, l)
            else:
                l = self._layout(ctx, label, size)
                cr.set_source_rgba(*c_label)
                cr.move_to(x, y)
                PangoCairo.show_layout(cr, l)
                lv = self._layout(ctx, value, size)
                cr.set_source_rgba(*c_value)
                # 数值起点固定为该列的“标签区右沿 + 间隔”，所以上下两行数值对齐，
                # 且数值长度变化不会带动标签与列位置
                cr.move_to(x + self.label_areas[i] + LABEL_GAP, y)
                PangoCairo.show_layout(cr, lv)
        return True

    # ---------- 刷新 ----------

    def _sample(self):
        """每秒采集一次数值文本：布局与绘制共用同一份, 避免重复采样"""
        self.show_cpu = bool(self.cfg.get('show_cpu', True))
        self.show_mem = bool(self.cfg.get('show_mem', True))
        self.show_net = bool(self.cfg.get('show_net', True))
        self.show_weather = bool(self.cfg.get('show_weather', True))
        u = self.cpu.usage() if self.show_cpu else None
        self._cpu_text = '--' if u is None else '%d%%' % int(round(u))
        m = mem_usage() if self.show_mem else None
        self._mem_text = '--' if m is None else '%d%%' % int(round(m))
        if self.show_net:
            up, down = self.net.rates()
        else:
            up = down = 0.0
        self._up_text = fmt_speed(up)
        self._down_text = fmt_speed(down)
        if self.show_weather:
            self.weather.maybe_refresh()      # 15 分钟到期才真正联网
            name = self.cfg.get('weather_display', '北京市')
            self._weather_text = self.weather.get_text() or '%s --' % name
        else:
            self._weather_text = ''

    def _watch_source(self):
        path = os.path.abspath(__file__)
        try:
            mtime = os.path.getmtime(path)
        except Exception:
            return
        if getattr(self, '_src_mtime', None) is None:
            self._src_mtime = mtime
        elif mtime != self._src_mtime:
            os.execv(sys.executable, [sys.executable, path])

    def tick(self):
        self._watch_source()
        self._sample()
        self.relayout()
        self.da.queue_draw()
        if self.panel is not None:
            self.panel.refresh()
        return True

    # ---------- 交互 ----------

    def on_press(self, widget, event):
        if event.button == 1:
            self.open_panel()
            return True
        if event.button == 3:
            self.show_menu(event)
            return True
        return False

    def open_panel(self):
        if self.panel is None:
            self.panel = InfoPanel(self, self.disk_sampler)
        self.panel.open()

    def show_menu(self, event):
        menu = Gtk.Menu()

        def add(label, cb):
            item = Gtk.MenuItem(label=label)
            item.connect('activate', cb)
            menu.append(item)

        add('增大字号', lambda w: self.change_scale(0.1))
        add('减小字号', lambda w: self.change_scale(-0.1))
        add('还原默认字号', lambda w: self.set_scale(1.0))
        menu.append(Gtk.SeparatorMenuItem())

        item = Gtk.CheckMenuItem(label='紧凑排版（数值变化时整行会伸缩）')
        item.set_active(bool(self.cfg.get('compact', True)))
        item.connect('toggled', self.on_toggle_item, 'compact')
        menu.append(item)
        menu.append(Gtk.SeparatorMenuItem())

        for key, label in (('show_weather', '显示 天气'),
                           ('show_cpu', '显示 CPU 占用'),
                           ('show_mem', '显示 内存占用'),
                           ('show_net', '显示 网速')):
            item = Gtk.CheckMenuItem(label=label)
            item.set_active(bool(self.cfg.get(key, True)))
            item.connect('toggled', self.on_toggle_item, key)
            menu.append(item)

        menu.append(Gtk.SeparatorMenuItem())
        add('设置天气城市...', lambda w: self.on_set_weather_city())
        menu.append(Gtk.SeparatorMenuItem())
        add('向左微调 20 像素', lambda w: self.nudge(-20))
        add('向右微调 20 像素', lambda w: self.nudge(20))
        menu.append(Gtk.SeparatorMenuItem())
        add('退出', lambda w: self.on_quit())

        menu.show_all()
        menu.popup(None, None, None, None, event.button, event.time)

    def on_toggle_item(self, item, key):
        self.cfg[key] = item.get_active()
        save_config(self.cfg)
        self.relayout()

    def on_set_weather_city(self):
        """天气城市搜索对话框：输入城市/乡村名 -> 候选列表 -> 存 名称+坐标"""
        dlg = Gtk.Dialog(title='设置天气城市', parent=self, flags=Gtk.DialogFlags.MODAL)
        dlg.add_buttons('取消', Gtk.ResponseType.CANCEL, '保存', Gtk.ResponseType.OK)
        dlg.set_default_size(420, 460)
        box = dlg.get_content_area()
        box.set_spacing(6)
        box.set_margin_start(12); box.set_margin_end(12); box.set_margin_top(12); box.set_margin_bottom(12)

        cur_name = str(self.cfg.get('weather_display', '北京市'))
        cur_loc = str(self.cfg.get('weather_location', '北京市'))
        pending = {}

        ent = Gtk.Entry(text=cur_name)
        btn = Gtk.Button(label='搜索')
        row = Gtk.Box(spacing=8)
        row.pack_start(Gtk.Label(label='城市/乡村'), False, False, 0)
        row.pack_start(ent, True, True, 0)
        row.pack_start(btn, False, False, 0)
        box.pack_start(row, False, False, 0)

        store = Gtk.ListStore(str, str, float, float)
        tree = Gtk.TreeView(model=store)
        tree.set_headers_visible(True)
        tree.append_column(Gtk.TreeViewColumn('地名', Gtk.CellRendererText(), text=0))
        tree.append_column(Gtk.TreeViewColumn('所属地区', Gtk.CellRendererText(), text=1))
        sel = tree.get_selection()
        sel.set_mode(Gtk.SelectionMode.SINGLE)

        def on_selected(s):
            model, it = s.get_selected()
            if it:
                pending.clear()
                pending.update({'name': model[it][0], 'lat': model[it][2], 'lon': model[it][3]})
                ent.set_text(model[it][0])
        sel.connect('changed', on_selected)

        scroll = Gtk.ScrolledWindow()
        scroll.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
        scroll.add(tree)
        scroll.set_size_request(-1, 150)
        box.pack_start(scroll, True, True, 0)

        hint = Gtk.Label(label='支持城市与乡村（如：北京市 / 成都市 / 某某镇），选中后保存')
        hint.set_line_wrap(True)
        box.pack_start(hint, False, False, 0)

        def do_search(*_a):
            store.clear()
            pending.clear()
            q = ent.get_text().strip()
            if not q:
                return
            btn.set_sensitive(False)
            while Gtk.events_pending():
                Gtk.main_iteration()
            try:
                results = []
                for name in (q, self._strip_suffix(q)):   # 原样 -> 剥离行政后缀
                    url = ('https://geocoding-api.open-meteo.com/v1/search?name=%s&count=10&language=zh'
                           % urllib.parse.quote(name))
                    data = self._json(url) or {}
                    results = data.get('results') or []
                    if results:
                        break
                for r in results:
                    area = ' '.join(x for x in (r.get('admin1'), r.get('admin2'), r.get('admin3')) if x)
                    store.append((r.get('name') or '', area,
                                  r.get('latitude') or 0.0, r.get('longitude') or 0.0))
            finally:
                btn.set_sensitive(True)
        btn.connect('clicked', do_search)
        ent.connect('activate', do_search)
        # 初始：当前坐标放入列表并选中（已是坐标格式时）
        if ',' in cur_loc:
            try:
                a, b = cur_loc.split(',', 1)
                store.append((cur_name, '当前设置', float(a), float(b)))
                sel.select_iter(store.get_iter_first())
            except Exception:
                pass

        box.show_all()
        if dlg.run() == Gtk.ResponseType.OK:
            new_name = ent.get_text().strip() or '北京市'
            self.cfg['weather_display'] = new_name
            if pending:
                self.cfg['weather_location'] = '%s,%s' % (pending['lat'], pending['lon'])
            elif new_name != cur_name:
                self.cfg['weather_location'] = new_name
            save_config(self.cfg)
            self.weather.maybe_refresh(force=True)   # 立即用新城市刷新
            self._weather_text = '%s 刷新中...' % new_name
            self.da.queue_draw()
        dlg.destroy()

    def change_scale(self, delta):
        self.set_scale(self.font_scale + delta)

    def nudge(self, delta):
        """在任务栏上左右微调显示位置"""
        cur = int(self.cfg.get('x_offset', 0) or 0)
        self.cfg['x_offset'] = max(0, min(cur + delta, max(0, self.screen_w - 100)))
        save_config(self.cfg)
        self.relayout()

    def set_scale(self, scale):
        self.font_scale = max(0.6, min(2.0, scale))
        self.cfg['font_scale'] = self.font_scale
        save_config(self.cfg)
        self.relayout()

    def on_quit(self, *args):
        if self.panel is not None:
            self.panel.close()
        save_config(self.cfg)
        Gtk.main_quit()
        return False


def main():
    lock = single_instance()
    if lock is None:
        sys.exit(0)
    TimeBar()
    Gtk.main()


if __name__ == '__main__':
    main()
