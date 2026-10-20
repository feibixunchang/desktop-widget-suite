#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""回归检查：扫描 .deb 内所有 Python 源码的 set_markup / set_label 调用，
比对格式串里的占位符个数与实际参数个数是否一致。

起因：v2.9.78 手工改字体时把 `% _SYS_FONT` 写进了字符串字面量内部
（`'<span ... font="%s 14" % _SYS_FONT>●</span>' % pc`），导致
TypeError: not enough arguments for format string，万年历的记事弹窗
一打开就崩。这类错误编译期发现不了，只能靠静态比对抓。

用法：
    python3 build/check_markup.py dist/desktop-widgets-suite_2.9.80_all.deb
    # 或扫描目录
    python3 build/check_markup.py --dir /usr/local/lib/desktop-calendar

退出码 0 = 无问题，1 = 发现不匹配。
"""
import ast
import io
import os
import re
import sys
import tarfile

PH_RE = re.compile(r'%[-#0 +]*[0-9]*(\.[0-9]+)?[sdfrex%]')


def count_placeholders(s):
    # 排除 %%（转义的字面百分号）
    return len([m for m in PH_RE.finditer(s) if m.group(0) != '%%'])


def scan_source(name, src):
    """返回 [(name, lineno, 占位符数, 参数数, 格式串片段)]"""
    out = []
    try:
        tree = ast.parse(src)
    except SyntaxError:
        return out
    for node in ast.walk(tree):
        if not (isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute)):
            continue
        if node.func.attr not in ('set_markup', 'set_label') or not node.args:
            continue
        a0 = node.args[0]
        fmt = None
        nargs = 0
        # 形式一：'...%s...' % (a, b)   （BinOp）
        if isinstance(a0, ast.BinOp) and isinstance(a0.op, ast.Mod) \
                and isinstance(a0.left, ast.Constant) and isinstance(a0.left.value, str):
            fmt = a0.left.value
            nargs = len(a0.right.elts) if isinstance(a0.right, ast.Tuple) else 1
        # 形式二：set_markup('...%s...', a, b)
        elif isinstance(a0, ast.Constant) and isinstance(a0.value, str):
            fmt = a0.value
            if len(node.args) >= 2 and isinstance(node.args[1], ast.Tuple):
                nargs = len(node.args[1].elts)
            elif len(node.args) >= 2:
                nargs = 1
        if fmt is None:
            continue
        n = count_placeholders(fmt)
        if n <= 1:
            continue          # 单占位符的 false positive 太多，跳过
        if nargs != n:
            out.append((name, node.lineno, n, nargs, fmt[:70]))
    return out


def data_tar_of_deb(path):
    raw = open(path, 'rb').read()
    i = 8
    while i < len(raw):
        hdr = raw[i:i + 60]
        i += 60
        nm = hdr[0:16].decode().strip()
        sz = int(hdr[48:58].decode().strip())
        if nm.startswith('data.tar'):
            return raw[i:i + sz]
        i += sz + (sz % 2)
    return None


def main():
    args = sys.argv[1:]
    problems = []
    checked = 0
    if args and args[0] == '--dir':
        for root, dirs, files in os.walk(args[1]):
            dirs[:] = [d for d in dirs if d != '__pycache__']
            for fn in files:
                if fn.endswith('.py'):
                    p = os.path.join(root, fn)
                    src = open(p, encoding='utf-8', errors='replace').read()
                    res = scan_source(p, src)
                    checked += 1
                    problems += res
    else:
        deb = args[0] if args else 'dist/desktop-widgets-suite_2.9.80_all.deb'
        blob = data_tar_of_deb(deb)
        with tarfile.open(fileobj=io.BytesIO(blob), mode='r:xz') as t:
            for m in t.getmembers():
                if m.isfile() and m.name.endswith('.py'):
                    src = t.extractfile(m).read().decode('utf-8', 'replace')
                    checked += 1
                    problems += scan_source(m.name, src)
    print('扫描 .py: %d 个' % checked)
    if problems:
        print('!! 占位符与参数个数不匹配：')
        for p in problems:
            print('   %s:%d  占位符=%d  参数=%d  |  %s' % p)
        return 1
    print('全部匹配，无隐患')
    return 0


if __name__ == '__main__':
    sys.exit(main())
