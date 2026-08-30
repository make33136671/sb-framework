#!/bin/bash
# ============================================================================
# SB Framework 通用 .NET 构建脚本 (sb-build.sh)
#
# 用途：赛博体系所有 .NET 项目的标准化构建入口。
#       处理版本号、License、产物清理、打包、直接部署等通用场景。
#
# AI 友好设计：
#   - 所有参数都有默认值，无参数运行时按最佳实践自动完成
#   - 每个参数都有详细注释解释用途和适用场景
#   - 输出格式结构化，便于后续工具解析
#   - 核心逻辑集中在 3 个函数：build() / pack() / deploy()
# ============================================================================
set -eo pipefail

# ============================================================================
# 1. 默认配置
#    修改这些默认值可以适配团队规范，无需改调用方
# ============================================================================

# 默认版本号前缀（major.minor.patch）
DEFAULT_VERSION_MAJOR=1
DEFAULT_VERSION_MINOR=0
DEFAULT_VERSION_PATCH=0

# 默认清空输出目录？false = 增量模式（保留已有文件）
DEFAULT_CLEAN_DIST=true

# 默认生成部署脚本？true = publish 后自动生成 deploy.sh + nginx.conf + app.service
DEFAULT_GEN_DEPLOY=true

# 默认跳过 License 校验？true = 本地开发常用，false = 正式发布（需要环境变量 SB_LICENSE_KEY_DIR）
DEFAULT_NO_LICENSE=true

# 默认删除 appsettings.json？true = 防覆盖线上配置
DEFAULT_REMOVE_APPSETTINGS=true

# 默认删除 wwwroot？true = wwwroot 由运行环境挂载
DEFAULT_REMOVE_WWWROOT=true

# 默认打 zip 包？true = 方便分发
DEFAULT_ZIP=true

# 默认输出目录（相对于 --project 所在目录）
DEFAULT_OUTPUT_SUBDIR="dist"

# 保留文件列表（即使匹配删除模式也保留，逗号分隔）
DEFAULT_KEEP_PATTERNS="installed.lock"

# ============================================================================
# 2. 参数解析
# ============================================================================

PROJECT=""
VERSION=""
OUTPUT_DIR=""
NAME=""
CLEAN_DIST="$DEFAULT_CLEAN_DIST"
NO_LICENSE="$DEFAULT_NO_LICENSE"
REMOVE_APPSETTINGS="$DEFAULT_REMOVE_APPSETTINGS"
REMOVE_WWWROOT="$DEFAULT_REMOVE_WWWROOT"
ZIP="$DEFAULT_ZIP"
DEPLOY_DIR=""         # 非空时启用 --deploy 模式
RESTART=""            # 非空时启用 --restart
RESTART_CMD=""        # 自定义重启命令
RID=""                # 目标运行时标识（如 linux-x64, osx-x64）
DELETE_PATTERNS=""    # 额外删除文件模式（逗号分隔）
KEEP_PATTERNS="$DEFAULT_KEEP_PATTERNS"
MERGE_DIRS=()         # 构建后合并的目录（可重复使用）
GEN_DEPLOY="$DEFAULT_GEN_DEPLOY"  # 生成部署脚本
DEPLOY_PORT=""        # 部署端口（传给 sb-deploy-gen）
DEPLOY_DOMAIN=""      # 部署域名（传给 sb-deploy-gen）

