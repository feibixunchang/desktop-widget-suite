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
TAG="win-v$VER"

echo "==> 1/6 更新版本号 -> $TAG"
"$DOTNET" --version >/dev/null
python - "$VER" << 'PY'
import re, sys, io
ver = sys.argv[1]
parts = ver.split('.')
while len(parts) < 3:
    parts.append('0')
ver3 = '.'.join(parts[:3])
ver4 = ver3 + '.0'

def patch(path, pattern, repl):
    s = io.open(path, encoding='utf-8').read()
    s, n = re.subn(pattern, repl, s)
    if n == 0:
        sys.exit('!! 版本号没改进 %s（没匹配到 %s）' % (path, pattern))
    io.open(path, 'w', encoding='utf-8').write(s)
    print('   %s' % path)

# 界面上的版本号读的是 AppInfo.Version，这几处必须一起改，
# 否则会出现「装的是新版、界面还显示旧版」。
patch('WidgetHub/WidgetHub/AppInfo.cs',
      r'(public const string Version = ")[^"]*(")', r'\g<1>' + ver + r'\g<2>')
patch('WidgetHub/Properties/AssemblyInfo.cs',
      r'(\[assembly: AssemblyFileVersion\(")[^"]*("\)\])', r'\g<1>' + ver4 + r'\g<2>')
patch('WidgetHub/Properties/AssemblyInfo.cs',
      r'(\[assembly: AssemblyInformationalVersion\(")[^"]*("\)\])', r'\g<1>' + ver3 + r'\g<2>')
patch('WidgetHub/Properties/AssemblyInfo.cs',
      r'(\[assembly: AssemblyVersion\(")[^"]*("\)\])', r'\g<1>' + ver4 + r'\g<2>')
patch('WidgetHub/app.manifest',
      r'(assemblyIdentity version=")[^"]*(")', r'\g<1>' + ver4 + r'\g<2>')
for proj in ('installer/WidgetHubSetup/WidgetHubSetup.csproj',
             'installer/WidgetHubUninstall/WidgetHubUninstall.csproj'):
    patch(proj, r'(<Version>)[^<]*(</Version>)', r'\g<1>' + ver3 + r'\g<2>')
    patch(proj, r'(<AssemblyVersion>)[^<]*(</AssemblyVersion>)', r'\g<1>' + ver4 + r'\g<2>')
    patch(proj, r'(<FileVersion>)[^<]*(</FileVersion>)', r'\g<1>' + ver4 + r'\g<2>')
patch('installer/WidgetHubSetup/app.manifest',
      r'(assemblyIdentity version=")[^"]*(")', r'\g<1>' + ver4 + r'\g<2>')
print('   版本号已统一更新为', ver)
PY

echo "==> 2/6 构建管理器"
"$DOTNET" publish WidgetHub/WidgetHub.csproj -c Release -o WidgetHub/publish48 -v quiet --nologo

echo "==> 3/6 构建卸载器"
"$DOTNET" publish installer/WidgetHubUninstall/WidgetHubUninstall.csproj -c Release -o installer/WidgetHubUninstall/out -v quiet --nologo

echo "==> 4/6 打包负载并构建安装程序"
PROGRAMS_DIR="$LOCALAPPDATA/Programs"
[ -d "$PROGRAMS_DIR" ] || PROGRAMS_DIR="$HOME/AppData/Local/Programs"
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
BODY=$(cat << EOF
桌面挂件合集 WidgetHub $TAG

* 13 个桌面挂件 + 统一托盘图标
* 目标电脑无需安装任何 .NET 运行时
* 安装包：$ASSET_NAME（约 $(du -m "WidgetHubSetup-$TAG.exe" | cut -f1)MB）

下载 $ASSET_NAME 后双击即可安装；已安装的用户可在管理器里点右上角版本号 ->「检查更新」一键升级。
EOF
)
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
