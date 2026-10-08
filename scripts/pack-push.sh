#!/bin/bash
# ============================================================================
# SB Framework NuGet 打包 + 推送脚本 (pack-push.sh)
#
# 用途：把框架各子项目编译成 NuGet 包并推送。
#       这是"框架的编译"——派生项目通过 NuGet 引用消费，改完框架必须走这步。
#
# 两个推送目标：
#   --source nuget  （**默认**）公网 nuget.org —— 正式发布地，任何人可 restore
#   --source baget            saibo 私有 BaGet —— 内网缓存/加速
#
# ⚠️ 定位不同，别再混为一谈：
#   · nuget.org 是**唯一发布源**（唯一真相），包只从这里"出厂"；
#   · BaGet 是**消费侧的缓存代理**，从 nuget.org 同步，不作为独立发布目标
#     （双写会带来版本分叉：本地 0.5.5 / 公网 0.5.4，外部使用者 restore 失败）。
#
# 铁律(来自 CHANGELOG [0.4.3] 事故):
#   每次发包必须 **删 bin/obj 干净重建**。否则包版本 bump 了但内部程序集版本没变,
#   派生项目运行时 FileNotFoundException 崩溃。本脚本默认强制清理,不要关。
#
# 典型用法:
#   ./pack-push.sh                                                # 全部包发到 nuget.org
#     （key 从 ~/.config/sb-framework/nuget-credentials 自动读取，无需手输）
#   ./pack-push.sh --project SB.Framework.WebApi --bump           # 只发 WebApi,patch +1
#   ./pack-push.sh --source baget                                 # 发到私有 BaGet
#   ./pack-push.sh --project SB.Framework.WebApi --no-push        # 只打包不推送(本地验证)
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

# 推送目标（2026-10-08 新增 nuget.org）
#
#   baget  —— saibo 私有 BaGet（内网缓存/加速）。默认公网 HTTPS，--source-http 走内网直连。
#   nuget  —— 公网 nuget.org（正式发布地，任何人可 restore）。
#
# ⚠️ 两者定位不同，别再混为一谈：
#   · nuget.org 是**唯一发布源**（唯一真相）——包只从这里"出厂"；
#   · BaGet 是**缓存代理**（消费侧的加速与兜底），按新架构它从 nuget.org 同步，
#     不再作为独立发布目标（双写会带来版本分叉风险）。
#   故 --source 默认从 baget 改为 **nuget**。
SOURCE_BAGET_HTTPS="https://nuget.saibo.club/v3/index.json"
SOURCE_BAGET_HTTP="http://192.168.10.68/v3/index.json"   # 内网后端(HTTP,需 allowInsecureConnections)
SOURCE_NUGET="https://api.nuget.org/v3/index.json"

# API key 读取顺序（2026-10-08）：
#   ① 环境变量（CI 里用这个，或临时覆盖）
#   ② 本地凭据文件 ~/.config/sb-framework/nuget-credentials（日常免手输）
#   ③ BaGet 的历史默认值（仅 BaGet 有；nuget.org **没有**内置默认值）
#
# ## 为什么用"凭据文件"而不是写进 ~/.bashrc
#
#   · 环境变量会对**所有子进程**可见（包括不该看到它的构建脚本）；
#   · 写进 shell 配置会被 dotfiles 备份、被 `env` 打印、易随终端会话泄露；
#   · 独立文件能单独设 600 权限、单独轮换，且**不进 git**（下面有意不放进仓库）。
#
# ⚠️ NuGet 自己的 `setapikey` 是**明文**存在 NuGet.Config 里，并不更安全；
#    本项目选独立文件，是为了"既能免输、又能一眼看出它存在哪"。
CRED_FILE="${SB_NUGET_CREDENTIALS:-$HOME/.config/sb-framework/nuget-credentials}"

# 从凭据文件读一个 KEY（文件不存在/无该键 → 输出空，不报错）
read_cred() {
  local key="$1"
  [ -f "$CRED_FILE" ] || return 0
  # 只取形如 KEY=VALUE 的行；忽略注释与空行；值两边的引号去掉
  sed -n "s/^[[:space:]]*${key}[[:space:]]*=[[:space:]]*//p" "$CRED_FILE" \
    | head -1 | sed -E 's/^"(.*)"$/\1/; s/^'"'"'(.*)'"'"'$/\1/' | tr -d '\r\n'
}

# 凭据文件权限检查：里面是明文密钥，权限必须收紧（组/其他可读就打警告）
if [ -f "$CRED_FILE" ]; then
  PERM=$(stat -c '%a' "$CRED_FILE" 2>/dev/null || stat -f '%Lp' "$CRED_FILE" 2>/dev/null || echo "")
  case "$PERM" in
    ""|600|400) ;;
    *) echo "[pack-push] WARN 凭据文件权限过宽（$PERM）：$CRED_FILE" >&2
       echo "[pack-push]      建议执行：chmod 600 $CRED_FILE" >&2 ;;
  esac
fi

