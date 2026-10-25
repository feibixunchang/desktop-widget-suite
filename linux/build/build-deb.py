#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Linux 端 .deb 纯标准库构建器（不依赖 dpkg-deb / ar / tar 等命令）。

两种用法：
  1) 以参考包为基准，剔除运行期垃圾并重新计算 md5sums（可复现、确定性）：
       python3 build-deb.py --reference ../dist/desktop-widgets-suite_2.9.74_all.deb \
                            --output ../dist/desktop-widgets-suite_2.9.74_all.deb

  2) 在本机（Deepin 25 不可变系统）以「已安装的挂件目录」为源重建：
       python3 build-deb.py --from-live --output ../dist/desktop-widgets-suite_2.9.74_all.deb
     该模式读取本脚本同级的 ../debian/ 元数据 + 主机已安装的挂件文件，重新打包装包。

始终输出一个内容自洽（md5sums 与 data.tar 一致）、可被 `dpkg -i` 安装的 .deb。
"""
import tarfile, io, hashlib, os, sys, argparse, gzip, re, lzma

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)                      # linux/
DIST = os.path.join(ROOT, 'dist')
DEFAULT_REF = os.path.join(DIST, 'desktop-widgets-suite_2.9.74_all.deb')
DEBIAN = os.path.join(ROOT, 'debian')

# 运行期垃圾：编译缓存、pid、日志、临时锁
SKIP_SUFFIXES = ('.pyc', '.pid', '.log')
SKIP_FRAGMENTS = ('/__pycache__',)

def norm(name):
    return name.strip().strip('/').lstrip('./')

def parse_ar(blob):
    assert blob[:8] == b'!<arch>\n', "not a deb/ar"
    off = 8
    members = []  # (raw16_name_bytes, data)
    while off < len(blob):
        hdr = blob[off:off + 60]
        raw_name = hdr[0:16]
        size = int(hdr[48:58].decode().strip() or '0')
        data = blob[off + 60:off + 60 + size]
        members.append((raw_name, data))
        off += 60 + size
        if size % 2 == 1:
            off += 1
    return members

def write_ar(members):
    out = bytearray(b'!<arch>\n')
    for raw_name, data in members:
        hdr = (raw_name[:16].ljust(16)
               + b'%-12d' % 0 + b'%-6d' % 0 + b'%-6d' % 0
               + b'%-8s' % b'100644' + b'%-10d' % len(data) + b'`\n')
        assert len(hdr) == 60
        out += hdr
        out += data
        if len(data) % 2 == 1:
            out += b'\n'
    return bytes(out)


def _tar_header_valid(block):
    """校验 tar 512 字节头是否真实(校验和匹配)，用于过滤数据块碎片误判。"""
    if len(block) < 512:
        return False
    chk = 0
    for i in range(512):
        chk += 0x20 if (148 <= i < 156) else block[i]
    fld = block[148:156].strip()
    if not fld:
        return False
    try:
        stored = int(fld, 8)
    except ValueError:
        return False
    return chk == stored


def assert_no_pax(xz_blob, label):
    """确认 xz 内的 tar 不含 PAX 扩展头(typeflag 'x'/'g')；否则 dpkg 报“tar 文件已损坏”。"""
    raw = lzma.decompress(xz_blob)
    n = len(raw)
    off = 0
    while off + 512 <= n:
        block = raw[off:off + 512]
        if block == b'\x00' * 512:
            break
        if not _tar_header_valid(block):
            off += 512
            continue
        tf = block[156:157]
        if tf in (b'x', b'g'):
            raise SystemExit(
                "错误: %s 含 PAX 扩展头(typeflag %r)，dpkg 无法安装。请使用 GNU 格式重建。" % (label, tf))
        try:
            s = block[124:136].strip()
            size = int(s, 8) if s else 0
        except ValueError:
            size = 0
        off += 512 + ((size + 511) // 512) * 512


def check_deb_format(deb):
    with open(deb, 'rb') as f:
        raw = f.read()
    members = parse_ar(raw)
    m = {norm(r.decode('utf-8', 'replace').strip()): d for r, d in members}
    for k in m:
        if k.startswith('data.tar') or k.startswith('control.tar'):
            assert_no_pax(m[k], k)
    print("格式检查: 无 PAX 扩展头 (dpkg 可安装) OK")

def should_skip(nm):
    if nm.endswith(SKIP_SUFFIXES):
        return True
    for frag in SKIP_FRAGMENTS:
        if frag in nm:
            return True
    # 桌面视频挂件的运行期 pid
    if nm == 'usr/local/lib/desktop-movie/movie.pid':
        return True
    return False

def rewrite_tar(in_blob, skip_pred, collect_md5=None, extra_files=None,
                patch_fn=None):
    """重写 data.tar。

    extra_files 里的文件若已存在于输入包中，则【替换】原条目（而不是在包尾
    再追加一份同名的重复条目——那会让 md5sums 出现两行且与包内旧内容不一致）。
    """
    extras = {}
    for item in (extra_files or []):
        arcname, payload, mode = item[0], item[1], item[2]
        extras[arcname] = (payload, mode)
    buf = io.BytesIO()
    with tarfile.open(fileobj=io.BytesIO(in_blob), mode='r:xz') as tin, \
         tarfile.open(fileobj=buf, mode='w:xz', format=tarfile.GNU_FORMAT) as tout:
        seen = set()
        for m in tin.getmembers():
            nm = norm(m.name)
            if not nm:
                # './' 之类的根目录条目：dpkg 会因「空文件名」直接致命报错
                continue
            if skip_pred(nm):
                print("  skip:", nm)
                continue
            newm = tarfile.TarInfo(nm)
            newm.mode = m.mode
            newm.mtime = 0
            newm.uid = 0
            newm.gid = 0
            newm.type = m.type
            newm.linkname = m.linkname
            if m.isfile():
                if nm in extras:
                    # 用注入的新内容替换参考包里的旧文件
                    payload, mode = extras.pop(nm)
                    newm.mode = mode
                    data = payload
                    seen.add(nm)
                else:
                    data = tin.extractfile(m).read()
                    if patch_fn is not None:
                        data = patch_fn(nm, data)
                newm.size = len(data)
                tout.addfile(newm, io.BytesIO(data))
                if collect_md5 is not None:
                    collect_md5(nm, data)
            else:
                tout.addfile(newm)
        # 输入包里没有的新文件才真正追加
        for arcname, (payload, mode) in extras.items():
            if arcname in seen:
                continue
            ti = tarfile.TarInfo(arcname)
            ti.mode = mode
            ti.mtime = 0
            ti.uid = 0
            ti.gid = 0
            ti.size = len(payload)
            tout.addfile(ti, io.BytesIO(payload))
            if collect_md5 is not None:
                collect_md5(arcname, payload)
    return buf.getvalue()

def build_from_reference(ref_deb, out_deb):
    with open(ref_deb, 'rb') as f:
        raw = f.read()
    print("参考包:", ref_deb, len(raw), "bytes")
    members = parse_ar(raw)
    m = {norm(r.decode('utf-8', 'replace').strip()): d for r, d in members}
    data_name = [k for k in m if k.startswith('data.tar')][0]
    ctrl_name = [k for k in m if k.startswith('control.tar')][0]
    bin_name = 'debian-binary'
    data_blob = m[data_name]
    ctrl_blob = m[ctrl_name]
    debian_bin = m[bin_name]

    md5lines = []
    def collector(nm, data):
        md5lines.append("%s  %s\n" % (hashlib.md5(data).hexdigest(), nm))

    new_data = rewrite_tar(data_blob, should_skip, collector)
    print("data 文件保留:", len(md5lines))

    md5content = "".join(md5lines).encode('utf-8')
    new_ctrl = rebuild_control(ctrl_blob, md5content)

    rawmap = {norm(r.decode('utf-8', 'replace').strip()): r for r, d in members}
    out = write_ar([
        (rawmap[bin_name], debian_bin),
        (rawmap[ctrl_name], new_ctrl),
        (rawmap[data_name], new_data),
    ])
    with open(out_deb, 'wb') as f:
        f.write(out)
    print("写出:", out_deb, len(out), "bytes")

def _control_version():
    try:
        with open(os.path.join(DEBIAN, 'control'), encoding='utf-8') as f:
            for ln in f:
                if ln.startswith('Version:'):
                    return ln.split(':', 1)[1].strip()
    except Exception:
        pass
    return None


def rebuild_control(ctrl_blob, md5content):
    target_version = _control_version()
    cbuf = io.BytesIO()
    with tarfile.open(fileobj=io.BytesIO(ctrl_blob), mode='r:xz') as tin, \
         tarfile.open(fileobj=cbuf, mode='w:xz', format=tarfile.GNU_FORMAT) as tout:
        for mm in tin.getmembers():
            if norm(mm.name) == 'md5sums':
                continue
            d = tin.extractfile(mm).read() if mm.isfile() else None
            # 修正 postinst 中 shot-ocr 的 basename 误把 --autostart 当成参数
            if norm(mm.name) == 'postinst' and d is not None:
                d = d.replace(b"awk '{print $NF}'", b"awk '{print $1}'")
            # control 整体以 debian/control 为准（版本 / 依赖 / 描述都同步），
            # 避免沿用参考包里的旧元数据
            if norm(mm.name) == 'control':
                try:
                    with open(os.path.join(DEBIAN, 'control'), 'rb') as cf:
                        d = cf.read()
                except Exception:
                    pass
            nm = tarfile.TarInfo(norm(mm.name))
            nm.mode = mm.mode; nm.mtime = 0; nm.uid = 0; nm.gid = 0
            nm.type = mm.type; nm.linkname = mm.linkname
            if mm.isfile():
                nm.size = len(d); tout.addfile(nm, io.BytesIO(d))
            else:
                tout.addfile(nm)
        mi = tarfile.TarInfo('md5sums'); mi.mode = 0o644; mi.mtime = 0
        mi.uid = 0; mi.gid = 0; mi.size = len(md5content)
        tout.addfile(mi, io.BytesIO(md5content))
    return cbuf.getvalue()

# ---- --from-live 模式：扫描主机已安装目录 + debian/ 元数据 ----
LIVE_SOURCES = [
    ('/usr/local/lib/desktop-calendar', 'usr/local/lib/desktop-calendar'),
    ('/usr/local/lib/desktop-frame', 'usr/local/lib/desktop-frame'),
    ('/opt/desktop-monitor', 'opt/desktop-monitor'),
    ('/usr/local/lib/desktop-movie', 'usr/local/lib/desktop-movie'),
    ('/usr/local/lib/desktop-music', 'usr/local/lib/desktop-music'),
    ('/usr/local/lib/desktop-notes', 'usr/local/lib/desktop-notes'),
    ('/usr/local/lib/desktop-radio', 'usr/local/lib/desktop-radio'),
    ('/usr/local/lib/desktop-rss', 'usr/local/lib/desktop-rss'),
    ('/usr/local/lib/desktop-timer', 'usr/local/lib/desktop-timer'),
    ('/usr/local/lib/desktop-wallet', 'usr/local/lib/desktop-wallet'),
    ('/usr/local/lib/desktop-weather', 'usr/local/lib/desktop-weather'),
    ('/usr/local/lib/edge-scroll-control', 'usr/local/lib/edge-scroll-control'),
    ('/usr/local/lib/monitor-input-switch', 'usr/local/lib/monitor-input-switch'),
    ('/usr/lib/taskbar-info', 'usr/lib/taskbar-info'),
    ('/usr/lib/shot-ocr', 'usr/lib/shot-ocr'),
    ('/usr/lib/mouse-trail', 'usr/lib/mouse-trail'),
    ('/usr/lib/copy-toolbar', 'usr/lib/copy-toolbar'),
    ('/usr/local/lib/desktop-widgets-supervisor', 'usr/local/lib/desktop-widgets-supervisor'),
    ('/usr/local/bin', 'usr/local/bin'),
    ('/usr/bin', 'usr/bin'),
    ('/usr/local/share/applications', 'usr/local/share/applications'),
    ('/usr/local/share/icons', 'usr/local/share/icons'),
    ('/usr/share/doc/desktop-widgets-suite', 'usr/share/doc/desktop-widgets-suite'),
]
LIVE_LAUNCHERS_SKIP = {'mpv'}  # 二进制不进仓库源，但打包时可单独加入

def build_from_live(out_deb):
    print("==> 扫描主机已安装目录（--from-live）")
    extra = []
    md5lines = []
    for host_dir, arc_prefix in LIVE_SOURCES:
        if not os.path.isdir(host_dir):
            print("  跳过缺失:", host_dir)
            continue
        for root, dirs, files in os.walk(host_dir):
            dirs[:] = [d for d in dirs if d != '__pycache__']
            for fn in files:
                full = os.path.join(root, fn)
                rel = os.path.relpath(full, host_dir)
                arc = (arc_prefix + '/' + rel).replace('\\', '/')
                if should_skip(arc):
                    continue
                if arc_prefix == 'usr/local/bin' and fn in LIVE_LAUNCHERS_SKIP:
                    continue
                if arc_prefix == 'usr/bin' and fn in LIVE_LAUNCHERS_SKIP:
                    continue
                with open(full, 'rb') as fh:
                    payload = fh.read()
                mode = 0o755 if os.access(full, os.X_OK) else 0o644
                extra.append((arc, payload, mode))
                md5lines.append("%s  %s\n" % (hashlib.md5(payload).hexdigest(), arc))
    print("   收集源文件:", len(extra))

    # 构建 data.tar.xz（仅额外文件，因为 live 模式是从零打）
    databuf = io.BytesIO()
    with tarfile.open(fileobj=databuf, mode='w:xz', format=tarfile.GNU_FORMAT) as tout:
        for arc, payload, mode in extra:
            ti = tarfile.TarInfo(arc)
            ti.mode = mode; ti.mtime = 0; ti.uid = 0; ti.gid = 0
            ti.size = len(payload)
            tout.addfile(ti, io.BytesIO(payload))
    new_data = databuf.getvalue()

    # control.tar.xz：来自 debian/ 目录 + 生成的 md5sums
    ctrl_files = {}
    for fn in ('control', 'postinst', 'prerm', 'postrm'):
        p = os.path.join(DEBIAN, fn)
        if os.path.exists(p):
            with open(p, 'rb') as fh:
                ctrl_files[fn] = fh.read()
    cbuf = io.BytesIO()
    with tarfile.open(fileobj=cbuf, mode='w:xz', format=tarfile.GNU_FORMAT) as tout:
        for fn, payload in ctrl_files.items():
            ti = tarfile.TarInfo(fn); ti.mode = 0o644 if fn == 'control' else 0o755
            ti.mtime = 0; ti.uid = 0; ti.gid = 0; ti.size = len(payload)
            tout.addfile(ti, io.BytesIO(payload))
        mi = tarfile.TarInfo('md5sums'); mi.mode = 0o644; mi.mtime = 0
        mi.uid = 0; mi.gid = 0; mi.size = len("".join(md5lines).encode())
        tout.addfile(mi, io.BytesIO("".join(md5lines).encode()))
    new_ctrl = cbuf.getvalue()
    debian_bin = b'2.0\n'

    out = write_ar([(b'debian-binary'.ljust(16), debian_bin),
                    (b'control.tar.xz'.ljust(16), new_ctrl),
                    (b'data.tar.xz'.ljust(16), new_data)])
    with open(out_deb, 'wb') as f:
        f.write(out)
    print("写出:", out_deb, len(out), "bytes")

# ── 字体补丁：把写死的字体族名改为「运行时读取系统默认字体」 ──
_FONT_RE = re.compile(
    r"^(?P<ind>\s*)(?P<name>FONT|[A-Za-z_]*_FONT)\s*=\s*"
    r"'(?P<fam>FZSongHei-B07|Sans)(?P<rest>[^']*)'(?P<tail>.*)$",
    re.M,
)

_IMPORT_BLOCK = (
    "from widget_font import SYS_FONT as _SYS_FONT\n"
)

# 需要打字体补丁的目标文件（data tar 内相对路径）
FONT_PATCH_TARGETS = {
    'usr/local/lib/desktop-calendar/calendar_widget.py',
    'usr/local/lib/desktop-calendar/note_editor.py',
    'opt/desktop-monitor/monitor.py',
    'usr/local/lib/desktop-music/music.py',
    'usr/local/lib/desktop-notes/notes.py',
    'usr/local/lib/desktop-radio/radio.py',
    'usr/local/lib/desktop-rss/rss.py',
    'usr/local/lib/desktop-timer/timer_alarm_widget.py',
    'usr/local/lib/desktop-wallet/wallet.py',
    'usr/local/lib/desktop-weather/widget.py',
    'usr/local/lib/edge-scroll-control/main.py',
    'usr/local/lib/monitor-input-switch/main.py',
}

# 注入的新文件（只放进【已存在】的目录，绝不新建顶层目录——
# Deepin 25 不可变系统下 dpkg 无法在解包阶段创建全新子目录，否则整个包安装中止）。
#   - widget_font.py 复制到每个挂件【自己的】目录（同目录 import widget_font 必然可用）
#   - 控制中心为自包含单文件脚本，放进已存在的 /usr/local/bin
#   - 菜单项放进已存在的 /usr/local/share/applications
INJECT_HUB_LAUNCHER = ('hub/desktop-widgets-hub', 'usr/local/bin/desktop-widgets-hub', 0o755)
INJECT_HUB_DESKTOP = ('hub/desktop-widgets-hub.desktop', 'usr/local/share/applications/desktop-widgets-hub.desktop', 0o644)
INJECT_SUPERVISOR = ('supervisor/supervisor.py', 'usr/local/lib/desktop-widgets-supervisor/supervisor.py', 0o755)
INJECT_MOVIE_WIDGET = ('widgets-src/desktop-movie.py', 'usr/local/lib/desktop-movie/desktop-movie.py', 0o644)
INJECT_MOVIE_FONT = ('common/widget_font.py', 'usr/local/lib/desktop-movie/widget_font.py', 0o644)
INJECT_DOC = ('doc/安装使用说明.txt', 'usr/share/doc/desktop-widgets-suite/安装使用说明.txt', 0o644)
# v2.9.78：mpv / tesseract 自包含运行时（替换 usr/local/bin 里的 mpv ELF，
# 新增 tesseract wrapper；依赖库平铺进已存在的 /usr/local/lib/mpv/）
INJECT_MPV_WRAPPER = ('widgets-src/mpv-wrapper', 'usr/local/bin/mpv', 0o755)
INJECT_TESSERACT_WRAPPER = ('widgets-src/tesseract-wrapper', 'usr/local/bin/tesseract', 0o755)
# v2.9.78：修过暗病的挂件源码（{{name}} f-string / 硬编码字体 / mpv 绝对路径）
INJECT_WIDGET_FIXES = [
    ('widgets-src/weather.py', 'usr/local/lib/desktop-weather/widget.py', 0o644),
    ('widgets-src/notes.py', 'usr/local/lib/desktop-notes/notes.py', 0o644),
    ('widgets-src/calendar.py', 'usr/local/lib/desktop-calendar/calendar_widget.py', 0o644),
    ('widgets-src/note_editor.py', 'usr/local/lib/desktop-calendar/note_editor.py', 0o644),
    ('widgets-src/rss.py', 'usr/local/lib/desktop-rss/rss.py', 0o644),
    ('widgets-src/music.py', 'usr/local/lib/desktop-music/music.py', 0o644),
    ('widgets-src/timer.py', 'usr/local/lib/desktop-timer/timer_alarm_widget.py', 0o644),
    ('widgets-src/frame.py', 'usr/local/lib/desktop-frame/frame.py', 0o644),
    ('widgets-src/radio.py', 'usr/local/lib/desktop-radio/radio.py', 0o644),
    ('widgets-src/shot-ocr.py', 'usr/lib/shot-ocr/main.py', 0o644),
    ('common/widget_font.py', 'usr/lib/shot-ocr/widget_font.py', 0o644),
    # frame 不在 FONT_PATCH_TARGETS 里，widget_font.py 必须显式注入，
    # 否则 frame.py 的 `from widget_font import ...` 会 ModuleNotFoundError
    ('common/widget_font.py', 'usr/local/lib/desktop-frame/widget_font.py', 0o644),
    # v2.9.81：鼠标拖尾重写版（自实现 SNI + dbusmenu + 空闲降频）。
    # 装在 usr/lib/mouse-trail/main.py → 文件头按路径自动切 mouse-trail 身份
    # （APP_ID=com.deepin.DesktopWidgetsMouseTrail），与控制中心/守护/.off 对齐
    ('widgets-src/mouse-trail.py', 'usr/lib/mouse-trail/main.py', 0o644),
    # v2.9.82：taskbar-info 净化版（参考包 2.9.80 里的旧文件带开发者
    # 本机默认城市，公开仓库/deb 前统一替换为通用默认值）
    ('widgets-src/taskbar-info.py', 'usr/lib/taskbar-info/taskbar-info.py', 0o644),
]


def patch_desktop_entry(nm, data):
    """给挂件/任务栏工具的菜单项加 NoDisplay=true，从启动器里隐藏。

    只保留「桌面挂件控制中心」可见——挂件统一由控制中心管理，启动器里
    不再罗列十几个图标。NoDisplay 只影响菜单显示，不影响 ~/.config/autostart
    里的自启动（那读的是另一份文件），也不影响控制中心里手动启动。
    """
    if not nm.endswith('.desktop'):
        return data
    base = nm.rsplit('/', 1)[-1]
    if base == 'desktop-widgets-hub.desktop':
        return data
    try:
        text = data.decode('utf-8')
    except Exception:
        return data
    lines = [l for l in text.splitlines() if not l.startswith('NoDisplay=')]
    lines.append('NoDisplay=true')
    if not text.endswith('\n'):
        lines.append('')
    return ('\n'.join(lines)).encode('utf-8')


# 输入法修正片段（插进挂件启动器 bash 脚本，必须在 python3 启动挂件之前生效）
IM_FIX_SNIPPET = '''# ── 输入法修正（挂件套件注入）────────────────────────────
# 症状：装的是 fcitx5 但 GTK_IM_MODULE 仍指向旧名 "fcitx"，GTK 找不到
# im-fcitx.so，输入框只能敲英文、切不出中文。这里按系统实际装了哪个
# GTK 输入法模块来纠正（fcitx5 > fcitx > ibus > xim）。
_wgt_imd=""
for _d in /usr/lib/x86_64-linux-gnu/gtk-3.0/3.0.0/immodules \\
         /usr/lib/gtk-3.0/3.0.0/immodules; do
    [ -d "$_d" ] && { _wgt_imd="$_d"; break; }
done
if [ -n "$_wgt_imd" ]; then
    if [ -n "${GTK_IM_MODULE:-}" ] && [ -f "$_wgt_imd/im-$GTK_IM_MODULE.so" ]; then
        :
    else
        for _m in fcitx5 fcitx ibus xim; do
            if [ -f "$_wgt_imd/im-$_m.so" ]; then
                export GTK_IM_MODULE="$_m"
                export XMODIFIERS="@im=$_m"
                break
            fi
        done
    fi
fi
# ── 输入法修正结束 ──────────────────────────────────────
'''


def patch_launcher(nm, data):
    """给挂件启动器（bash 脚本）补输入法修正。

    覆盖 usr/local/bin/ 与 usr/bin/ 下所有 shell 启动器。早期只匹配
    usr/local/bin/desktop-*，漏掉了两类：usr/bin/ 下的 taskbar-info、
    shot-ocr、mouse-trail、copy-toolbar、system-cleaner、deepin-taskbar-look，
    以及 usr/local/bin/ 下非 desktop- 前缀的 edge-scroll-control、
    monitor-input-switch、detect-mouse-button。这些工具由 dde-autostart
    用 runuser+env 精简环境启动，没有 GTK_IM_MODULE/XMODIFIERS，
    GTK 输入框只能打英文（v2.9.87：任务栏「设置天气城市」输不了中文）。
    控制中心 desktop-widgets-hub 是 python 脚本、mpv/tesseract 是二进制，
    会被 shebang 判断自动跳过（hub 的输入法修正在其源码内做）。
    """
    if not (nm.startswith('usr/local/bin/') or nm.startswith('usr/bin/')):
        return data
    try:
        text = data.decode('utf-8')
    except Exception:
        return data
    if not text.startswith('#!'):
        return data
    first = text.split('\n', 1)[0]
    if ('bash' not in first) and (not first.endswith('/sh')):
        return data
    if '_wgt_imd' in text:
        return data  # 已注入，幂等
    lines = text.split('\n')
    lines.insert(1, IM_FIX_SNIPPET)
    return '\n'.join(lines).encode('utf-8')


def patch_widget_source(name, data):
    try:
        text = data.decode('utf-8')
    except Exception:
        return data
    if '_SYS_FONT' in text:
        return data  # 已打过补丁，幂等
    # 在 shebang 之后插入 import 块
    lines = text.split('\n')
    insert_at = 0
    if lines and lines[0].startswith('#!'):
        insert_at = 1
    for i, ln in enumerate(lines):
        if ln.startswith('"""') or ln.startswith("'''"):
            # 若紧接 shebang 后是模块 docstring，import 放到 docstring 之后更安全
            if i == insert_at:
                insert_at = i + 1
    lines[insert_at:insert_at] = _IMPORT_BLOCK.rstrip('\n').split('\n')
    text = '\n'.join(lines)
    text = _FONT_RE.sub(r"\g<ind>\g<name> = _SYS_FONT + '\g<rest>'\g<tail>", text)
    return text.encode('utf-8')