show_help() {
    cat << 'HELP'
SB Framework 通用 .NET 构建脚本

用法:
  sb-build.sh                                          # 自动检测项目并构建（交互式）
  sb-build.sh --project <path> [options]               # 指定项目构建

必选参数:
  --project <path>    .csproj 文件路径，或项目目录（自动查找 *.csproj）

版本参数:
  --version <str>     版本号（默认 auto: <major>.<minor>.<patch>.<commit-count>）
                      例: --version 2.0.0

License 参数:
  --no-license        跳过 License 校验（默认启用，本地开发用）
  --with-license      注入 License 公钥（需要环境变量 SB_LICENSE_KEY_DIR）

产物输出参数:
  --output <dir>      产物输出目录（默认 <project-dir>/dist/）
                      例: --output /opt/dist
  --name <str>        zip 文件名和子目录名（默认取 csproj 的目录名）
  --no-zip            不打 zip 包（配合 --output 使用）
  --keep-dist         不清空输出目录（增量构建）

文件过滤参数:
  --keep-config       保留 appsettings.json（默认删除，防覆盖线上）
  --keep-wwwroot      保留 wwwroot（默认删除）
  --delete <pattern>  额外删除的文件 glob（逗号分隔）
                      例: --delete "*.txt,*.md"
  --keep <pattern>    保留文件 glob（逗号分隔，默认: installed.lock）
                      例: --keep "installed.lock,appsettings.Production.json"

运行时参数:
  --rid <id>          目标运行时标识
                      例: --rid linux-x64  --rid osx-x64  --rid win-x64

合并模式参数:
  --merge-dir <path>  构建后将指定目录内容复制到产物（可重复）
                      用于合并前端构建产物等
                      例: --merge-dir ../admin/apps/web-antd/dist

直接部署参数（服务器上用）:
  --deploy <path>     直接发布到运行目录（不打 zip，保留线上配置）
                      例: --deploy /opt/apps/pm/api
  --restart [cmd]     部署后重启服务（默认: systemctl restart <name>）
                      例: --restart                         # systemctl restart api
                           --restart "supervisorctl restart pm"

部署脚本生成参数:
  --gen-deploy        生成部署脚本（deploy.sh + nginx.conf + app.service）（默认启用）
  --no-gen-deploy     不生成部署脚本
  --deploy-port <num> 部署端口（默认 5000）
  --deploy-domain <str> 部署域名（如 example.com）

HELP
    exit 0
}

# 无参数或者 --help 时显示帮助
if [ $# -eq 0 ] || [ "$1" = "--help" ] || [ "$1" = "-h" ]; then
    show_help
fi

while [ $# -gt 0 ]; do
    case "$1" in
        --project)      PROJECT="$2"; shift 2 ;;
        --version)      VERSION="$2"; shift 2 ;;
        --output)       OUTPUT_DIR="$2"; shift 2 ;;
        --name)         NAME="$2"; shift 2 ;;
        --no-license)   NO_LICENSE=true; shift ;;
        --with-license) NO_LICENSE=false; shift ;;
        --no-zip)       ZIP=false; shift ;;
        --keep-dist)    CLEAN_DIST=false; shift ;;
        --keep-config)  REMOVE_APPSETTINGS=false; shift ;;
        --keep-wwwroot) REMOVE_WWWROOT=false; shift ;;
        --delete)       DELETE_PATTERNS="$2"; shift 2 ;;
        --keep)         KEEP_PATTERNS="$2"; shift 2 ;;
        --rid)          RID="$2"; shift 2 ;;
        --merge-dir)    MERGE_DIRS+=("$2"); shift 2 ;;
        --deploy)       DEPLOY_DIR="$2"; ZIP=false; shift 2 ;;
        --restart)      RESTART="${2:-true}"; shift 2 ;;
        --restart-cmd)  RESTART_CMD="$2"; shift 2 ;;
        --gen-deploy)   GEN_DEPLOY=true; shift ;;
        --no-gen-deploy) GEN_DEPLOY=false; shift ;;
        --deploy-port)  DEPLOY_PORT="$2"; shift 2 ;;
        --deploy-domain) DEPLOY_DOMAIN="$2"; shift 2 ;;
        *)              echo "未知参数: $1"; show_help ;;
    esac
done

# ============================================================================
# 3. 辅助函数
# ============================================================================

info()  { echo "[sb-build] INFO  $*"; }
warn()  { echo "[sb-build] WARN  $*" >&2; }
error() { echo "[sb-build] ERROR $*" >&2; exit 1; }