BAGET_API_KEY="${SB_BAGET_API_KEY:-$(read_cred SB_BAGET_API_KEY)}"
[ -n "$BAGET_API_KEY" ] || BAGET_API_KEY="d77f86fba8cd73cee8e4bcb3938a9fb3"   # 历史默认值
NUGET_API_KEY="${NUGET_ORG_API_KEY:-$(read_cred NUGET_ORG_API_KEY)}"

NUPKG_OUT="$ROOT/dist/nupkgs"

# ---- 参数 ----
PROJECTS=()
BUMP=false
VERSION=""
PUSH=true
USE_HTTP=false
TARGET="nuget"      # baget | nuget（默认发公网）

show_help() {
  sed -n '2,30p' "$0" | sed 's/^# \{0,1\}//'
  exit 0
}

while [ $# -gt 0 ]; do
  case "$1" in
    --project)     PROJECTS+=("$2"); shift 2 ;;
    --bump)        BUMP=true; shift ;;
    --version)     VERSION="$2"; shift 2 ;;
    --no-push)     PUSH=false; shift ;;
    --source)      TARGET="$2"; shift 2 ;;
    --source-http) USE_HTTP=true; shift ;;
    --api-key)     BAGET_API_KEY="$2"; shift 2 ;;
    -h|--help)     show_help ;;
    *) echo "未知参数: $1"; exit 1 ;;
  esac
done

# 未指定 --project 则打全部
if [ ${#PROJECTS[@]} -eq 0 ]; then
  PROJECTS=("${ALL_PROJECTS[@]}")
fi

info()  { echo "[pack-push] $*"; }
warn()  { echo "[pack-push] WARN $*" >&2; }
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

# ---- 校验推送目标与凭据（在做任何构建之前就失败，别等打完包才发现没 key）----
case "$TARGET" in
  baget|nuget) ;;
  *) error "--source 只能是 baget 或 nuget（收到：$TARGET）" ;;
esac

if [ "$PUSH" = true ]; then
  if [ "$TARGET" = "nuget" ] && [ -z "$NUGET_API_KEY" ]; then
    error "发布到 nuget.org 需要 API Key，但环境变量 NUGET_ORG_API_KEY 未设置。
       申请：https://www.nuget.org/account/apikeys （Scope 选 Push new packages and package versions）
       用法：NUGET_ORG_API_KEY=xxx ./scripts/pack-push.sh --source nuget"
  fi
fi

rm -rf "$NUPKG_OUT"
mkdir -p "$NUPKG_OUT"

if [ "$TARGET" = "nuget" ]; then
  TARGET_DESC="$SOURCE_NUGET"
else
  TARGET_DESC="$([ "$USE_HTTP" = true ] && echo "$SOURCE_BAGET_HTTP" || echo "$SOURCE_BAGET_HTTPS")"
fi

echo "============================================"
echo "  SB Framework 打包"
echo "  项目: ${PROJECTS[*]}"
echo "  目标: $TARGET"
echo "  推送: $([ "$PUSH" = true ] && echo "是 → $TARGET_DESC" || echo "否(--no-push)")"
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
  if [ "$TARGET" = "nuget" ]; then
    # 公网 nuget.org：直接用官方源，无需自定义 config
    SOURCE_URL="$SOURCE_NUGET"
    CONFIG_ARGS=(--source "$SOURCE_NUGET")
    PUSH_KEY="$NUGET_API_KEY"
  elif [ "$USE_HTTP" = true ]; then
    # 内网 BaGet 后端是纯 HTTP，必须 allowInsecureConnections（否则 dotnet 拒绝推送）
    SOURCE_URL="$SOURCE_BAGET_HTTP"
    PUSH_CONFIG="$(mktemp /tmp/baget-push.XXXXXX.config)"
    cat > "$PUSH_CONFIG" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="baget" value="$SOURCE_BAGET_HTTP" allowInsecureConnections="true" />
  </packageSources>
</configuration>
XML
    CONFIG_ARGS=(--source baget --configfile "$PUSH_CONFIG")
    PUSH_KEY="$BAGET_API_KEY"
  else
    SOURCE_URL="$SOURCE_BAGET_HTTPS"
    CONFIG_ARGS=(--source "$SOURCE_BAGET_HTTPS")
    PUSH_KEY="$BAGET_API_KEY"
  fi

  echo ""
  info "推送到 $SOURCE_URL ..."
  FAILED=0
  for pkg in "$NUPKG_OUT"/*.nupkg; do
    info "push $(basename "$pkg")"
    # 不用 --skip-duplicate：同版本重复推送**静默跳过**会让人误以为发布成功。
    # 让它报错、由人工确认是"确实已发布"还是"版本号忘了 bump"。
    if ! dotnet nuget push "$pkg" "${CONFIG_ARGS[@]}" --api-key "$PUSH_KEY"; then
      warn "  ✗ 推送失败：$(basename "$pkg")（版本已存在？还是凭据/网络问题？）"
      FAILED=$((FAILED + 1))
    fi
  done
  [ -n "${PUSH_CONFIG:-}" ] && rm -f "$PUSH_CONFIG"

  if [ "$FAILED" -gt 0 ]; then
    error "有 $FAILED 个包推送失败 —— 发布**未完成**。请逐个确认原因后重试。"
  fi
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