def build_patched(ref_deb, out_deb, src_root):
    with open(ref_deb, 'rb') as f:
        raw = f.read()
    print("参考包:", ref_deb, len(raw), "bytes")
    members = parse_ar(raw)
    m = {norm(r.decode('utf-8', 'replace').strip()): d for r, d in members}
    data_name = [k for k in m if k.startswith('data.tar')][0]
    ctrl_name = [k for k in m if k.startswith('control.tar')][0]
    bin_name = 'debian-binary'
    data_blob = m[data_name]
    ctrl_blob = m[ctrl_name]
    debian_bin = m[bin_name]

    extra = []
    # ① widget_font.py 复制到每个需要字体补丁的挂件目录（已存在的目录）
    wf_src = os.path.join(src_root, 'common/widget_font.py')
    with open(wf_src, 'rb') as fh:
        wf_data = fh.read()
    widget_dirs = sorted({os.path.dirname(t) for t in FONT_PATCH_TARGETS})
    for d in widget_dirs:
        extra.append((d + '/widget_font.py', wf_data, 0o644))
    print("  widget_font.py 复制到 %d 个挂件目录" % len(widget_dirs))
    # ② 控制中心自包含启动器 + 菜单项 + 自愈守护 + 修过暗病的挂件源码
    #    （均放进已存在目录，替换参考包同名文件）
    inject_list = [INJECT_HUB_LAUNCHER, INJECT_HUB_DESKTOP,
                   INJECT_SUPERVISOR, INJECT_DOC,
                   INJECT_MOVIE_WIDGET, INJECT_MOVIE_FONT,
                   INJECT_MPV_WRAPPER, INJECT_TESSERACT_WRAPPER] + INJECT_WIDGET_FIXES
    for src_rel, arcname, mode in inject_list:
        p = os.path.join(src_root, src_rel)
        with open(p, 'rb') as fh:
            data = fh.read()
        # v2.9.82：hub 的 VERSION 常量是手改的，漏改 → 界面标题/状态栏
        # 显示旧版本（v2.9.81 就踩了：新逻辑旧版本号，用户以为没升级）。
        # 注入时统一替换为 debian/control 的版本号，杜绝此类脱节。
        if arcname == 'usr/local/bin/desktop-widgets-hub':
            ver = _control_version()
            if ver:
                txt = re.sub(r"(?m)^VERSION\s*=\s*['\"][^'\"]*['\"]",
                             "VERSION = '%s'" % ver, data.decode('utf-8'), count=1)
                data = txt.encode('utf-8')
                print("  hub VERSION 常量 -> %s" % ver)
        extra.append((arcname, data, mode))
    # ③ mpv / tesseract 自包含运行时：二进制 + 92 个依赖 .so + 语言数据，
    #    全部平铺进已存在的 /usr/local/lib/mpv/（不建任何新目录）
    mpv_dir = os.path.join(src_root, 'widgets-src', 'mpv-libs')
    mpv_count = 0
    for f in sorted(os.listdir(mpv_dir)):
        p = os.path.join(mpv_dir, f)
        if os.path.isfile(p):
            with open(p, 'rb') as fh:
                extra.append(('usr/local/lib/mpv/' + f, fh.read(), 0o755))
            mpv_count += 1
    for f in sorted(os.listdir(os.path.join(mpv_dir, 'tessdata'))):
        with open(os.path.join(mpv_dir, 'tessdata', f), 'rb') as fh:
            extra.append(('usr/local/lib/mpv/' + f, fh.read(), 0o644))
        mpv_count += 1
    print("  mpv 运行时: %d 个文件（mpv.bin/tesseract.bin + 依赖库 + chi_sim/eng）" % mpv_count)
    # 防复发：注入的挂件源码若 import widget_font，同目录必须也注入该模块。
    # （v2.9.78 漏了 desktop-frame，导致相框 ModuleNotFoundError 起不来）
    injected = {item[0] for item in extra}   # extra 元素为 (arcname, data, mode)
    missing_font = []
    py_injected = 0
    for src_rel, arcname, _mode in inject_list:
        if not arcname.endswith('.py'):
            continue
        py_injected += 1
        with open(os.path.join(src_root, src_rel), encoding='utf-8',
                  errors='replace') as fh:
            txt = fh.read()
        if 'widget_font import' in txt:
            if (os.path.dirname(arcname) + '/widget_font.py') not in injected:
                missing_font.append(arcname)
    if missing_font:
        raise SystemExit('构建中止：以下注入源码 import widget_font，但同目录未注入该模块：\n  '
                         + '\n  '.join(missing_font))
    print("  字体模块依赖检查: 通过（%d 个注入 py）" % py_injected)
    print("注入新文件:", len(extra))

    md5lines = []
    def collector(nm, data):
        md5lines.append("%s  %s\n" % (hashlib.md5(data).hexdigest(), nm))

    def patch_fn(nm, data):
        if nm in FONT_PATCH_TARGETS:
            data = patch_widget_source(nm, data)
        data = patch_desktop_entry(nm, data)
        return patch_launcher(nm, data)

    new_data = rewrite_tar(data_blob, should_skip, collector,
                           extra_files=extra, patch_fn=patch_fn)
    print("data 文件保留:", len(md5lines))

    md5content = "".join(md5lines).encode('utf-8')
    new_ctrl = rebuild_control(ctrl_blob, md5content)

    rawmap = {norm(r.decode('utf-8', 'replace').strip()): r for r, d in members}
    out = write_ar([
        (rawmap[bin_name], debian_bin),
        (rawmap[ctrl_name], new_ctrl),
        (rawmap[data_name], new_data),
    ])
    with open(out_deb, 'wb') as f:
        f.write(out)
    print("写出:", out_deb, len(out), "bytes")


