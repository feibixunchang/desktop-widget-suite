#!/usr/bin/env python3
from widget_font import SYS_FONT as _SYS_FONT
# -*- coding: utf-8 -*-
"""桌面时间天气挂件 - 透明背景/可拖动/自定义颜色大小字体"""
import datetime
import json
import os
import re
import shutil
import subprocess
import sys
import threading
import time
import urllib.parse
import urllib.request

import gi
gi.require_version('Gtk', '3.0')
gi.require_version('Pango', '1.0')
from gi.repository import Gtk, Gdk, Pango, GLib
import cairo

APP_DIR = os.path.dirname(os.path.abspath(__file__))
USER_DIR = os.path.expanduser('~/.local/share/desktop-widget/')
CONFIG_PATH = os.path.join(USER_DIR, 'config.json')
DEFAULT_FONT = _SYS_FONT + ''
BASE_FONT_SIZE = 34


def log(msg):
    os.makedirs(USER_DIR, exist_ok=True)
    with open(os.path.join(USER_DIR, 'widget.log'), 'a') as f:
        f.write(time.strftime('%Y-%m-%d %H:%M:%S') + ' ' + str(msg) + '\n')


def load_config():
    cfg = {
        'x': None, 'y': None,
        'font_size': 34,
        'color': '#FFEB3B',
        'time_color': '#4A90D9',
        'weather_color': '#87CEEB',
        'forecast_color': '#87CEEB',
        'city': '北京市',
        'lat': None,
        'lon': None,
        'lock_position': False,
        'show_forecast': False,
    }
    if not os.path.exists(CONFIG_PATH):
        src = os.path.join(APP_DIR, 'config.json')
        if os.path.exists(src):
            try:
                os.makedirs(USER_DIR, exist_ok=True)
                shutil.copyfile(src, CONFIG_PATH)
            except Exception:
                pass
    try:
        with open(CONFIG_PATH) as f:
            cfg.update(json.load(f))
    except Exception as e:
        log('config load: ' + str(e))
    return cfg


