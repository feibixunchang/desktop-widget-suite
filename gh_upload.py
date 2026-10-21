#!/usr/bin/env python3
# 把构建好的安装包上传到已存在的 win-<ver> Release，并刷新正文。
# 用 urllib 直连，避开 Windows 上 curl 的 /dev/fd 进程替换坑。
import json, os, sys, urllib.request, urllib.error

HERE = os.path.dirname(os.path.abspath(__file__))
OWNER = "feibixunchang"
REPO = "desktop-widget-suite"
VER = sys.argv[1] if len(sys.argv) > 1 else "1.2"
TAG = "win-v" + VER
ASSET_NAME = "WidgetHubSetup.exe"          # 管理器检查更新只看这个名字
LOCAL_FILE = os.path.join(HERE, "WidgetHubSetup-%s.exe" % TAG)

cfg = os.path.expandvars(r"%APPDATA%\WidgetHub\config.json")
TOKEN = json.load(open(cfg, encoding="utf-8")).get("UpdateToken", "").strip()

API = "https://api.github.com/repos/%s/%s" % (OWNER, REPO)
UPLOAD = "https://uploads.github.com/repos/%s/%s/releases/%%d/assets" % (OWNER, REPO)

def hdr(extra=None):
    h = {"Authorization": "token " + TOKEN, "Accept": "application/vnd.github+json",
         "User-Agent": "WidgetHub-Release"}
    if extra:
        h.update(extra)
    return h

def req(method, url, data=None, headers=None, is_json=True):
    r = urllib.request.Request(url, data=data, method=method)
    for k, v in (headers or {}).items():
        r.add_header(k, v)
    try:
        with urllib.request.urlopen(r) as resp:
            body = resp.read().decode("utf-8", "replace")
            return resp.status, (json.loads(body) if is_json and body else body)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")

# 1) 找到 win-v 这个 Release 的 id
st, rel = req("GET", "%s/releases/tags/%s" % (API, TAG), headers=hdr())
if st != 200 or not isinstance(rel, dict) or "id" not in rel:
    print("找不到 Release %s (HTTP %s): %s" % (TAG, st, rel))
    sys.exit(1)
rel_id = rel["id"]
print("Release %s 存在, id=%s" % (TAG, rel_id))

# 2) 刷新正文（用 RELEASE_NOTES.md）
notes = os.path.join(HERE, "RELEASE_NOTES.md")
body = open(notes, encoding="utf-8").read() if os.path.exists(notes) else (TAG + " 发布")
st, _ = req("PATCH", "%s/releases/%d" % (API, rel_id),
            data=json.dumps({"name": "桌面挂件合集 " + TAG, "body": body,
                             "draft": False, "prerelease": False},
                            ensure_ascii=False).encode("utf-8"),
            headers=hdr({"Content-Type": "application/json; charset=utf-8"}))
print("刷新正文: HTTP %s" % st)

# 3) 删除同名旧附件（幂等，避免 422）
st, assets = req("GET", "%s/releases/%d/assets" % (API, rel_id), headers=hdr())
old_id = None
if isinstance(assets, list):
    for a in assets:
        if a.get("name") == ASSET_NAME:
            old_id = a.get("id")
if old_id:
    st, _ = req("DELETE", "%s/releases/assets/%d" % (API, old_id), headers=hdr())
    print("删除旧附件 id=%s: HTTP %s" % (old_id, st))

# 4) 上传新附件
if not os.path.exists(LOCAL_FILE):
    print("本地安装包不存在: %s" % LOCAL_FILE)
    sys.exit(1)
size = os.path.getsize(LOCAL_FILE)
print("上传 %s (%.2f MB) 为 %s ..." % (os.path.basename(LOCAL_FILE), size/1e6, ASSET_NAME))
with open(LOCAL_FILE, "rb") as f:
    data = f.read()
st, up = req("POST", (UPLOAD % rel_id) + "?name=" + ASSET_NAME,
             data=data, headers=hdr({"Content-Type": "application/octet-stream"}))
print("上传附件: HTTP %s" % st)
if isinstance(up, dict):
    print("   -> name=%s  download=%s" % (up.get("name"), up.get("browser_download_url")))

# 5) 复核
st, rel2 = req("GET", "%s/releases/%d" % (API, rel_id), headers=hdr())
if isinstance(rel2, dict):
    print("最终 Release 附件:", [a["name"] for a in rel2.get("assets", [])])
