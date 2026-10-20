#!/usr/bin/env python3
"""桌面挂件自愈守护

规则：
  * 挂件进程消失（崩溃 / 被 kill -9 等异常退出）→ 自动重新拉起；
  * 用户在挂件菜单里点「退出」→ wrapper 捕获到退出码 0，写入
    ~/.config/desktop-widgets/<name>.off 标记 → 本会话不再拉起；
  * 重新从启动器打开挂件 → wrapper 启动时清掉标记 → 恢复守护；
  * 每次登录，自启项会正常启动挂件并清掉标记，一切回到默认状态。

守护本身只做“补位”，不参与首次启动（首次由 autostart 负责），
避免重复拉起。
"""
import json
import os
import shlex
import signal
import subprocess
import sys
import time

HOME = os.path.expanduser('~')
MARK_DIR = os.path.join(HOME, '.config', 'desktop-widgets')
SETTINGS_PATH = os.path.join(MARK_DIR, 'settings.json')
LOG_DIR = os.path.join(HOME, '.local', 'share', 'desktop-widgets-supervisor')
LOG_PATH = os.path.join(LOG_DIR, 'supervisor.log')
PID_PATH = os.path.join(LOG_DIR, 'supervisor.pid')

# (key, 启动命令, pgrep 匹配串)
WIDGETS = [
    ('desktop-calendar', '/usr/local/bin/desktop-calendar',
     '/usr/local/lib/desktop-calendar/calendar_widget.py'),
    ('desktop-frame', '/usr/local/bin/desktop-frame',
     '/usr/local/lib/desktop-frame/frame.py'),
    ('desktop-monitor', '/usr/local/bin/desktop-monitor',
     '/opt/desktop-monitor/monitor.py'),
    ('desktop-movie', '/usr/local/bin/desktop-movie',
     '/usr/local/lib/desktop-movie/desktop-movie.py'),
    ('desktop-music', '/usr/local/bin/desktop-music',
     '/usr/local/lib/desktop-music/music.py'),
    ('desktop-notes', '/usr/local/bin/desktop-notes',
     '/usr/local/lib/desktop-notes/notes.py'),
    ('desktop-radio', '/usr/local/bin/desktop-radio',
     '/usr/local/lib/desktop-radio/radio.py'),
    ('desktop-rss', '/usr/local/bin/desktop-rss',
     '/usr/local/lib/desktop-rss/rss.py'),
    ('desktop-timer', '/usr/local/bin/desktop-timer',
     '/usr/local/lib/desktop-timer/timer_alarm_widget.py'),
    ('desktop-wallet', '/usr/local/bin/desktop-wallet',
     '/usr/local/lib/desktop-wallet/wallet.py'),
    ('desktop-weather', '/usr/local/bin/desktop-weather',
     '/usr/local/lib/desktop-weather/widget.py'),
    ('edge-scroll-control', '/usr/local/bin/edge-scroll-control',
     '/usr/local/lib/edge-scroll-control/main.py'),
    ('monitor-input-switch', '/usr/local/bin/monitor-input-switch',
     '/usr/local/lib/monitor-input-switch/main.py'),
    ('taskbar-info', '/usr/bin/taskbar-info',
     '/usr/lib/taskbar-info/taskbar-info.py'),
    ('shot-ocr', '/usr/bin/shot-ocr --autostart',
     '/usr/lib/shot-ocr/main.py'),
    ('mouse-trail', '/usr/bin/mouse-trail',
     '/usr/lib/mouse-trail/main.py'),
    ('copy-toolbar', '/usr/bin/copy-toolbar',
     '/usr/lib/copy-toolbar/main.py'),
]

INTERVAL = 5          # 巡检间隔(秒)
START_DELAY = 15      # 启动后先等待，给 autostart 留出启动时间
RESTART_GAP = 12      # 同一挂件两次拉起的最小间隔，避免崩溃循环
MISS_LIMIT = 2        # 连续缺失多少次才补位，避开退出瞬间的竞态

_running = True


def log(msg):
    try:
        os.makedirs(LOG_DIR, exist_ok=True)
        with open(LOG_PATH, 'a') as f:
            f.write('%s %s\n' % (time.strftime('%Y-%m-%d %H:%M:%S'), msg))
    except Exception:
        pass