def verify(deb):
    with open(deb, 'rb') as f:
        raw = f.read()
    members = parse_ar(raw)
    m = {norm(r.decode('utf-8', 'replace').strip()): d for r, d in members}
    data = m[[k for k in m if k.startswith('data.tar')][0]]
    ctrl = m[[k for k in m if k.startswith('control.tar')][0]]
    sums = {}
    with tarfile.open(fileobj=io.BytesIO(ctrl), mode='r:xz') as t:
        for mm in t.getmembers():
            if norm(mm.name) == 'md5sums':
                for l in t.extractfile(mm).read().decode().splitlines():
                    if '  ' not in l:
                        continue
                    h, p = l.split('  ', 1)
                    sums[p] = h
    ok = 0; bad = 0; missing = 0
    with tarfile.open(fileobj=io.BytesIO(data), mode='r:xz') as t:
        for mm in t.getmembers():
            nm = norm(mm.name)
            if not mm.isfile() or nm in ('', '.'):
                continue
            d = t.extractfile(mm).read()
            exp = sums.get(nm)
            if exp is None:
                missing += 1
            elif exp == hashlib.md5(d).hexdigest():
                ok += 1
            else:
                bad += 1
                print("  MD5 MISMATCH:", nm)
    print("校验: ok=%d bad=%d missing_in_md5=%d" % (ok, bad, missing))
    return bad == 0

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--reference', default=DEFAULT_REF)
    ap.add_argument('--from-live', action='store_true')
    ap.add_argument('--patch', action='store_true',
                    help='在参考包基础上注入 hub / 字体解析 / 别名，并补丁挂件字体')
    ap.add_argument('--source', default=ROOT, help='注入文件的来源根目录(linux/)')
    ap.add_argument('--output', default=DEFAULT_REF)
    ap.add_argument('--check', action='store_true', help='仅校验 --output 的 md5sums 自洽性')
    a = ap.parse_args()
    if a.check:
        sys.exit(0 if verify(a.output) else 1)
    if a.from_live:
        build_from_live(a.output)
    elif a.patch:
        build_patched(a.reference, a.output, a.source)
    else:
        build_from_reference(a.reference, a.output)
    check_deb_format(a.output)
    verify(a.output)

if __name__ == '__main__':
    main()
