#!/usr/bin/env bash
# ==========================================================
#  桌面挂件合集 · 一键发布
#  用法:  export GITHUB_TOKEN=你的PAT
#         bash release.sh 1.1
#  流程: 改版本号 → 构建 → 提交 → 推送 → 打 tag → 发 GitHub Release（带安装包）
# ==========================================================
set -e

VER="${1:-}"
if [ -z "$VER" ]; then echo "用法: bash release.sh <版本号，例如 1.1>"; exit 1; fi
if [ -z "$GITHUB_TOKEN" ]; then echo "请先设置 GITHUB_TOKEN 环境变量"; exit 1; fi

HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$HERE"
DOTNET="/c/Program Files/dotnet/dotnet.exe"
REPO_OWNER="feibixunchang"
REPO_NAME="desktop-widget-suite"
TAG="v$VER"

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
"$DOTNET" run --project tools/Diagnostics -c Release --no-build -- --pack \
  "$HERE/WidgetHub/publish48" "$PROGRAMS_DIR" "$HERE/stage" "$HERE/installer/WidgetHubSetup/payload.zip"
"$DOTNET" publish installer/WidgetHubSetup/WidgetHubSetup.csproj -c Release -o installer/WidgetHubSetup/out -v quiet --nologo
cp -f installer/WidgetHubSetup/out/WidgetHubSetup.exe ./WidgetHubSetup-$TAG.exe

echo "==> 5/6 提交并推送"
git add -A
git commit -m "release: $TAG" || echo "   (没有变更，跳过提交)"
git tag -f "$TAG" >/dev/null 2>&1 || true
git push origin HEAD
git push origin "$TAG" --force

echo "==> 6/6 创建 GitHub Release"
ASSET_NAME="WidgetHubSetup.exe"
# 有 RELEASE_NOTES.md 就用它当正文（每次发版前写这一版的改动），
# 没有就退回默认的三行说明。默认说明对"这一版改了什么"一无所知。
if [ -f RELEASE_NOTES.md ]; then
  BODY="$(cat RELEASE_NOTES.md)

---
* 10 个桌面挂件 + 统一托盘图标
* 目标电脑无需安装任何 .NET 运行时
* 安装包：$ASSET_NAME（约 $(du -m "WidgetHubSetup-$TAG.exe" | cut -f1)MB）

下载 $ASSET_NAME 后双击即可安装；已安装的用户可在管理器里点右上角版本号 →「检查更新」一键升级。"
else
BODY=$(cat << EOF
桌面挂件合集 WidgetHub $TAG

* 10 个桌面挂件 + 统一托盘图标
* 目标电脑无需安装任何 .NET 运行时
* 安装包：$ASSET_NAME（约 $(du -m "WidgetHubSetup-$TAG.exe" | cut -f1)MB）

下载 $ASSET_NAME 后双击即可安装；已安装的用户可在管理器里点右上角版本号 ->「检查更新」一键升级。
EOF
)
fi
curl -s -X POST "https://api.github.com/repos/$REPO_OWNER/$REPO_NAME/releases" \
  -H "Authorization: token $GITHUB_TOKEN" \
  -H "Accept: application/vnd.github+json" \
  -H "User-Agent: WidgetHub-Release" \
  -H "Content-Type: application/json; charset=utf-8" \
  --data-binary @<(python -c "
import json,sys
print(json.dumps({'tag_name':'$TAG','name':'桌面挂件合集 $TAG','body':sys.stdin.read(),'draft':False,'prerelease':False},ensure_ascii=False))
" <<< "$BODY") > /dev/null

UPLOAD_URL=$(curl -s "https://api.github.com/repos/$REPO_OWNER/$REPO_NAME/releases/tags/$TAG" \
  -H "Authorization: token $GITHUB_TOKEN" -H "Accept: application/vnd.github+json" -H "User-Agent: WidgetHub-Release" \
  | python -c "import json,sys; d=json.load(sys.stdin); print(d.get('upload_url',''))")
UPLOAD_URL=${UPLOAD_URL%%\?*}
if [ -n "$UPLOAD_URL" ]; then
  curl -s -X POST "$UPLOAD_URL?name=$ASSET_NAME" \
    -H "Authorization: token $GITHUB_TOKEN" \
    -H "Content-Type: application/octet-stream" \
    --data-binary "@./WidgetHubSetup-$TAG.exe" -o /dev/null -w "   上传安装包: %{http_code}\n"
fi

echo ""
echo "发布完成: $TAG  ->  https://github.com/$REPO_OWNER/$REPO_NAME/releases"
