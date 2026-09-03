#!/bin/bash
# ============================================================================
# SB Framework NuGet 打包 + 推送脚本 (pack-push.sh)
#
# 用途：把框架各子项目编译成 NuGet 包，推到 BaGet 私有源。
#       这是"框架的编译"——派生项目(pm/admin 等)通过 NuGet 引用消费,改完框架必须走这步才能让
#       派生项目 restore 到新版本。
#
# 铁律(来自 CHANGELOG [0.4.3] 事故):
#   每次发包必须 **删 bin/obj 干净重建**。否则包版本 bump 了但内部程序集版本没变,
#   派生项目运行时 FileNotFoundException 崩溃。本脚本默认强制清理,不要关。
#
# 典型用法:
#   ./pack-push.sh --project SB.Framework.WebApi --bump          # 只发 WebApi,patch +1 后推送
#   ./pack-push.sh --bump                                        # 所有可打包项目 patch +1 后推送
#   ./pack-push.sh --project SB.Framework.Core --version 0.5.0   # 指定版本号
#   ./pack-push.sh --project SB.Framework.WebApi --no-push       # 只打包不推送(本地验证)
# ============================================================================
set -eo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# ---- 默认配置 ----
# 可打包项目清单(短名,对应 <name>/<name>.csproj)。DevHost 是宿主 app,不打包,故不在此列。
ALL_PROJECTS=(
  SB.Framework.Models
  SB.Framework.Core
  SB.Framework.Services
  SB.Framework.WebApi
  SB.Framework.Analyzers
  SB.Feature.AccessToken
  SB.Feature.BackupReceive
)

# BaGet 源:默认走公网 HTTPS(nuget.saibo.club)。内网直连后端用 --source-http。
SOURCE_HTTPS="https://nuget.saibo.club/v3/index.json"
SOURCE_HTTP="http://192.168.10.68/v3/index.json"   # 内网后端(HTTP,需 allowInsecureConnections)
# API key 优先取环境变量 SB_BAGET_API_KEY,否则用已知默认(见 docs/AI_BaGet_Feed.md)
API_KEY="${SB_BAGET_API_KEY:-d77f86fba8cd73cee8e4bcb3938a9fb3}"

NUPKG_OUT="$ROOT/dist/nupkgs"

# ---- 参数 ----
PROJECTS=()
BUMP=false
VERSION=""
PUSH=true
USE_HTTP=false

show_help() {
  sed -n '2,28p' "$0" | sed 's/^# \{0,1\}//'
  exit 0
}

while [ $# -gt 0 ]; do
  case "$1" in
    --project)     PROJECTS+=("$2"); shift 2 ;;
    --bump)        BUMP=true; shift ;;
    --version)     VERSION="$2"; shift 2 ;;
    --no-push)     PUSH=false; shift ;;
    --source-http) USE_HTTP=true; shift ;;
    --api-key)     API_KEY="$2"; shift 2 ;;
    -h|--help)     show_help ;;
    *) echo "未知参数: $1"; exit 1 ;;
  esac
done