class WeatherFetcher:
    """多数据源天气获取: 和风天气(需key) -> Open-Meteo(公共) -> wttr.in(公共), 自动切换"""

    WMO = {
        0: '晴', 1: '多云', 2: '多云', 3: '阴', 45: '雾', 48: '雾凇',
        51: '毛毛雨', 53: '毛毛雨', 55: '毛毛雨', 56: '冻雨', 57: '冻雨',
        61: '小雨', 63: '中雨', 65: '大雨', 66: '冻雨', 67: '冻雨',
        71: '小雪', 73: '中雪', 75: '大雪', 77: '雪粒',
        80: '阵雨', 81: '阵雨', 82: '强阵雨', 85: '阵雪', 86: '阵雪',
        95: '雷阵雨', 96: '雷阵雨', 99: '雷阵雨',
    }
    DIRS = ['北', '东北偏北', '东北', '东北偏东', '东', '东南偏东', '东南', '东南偏南',
            '南', '西南偏南', '西南', '西南偏西', '西', '西北偏西', '西北', '西北偏北']
    GEO_URL = 'https://geocoding-api.open-meteo.com/v1/search'

    def __init__(self, cfg):
        self.cfg = cfg
        self.cache = None
        self.forecast = None
        self.lock = threading.Lock()
        self.last_success = 0.0
        self.last_attempt = 0.0
        self._geo = None
        self._source = ''

    # ─── 通用工具 ───
    @staticmethod
    def _http_get(url, timeout=10):
        req = urllib.request.Request(url, headers={
            'User-Agent': 'Mozilla/5.0 (X11; Linux x86_64) desktop-weather/1.0',
            'Accept-Encoding': 'gzip'})
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw = r.read()
        if r.headers.get('Content-Encoding', '').lower() == 'gzip':
            import gzip
            raw = gzip.decompress(raw)
        return json.loads(raw.decode())

    @staticmethod
    def _deg_dir(deg):
        try:
            deg = float(deg)
            return WeatherFetcher.DIRS[int((deg % 360) / 22.5) % 16]
        except Exception:
            return ''

    @staticmethod
    def _kmh_scale(kmh):
        """风速 km/h -> 蒲福风级"""
        try:
            kmh = float(kmh)
        except Exception:
            return ''
        if kmh < 1: return '0'
        if kmh < 6: return '1'
        if kmh < 12: return '2'
        if kmh < 20: return '3'
        if kmh < 29: return '4'
        if kmh < 39: return '5'
        if kmh < 50: return '6'
        if kmh < 62: return '7'
        if kmh < 75: return '8'
        if kmh < 89: return '9'
        if kmh < 103: return '10'
        if kmh < 117: return '11'
        return '12'

    @staticmethod
    def _hhmm(s):
        """把各方来源的时间字符串规整成 HH:MM"""
        if not s:
            return ''
        s = str(s).strip()
        if 'T' in s:                                  # Open-Meteo: 2026-09-26T07:02
            return s.split('T')[-1][:5]
        for fmt in ('%I:%M %p', '%I:%M%p', '%H:%M'):  # wttr.in: 06:03 AM
            try:
                return time.strftime('%H:%M', time.strptime(s, fmt))
            except Exception:
                continue
        return s[:5]

    @staticmethod
    def _strip_suffix(name):
        """GeoNames 对「北京市」这类带行政后缀的词条常返回空，剥离后再试"""
        for suf in ('特别行政区', '自治区', '地区', '盟', '自治州', '市', '县', '区'):
            if name.endswith(suf) and len(name) > len(suf):
                return name[:-len(suf)]
        return name

    def search_cities(self, query, count=10):
        """城市/乡村名 -> 候选列表 [{name, area, lat, lon}]

        Open-Meteo geocoding 基于 GeoNames，覆盖到中国村级地名。
        """
        q = (query or '').strip()
        if not q:
            return []
        results = None
        for name in (q, self._strip_suffix(q)):     # 原样 -> 剥离行政后缀
            try:
                data = self._http_get('%s?name=%s&count=%d&language=zh' %
                                      (self.GEO_URL, urllib.parse.quote(name), count))
                results = data.get('results') or []
            except Exception as e:
                log('city search: ' + str(e))
                return []
            if results:
                break
        out = []
        for r in results or []:
            area = ' '.join(x for x in (r.get('admin1'), r.get('admin2'),
                                        r.get('admin3')) if x)
            out.append({'name': r.get('name') or '',
                        'area': area,
                        'lat': r.get('latitude'),
                        'lon': r.get('longitude')})
        return out

    # ─── 数据源1: Open-Meteo (公共免费, 无需key, 支持城市/乡村) ───
    def _geo_lookup(self):
        """城市名 -> 经纬度 (Open-Meteo geocoding, 结果写回配置缓存)"""
        if self._geo:
            return self._geo
        lat = self.cfg.get('lat')
        lon = self.cfg.get('lon')
        if lat is not None and lon is not None:
            self._geo = (float(lat), float(lon))
            return self._geo
        city = self.cfg.get('city', '北京市')
        for suf in ('市', '地区', '盟', '州', '县', '区'):
            if city.endswith(suf):
                city = city[:-len(suf)]
                break
        try:
            data = self._http_get('%s?name=%s&count=1&language=zh' %
                                  (self.GEO_URL, urllib.parse.quote(city)))
            if data.get('results'):
                r = data['results'][0]
                self._geo = (float(r['latitude']), float(r['longitude']))
                try:
                    with open(CONFIG_PATH) as f:
                        c = json.load(f)
                    c['lat'] = self._geo[0]
                    c['lon'] = self._geo[1]
                    with open(CONFIG_PATH, 'w') as f:
                        json.dump(c, f, ensure_ascii=False, indent=2)
                except Exception:
                    pass
                log('geo: %s -> %s,%s' % (city, self._geo[0], self._geo[1]))
                return self._geo
        except Exception as e:
            log('geo lookup: ' + str(e))
        return None

    def _fetch_openmeteo(self):
        """一次请求同时取实时 + 7天预报"""
        geo = self._geo_lookup()
        if not geo:
            return None
        lat, lon = geo
        url = ('https://api.open-meteo.com/v1/forecast?latitude=%s&longitude=%s'
               '&current=temperature_2m,relative_humidity_2m,apparent_temperature,'
               'weather_code,wind_speed_10m,wind_direction_10m'
               '&daily=weather_code,temperature_2m_max,temperature_2m_min,'
               'precipitation_probability_max,sunrise,sunset'
               '&forecast_days=7&timezone=auto' % (lat, lon))
        data = self._http_get(url)
        cur = data.get('current', {})
        if not cur:
            return None
        daily = data.get('daily', {})
        wmo = cur.get('weather_code')
        now = {'text': self.WMO.get(wmo, '未知'),
               'temp': cur.get('temperature_2m'),
               'feels': cur.get('apparent_temperature'),
               'wind_dir': self._deg_dir(cur.get('wind_direction_10m')),
               'wind_scale': self._kmh_scale(cur.get('wind_speed_10m')),
               'humidity': cur.get('relative_humidity_2m'),
               'pop': (daily.get('precipitation_probability_max') or [None])[0],
               'sunrise': self._hhmm((daily.get('sunrise') or [''])[0]),
               'sunset': self._hhmm((daily.get('sunset') or [''])[0]),
               'city': self.cfg.get('city', '')}
        times = daily.get('time', [])
        days = []
        for i, t in enumerate(times[:7]):
            days.append({'date': t,
                         'text': self.WMO.get(daily['weather_code'][i], '未知'),
                         'tmax': daily['temperature_2m_max'][i],
                         'tmin': daily['temperature_2m_min'][i]})
        if days:
            self.forecast = days
        return now

    # ─── 数据源2: wttr.in (公共, 无需key) ───
    EN_DIR = {'N': '北', 'NNE': '东北偏北', 'NE': '东北', 'ENE': '东北偏东',
              'E': '东', 'ESE': '东南偏东', 'SE': '东南', 'SSE': '东南偏南',
              'S': '南', 'SSW': '西南偏南', 'SW': '西南', 'WSW': '西南偏西',
              'W': '西', 'WNW': '西北偏西', 'NW': '西北', 'NNW': '西北偏北'}
    EN_WEATHER = {'Sunny': '晴', 'Clear': '晴', 'Partly cloudy': '多云',
                  'Cloudy': '阴', 'Overcast': '阴', 'Mist': '雾', 'Fog': '雾',
                  'Light drizzle': '毛毛雨', 'Drizzle': '毛毛雨',
                  'Patchy light drizzle': '零星小雨',
                  'Light rain': '小雨', 'Patchy rain possible': '小雨',
                  'Patchy rain nearby': '局部小雨',
                  'Moderate rain': '中雨', 'Heavy rain': '大雨',
                  'Light rain shower': '阵雨', 'Patchy light rain': '小雨',
                  'Moderate rain at times': '中雨', 'Heavy rain at times': '大雨',
                  'Moderate or heavy rain shower': '阵雨',
                  'Torrential rain shower': '强阵雨',
                  'Light snow': '小雪', 'Moderate snow': '中雪', 'Heavy snow': '大雪',
                  'Blizzard': '暴雪', 'Blowing snow': '大风雪',
                  'Thundery outbreaks possible': '雷阵雨',
                  'Patchy light snow': '小雪', 'Freezing fog': '雾凇',
                  'Light sleet': '冻雨', 'Moderate or heavy sleet': '冻雨',
                  'Light freezing rain': '冻雨', 'Moderate or heavy freezing rain': '冻雨',
                  'Moderate or heavy rain with thunder': '雷阵雨',
                  'Light rain with thunder': '雷阵雨',
                  'Patchy light rain with thunder': '雷阵雨',
                  'Patchy light snow with thunder': '雷阵雪',
                  'Moderate or heavy snow with thunder': '雷阵雪',
                  'Light snow showers': '阵雪',
                  'Moderate or heavy snow showers': '阵雪',
                  'Patchy light snow with thunder': '雷阵雪'}

    @staticmethod
    def _en_dir_zh(abbr):
        return WeatherFetcher.EN_DIR.get(abbr, abbr)

    @staticmethod
    def _en_weather_zh(desc):
        key = (desc or '').strip().lower()
        table = {k.lower(): v for k, v in WeatherFetcher.EN_WEATHER.items()}
        return table.get(key, desc)

    @staticmethod
    def _wttr_astro(data, key):
        """从 wttr.in 的 weather[0].astronomy[0] 取日出/日落"""
        try:
            return (data['weather'][0]['astronomy'][0].get(key) or '')
        except Exception:
            return ''

    @staticmethod
    def _wttr_pop(wdays):
        """从 wttr.in 当天 hourly[0].chanceofrain 取降水概率"""
        try:
            return (wdays[0].get('hourly') or [{}])[0].get('chanceofrain')
        except Exception:
            return None

    def _fetch_wttr(self):
        city = urllib.parse.quote(self.cfg.get('city', '北京市'))
        url = 'https://wttr.in/%s?format=j1&lang=zh' % city
        data = self._http_get(url)
        cc = data.get('current_condition', [{}])[0]
        desc = ''
        for k in ('lang_zh', 'weatherDesc'):
            arr = cc.get(k) or []
            if arr and arr[0].get('value'):
                desc = arr[0]['value']
                break
        now = {'text': WeatherFetcher._en_weather_zh(desc),
               'temp': cc.get('temp_C'),
               'feels': cc.get('FeelsLikeC'),
               'wind_dir': WeatherFetcher._en_dir_zh(cc.get('winddir16Point', '')),
               'wind_scale': self._kmh_scale(cc.get('windspeedKmph')),
               'humidity': cc.get('humidity'),
               'pop': (self._wttr_pop(data.get('weather') or [{}])),
               'sunrise': self._hhmm(self._wttr_astro(data, 'sunrise')),
               'sunset': self._hhmm(self._wttr_astro(data, 'sunset')),
               'city': self.cfg.get('city', '')}
        days = []
        for d in data.get('weather', [])[:7]:
            desc_d = ''
            h = d.get('hourly') or [{}]
            for k in ('lang_zh', 'weatherDesc'):
                arr = h[0].get(k) or []
                if arr and arr[0].get('value'):
                    desc_d = arr[0]['value']
                    break
            days.append({'date': d.get('date', ''),
                         'text': WeatherFetcher._en_weather_zh(desc_d),
                         'tmax': d.get('maxtempC', ''),
                         'tmin': d.get('mintempC', '')})
        if days:
            self.forecast = days
        return now

    # ─── 主流程: Open-Meteo 优先, wttr.in 兜底 ───
    def fetch(self):
        self.last_attempt = time.time()
        for name, fn in (('Open-Meteo', self._fetch_openmeteo),
                         ('wttr.in', self._fetch_wttr)):
            try:
                now = fn()
            except Exception as e:
                log('weather %s: %s' % (name, e))
                now = None
            if now:
                self._source = name
                self.cache = dict(now)
                self.cache['time'] = time.strftime('%H:%M')
                self.last_success = time.time()
                log('weather ok [%s]: %s' % (name, self.get_text()))
                return self.cache
        log('weather: all sources failed')
        return None

    def fetch_forecast(self):
        for name, fn in (('Open-Meteo', self._fetch_openmeteo),
                         ('wttr.in', self._fetch_wttr)):
            try:
                self.forecast = None
                fn()
            except Exception as e:
                log('forecast %s: %s' % (name, e))
            if self.forecast:
                log('forecast ok [%s]: %s' % (name, self.get_forecast_text()))
                return

    def get_forecast_text(self):
        with self.lock:
            f = self.forecast
        if not f:
            return '一周天气获取中...'
        week_map = '一二三四五六日'
        parts = []
        for d in f:
            try:
                wd = week_map[datetime.datetime.strptime(d['date'], '%Y-%m-%d').weekday()]
            except Exception:
                wd = '?'
            parts.append('周%s %s %s°/%s°' % (wd, d['text'], d['tmax'], d['tmin']))
        return '  '.join(parts)

    def get_text(self):
        with self.lock:
            c = self.cache
        if c and time.time() - self.last_success < 21600:
            parts = [c['city'], c['text'], str(c['temp']) + '℃']
            wd = c.get('wind_dir') or ''
            ws = c.get('wind_scale')
            if wd or (ws not in (None, '')):
                if wd and not wd.endswith('风'):
                    wd += '风'
                parts.append('%s%s级' % (wd, '' if ws is None else ws))
            if c.get('humidity'):
                parts.append('湿度' + str(c['humidity']) + '%')
            pop = c.get('pop')
            if pop not in (None, '', 'None'):
                parts.append('降水概率' + str(pop) + '%')
            if c.get('sunrise') and c.get('sunset'):
                parts.append('日出%s 日落%s' % (c['sunrise'], c['sunset']))
            if c.get('time'):
                parts.append('刷新' + str(c['time']))
            return ' '.join(parts)
        return '天气获取中...'