# 解析 --project 参数，定位 .csproj 文件和项目名称
resolve_project() {
    if [ -z "$PROJECT" ]; then
        error "请指定 --project 参数"
    fi

    if [ -f "$PROJECT" ]; then
        # 传的是 .csproj 文件
        CSPROJ="$PROJECT"
        PROJECT_DIR="$(cd "$(dirname "$CSPROJ")" && pwd)"
        # 取 csproj 所在目录的父目录名作为默认项目名（去掉 .WebApi .Service 后缀）
        if [ -z "$NAME" ]; then
            local parent_dir
            parent_dir="$(cd "$PROJECT_DIR/.." && pwd)"
            NAME=$(basename "$parent_dir")
        fi
    elif [ -d "$PROJECT" ]; then
        # 传的是目录，自动查找 .csproj
        PROJECT_DIR="$(cd "$PROJECT" && pwd)"
        CSPROJ=$(find "$PROJECT_DIR" -maxdepth 1 -name "*.csproj" | head -1)
        if [ -z "$CSPROJ" ]; then
            error "在 $PROJECT_DIR 中未找到 *.csproj 文件"
        fi
        # 默认项目名：取目录的父目录名
        if [ -z "$NAME" ]; then
            local parent_dir
            parent_dir="$(cd "$PROJECT_DIR/.." && pwd)"
            NAME=$(basename "$parent_dir")
        fi
    else
        error "项目路径不存在: $PROJECT"
    fi

    # 项目名：默认为目录名
    if [ -z "$NAME" ]; then
        NAME=$(basename "$PROJECT_DIR")
    fi

    info "项目文件: $CSPROJ"
    info "项目目录: $PROJECT_DIR"
    info "项目名称: $NAME"
}

# 生成版本号
# 规则：如果指定了 --version 就用指定的，否则 auto 生成
# auto 格式：<major>.<minor>.<patch>.<git-commit-count>
resolve_version() {
    if [ -n "$VERSION" ]; then
        info "版本号: $VERSION（指定）"
        return
    fi

    local commit_count
    commit_count=$(cd "$PROJECT_DIR" && git rev-list --count HEAD 2>/dev/null || echo "0")
    VERSION="${DEFAULT_VERSION_MAJOR}.${DEFAULT_VERSION_MINOR}.${DEFAULT_VERSION_PATCH}.${commit_count}"
    info "版本号: $VERSION（自动: git commit $commit_count 个）"
}