_SETTINGS_CACHE = {'mtime': None, 'data': {}}


def load_settings():
    """读取控制中心写入的每个挂件的设置（args / delay / auto_restart）。"""
    try:
        mtime = os.path.getmtime(SETTINGS_PATH)
    except OSError:
        _SETTINGS_CACHE.update(mtime=None, data={})
        return {}
    if _SETTINGS_CACHE['mtime'] == mtime:
        return _SETTINGS_CACHE['data']
    try:
        with open(SETTINGS_PATH) as f:
            data = json.load(f)
        if not isinstance(data, dict):
            data = {}
    except Exception:
        data = {}
    _SETTINGS_CACHE.update(mtime=mtime, data=data)
    return data


def wconf(name):
    conf = {'args': '', 'delay': 0, 'auto_restart': True}
    conf.update(load_settings().get(name) or {})
    return conf


def launch(cmd, args=''):
    cmd = (cmd + ' ' + args).strip() if args else cmd
    try:
        subprocess.Popen(shlex.split(cmd), stdin=subprocess.DEVNULL,
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        return True
    except Exception as e:
        log('launch failed %s: %s' % (cmd, e))
        return False


def is_running(pattern):
    try:
        r = subprocess.run(['pgrep', '-f', pattern],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                           timeout=5)
        return r.returncode == 0
    except Exception:
        return True   # 查询失败时不贸然拉起，避免重复




def _on_term(signum, frame):
    global _running
    _running = False


def single_instance():
    try:
        os.makedirs(LOG_DIR, exist_ok=True)
        if os.path.exists(PID_PATH):
            with open(PID_PATH) as f:
                old = int(f.read().strip() or 0)
            if old and old != os.getpid() and os.path.isdir('/proc/%d' % old):
                with open('/proc/%d/cmdline' % old, 'rb') as cf:
                    if b'supervisor.py' in cf.read():
                        return False
        with open(PID_PATH, 'w') as f:
            f.write(str(os.getpid()))
    except Exception:
        pass
    return True


def main():
    if not single_instance():
        return
    signal.signal(signal.SIGTERM, _on_term)
    signal.signal(signal.SIGINT, _on_term)
    log('supervisor start')
    time.sleep(START_DELAY)

    miss = {}
    last_start = {}
    first_miss = {}
    fail_count = {}   # 连续启动失败次数（启动后立刻消失即计一次失败）
    while _running:
        now = time.time()
        for name, cmd, pattern in WIDGETS:
            off = os.path.join(MARK_DIR, name + '.off')
            conf = wconf(name)
            if os.path.exists(off) or not conf.get('auto_restart', True):
                miss[name] = 0
                first_miss[name] = None
                continue
            if is_running(pattern):
                miss[name] = 0
                first_miss[name] = None
                fail_count[name] = 0      # 稳定运行 → 清零失败计数
                continue
            miss[name] = miss.get(name, 0) + 1
            if miss[name] < MISS_LIMIT:
                continue
            # 挂件自定义的「崩溃重启延迟」
            delay = int(conf.get('delay') or 0)
            if delay > 0:
                if first_miss.get(name) is None:
                    first_miss[name] = now
                elif now - first_miss[name] < delay:
                    continue
            # 连续失败退避：坏挂件（如缺模块必然秒退）不再每 12 秒无限重启，
            # 间隔按 12s → 2min → 5min → 10min 递增，避免刷日志、空耗 CPU
            fc = fail_count.get(name, 0)
            gap = RESTART_GAP if fc < 3 else min(600, 120 * (fc - 2))
            if now - last_start.get(name, 0) < gap:
                continue
            last_start[name] = now
            if launch(cmd, conf.get('args', '')):
                fail_count[name] = fc + 1
                log('restart %s%s' % (
                    name,
                    ('（连续失败 %d 次，退避 %ds）' % (fc + 1, gap)) if fc >= 2 else ''))
            miss[name] = 0
            first_miss[name] = None
        # 分段睡眠，收到结束信号能及时退出
        for _ in range(INTERVAL * 2):
            if not _running:
                break
            time.sleep(0.5)
    try:
        os.remove(PID_PATH)
    except Exception:
        pass
    log('supervisor stop')


if __name__ == '__main__':
    main()
