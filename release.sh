#!/usr/bin/env bash
# ==========================================================
#  桌面挂件合集（Windows 线）· 一键发布
#  用法:  export GITHUB_TOKEN=你的PAT
#         bash release.sh 1.2
#  流程: 改版本号 → 构建 → 提交 → 推送 windows-suite → 打 win-v<版本> tag → 发 Release（带安装包）
#
#  ⚠️ tag 必须带 win- 前缀：同一个仓库的 main 上是 Linux 版（tag 形如 2.9.80），
#     管理器的检查更新按 win- 前缀过滤，不带前缀的 tag 会被当成别的平台而忽略。
# ==========================================================
set -e

VER="${1:-}"
if [ -z "$VER" ]; then echo "用法: bash release.sh <版本号，例如 1.2>"; exit 1; fi
if [ -z "$GITHUB_TOKEN" ]; then echo "请先设置 GITHUB_TOKEN 环境变量"; exit 1; fi

HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$HERE"
DOTNET="/c/Program Files/dotnet/dotnet.exe"
REPO_OWNER="feibixunchang"
REPO_NAME="desktop-widget-suite"
# Windows 线只推 windows-suite 分支：main 被 Linux 版占着，不要往上推
WIN_BRANCH="windows-suite"
TAG="win-v$VER"

echo "==> 1/6 更新版本号 -> $TAG"
"$DOTNET" --version >/dev/null
python - "$VER" << 'PY'
import re, sys, io
ver = sys.argv[1]
p = 'WidgetHub/Program.cs'
s = io.open(p, encoding='utf-8').read()
s = re.sub(r'(public const string Version = ")[^"]*(")', r'\g<1>' + ver + r'\g<2>', s, count=1)
io.open(p, 'w', encoding='utf-8').write(s)
print('   WidgetHub/Program.cs 版本号已更新为', ver)
PY

echo "==> 2/6 构建管理器"
"$DOTNET" publish WidgetHub/WidgetHub.csproj -c Release -o WidgetHub/publish48 -v quiet --nologo

echo "==> 3/6 构建卸载器"
"$DOTNET" publish installer/WidgetHubUninstall/WidgetHubUninstall.csproj -c Release -o installer/WidgetHubUninstall/out -v quiet --nologo

echo "==> 4/6 打包负载并构建安装程序"
# 优先用管理器自己维护的 Skins 汇总目录，而不是 Programs 下那 10 个各自
# 独立的安装目录。
#
# Programs 下每个挂件是自己装的，里面混着运行期产物（截图、日志、.bak，
# 桌面音乐那台还多出一份播放时生成的 shot_*.png），而且这些文件夹归各自的
# 安装器管，时不时会因为被占用或权限问题读不出来一个 txt 就把整次发布搞停。
# Skins 汇总是 WidgetHub 正在用的那一份，是干净的、和实际运行完全一致的。
PROGRAMS_DIR="$LOCALAPPDATA/Programs/WidgetHubSuite/Skins"
[ -d "$PROGRAMS_DIR" ] || PROGRAMS_DIR="$LOCALAPPDATA/Programs"
[ -d "$PROGRAMS_DIR" ] || PROGRAMS_DIR="$HOME/AppData/Local/Programs/WidgetHubSuite/Skins"
[ -d "$PROGRAMS_DIR" ] || PROGRAMS_DIR="$HOME/AppData/Local/Programs"
echo "    挂件来源: $PROGRAMS_DIR"
"$DOTNET" run --project tools/Diagnostics -c Release -- --pack \
  "$HERE/WidgetHub/publish48" "$PROGRAMS_DIR" "$HERE/stage" "$HERE/installer/WidgetHubSetup/payload.zip"
"$DOTNET" publish installer/WidgetHubSetup/WidgetHubSetup.csproj -c Release -o installer/WidgetHubSetup/out -v quiet --nologo
cp -f installer/WidgetHubSetup/out/WidgetHubSetup.exe ./WidgetHubSetup-$TAG.exe

echo "==> 5/6 提交并推送到 $WIN_BRANCH"
git add -A
git commit -m "release: $TAG" || echo "   (没有变更，跳过提交)"
git tag -f "$TAG" >/dev/null 2>&1 || true
git push origin "HEAD:refs/heads/$WIN_BRANCH"
git push origin "refs/tags/$TAG" --force

echo "==> 6/6 创建 GitHub Release"
ASSET_NAME="WidgetHubSetup.exe"
# 有 RELEASE_NOTES.md 就用它当正文（每次发版前写这一版的改动），
# 没有就退回默认的三行说明。默认说明对"这一版改了什么"一无所知。
if [ -f RELEASE_NOTES.md ]; then
  BODY="$(cat RELEASE_NOTES.md)