# 确定输出目录
# 三种模式：
#   1. --deploy <path> → 直接输出到运行目录
#   2. --output <path> → 输出到指定目录
#   3. 默认 → <project-dir>/dist/
resolve_output() {
    if [ -n "$DEPLOY_DIR" ]; then
        OUTPUT_DIR="$DEPLOY_DIR"
        info "输出模式: 直接部署 → $OUTPUT_DIR"
    elif [ -n "$OUTPUT_DIR" ]; then
        # 相对路径转为绝对路径
        if [[ "$OUTPUT_DIR" != /* ]]; then
            OUTPUT_DIR="$(cd "$PROJECT_DIR" && realpath -m "$OUTPUT_DIR" 2>/dev/null || echo "$PROJECT_DIR/$OUTPUT_DIR")"
        fi
        info "输出模式: 指定目录 → $OUTPUT_DIR"
    else
        OUTPUT_DIR="$PROJECT_DIR/$DEFAULT_OUTPUT_SUBDIR/$NAME"
        info "输出模式: 默认目录 → $OUTPUT_DIR"
    fi

    mkdir -p "$OUTPUT_DIR"

    if [ "$CLEAN_DIST" = true ] && [ -z "$DEPLOY_DIR" ]; then
        # 清空但不删目录本身
        info "清空输出目录..."
        rm -rf "${OUTPUT_DIR:?}"/*
    fi
}

# 构建 .NET 项目
# 核心：dotnet publish，传入版本号和 License 开关
build() {
    info "开始构建: $CSPROJ"

    local publish_args=(
        "$CSPROJ"
        -c Release
        -o "$OUTPUT_DIR"
        -p:IncludeSourceRevisionInInformationalVersion=false
        -p:Version="$VERSION"
        --nologo
        -v minimal
    )

    # License 开关
    if [ "$NO_LICENSE" = true ]; then
        publish_args+=(-p:DisableLicense=true)
        info "License: 跳过（--no-license）"
    else
        info "License: 注入公钥（需要 SB_LICENSE_KEY_DIR 环境变量）"
        # 框架内部通过 SB_LICENSE_KEY_DIR 环境变量定位密钥文件
    fi

    # 目标运行时
    if [ -n "$RID" ]; then
        publish_args+=(-r "$RID" --self-contained false)
        info "目标运行时: $RID"
    fi

    dotnet publish "${publish_args[@]}"

    info "构建完成: $(du -sh "$OUTPUT_DIR" | cut -f1)"
}

# 清理产物中的运行时配置文件
# 原则：只删运行环境会挂载/覆盖的文件，保留 installed.lock
clean_artifacts() {
    info "清理产物..."

    # 构建保留文件列表（逗号分隔 → 空格分隔）
    local keep_list=()
    IFS=',' read -ra keep_list <<< "$KEEP_PATTERNS"

    # 删除 appsettings*.json（除非在保留列表里）
    if [ "$REMOVE_APPSETTINGS" = true ]; then
        shopt -s nullglob
        for f in "$OUTPUT_DIR"/appsettings*.json; do
            shopt -u nullglob
            local basename_f=$(basename "$f")
            local should_keep=false
            for k in "${keep_list[@]}"; do
                if [[ "$basename_f" == $k ]]; then
                    should_keep=true
                    break
                fi
            done
            if [ "$should_keep" = false ]; then
                rm -f "$f"
                info "  删除: appsettings ($basename_f)"
            fi
        done
        shopt -u nullglob
    fi

    # 删除 wwwroot（除非保留）
    if [ "$REMOVE_WWWROOT" = true ]; then
        local should_keep=false
        for k in "${keep_list[@]}"; do
            if [ "$k" = "wwwroot" ]; then
                should_keep=true
                break
            fi
        done
        if [ "$should_keep" = false ]; then
            rm -rf "${OUTPUT_DIR:?}"/wwwroot
            info "  删除: wwwroot"
        fi
    fi

    # 额外删除模式
    if [ -n "$DELETE_PATTERNS" ]; then
        IFS=',' read -ra extra_deletes <<< "$DELETE_PATTERNS"
        for pattern in "${extra_deletes[@]}"; do
            pattern_trimmed=$(echo "$pattern" | xargs)
            for f in "$OUTPUT_DIR"/$pattern_trimmed; do
                if [ -e "$f" ]; then
                    local basename_f=$(basename "$f")
                    local should_keep=false
                    for k in "${keep_list[@]}"; do
                        if [[ "$basename_f" == $k ]]; then
                            should_keep=true
                            break
                        fi
                    done
                    if [ "$should_keep" = false ]; then
                        rm -f "$f"
                        info "  删除(额外): $basename_f"
                    fi
                fi
            done
        done
    fi
}

# 合并额外目录到产物
# 用于合并前端构建产物等
merge_dirs() {
    if [ ${#MERGE_DIRS[@]} -eq 0 ]; then
        return
    fi

    info "合并额外目录到产物..."
    for dir in "${MERGE_DIRS[@]}"; do
        if [ -d "$dir" ]; then
            cp -r "$dir"/* "$OUTPUT_DIR/"
            info "  合并: $dir → $OUTPUT_DIR"
        else
            warn "合并目录不存在: $dir"
        fi
    done
}

# 打 zip 包
# 输出到 OUTPUT_DIR 的父目录
pack() {
    if [ "$ZIP" != true ]; then
        info "跳过 zip 打包（--no-zip）"
        return
    fi

    local zip_dir
    zip_dir=$(dirname "$OUTPUT_DIR")
    local zip_file="$zip_dir/${NAME}.zip"

    info "打包: $zip_file"
    rm -f "$zip_file"
    cd "$OUTPUT_DIR"
    zip -qr "$zip_file" .
    cd "$OLDPWD"

    info "打包完成: $(du -sh "$zip_file" | cut -f1)"
}

# 生成部署脚本（deploy.sh + nginx.conf + app.service）
generate_deploy() {
    if [ "$GEN_DEPLOY" != true ]; then
        info "跳过部署脚本生成（--no-gen-deploy）"
        return
    fi

    info "生成部署脚本..."

    local SCRIPT_DIR
    SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
    local GEN_SCRIPT="$SCRIPT_DIR/sb-deploy-gen.sh"

    if [ ! -f "$GEN_SCRIPT" ]; then
        warn "部署脚本生成器不存在: $GEN_SCRIPT，跳过"
        return
    fi

    local gen_args=(
        --output "$OUTPUT_DIR"
        --name "$NAME"
    )

    if [ -n "$DEPLOY_PORT" ]; then
        gen_args+=(--port "$DEPLOY_PORT")
    fi

    if [ -n "$DEPLOY_DOMAIN" ]; then
        gen_args+=(--domain "$DEPLOY_DOMAIN")
    fi

    bash "$GEN_SCRIPT" "${gen_args[@]}"

    # 如果打 zip，确保 deploy 目录也包含在 zip 内
    if [ "$ZIP" = true ]; then
        info "部署脚本已包含在 zip 中"
    fi
}

# 部署后重启服务
# --deploy 模式下使用
do_restart() {
    if [ -z "$DEPLOY_DIR" ]; then
        return
    fi
    if [ "$RESTART" = "" ]; then
        return
    fi

    if [ -n "$RESTART_CMD" ]; then
        info "执行重启命令: $RESTART_CMD"
        eval "$RESTART_CMD"
    else
        # 默认：尝试 systemctl restart <name>
        local svc_name="$NAME"
        # 去掉 .WebApi .Service 等后缀，更符合 systemd 命名习惯
        svc_name=$(echo "$svc_name" | sed 's/\.WebApi$//i; s/\.Service$//i; s/\.Api$//i' | tr '[:upper:]' '[:lower:]')
        info "重启服务: $svc_name"
        systemctl restart "$svc_name" 2>/dev/null || \
            supervisorctl restart "$svc_name" 2>/dev/null || \
            warn "无法自动重启 $svc_name，请手动重启"
    fi
}

# ============================================================================
# 4. 报告输出
#    结构化输出，便于 AI 或 CI 工具消费
# ============================================================================
report() {
    echo ""
    echo "============================================"
    echo "  构建报告"
    echo "============================================"
    echo "  项目:     $NAME"
    echo "  版本:     $VERSION"
    echo "  产物:     $OUTPUT_DIR"
    if [ "$ZIP" = true ]; then
        echo "  zip:      $(dirname "$OUTPUT_DIR")/${NAME}.zip"
    fi
    echo "  License:  $([ "$NO_LICENSE" = true ] && echo '跳过' || echo '注入')"
    if [ "$GEN_DEPLOY" = true ]; then
        echo "  部署脚本: $OUTPUT_DIR/deploy/"
    fi
    if [ -n "$RID" ]; then
        echo "  运行时:   $RID"
    fi
    if [ -n "$DEPLOY_DIR" ]; then
        echo "  部署到:   $DEPLOY_DIR"
        echo "  重启:     $([ -n "$RESTART" ] && echo '是' || echo '否')"
    fi
    echo "  产物大小: $(du -sh "$OUTPUT_DIR" 2>/dev/null | cut -f1)"
    echo "============================================"
}

# ============================================================================
# 5. 主流程
# ============================================================================

resolve_project
resolve_version
resolve_output
build
clean_artifacts
merge_dirs
generate_deploy
pack
do_restart
report
