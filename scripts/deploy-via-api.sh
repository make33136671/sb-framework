#!/bin/bash
# ============================================================================
# 通过 SB 框架内置 API 远程更新部署 (deploy-via-api.sh)
#
# 用途："如何通过 api 来更新部署"——不用 ssh/rsync,直接把构建好的 zip 产物 POST 给目标机
#       已运行的 SB 框架,框架原子替换二进制并优雅重启。所有 sb 应用(api/admin/h5)通用。
#
# 两步流程(框架 AppController):
#   1. POST {url}/App/Upload   multipart: target + file(zip)   → 返回 data.jobId
#   2. POST {url}/App/Deploy   form: uploadId + keepConfig + clearCache → 解压 + 重启
#
# 认证:Authorization: Bearer <sk_ 访问令牌>。该 key 由目标机签发,需 FullAccess(部署接口
#       未声明 Scope,只有全权 key 或密码 JWT 放行)。
#
# 配置保留:keepConfig=true(默认)时,目标机根目录的 appsettings.json / appsettings.*.json /
#          installed.lock 不会被产物覆盖(框架 IsProtectedConfigFile 兜底)。
#
# 典型用法:
#   ./deploy-via-api.sh --url https://pm.saibo.club --token sk_xxx --zip ../../apps/pm/dist/api.zip
#   ./deploy-via-api.sh --url https://pm.saibo.club --token sk_xxx --zip api.zip --clear-cache --wait
# ============================================================================
set -eo pipefail

URL=""
TOKEN="${SB_DEPLOY_TOKEN:-}"
ZIP=""
TARGET="api"
KEEP_CONFIG=true
CLEAR_CACHE=false
WAIT=false

show_help() { sed -n '2,28p' "$0" | sed 's/^# \{0,1\}//'; exit 0; }

while [ $# -gt 0 ]; do
  case "$1" in
    --url)          URL="${2%/}"; shift 2 ;;
    --token)        TOKEN="$2"; shift 2 ;;
    --zip)          ZIP="$2"; shift 2 ;;
    --target)       TARGET="$2"; shift 2 ;;
    --no-keep-config) KEEP_CONFIG=false; shift ;;
    --clear-cache)  CLEAR_CACHE=true; shift ;;
    --wait)         WAIT=true; shift ;;
    -h|--help)      show_help ;;
    *) echo "未知参数: $1"; exit 1 ;;
  esac
done

error() { echo "[deploy-api] ERROR $*" >&2; exit 1; }
info()  { echo "[deploy-api] $*"; }

[ -n "$URL" ]   || error "缺少 --url(目标机基址,如 https://pm.saibo.club)"
[ -n "$TOKEN" ] || error "缺少 --token(sk_ 访问令牌;或设环境变量 SB_DEPLOY_TOKEN)"
[ -n "$ZIP" ]   || error "缺少 --zip(构建产物 zip 路径)"
[ -f "$ZIP" ]   || error "产物不存在: $ZIP"
command -v curl >/dev/null || error "未找到 curl"

# 解析 JSON 字段(支持点号路径,如 data.jobId):优先 python3,无则退化到 grep
json_get() {
  local json="$1" key="$2"
  if command -v python3 >/dev/null 2>&1; then
    printf '%s' "$json" | KEY="$key" python3 -c '
import sys, json, os
try:
    o = json.load(sys.stdin)
except Exception:
    sys.exit(0)
for p in os.environ["KEY"].split("."):
    if isinstance(o, dict) and p in o:
        o = o[p]
    else:
        sys.exit(0)
print("" if o is None else o)' 2>/dev/null
    return
  fi
  # fallback(无 python3):按叶子键名提取 "key": value / "key":"value"
  local leaf="${key##*.}"
  printf '%s' "$json" | grep -oE "\"$leaf\"[[:space:]]*:[[:space:]]*\"?[^,\"}]*\"?" | head -1 | sed -E "s/.*:[[:space:]]*\"?//; s/\"$//"
}

echo "============================================"
echo "  远程部署(BuiltInApi)"
echo "  目标:     $URL"
echo "  target:   $TARGET"
echo "  产物:     $ZIP ($(du -h "$ZIP" | cut -f1))"
echo "  保留配置: $KEEP_CONFIG   清缓存: $CLEAR_CACHE"
echo "============================================"

# ---- 1. 上传 ----
info "上传产物 → $URL/App/Upload"
UP_RESP=$(curl -sS --fail-with-body -X POST "$URL/App/Upload" \
  -H "Authorization: Bearer $TOKEN" \
  -F "target=$TARGET" \
  -F "file=@$ZIP;type=application/zip") || error "上传请求失败: $UP_RESP"

UP_CODE=$(json_get "$UP_RESP" "code")
[ "$UP_CODE" = "0" ] || error "上传被拒: $(json_get "$UP_RESP" "message") (原始: $UP_RESP)"

JOB_ID=$(json_get "$UP_RESP" "data.jobId")
[ -n "$JOB_ID" ] || JOB_ID=$(json_get "$UP_RESP" "jobId")
[ -n "$JOB_ID" ] || error "上传成功但未解析到 jobId(原始: $UP_RESP)"
info "上传成功,jobId=$JOB_ID"

# ---- 2. 部署(触发重启)----
info "触发部署 → $URL/App/Deploy (uploadId=$JOB_ID)"
# Deploy 成功后服务端会优雅重启;响应通常先于停机返回,但连接被重置也属正常,按成功对待。
DEP_RESP=$(curl -sS -X POST "$URL/App/Deploy" \
  -H "Authorization: Bearer $TOKEN" \
  -F "uploadId=$JOB_ID" \
  -F "keepConfig=$KEEP_CONFIG" \
  -F "clearCache=$CLEAR_CACHE" \
  --max-time 60 2>/dev/null) || {
    info "部署请求连接中断——通常是服务端已开始重启,视为已触发。"
    DEP_RESP=""
  }

if [ -n "$DEP_RESP" ]; then
  DEP_CODE=$(json_get "$DEP_RESP" "code")
  if [ "$DEP_CODE" = "0" ]; then
    info "部署已触发: $(json_get "$DEP_RESP" "message")"
  else
    error "部署失败: $(json_get "$DEP_RESP" "message") (原始: $DEP_RESP)"
  fi
fi

# ---- 3. 可选:等待服务重新就绪 ----
if [ "$WAIT" = true ]; then
  info "等待服务重启就绪(轮询 /App/Info,最多 90s)..."
  ok=false
  for i in $(seq 1 30); do
    sleep 3
    INFO=$(curl -sS --max-time 5 "$URL/App/Info" -H "Authorization: Bearer $TOKEN" 2>/dev/null || true)
    if [ -n "$INFO" ] && [ "$(json_get "$INFO" "code")" = "0" ]; then
      ver=$(json_get "$INFO" "data.version")
      info "服务已就绪 ✓  版本: ${ver:-未知}"
      ok=true; break
    fi
    echo -n "."
  done
  echo ""
  [ "$ok" = true ] || error "等待超时:服务未在 90s 内恢复,请到目标机查日志。"
fi

echo ""
echo "============================================"
echo "  ✓ 部署完成 (jobId=$JOB_ID)"
echo "  回滚:目标机 admin → 部署作业列表,对上一条 Deployed 记录回滚;"
echo "       或 POST /App/Deploy 传旧 jobId 重新部署。"
echo "============================================"