# 未指定 --project 则打全部
if [ ${#PROJECTS[@]} -eq 0 ]; then
  PROJECTS=("${ALL_PROJECTS[@]}")
fi

info()  { echo "[pack-push] $*"; }
error() { echo "[pack-push] ERROR $*" >&2; exit 1; }

# 取/改 csproj 的 <Version>
get_version() { grep -oE '<Version>[^<]+</Version>' "$1" | head -1 | sed -E 's/<\/?Version>//g'; }
set_version() {
  local csproj="$1" newver="$2"
  # 仅替换第一个 <Version>...</Version>(项目版本,不碰依赖里的 Version 属性——那些是 PackageReference 的 attribute,不是元素)
  perl -0pi -e 's{<Version>[^<]+</Version>}{<Version>'"$newver"'</Version>}' "$csproj"
}
bump_patch() {
  # 末段 +1: 0.4.8 -> 0.4.9 ; 0.4.8.12 -> 0.4.8.13
  local v="$1"; local head="${v%.*}"; local last="${v##*.}"
  echo "${head}.$((last + 1))"
}

command -v dotnet >/dev/null || error "未找到 dotnet"
command -v perl   >/dev/null || error "未找到 perl(用于改版本号)"

rm -rf "$NUPKG_OUT"
mkdir -p "$NUPKG_OUT"

echo "============================================"
echo "  SB Framework 打包"
echo "  项目: ${PROJECTS[*]}"
echo "  推送: $([ "$PUSH" = true ] && echo "是 → $([ "$USE_HTTP" = true ] && echo "$SOURCE_HTTP" || echo "$SOURCE_HTTPS")" || echo "否(--no-push)")"
echo "============================================"

declare -a PACKED_SUMMARY

for name in "${PROJECTS[@]}"; do
  csproj="$ROOT/$name/$name.csproj"
  [ -f "$csproj" ] || error "找不到项目: $csproj"

  # 版本号处理
  cur=$(get_version "$csproj")
  if [ -n "$VERSION" ]; then
    target_ver="$VERSION"
  elif [ "$BUMP" = true ]; then
    target_ver=$(bump_patch "$cur")
  else
    target_ver="$cur"
  fi
  if [ "$target_ver" != "$cur" ]; then
    set_version "$csproj" "$target_ver"
    info "$name 版本: $cur → $target_ver"
  else
    info "$name 版本: $target_ver(不变)"
  fi

  # 用 dotnet build(项目设了 GeneratePackageOnBuild=true,构建即出包)而非 dotnet pack:
  # WebApi 是 Web SDK 项目,清掉 obj 后 dotnet pack 会因 staticwebassets 清单缺失报错。
  # 铁律:构建前删 bin/obj 干净重建,确保包版本与内部程序集版本一致。
  info "$name 构建出包..."
  rm -rf "$ROOT/$name/bin" "$ROOT/$name/obj"
  dotnet build "$csproj" -c Release --nologo -v minimal -p:PackageOutputPath="$NUPKG_OUT"

  PACKED_SUMMARY+=("$name@$target_ver")
done

echo ""
info "已生成 nupkg:"
ls -1 "$NUPKG_OUT"/*.nupkg | sed 's/^/  /'

# ---- 推送 ----
if [ "$PUSH" = true ]; then
  if [ "$USE_HTTP" = true ]; then
    SOURCE_URL="$SOURCE_HTTP"
    PUSH_CONFIG="$(mktemp /tmp/baget-push.XXXXXX.config)"
    cat > "$PUSH_CONFIG" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="baget" value="$SOURCE_HTTP" allowInsecureConnections="true" />
  </packageSources>
</configuration>
XML
    CONFIG_ARGS=(--source baget --configfile "$PUSH_CONFIG")
  else
    SOURCE_URL="$SOURCE_HTTPS"
    CONFIG_ARGS=(--source "$SOURCE_HTTPS")
  fi

  echo ""
  info "推送到 $SOURCE_URL ..."
  for pkg in "$NUPKG_OUT"/*.nupkg; do
    info "push $(basename "$pkg")"
    dotnet nuget push "$pkg" "${CONFIG_ARGS[@]}" --api-key "$API_KEY" --skip-duplicate
  done
  [ -n "${PUSH_CONFIG:-}" ] && rm -f "$PUSH_CONFIG"
fi

echo ""
echo "============================================"
echo "  完成"
for s in "${PACKED_SUMMARY[@]}"; do echo "  ✓ $s"; done
echo "  nupkg 目录: $NUPKG_OUT"
[ "$PUSH" = true ] && echo "  已推送至: $SOURCE_URL" || echo "  未推送(--no-push)"
echo "============================================"
echo ""
echo "派生项目升级:把对应 PackageReference 的 Version 改成上面的新版本号,然后 dotnet restore。"