class WidgetWindow(Gtk.Window):
    def __init__(self):
        super().__init__(type=Gtk.WindowType.TOPLEVEL)
        visual = self.get_screen().get_rgba_visual()
        if visual is not None:
            self.set_visual(visual)
        self.cfg = load_config()
        self.set_keep_below(True)
        self.set_type_hint(Gdk.WindowTypeHint.UTILITY)
        self.set_skip_taskbar_hint(True)
        self.set_skip_pager_hint(True)
        self.set_app_paintable(True)
        self.set_decorated(False)
        self.set_accept_focus(False)
        self.set_can_focus(False)

        self.weather = WeatherFetcher(self.cfg)

        self.box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
        self.row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
        # 顺序: 日期 | 周三 | 时间 | 农历, 四块紧挨在一起, 间距统一为 10px
        self.row.set_halign(Gtk.Align.CENTER)
        self.row.set_hexpand(False)
        self.time_label = Gtk.Label()
        self.date_label = Gtk.Label()
        self.week_label = Gtk.Label()
        self.lunar_label = Gtk.Label()
        self.weather_label = Gtk.Label()
        self.forecast_label = Gtk.Label()
        self.add(self.box)

        self.build_ui()
        self.apply_style()

        self.connect('draw', self.on_draw)
        self.connect('button-press-event', self.on_button_press)
        self.connect('motion-notify-event', self.on_motion)
        self.connect('button-release-event', self.on_button_release)
        self.connect('scroll-event', self.on_scroll)

        self.dragging = False
        # 窗口按屏幕宽度摆放, 内容在窗口内居中, 因此永不左右移动
        self.user_moved = False
        self.drag_start = None
        self.drag_orig = None
        self._weather_thread = None

        GLib.timeout_add_seconds(1, self.tick)
        GLib.timeout_add_seconds(60, self.weather_tick)
        self.update_time()
        self.schedule_weather(0.5)

        self.show_all()
        self.apply_style()   # show_all 会把"一周天气"行强制显示, 这里恢复其可见性
        self.move(0, max(0, int(self.cfg.get('y') or 0)))
        GLib.idle_add(self._resize_to_screen)
        GLib.timeout_add(800, self._all_desktops)
        GLib.timeout_add(300, self._shape_tick)

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

    def build_ui(self):
        self.time_label.set_line_wrap(False)
        self.date_label.set_line_wrap(False)
        self.week_label.set_line_wrap(False)
        self.lunar_label.set_line_wrap(False)
        self.weather_label.set_line_wrap(False)
        self.forecast_label.set_line_wrap(False)
        self.time_label.set_halign(Gtk.Align.CENTER)
        self.date_label.set_halign(Gtk.Align.CENTER)
        self.week_label.set_halign(Gtk.Align.CENTER)
        self.lunar_label.set_halign(Gtk.Align.CENTER)
        self.weather_label.set_halign(Gtk.Align.CENTER)
        self.forecast_label.set_halign(Gtk.Align.CENTER)
        self.time_label.set_valign(Gtk.Align.CENTER)
        self.date_label.set_valign(Gtk.Align.CENTER)
        self.week_label.set_valign(Gtk.Align.CENTER)
        self.lunar_label.set_valign(Gtk.Align.CENTER)
        self.weather_label.set_valign(Gtk.Align.CENTER)
        self.forecast_label.set_valign(Gtk.Align.CENTER)
        self.row.set_halign(Gtk.Align.CENTER)
        self.row.pack_start(self.date_label, False, False, 0)
        self.row.pack_start(self.week_label, False, False, 0)
        self.row.pack_start(self.time_label, False, False, 0)
        self.row.pack_start(self.lunar_label, False, False, 0)
        self.box.pack_start(self.row, False, False, 0)
        self.box.pack_start(self.weather_label, False, False, 0)
        self.box.pack_start(self.forecast_label, False, False, 0)

    def apply_style(self):
        font_name = self.cfg.get('font', DEFAULT_FONT)
        size = int(self.cfg.get('font_size', BASE_FONT_SIZE))
        color = self.cfg.get('color', '#FFEB3B')
        weather_color = self.cfg.get('weather_color', '#87CEEB')
        try:
            rgba = Gdk.RGBA()
            rgba.parse(color)
        except Exception:
            rgba = Gdk.RGBA()
            rgba.parse('#FFEB3B')
        try:
            w_rgba = Gdk.RGBA()
            w_rgba.parse(weather_color)
        except Exception:
            w_rgba = Gdk.RGBA()
            w_rgba.parse('#87CEEB')
        try:
            f_rgba = Gdk.RGBA()
            f_rgba.parse(self.cfg.get('forecast_color', '#87CEEB'))
        except Exception:
            f_rgba = Gdk.RGBA()
            f_rgba.parse('#87CEEB')
        for lbl in (self.time_label, self.date_label, self.week_label, self.lunar_label):
            lbl.modify_font(Pango.FontDescription('%s %d' % (font_name, size)))
        # 具体时间(时:分:秒)用蓝色, 日期/星期用配置色
        try:
            t_rgba = Gdk.RGBA()
            t_rgba.parse(self.cfg.get('time_color', '#4A90D9'))
        except Exception:
            t_rgba = Gdk.RGBA()
            t_rgba.parse('#4A90D9')
        t_gcolor = Gdk.Color(red=int(t_rgba.red * 65535),
                             green=int(t_rgba.green * 65535),
                             blue=int(t_rgba.blue * 65535))
        self.time_label.modify_fg(Gtk.StateType.NORMAL, t_gcolor)
        for lbl in (self.date_label, self.week_label, self.lunar_label):
            gcolor = Gdk.Color(red=int(rgba.red * 65535), green=int(rgba.green * 65535), blue=int(rgba.blue * 65535))
            lbl.modify_fg(Gtk.StateType.NORMAL, gcolor)
        weather_size = max(12, size - 10)
        self.weather_label.modify_font(Pango.FontDescription('%s %d' % (font_name, weather_size)))
        w_gcolor = Gdk.Color(red=int(w_rgba.red * 65535), green=int(w_rgba.green * 65535), blue=int(w_rgba.blue * 65535))
        self.weather_label.modify_fg(Gtk.StateType.NORMAL, w_gcolor)
        forecast_size = 12
        self.forecast_label.modify_font(Pango.FontDescription('%s %d' % (font_name, forecast_size)))
        f_gcolor = Gdk.Color(red=int(f_rgba.red * 65535), green=int(f_rgba.green * 65535), blue=int(f_rgba.blue * 65535))
        self.forecast_label.modify_fg(Gtk.StateType.NORMAL, f_gcolor)
        self.forecast_label.set_visible(bool(self.cfg.get('show_forecast', False)))
        self._size = size
        self._weather_size = weather_size
        self._forecast_size = forecast_size
        # 窗口一律按屏幕宽度摆放, 内容在窗口内自然居中,
        # 每行的正中心因此永远与屏幕垂直中线重合, 且窗口宽度不变, 不会左右晃动
        GLib.idle_add(self._resize_to_screen)

    def _resize_to_screen(self):
        """把窗口拉宽到整个屏幕。
        内容是整块居中的, 于是每一行(日期|时间、天气、一周天气)的正中心
        都落在屏幕垂直中线上; 窗口宽度恒定, 也就不存在左右来回调整的问题。"""
        try:
            sw = self.get_screen().get_width()
            if sw <= 0:
                return
            cur_w, cur_h = self.get_size()
            if cur_w != sw or cur_h <= 0:
                self.resize(sw, max(cur_h, 1))
            self.set_size_request(sw, -1)   # 宽度下限=屏幕宽, 内容再宽窗口也不会反复伸缩
        except Exception:
            pass

    def _shape_tick(self):
        """窗口铺满全屏后, 只让文字所在的区域响应点击, 其余位置点击直接穿透到桌面"""
        try:
            self._update_input_shape()
        except Exception:
            pass
        return True

    def _update_input_shape(self):
        """把鼠标输入区域限制在所有文字行的外接矩形内,
        避免窗口铺满全屏后挡住桌面上的点击。"""
        try:
            minx = miny = 1 << 30
            maxx = maxy = -1
            for lbl in (self.date_label, self.week_label, self.time_label,
                        self.lunar_label, self.weather_label, self.forecast_label):
                if not lbl.get_visible():
                    continue
                a = lbl.get_allocation()
                if a.width <= 0 or a.height <= 0:
                    continue
                x, y = a.x, a.y        # 相对窗口左上角, 无需转换到根坐标
                minx, miny = min(minx, x), min(miny, y)
                maxx = max(maxx, x + a.width)
                maxy = max(maxy, y + a.height)
            if maxx < 0:
                return
            reg = cairo.Region(cairo.RectangleInt(minx, miny, maxx - minx, maxy - miny))
            self.input_shape_combine_region(reg)
        except Exception:
            pass

    def on_draw(self, widget, cr):
        cr.set_operator(cairo.OPERATOR_SOURCE)
        cr.set_source_rgba(0, 0, 0, 0)
        cr.paint()
        return False

    def tick(self):
        self.update_time()
        return True

    def update_time(self):
        now = time.localtime()
        time_str = time.strftime('%H:%M:%S', now)
        date_str = time.strftime('%Y年%m月%d日', now)
        week_map = '一二三四五六日'
        week_str = '周' + week_map[now.tm_wday]
        # 农历单独一块显示: 丙午马年八月十三
        lunar_str = ''
        try:
            sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
            from lunar_python import Solar
            l = Solar.fromYmd(now.tm_year, now.tm_mon, now.tm_mday).getLunar()
            lunar_str = '%s%s年%s月%s' % (l.getYearInGanZhi(), l.getYearShengXiao(),
                                          l.getMonthInChinese(), l.getDayInChinese())
        except Exception:
            pass
        self.time_label.set_text(time_str)
        self.date_label.set_text(date_str)
        self.week_label.set_text(week_str)
        self.lunar_label.set_text(lunar_str)
        self.weather_label.set_text(self.weather.get_text())
        self.weather_label.modify_font(Pango.FontDescription('%s %d' % (self.cfg.get('font', DEFAULT_FONT), self._weather_size)))
        if self.cfg.get('show_forecast', False):
            self.forecast_label.set_text(self.weather.get_forecast_text())
            self.forecast_label.modify_font(Pango.FontDescription('%s %d' % (self.cfg.get('font', DEFAULT_FONT), self._forecast_size)))

    def weather_tick(self):
        w = self.weather
        if (time.time() - w.last_success >= 1800
                and time.time() - w.last_attempt > 60):
            self.schedule_weather(0)
        return True

    def schedule_weather(self, delay):
        def run():
            self.weather.fetch()
            if self.cfg.get('show_forecast', False):
                self.weather.fetch_forecast()
            GLib.idle_add(self.update_time)
        t = threading.Timer(delay, run)
        t.daemon = True
        t.start()

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
            dx = event.x_root - self.drag_start[0]
            dy = event.y_root - self.drag_start[1]
            # 窗口铺满全屏, x 恒为 0, 只允许上下移动
            self.move(0, max(0, self.drag_orig[1] + dy))
            return True
        return False

    def on_button_release(self, widget, event):
        if event.button == 1:
            self.dragging = False
            x, y = self.get_position()
            self.cfg['x'], self.cfg['y'] = x, y
            self.save_config()
        return False

    def on_scroll(self, widget, event):
        self.resize_by(1 if event.direction == Gdk.ScrollDirection.UP else -1)
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

    def show_menu(self, event):
        menu = Gtk.Menu()
        def add(label, cb):
            item = Gtk.MenuItem(label=label)
            item.connect('activate', cb)
            menu.append(item)
        add('增大字号', lambda w: self.resize_by(2))
        add('减小字号', lambda w: self.resize_by(-2))
        lock_item = Gtk.CheckMenuItem(label='锁定位置')
        lock_item.set_active(bool(self.cfg.get('lock_position', False)))
        lock_item.connect('toggled', self.on_lock_toggle)
        menu.append(lock_item)
        fc_item = Gtk.CheckMenuItem(label='显示一周天气')
        fc_item.set_active(bool(self.cfg.get('show_forecast', False)))
        fc_item.connect('toggled', self.on_forecast_toggle)
        menu.append(fc_item)
        menu.append(Gtk.SeparatorMenuItem())
        tsub = Gtk.Menu()
        t_item = Gtk.MenuItem(label='时间颜色')
        t_item.set_submenu(tsub)
        for name, col in (('白色', '#FFFFFF'), ('黄色', '#FFEB3B'), ('橙色', '#FFA726'), ('红色', '#FF5252'), ('粉色', '#FF8FAB'), ('蓝色', '#64B5F6'), ('绿色', '#69F0AE'), ('黑色', '#000000')):
            it = Gtk.MenuItem(label=name)
            it.connect('activate', lambda w, c=col: self.set_time_color(c))
            tsub.append(it)
        menu.append(t_item)
        dsub = Gtk.Menu()
        d_item = Gtk.MenuItem(label='年月日星期农历颜色')
        d_item.set_submenu(dsub)
        for name, col in (('白色', '#FFFFFF'), ('黄色', '#FFEB3B'), ('橙色', '#FFA726'), ('红色', '#FF5252'), ('粉色', '#FF8FAB'), ('蓝色', '#64B5F6'), ('绿色', '#69F0AE'), ('黑色', '#000000')):
            it = Gtk.MenuItem(label=name)
            it.connect('activate', lambda w, c=col: self.set_color(c))
            dsub.append(it)
        menu.append(d_item)
        wsub = Gtk.Menu()
        w_item = Gtk.MenuItem(label='天气颜色')
        w_item.set_submenu(wsub)
        for name, col in (('红色', '#FF5252'), ('黄色', '#FFEB3B'), ('蓝色', '#64B5F6'), ('绿色', '#69F0AE'), ('白色', '#FFFFFF'), ('黑色', '#000000')):
            it = Gtk.MenuItem(label=name)
            it.connect('activate', lambda w, c=col: self.set_weather_color(c))
            wsub.append(it)
        menu.append(w_item)
        fsub = Gtk.Menu()
        f_item = Gtk.MenuItem(label='一周天气颜色')
        f_item.set_submenu(fsub)
        for name, col in (('红色', '#FF5252'), ('黄色', '#FFEB3B'), ('蓝色', '#64B5F6'), ('绿色', '#69F0AE'), ('白色', '#FFFFFF'), ('黑色', '#000000')):
            it = Gtk.MenuItem(label=name)
            it.connect('activate', lambda w, c=col: self.set_forecast_color(c))
            fsub.append(it)
        menu.append(f_item)
        menu.append(Gtk.SeparatorMenuItem())
        add('设置城市/API Key', self.on_settings)
        add('刷新天气', lambda w: self.schedule_weather(0))
        
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
        self.save_config()

    def on_forecast_toggle(self, item):
        self.cfg['show_forecast'] = item.get_active()
        self.apply_style()
        self.update_time()
        self.save_config()
        if item.get_active():
            self.schedule_weather(0)

    def resize_by(self, delta):
        self.cfg['font_size'] = max(16, min(96, int(self.cfg.get('font_size', BASE_FONT_SIZE)) + delta))
        self.apply_style()
        self.update_time()
        self.save_config()

    def set_color(self, color):
        self.cfg['color'] = color
        self.apply_style()
        self.save_config()

    def set_time_color(self, color):
        self.cfg['time_color'] = color
        self.apply_style()
        self.save_config()

    def set_weather_color(self, color):
        self.cfg['weather_color'] = color
        self.apply_style()
        self.update_time()
        self.save_config()

    def set_forecast_color(self, color):
        self.cfg['forecast_color'] = color
        self.apply_style()
        self.update_time()
        self.save_config()

    def on_settings(self, widget):
        dlg = Gtk.Dialog(title='挂件设置', parent=self, flags=Gtk.DialogFlags.MODAL)
        dlg.add_buttons('取消', Gtk.ResponseType.CANCEL, '保存', Gtk.ResponseType.OK)
        dlg.set_default_size(440, 520)
        box = dlg.get_content_area()
        box.set_spacing(6)
        box.set_margin_start(12); box.set_margin_end(12); box.set_margin_top(12); box.set_margin_bottom(12)

        # ── 城市/乡村搜索区 ──
        old_city = self.cfg.get('city', '北京市')
        old_lat, old_lon = self.cfg.get('lat'), self.cfg.get('lon')
        pending = {}          # 待保存的选中结果 {name, lat, lon}

        city_ent = Gtk.Entry(text=old_city)
        search_btn = Gtk.Button(label='搜索')
        srow = Gtk.Box(spacing=8)
        srow.pack_start(Gtk.Label(label='城市/乡村'), False, False, 0)
        srow.pack_start(city_ent, True, True, 0)
        srow.pack_start(search_btn, False, False, 0)
        box.pack_start(srow, False, False, 0)

        store = Gtk.ListStore(str, str, float, float)   # 地名 | 所属地区 | lat | lon
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
                city_ent.set_text(model[it][0])
        sel.connect('changed', on_selected)

        scroll = Gtk.ScrolledWindow()
        scroll.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
        scroll.add(tree)
        scroll.set_size_request(-1, 150)
        box.pack_start(scroll, True, True, 0)

        hint = Gtk.Label(label='输入城市或乡村名（如：北京市 / 成都市 / 某某镇），点搜索后在列表中选择')
        hint.set_line_wrap(True)
        hint.get_style_context().add_class('dim-label')
        box.pack_start(hint, False, False, 0)

        def do_search(*_a):
            store.clear()
            pending.clear()
            q = city_ent.get_text().strip()
            if not q:
                return
            search_btn.set_sensitive(False)
            while Gtk.events_pending():
                Gtk.main_iteration()
            try:
                for r in self.weather.search_cities(q):
                    store.append((r['name'], r['area'], r['lat'] or 0.0, r['lon'] or 0.0))
            finally:
                search_btn.set_sensitive(True)
        search_btn.connect('clicked', do_search)
        city_ent.connect('activate', do_search)
        # 初始：把当前配置的城市放入列表并选中（已有坐标时）
        if old_lat is not None and old_lon is not None:
            store.append((old_city, '当前设置', float(old_lat), float(old_lon)))
            sel.select_iter(store.get_iter_first())

        # ── 字号 / 颜色 ──
        size_adj = Gtk.Adjustment(value=float(self.cfg.get('font_size', BASE_FONT_SIZE)), lower=16, upper=96, step_increment=2)
        size_spin = Gtk.SpinButton(adjustment=size_adj)
        color_ent = Gtk.Entry(text=self.cfg.get('color', '#FFEB3B'))
        time_color_ent = Gtk.Entry(text=self.cfg.get('time_color', '#4A90D9'))
        weather_color_ent = Gtk.Entry(text=self.cfg.get('weather_color', '#87CEEB'))
        forecast_color_ent = Gtk.Entry(text=self.cfg.get('forecast_color', '#87CEEB'))
        row_size = Gtk.Box(spacing=8); row_size.pack_start(Gtk.Label(label='字号'), False, False, 0); row_size.pack_start(size_spin, True, True, 0)
        row_c1 = Gtk.Box(spacing=8); row_c1.pack_start(Gtk.Label(label='年月日星期农历颜色(#RRGGBB)'), False, False, 0); row_c1.pack_start(color_ent, True, True, 0)
        row_c2 = Gtk.Box(spacing=8); row_c2.pack_start(Gtk.Label(label='时间颜色(#RRGGBB)'), False, False, 0); row_c2.pack_start(time_color_ent, True, True, 0)
        row_c3 = Gtk.Box(spacing=8); row_c3.pack_start(Gtk.Label(label='天气颜色(#RRGGBB)'), False, False, 0); row_c3.pack_start(weather_color_ent, True, True, 0)
        row_c4 = Gtk.Box(spacing=8); row_c4.pack_start(Gtk.Label(label='一周天气颜色(#RRGGBB)'), False, False, 0); row_c4.pack_start(forecast_color_ent, True, True, 0)
        for r in (row_size, row_c1, row_c2, row_c3, row_c4):
            box.pack_start(r, False, False, 0)
        fc_chk = Gtk.CheckButton(label='显示一周天气')
        fc_chk.set_active(bool(self.cfg.get('show_forecast', False)))
        box.pack_start(fc_chk, False, False, 0)
        box.show_all()
        if dlg.run() == Gtk.ResponseType.OK:
            new_city = city_ent.get_text().strip() or '北京市'
            self.cfg['city'] = new_city
            if pending:
                self.cfg['lat'] = pending['lat']
                self.cfg['lon'] = pending['lon']
            elif new_city != old_city:
                # 换了城市名但没选候选：清坐标，按新名字重新查找
                self.cfg['lat'] = None
                self.cfg['lon'] = None
            self.cfg['font_size'] = int(size_spin.get_value())
            self.cfg['show_forecast'] = fc_chk.get_active()
            col = color_ent.get_text().strip()
            if re.match(r'^#[0-9A-Fa-f]{6}$', col):
                self.cfg['color'] = col
            tcol = time_color_ent.get_text().strip()
            if re.match(r'^#[0-9A-Fa-f]{6}$', tcol):
                self.cfg['time_color'] = tcol
            wcol = weather_color_ent.get_text().strip()
            if re.match(r'^#[0-9A-Fa-f]{6}$', wcol):
                self.cfg['weather_color'] = wcol
            fcol = forecast_color_ent.get_text().strip()
            if re.match(r'^#[0-9A-Fa-f]{6}$', fcol):
                self.cfg['forecast_color'] = fcol
            self.weather._geo = None          # 清地理缓存，强制重新解析
            self.apply_style()
            self.update_time()
            self.save_config()
            self.schedule_weather(0)
        dlg.destroy()

    def save_config(self):
        try:
            os.makedirs(USER_DIR, exist_ok=True)
            with open(CONFIG_PATH, 'w') as f:
                json.dump(self.cfg, f, ensure_ascii=False, indent=2)
        except Exception as e:
            log('config save: ' + str(e))


def main():
    log('widget start')
    win = WidgetWindow()
    Gtk.main()


if __name__ == '__main__':
    main()