---
* 12 个桌面挂件 + 统一托盘图标
* 目标电脑无需安装任何 .NET 运行时
* 安装包：$ASSET_NAME（约 $(du -m "WidgetHubSetup-$TAG.exe" | cut -f1)MB）

下载 $ASSET_NAME 后双击即可安装；已安装的用户可在管理器里点右上角版本号 →「检查更新」一键升级。"
else
BODY=$(cat << EOF
桌面挂件合集 WidgetHub $TAG

* 12 个桌面挂件 + 统一托盘图标
* 目标电脑无需安装任何 .NET 运行时
* 安装包：$ASSET_NAME（约 $(du -m "WidgetHubSetup-$TAG.exe" | cut -f1)MB）

下载 $ASSET_NAME 后双击即可安装；已安装的用户可在管理器里点右上角版本号 ->「检查更新」一键升级。
EOF
)
fi
API="https://api.github.com/repos/$REPO_OWNER/$REPO_NAME"
AUTH=(-H "Authorization: token $GITHUB_TOKEN" -H "Accept: application/vnd.github+json" -H "User-Agent: WidgetHub-Release")

# 注意：不能用 <(python ...) 进程替换喂给 curl 的 --data-binary —— Windows 上的 curl
# 打不开 /dev/fd/NN 这种伪路径（实测报 "error encountered when reading a file"）。
# 先落成临时文件再传。用仓库内相对路径（不带盘符），否则沙箱的 safe-delete 会拒绝对
# 带盘符的临时文件执行 rm。
JSON_TMP="$HERE/.relbody.tmp"
trap 'rm -f "$JSON_TMP"' EXIT
BODY="$BODY" TAG="$TAG" OUT="$JSON_TMP" python - > /dev/null << 'PY'
import json, os
json.dump({'tag_name': os.environ['TAG'],
           'name': '桌面挂件合集 ' + os.environ['TAG'],
           'body': os.environ['BODY'],
           'draft': False, 'prerelease': False},
          open(os.environ['OUT'], 'w', encoding='utf-8'), ensure_ascii=False)
PY
echo "    正文已写入 $JSON_TMP（$(wc -c < "$JSON_TMP") 字节）"

# 幂等：Release 已存在就复用（重跑不会 422），顺便把正文刷成最新的 RELEASE_NOTES.md
REL_ID=$(curl -s "$API/releases/tags/$TAG" "${AUTH[@]}" \
  | python -c "import json,sys; d=json.load(sys.stdin); print(d.get('id') or '')")
if [ -n "$REL_ID" ]; then
  echo "    Release $TAG 已存在（id=$REL_ID），更新正文并复用"
  curl -s -X PATCH "$API/releases/$REL_ID" "${AUTH[@]}" \
    -H "Content-Type: application/json; charset=utf-8" \
    --data-binary "@$JSON_TMP" -o /dev/null -w "   更新正文: %{http_code}\n"
else
  curl -s -X POST "$API/releases" "${AUTH[@]}" \
    -H "Content-Type: application/json; charset=utf-8" \
    --data-binary "@$JSON_TMP" -o /dev/null -w "   创建 Release: %{http_code}\n"
fi

UPLOAD_URL=$(curl -s "$API/releases/tags/$TAG" "${AUTH[@]}" \
  | python -c "import json,sys; d=json.load(sys.stdin); print(d.get('upload_url',''))")
# upload_url 形如 .../assets{?name,label}（GitHub 的 URI 模板语法），里面既没有 '?'
# 也没有 '{'，所以 %%?* 根本切不掉那段模板——直接把首个 '?' 或 '{' 起的全部截掉。
UPLOAD_URL=$(echo "$UPLOAD_URL" | sed -E 's/[?{].*//')
if [ -n "$UPLOAD_URL" ]; then
  # 同名附件已存在先删掉，否则上传会 422
  OLD_ID=$(curl -s "$API/releases/tags/$TAG/assets" "${AUTH[@]}" \
    | python -c "
import json,sys
try: d=json.load(sys.stdin)
except Exception: d=[]
for a in (d if isinstance(d,list) else []):
    if a.get('name')=='$ASSET_NAME': print(a.get('id') or '')
")
  if [ -n "$OLD_ID" ]; then
    curl -s -X DELETE "$API/releases/assets/$OLD_ID" "${AUTH[@]}" > /dev/null
    echo "    已删除旧附件（id=$OLD_ID）"
  fi
  curl -s -X POST "$UPLOAD_URL?name=$ASSET_NAME" \
    -H "Authorization: token $GITHUB_TOKEN" \
    -H "Content-Type: application/octet-stream" \
    --data-binary "@./WidgetHubSetup-$TAG.exe" -o /dev/null -w "   上传安装包: %{http_code}\n"
fi

echo ""
echo "发布完成: $TAG  ->  https://github.com/$REPO_OWNER/$REPO_NAME/releases"
