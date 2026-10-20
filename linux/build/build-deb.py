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
import tarfile, io, hashlib, os, sys, argparse, gzip, re

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
    buf = io.BytesIO()
    with tarfile.open(fileobj=io.BytesIO(in_blob), mode='r:xz') as tin, \
         tarfile.open(fileobj=buf, mode='w:xz') as tout:
        for m in tin.getmembers():
            nm = norm(m.name)
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
                data = tin.extractfile(m).read()
                if patch_fn is not None:
                    data = patch_fn(nm, data)
                newm.size = len(data)
                tout.addfile(newm, io.BytesIO(data))
                if collect_md5 is not None:
                    collect_md5(nm, data)
            else:
                tout.addfile(newm)
        # 追加额外源文件（--patch 模式注入 hub / 字体解析 / 别名）
        if extra_files:
            for arcname, payload, mode in extra_files:
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

def rebuild_control(ctrl_blob, md5content):
    cbuf = io.BytesIO()
    with tarfile.open(fileobj=io.BytesIO(ctrl_blob), mode='r:xz') as tin, \
         tarfile.open(fileobj=cbuf, mode='w:xz') as tout:
        for mm in tin.getmembers():
            if norm(mm.name) == 'md5sums':
                continue
            d = tin.extractfile(mm).read() if mm.isfile() else None
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
    with tarfile.open(fileobj=databuf, mode='w:xz') as tout:
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
    with tarfile.open(fileobj=cbuf, mode='w:xz') as tout:
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
    "import sys as _wfsys\n"
    "_wf_path = '/usr/local/lib/desktop-widgets-suite'\n"
    "if _wf_path not in _wfsys.path:\n"
    "    _wfsys.path.insert(0, _wf_path)\n"
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

# 注入的新文件：(仓库内相对路径, data tar 内目标路径, 权限)
INJECT_FILES = [
    ('common/widget_font.py', 'usr/local/lib/desktop-widgets-suite/widget_font.py', 0o644),
    ('fonts/99-desktop-widgets.conf', 'etc/fonts/conf.d/99-desktop-widgets.conf', 0o644),
    ('hub/hub.py', 'usr/local/lib/desktop-widgets-hub/hub.py', 0o644),
    ('hub/desktop-widgets-hub', 'usr/local/bin/desktop-widgets-hub', 0o755),
    ('hub/desktop-widgets-hub.desktop', 'usr/share/applications/desktop-widgets-hub.desktop', 0o644),
]


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
    for src_rel, arcname, mode in INJECT_FILES:
        p = os.path.join(src_root, src_rel)
        with open(p, 'rb') as fh:
            extra.append((arcname, fh.read(), mode))
    print("注入新文件:", len(extra))

    md5lines = []
    def collector(nm, data):
        md5lines.append("%s  %s\n" % (hashlib.md5(data).hexdigest(), nm))

    def patch_fn(nm, data):
        if nm in FONT_PATCH_TARGETS:
            return patch_widget_source(nm, data)
        return data

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
    verify(a.output)

if __name__ == '__main__':
    main()
