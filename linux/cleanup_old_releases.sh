#!/bin/bash
# ============================================================
# 删除 GitHub 旧 release：只删旧 Linux 版本
# 保留：linux-v2.9.84（当前版）+ 所有 Windows 版本 + draft 草稿
# 判定规则：
#   - tag 为 linux-v* 且非当前版        → 删
#   - tag 无前缀但资产含 .deb（旧 Linux 版）→ 删
#   - 资产为 .exe / 无资产 / draft      → 一律保留
# 凭证：curl + PAT（与 upgrade_publish_2.9.84.sh 同一套方式，不依赖 gh CLI）
#   $GHPAT 环境变量 → gh auth token → 交互粘贴
# 网络：api.github.com 连接被重置时自动重试 3 次（大陆网络常见）
# ============================================================
set -u
GH=feibixunchang/desktop-widget-suite
KEEP=linux-v2.9.84
API="${CLEANUP_API:-https://api.github.com/repos/$GH}"

TMP=$(mktemp /tmp/_rels.XXXXXX)
BODY=$(mktemp /tmp/_api.XXXXXX)
trap 'rm -f "$TMP" "$TMP.tsv" "$BODY"' EXIT

# api_call <METHOD> <path>：响应体落 $BODY，HTTP 码落 $API_CODE；网络失败重试 3 次
API_CODE=""
api_call() {
  local method=$1 path=$2 i code
  for i in 1 2 3; do
    code=$(curl -sS -X "$method" -H "Authorization: Bearer $TOKEN" \
      -H "Accept: application/vnd.github+json" \
      "$API$path" --max-time 30 -o "$BODY" -w "%{http_code}" 2>/dev/null) || code=""
    API_CODE=$code
    if [ -n "$code" ] && [ "$code" != "000" ]; then return 0; fi
    echo "  (网络失败 http=$code，第 $i/3 次，3 秒后重试...)" >&2
    sleep 3
  done
  return 1
}

# ---------- 1. 获取 PAT ----------
TOKEN="${GHPAT:-}"
if [ -z "$TOKEN" ] && command -v gh >/dev/null 2>&1; then
  TOKEN=$(gh auth token 2>/dev/null || true)
  [ -n "$TOKEN" ] && echo "已从 gh CLI 获取凭证"
fi
if [ -z "$TOKEN" ]; then
  echo "请粘贴 GitHub PAT（需 repo 权限；输入不回显）:"
  read -rsp "PAT: " TOKEN; echo
fi
[ -n "$TOKEN" ] || { echo "✗ 无 PAT，退出"; exit 1; }

# ---------- 2. 校验 token ----------
api_call GET ""
echo "token 校验: http=$API_CODE"
if [ "$API_CODE" != "200" ]; then
  echo "✗ 无法连接 api.github.com，或 token 无效。"
  echo "  http=000(连接被重置) 一般是大陆网络/代理问题，不是 PAT 错；稍后重跑即可（脚本幂等）。"
  echo "  自检: curl -sS -o /dev/null -w '%{http_code}\\n' --max-time 15 https://api.github.com"
  exit 1
fi

# ---------- 3. 拉取全部 release ----------
api_call GET "/releases?per_page=100" || { echo "✗ 拉取 release 列表失败（网络持续不可达）"; exit 1; }
/usr/bin/python3 -c "
import json,sys
try:
    rels=json.load(open('$BODY'))
except Exception as e:
    sys.exit('parse: %s'%e)
for r in rels:
    if r.get('draft'): flag='draft'
    elif r.get('prerelease'): flag='prerelease'
    else: flag='stable'
    assets=','.join(a.get('name','') for a in r.get('assets',[]))
    print('%s\t%s\t%s\t%s\t%s'%(r['id'],r['tag_name'],r.get('name') or '',flag,assets))
" > "$TMP.tsv" || { echo "✗ 解析失败"; exit 1; }
[ -s "$TMP.tsv" ] || { echo "未获取到任何 release"; exit 1; }

echo; echo "== 当前全部 releases（id / tag / 名称 / 状态 / 资产）=="
column -t -s "$(printf '\t')" "$TMP.tsv" 2>/dev/null || cat "$TMP.tsv"

# ---------- 4. 筛选 ----------
DEL_IDS=(); DEL_TAGS=()
while IFS="$(printf '\t')" read -r rid tag name flag assets; do
  if [ "$flag" = "draft" ]; then
    echo "  保留(draft 未发布): $tag"; continue
  fi
  case "$tag" in
    linux-v*)
      if [ "$tag" = "$KEEP" ]; then echo "  保留(当前版): $tag"
      else DEL_IDS+=("$rid"); DEL_TAGS+=("$tag"); fi
      ;;
    *)
      case "$assets" in
        *.deb*) echo "  待删(无前缀旧 Linux 版, 资产=.deb): $tag"; DEL_IDS+=("$rid"); DEL_TAGS+=("$tag") ;;
        *)      echo "  保留(Windows/其他): $tag" ;;
      esac
      ;;
  esac
done < "$TMP.tsv"

if [ ${#DEL_IDS[@]} -eq 0 ]; then
  echo; echo "没有需要删除的旧 Linux 版本"; exit 0
fi

echo; echo "== 以下旧版本将被删除（release + git tag）=="
printf '  %s\n' "${DEL_TAGS[@]}"
read -r -p "确认删除? [y/N] " ans
[[ "$ans" =~ ^[Yy]$ ]] || { echo "已取消，未做任何修改"; exit 1; }

# ---------- 5. 执行删除 ----------
for i in "${!DEL_IDS[@]}"; do
  rid=${DEL_IDS[$i]}; tag=${DEL_TAGS[$i]}
  api_call DELETE "/releases/$rid"; c1=$API_CODE
  api_call DELETE "/git/refs/tags/$tag"; c2=$API_CODE
  echo "  删除 $tag: release=$c1 tag=$c2（204=成功，404=已不存在）"
done

echo; echo "== 删除后剩余 releases =="
api_call GET "/releases?per_page=100" && /usr/bin/python3 -c "
import json
for r in json.load(open('$BODY')):
    print('  %-20s %s'%(r['tag_name'], r.get('name') or ''))
"
echo "完成。Windows 版本与 draft 草稿未做任何改动。"
