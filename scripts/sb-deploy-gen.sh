#!/bin/bash
# ============================================================================
# SB Framework 部署脚本生成器 (sb-deploy-gen.sh)
#
# 用途：dotnet publish 后生成全套部署脚本，服务器端 sudo ./deploy.sh 一路回车完成：
#   - .NET 检测/安装
#   - 宝塔 API 创建网站（API + Admin + H5）
#   - 宝塔 API 创建数据库（自动生成用户名密码）
#   - Nginx 反代配置
#   - Storage 软连接
#   - systemd 注册 + 启动
#   - 输出安装向导地址
#
# 生成文件（放在 <publish-dir>/deploy/）：
#   - deploy.sh          服务器端交互式部署
#   - deploy.conf        部署配置（本地预填 + SKIP 标记）
#   - app.service        systemd 模板（参考）
#   - README.md          说明
# ============================================================================
set -eo pipefail

OUTPUT_DIR=""
NAME=""
PORT="5000"
DOMAIN=""
APP_DIR="/www/wwwroot"
RUN_AS="root"

show_help() {
    cat << 'HELP'
SB Framework 部署脚本生成器

用法:
  sb-deploy-gen.sh --output <dir> --name <name> [options]

必选参数:
  --output <dir>    publish 产物目录
  --name <str>      产品英文名

可选参数:
  --port <num>      应用监听端口（默认 5000）
  --domain <str>    主域名（如 example.com）
  --app-dir <path>  部署根目录（默认 /www/wwwroot）
  --run-as <user>   运行用户（默认 root）
HELP
    exit 0
}

while [ $# -gt 0 ]; do
    case "$1" in
        --output)  OUTPUT_DIR="$2"; shift 2 ;;
        --name)    NAME="$2"; shift 2 ;;
        --port)    PORT="$2"; shift 2 ;;
        --domain)  DOMAIN="$2"; shift 2 ;;
        --app-dir) APP_DIR="$2"; shift 2 ;;
        --run-as)  RUN_AS="$2"; shift 2 ;;
        --help|-h) show_help ;;
        *) echo "未知参数: $1"; show_help ;;
    esac
done

[ -z "$OUTPUT_DIR" ] && { echo "ERROR: --output 必填"; exit 1; }
[ -z "$NAME" ] && { echo "ERROR: --name 必填"; exit 1; }

APP_PATH="${APP_DIR}/${NAME}_api"
ADMIN_PATH="${APP_DIR}/${NAME}_admin"
H5_PATH="${APP_DIR}/${NAME}_h5"
DB_NAME="${NAME//[^a-zA-Z0-9]/_}_db"
DB_USER="${NAME//[^a-zA-Z0-9]/_}_user"
DEPLOY_DIR="${OUTPUT_DIR}/deploy"

info()  { echo "[deploy-gen] $*"; }
mkdir -p "$DEPLOY_DIR"

# ============================================================================
# 1. deploy.conf — 本地预填默认值 + SKIP 标记
# ============================================================================
cat > "$DEPLOY_DIR/deploy.conf" << EOF
# ============================================================================
# SB Framework 部署配置文件
#
# SKIP_xxx=true  → 服务端直接使用默认值，不再询问
# SKIP_xxx=false → 服务端二次确认（可修改默认值）
# 全部设为 true 即可实现无人值守部署
# ============================================================================

# --- 宝塔面板 ---
# 配置宝塔 API 后，deploy.sh 可通过 API 自动创建网站和数据库
# 宝塔后台可视化管理，推荐配置
#
# 获取步骤：
#   1. 登录宝塔面板 → 左侧菜单"设置" → "API接口"
#   2. 开启"API接口"开关
#   3. 复制"接口密钥"（api_sk）填入下方 BT_API_SK
#   4. 在"IP白名单"中添加服务器本机 IP（或 127.0.0.1）
#   5. 保存
#
# 注意：
#   - 面板地址默认 http://127.0.0.1:8888，如修改过端口请同步修改
#   - IP 白名单必须包含发起请求的 IP，否则 API 会返回 403
#   - 未配置 BT_API_SK 时自动回退到直接写 Nginx 配置文件模式
BT_PANEL_URL="http://127.0.0.1:8888"
BT_API_SK=""
SKIP_BT=false

# --- 应用基本信息 ---
APP_NAME="${NAME}"
APP_PORT=${PORT}
APP_PATH="${APP_PATH}"
ADMIN_PATH="${ADMIN_PATH}"
H5_PATH="${H5_PATH}"
RUN_AS="${RUN_AS}"
SKIP_APP=true

# --- 域名 ---
# 主域名，子域名自动派生：api.${DOMAIN} / admin.${DOMAIN} / h5.${DOMAIN}
DOMAIN="${DOMAIN}"
API_DOMAIN="api.${DOMAIN}"
ADMIN_DOMAIN="admin.${DOMAIN}"
H5_DOMAIN="h5.${DOMAIN}"
SKIP_DOMAIN=true

# --- 数据库 ---
DB_NAME="${DB_NAME}"
DB_USER="${DB_USER}"
DB_PASSWORD=""
# 留空则自动生成随机密码
# 字符集（utf8mb4 推荐）
DB_CHARSET="utf8mb4"
# 访问权限：localhost 仅本地，% 任意 IP
DB_ACCESS="localhost"
SKIP_DB=false

# --- .NET 运行时 ---
AUTO_INSTALL_DOTNET=true
DOTNET_CHANNEL="10.0"
SKIP_DOTNET=false

# --- systemd ---
ENABLE_ON_BOOT=true
SKIP_SYSTEMD=true

# --- Storage 软连接 ---
GEN_SYMLINKS=true
SKIP_SYMLINKS=false

# --- Nginx ---
# 宝塔模式下由 API 创建网站自动生成 Nginx 配置
# 非 宝塔模式下直接写配置文件
NGINX_CONF_DIR="/www/server/panel/vhost/nginx"
SKIP_NGINX=false
EOF

# ============================================================================
# 2. deploy.sh — 服务器端交互式部署脚本
# ============================================================================
cat > "$DEPLOY_DIR/deploy.sh" << 'DEPLOY_SH'
#!/bin/bash
# ============================================================================
# SB Framework 服务器端交互式部署脚本
#
# 理想流程：新服务器 → 装好宝塔 → 域名解析好 → 上传包 → sudo ./deploy.sh → 一路回车
#
# 自动完成：
#   1. .NET 检测/安装
#   2. 宝塔 API 创建网站（API + Admin + H5）
#   3. 宝塔 API 创建数据库（自动生成用户名密码）
#   4. 复制文件
#   5. Nginx 反代配置（API 站点）
#   6. Storage 软连接
#   7. systemd 注册 + 启动
#   8. 输出安装向导地址
# ============================================================================
set -eo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
CONF_FILE="$SCRIPT_DIR/deploy.conf"
SOURCE_DIR="$(dirname "$SCRIPT_DIR")"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

info()  { echo -e "${GREEN}[deploy]${NC} $*"; }
warn()  { echo -e "${YELLOW}[deploy]${NC} $*"; }
error() { echo -e "${RED}[deploy]${NC} $*" >&2; exit 1; }

ask_value() {
    local prompt="$1" default="$2" answer
    read -rp "$(echo -e "${CYAN}${prompt}${NC} [${default}]: ")" answer
    echo "${answer:-$default}"
}

ask_yes() {
    local prompt="$1" default="$2" hint default_show answer
    if [ "$default" = "y" ] || [ "$default" = "true" ]; then
        hint="Y/n"; default_show="y"
    else
        hint="y/N"; default_show="n"
    fi
    read -rp "$(echo -e "${CYAN}${prompt}${NC} [${hint}]: ")" answer
    answer="${answer:-$default_show}"
    case "$(echo "$answer" | tr '[:upper:]' '[:lower:]')" in
        y|yes|true) echo "true" ;;
        *) echo "false" ;;
    esac
}

ask_if() {
    local skip_var="$1" prompt="$2" default="$3" skip_val
    eval "skip_val=\${$skip_var:-false}"
    if [ "$skip_val" = "true" ]; then
        echo "$default"
    else
        ask_value "$prompt" "$default"
    fi
}

ask_yes_if() {
    local skip_var="$1" prompt="$2" default="$3" skip_val
    eval "skip_val=\${$skip_var:-false}"
    if [ "$skip_val" = "true" ]; then
        echo "$default"
    else
        ask_yes "$prompt" "$default"
    fi
}

# 生成随机密码（字母+数字+特殊字符，16位）
gen_password() {
    local chars='abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%'
    local pass=""
    for i in $(seq 1 16); do
        pass="${pass}${chars:$((RANDOM % ${#chars})):1}"
    done
    echo "$pass"
}

# ============================================================================
# 宝塔 API 调用
# ============================================================================
bt_api() {
    local action="$1"; shift
    local panel_url="${BT_PANEL_URL%/}"
    local api_sk="$BT_API_SK"

    if [ -z "$api_sk" ]; then
        return 1
    fi

    local request_time
    request_time=$(date +%s)
    local sk_md5
    sk_md5=$(echo -n "$api_sk" | md5sum | awk '{print $1}')
    local request_token
    request_token=$(echo -n "${request_time}${sk_md5}" | md5sum | awk '{print $1}')

    local data="request_time=${request_time}&request_token=${request_token}"
    local arg
    for arg in "$@"; do
        data="${data}&${arg}"
    done

    local resp
    resp=$(curl -sf -X POST "${panel_url}${action}" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        -d "$data" 2>/dev/null) || return 1

    echo "$resp"
}

# 检查宝塔 API 是否可用
bt_check() {
    if [ -z "$BT_API_SK" ]; then
        echo "no_sk"
        return
    fi

    local resp
    resp=$(bt_api "/system" "action=GetAllInfo") || {
        echo "api_fail"
        return
    }

    if echo "$resp" | grep -q '"version"'; then
        echo "ok"
    else
        echo "api_fail"
    fi
}

# 通过宝塔 API 创建网站
bt_add_site() {
    local domain="$1" path="$2" ps="$3"
    local webname="{\"domain\":\"${domain}\",\"domainlist\":[],\"count\":0}"

    local resp
    resp=$(bt_api "/site" \
        "action=AddSite" \
        "webname=${webname}" \
        "path=${path}" \
        "type=" \
        "version=00" \
        "port=80" \
        "ps=${ps}") || return 1

    if echo "$resp" | grep -q '"siteStatus":true'; then
        return 0
    else
        return 1
    fi
}

# 通过宝塔 API 创建数据库
bt_add_database() {
    local name="$1" user="$2" pass="$3" charset="$4" access="$5"

    local resp
    resp=$(bt_api "/database" \
        "action=AddDatabase" \
        "sid=0" \
        "name=${name}" \
        "db_user=${user}" \
        "password=${pass}" \
        "address=${access}" \
        "codeing=${charset}" \
        "ps=${name}") || return 1

    if echo "$resp" | grep -q '"status":true'; then
        return 0
    else
        return 1
    fi
}

# 直接写 Nginx 配置文件（非宝塔模式回退）
write_nginx_conf() {
    local domain="$1" port="$2" app_path="$3" conf_name="$4"
    local conf_file="${NGINX_CONF_DIR}/${conf_name}.conf"

    mkdir -p "$NGINX_CONF_DIR"

    cat > "$conf_file" << NGXEOF
upstream ${conf_name}_backend {
    server 127.0.0.1:${port};
    keepalive 32;
}

server {
    listen 80;
    server_name ${domain};

    client_max_body_size 500m;

    location /storage/ {
        alias ${app_path}/wwwroot/storage/;
        expires 30d;
        access_log off;
    }

    location /setup/ {
        alias ${app_path}/wwwroot/setup/;
        try_files \$uri \$uri/ /setup/index.html;
    }

    location / {
        proxy_pass http://${conf_name}_backend;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_cache_bypass \$http_upgrade;
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
    }

    location /health {
        proxy_pass http://${conf_name}_backend/health;
        access_log off;
    }
}
NGXEOF

    if nginx -t 2>/dev/null; then
        nginx -s reload
        return 0
    else
        return 1
    fi
}

# 直接创建静态站点 Nginx 配置
write_static_nginx() {
    local domain="$1" root_path="$2" conf_name="$3"
    local conf_file="${NGINX_CONF_DIR}/${conf_name}.conf"

    mkdir -p "$NGINX_CONF_DIR"

    cat > "$conf_file" << NGXEOF
server {
    listen 80;
    server_name ${domain};
    root ${root_path};
    index index.html;

    location / {
        try_files \$uri \$uri/ /index.html;
    }

    location /storage/ {
        alias ${APP_PATH}/wwwroot/storage/;
        expires 30d;
        access_log off;
    }
}
NGXEOF
}

# ============================================================================
# 0. 加载配置
# ============================================================================
[ "$(id -u)" -ne 0 ] && error "请使用 root 用户或 sudo 执行"
[ ! -f "$CONF_FILE" ] && error "配置文件不存在: $CONF_FILE"
source "$CONF_FILE"

echo ""
echo -e "${BOLD}========================================${NC}"
echo -e "${BOLD}  SB Framework 服务器部署${NC}"
echo -e "${BOLD}========================================${NC}"
echo -e "  项目: ${CYAN}${APP_NAME}${NC}"
echo ""

# ============================================================================
# 1. 确认基本参数
# ============================================================================
echo -e "${BOLD}--- 1/8 部署参数确认 ---${NC}"

APP_NAME=$(ask_if "SKIP_APP" "应用名称" "$APP_NAME")
APP_PORT=$(ask_if "SKIP_APP" "监听端口" "$APP_PORT")
APP_PATH=$(ask_if "SKIP_APP" "API 部署目录" "$APP_PATH")
ADMIN_PATH=$(ask_if "SKIP_APP" "Admin 部署目录" "$ADMIN_PATH")
H5_PATH=$(ask_if "SKIP_APP" "H5 部署目录" "$H5_PATH")
RUN_AS=$(ask_if "SKIP_APP" "运行用户" "$RUN_AS")

DOMAIN=$(ask_if "SKIP_DOMAIN" "主域名" "$DOMAIN")
API_DOMAIN=$(ask_if "SKIP_DOMAIN" "API 子域名" "api.${DOMAIN}")
ADMIN_DOMAIN=$(ask_if "SKIP_DOMAIN" "Admin 子域名" "admin.${DOMAIN}")
H5_DOMAIN=$(ask_if "SKIP_DOMAIN" "H5 子域名" "h5.${DOMAIN}")

echo ""
echo -e "  应用名称:    ${CYAN}${APP_NAME}${NC}"
echo -e "  监听端口:    ${CYAN}${APP_PORT}${NC}"
echo -e "  API 目录:    ${CYAN}${APP_PATH}${NC}"
echo -e "  Admin 目录:  ${CYAN}${ADMIN_PATH}${NC}"
echo -e "  H5 目录:     ${CYAN}${H5_PATH}${NC}"
echo -e "  API 域名:    ${CYAN}${API_DOMAIN}${NC}"
echo -e "  Admin 域名:  ${CYAN}${ADMIN_DOMAIN}${NC}"
echo -e "  H5 域名:     ${CYAN}${H5_DOMAIN}${NC}"
echo ""

# ============================================================================
# 2. 宝塔 API 检测
# ============================================================================
echo -e "${BOLD}--- 2/8 宝塔面板检测 ---${NC}"

BT_STATUS=$(bt_check)

case "$BT_STATUS" in
    ok)
        info "宝塔 API 连接成功"
        USE_BT_API=true
        ;;
    no_sk)
        warn "未配置宝塔 API 密钥（BT_API_SK 为空）"
        USE_BT_API=false
        info "将使用直接写 Nginx 配置文件模式"
        info "提示：在宝塔后台 → 设置 → API接口 获取密钥后填入 deploy.conf 可启用宝塔模式"
        ;;
    api_fail)
        warn "宝塔 API 连接失败（密钥错误或 IP 未加白名单）"
        USE_BT_API=false
        info "回退到直接写 Nginx 配置文件模式"
        ;;
esac

# ============================================================================
# 3. .NET 运行时检测
# ============================================================================
echo ""
echo -e "${BOLD}--- 3/8 .NET 运行时检测 ---${NC}"

check_dotnet() {
    if ! command -v dotnet &>/dev/null; then
        echo "not_found"
        return
    fi
    local runtimes
    runtimes=$(dotnet --list-runtimes 2>/dev/null | grep "Microsoft.AspNetCore.App" | tail -1 | awk '{print $2}')
    if [ -n "$runtimes" ]; then
        info "检测到 ASP.NET Core 运行时: $runtimes"
        echo "$runtimes"
    else
        echo "no_runtime"
    fi
}

DOTNET_STATUS=$(check_dotnet)

case "$DOTNET_STATUS" in
    not_found)
        warn "服务器未安装 .NET"
        DO_INSTALL=$(ask_yes_if "SKIP_DOTNET" "是否自动安装 .NET $DOTNET_CHANNEL" "${AUTO_INSTALL_DOTNET}")
        if [ "$DO_INSTALL" = "true" ]; then
            info "安装 .NET $DOTNET_CHANNEL ..."
            curl -fsSL "https://dot.net/v1/dotnet-install.sh" -o /tmp/dotnet-install.sh
            chmod +x /tmp/dotnet-install.sh
            /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --runtime aspnetcore
            rm -f /tmp/dotnet-install.sh
            ln -sf "$HOME/.dotnet/dotnet" /usr/bin/dotnet
            if command -v dotnet &>/dev/null; then
                info ".NET 安装成功"
            else
                error ".NET 安装失败，请手动安装"
            fi
        else
            error "请手动安装 .NET 后重新执行"
        fi
        ;;
    no_runtime)
        warn "检测到 SDK 但缺少 ASP.NET Core 运行时"
        DO_INSTALL=$(ask_yes_if "SKIP_DOTNET" "是否自动安装 ASP.NET Core 运行时" "${AUTO_INSTALL_DOTNET}")
        if [ "$DO_INSTALL" = "true" ]; then
            info "安装 ASP.NET Core $DOTNET_CHANNEL 运行时..."
            curl -fsSL "https://dot.net/v1/dotnet-install.sh" -o /tmp/dotnet-install.sh
            chmod +x /tmp/dotnet-install.sh
            /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --runtime aspnetcore
            rm -f /tmp/dotnet-install.sh
            info "ASP.NET Core 运行时安装完成"
        else
            error "请手动安装 ASP.NET Core 运行时后重新执行"
        fi
        ;;
    *)
        info ".NET 运行时已就绪"
        ;;
esac

# ============================================================================
# 4. 复制文件
# ============================================================================
echo ""
echo -e "${BOLD}--- 4/8 复制文件 ---${NC}"

[ ! -d "$SOURCE_DIR" ] && error "源目录不存在: $SOURCE_DIR"

# API 文件
mkdir -p "$APP_PATH"
if [ "$(cd "$SOURCE_DIR" && pwd)" != "$(cd "$APP_PATH" && pwd)" ]; then
    info "复制 API 文件到 $APP_PATH"
    rsync -a --exclude='deploy' "$SOURCE_DIR/" "$APP_PATH/" 2>/dev/null || {
        cp -a "$SOURCE_DIR"/* "$APP_PATH/" 2>/dev/null || true
        cp -a "$SOURCE_DIR"/.* "$APP_PATH/" 2>/dev/null || true
    }
fi
chown -R "$RUN_AS":"$RUN_AS" "$APP_PATH"
info "API 文件复制完成"

# Admin 前端（如果产物中有 admin 目录）
if [ -d "$SOURCE_DIR/admin" ] || [ -d "$SOURCE_DIR/wwwroot/admin" ]; then
    local_admin_src="$SOURCE_DIR/admin"
    [ ! -d "$local_admin_src" ] && local_admin_src="$SOURCE_DIR/wwwroot/admin"
    mkdir -p "$ADMIN_PATH"
    cp -a "$local_admin_src"/* "$ADMIN_PATH/" 2>/dev/null || true
    chown -R "$RUN_AS":"$RUN_AS" "$ADMIN_PATH"
    info "Admin 前端文件复制完成"
else
    warn "未找到 Admin 前端产物，跳过（可后续手动上传）"
    mkdir -p "$ADMIN_PATH"
fi

# H5 前端（如果产物中有 h5 目录）
if [ -d "$SOURCE_DIR/h5" ] || [ -d "$SOURCE_DIR/wwwroot/h5" ]; then
    local_h5_src="$SOURCE_DIR/h5"
    [ ! -d "$local_h5_src" ] && local_h5_src="$SOURCE_DIR/wwwroot/h5"
    mkdir -p "$H5_PATH"
    cp -a "$local_h5_src"/* "$H5_PATH/" 2>/dev/null || true
    chown -R "$RUN_AS":"$RUN_AS" "$H5_PATH"
    info "H5 前端文件复制完成"
else
    warn "未找到 H5 前端产物，跳过（可后续手动上传）"
    mkdir -p "$H5_PATH"
fi

# ============================================================================
# 5. 创建网站 + 数据库
# ============================================================================
echo ""
echo -e "${BOLD}--- 5/8 创建网站与数据库 ---${NC}"

if [ "$USE_BT_API" = "true" ]; then
    # ---- 宝塔 API 模式 ----
    info "使用宝塔 API 创建网站"

    # 创建 API 站点
    if bt_add_site "$API_DOMAIN" "$APP_PATH" "${APP_NAME} API"; then
        info "API 站点已创建: $API_DOMAIN → $APP_PATH"
    else
        warn "API 站点创建失败（可能已存在），使用直接写 Nginx 配置"
        write_nginx_conf "$API_DOMAIN" "$APP_PORT" "$APP_PATH" "${APP_NAME}_api"
    fi

    # 创建 Admin 站点
    if bt_add_site "$ADMIN_DOMAIN" "$ADMIN_PATH" "${APP_NAME} Admin"; then
        info "Admin 站点已创建: $ADMIN_DOMAIN → $ADMIN_PATH"
    else
        warn "Admin 站点创建失败（可能已存在），跳过"
    fi

    # 创建 H5 站点
    if bt_add_site "$H5_DOMAIN" "$H5_PATH" "${APP_NAME} H5"; then
        info "H5 站点已创建: $H5_DOMAIN → $H5_PATH"
    else
        warn "H5 站点创建失败（可能已存在），跳过"
    fi

    # 重写 API 站点的 Nginx 配置为反代模式
    info "配置 API 站点 Nginx 反代..."
    API_CONF="${NGINX_CONF_DIR}/${API_DOMAIN}.conf"
    if [ -f "$API_CONF" ]; then
        # 宝塔创建的是静态站点配置，替换为反代配置
        cat > "$API_CONF" << NGXEOF
upstream ${APP_NAME}_backend {
    server 127.0.0.1:${APP_PORT};
    keepalive 32;
}

server {
    listen 80;
    server_name ${API_DOMAIN};
    root ${APP_PATH}/wwwroot;
    index index.html;

    client_max_body_size 500m;

    location /storage/ {
        alias ${APP_PATH}/wwwroot/storage/;
        expires 30d;
        access_log off;
    }

    location /setup/ {
        alias ${APP_PATH}/wwwroot/setup/;
        try_files \$uri \$uri/ /setup/index.html;
    }

    location / {
        proxy_pass http://${APP_NAME}_backend;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_cache_bypass \$http_upgrade;
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
    }

    location /health {
        proxy_pass http://${APP_NAME}_backend/health;
        access_log off;
    }
}
NGXEOF
        nginx -t 2>/dev/null && nginx -s reload
        info "API Nginx 反代配置已生效"
    fi

    # 在 Admin/H5 站点配置中追加 storage 软连接 location
    for conf_file in "${NGINX_CONF_DIR}/${ADMIN_DOMAIN}.conf" "${NGINX_CONF_DIR}/${H5_DOMAIN}.conf"; do
        if [ -f "$conf_file" ]; then
            # 检查是否已有 storage location
            if ! grep -q "location /storage/" "$conf_file"; then
                # 在最后一个 } 前插入 storage location
                sed -i "/^}/i\\
    location /storage/ {\\
        alias ${APP_PATH}/wwwroot/storage/;\\
        expires 30d;\\
        access_log off;\\
    }" "$conf_file"
            fi
        fi
    done
    nginx -t 2>/dev/null && nginx -s reload

else
    # ---- 直接写 Nginx 配置模式 ----
    info "直接写 Nginx 配置文件"
    mkdir -p "$NGINX_CONF_DIR"

    # API 反代
    write_nginx_conf "$API_DOMAIN" "$APP_PORT" "$APP_PATH" "${APP_NAME}_api"
    info "API Nginx 配置: $API_DOMAIN → :$APP_PORT"

    # Admin 静态
    write_static_nginx "$ADMIN_DOMAIN" "$ADMIN_PATH" "${APP_NAME}_admin"
    info "Admin Nginx 配置: $ADMIN_DOMAIN → $ADMIN_PATH"

    # H5 静态
    write_static_nginx "$H5_DOMAIN" "$H5_PATH" "${APP_NAME}_h5"
    info "H5 Nginx 配置: $H5_DOMAIN → $H5_PATH"

    nginx -t 2>/dev/null && nginx -s reload
fi

# ---- 数据库 ----
echo ""
echo -e "${BOLD}  数据库创建${NC}"

DB_NAME=$(ask_if "SKIP_DB" "数据库名" "$DB_NAME")
DB_USER=$(ask_if "SKIP_DB" "数据库用户名" "$DB_USER")

# 自动生成密码（如果为空）
if [ -z "$DB_PASSWORD" ]; then
    DB_PASSWORD=$(gen_password)
    info "已自动生成数据库密码"
else
    DB_PASSWORD=$(ask_if "SKIP_DB" "数据库密码" "$DB_PASSWORD")
fi

DB_CHARSET=$(ask_if "SKIP_DB" "字符集" "$DB_CHARSET")
DB_ACCESS=$(ask_if "SKIP_DB" "访问权限(localhost/%)" "$DB_ACCESS")

if [ "$USE_BT_API" = "true" ]; then
    info "通过宝塔 API 创建数据库..."
    if bt_add_database "$DB_NAME" "$DB_USER" "$DB_PASSWORD" "$DB_CHARSET" "$DB_ACCESS"; then
        info "数据库创建成功: $DB_NAME (用户: $DB_USER)"
    else
        warn "宝塔 API 创建数据库失败，尝试直接通过 MySQL 创建"
        # 回退：直接用 mysql 命令
        if command -v mysql &>/dev/null || [ -x /www/server/mysql/bin/mysql ]; then
            MYSQL_CMD="mysql"
            [ -x /www/server/mysql/bin/mysql ] && MYSQL_CMD="/www/server/mysql/bin/mysql"
            "$MYSQL_CMD" -uroot -e "CREATE DATABASE IF NOT EXISTS \`$DB_NAME\` CHARACTER SET $DB_CHARSET;" 2>/dev/null
            "$MYSQL_CMD" -uroot -e "CREATE USER IF NOT EXISTS '$DB_USER'@'$DB_ACCESS' IDENTIFIED BY '$DB_PASSWORD';" 2>/dev/null
            "$MYSQL_CMD" -uroot -e "GRANT ALL PRIVILEGES ON \`$DB_NAME\`.* TO '$DB_USER'@'$DB_ACCESS'; FLUSH PRIVILEGES;" 2>/dev/null
            info "数据库已通过 MySQL 命令创建: $DB_NAME"
        else
            warn "MySQL 不可用，请手动创建数据库"
        fi
    fi
else
    # 非 宝塔模式：直接用 mysql 命令
    if command -v mysql &>/dev/null || [ -x /www/server/mysql/bin/mysql ]; then
        MYSQL_CMD="mysql"
        [ -x /www/server/mysql/bin/mysql ] && MYSQL_CMD="/www/server/mysql/bin/mysql"
        info "通过 MySQL 命令创建数据库..."
        "$MYSQL_CMD" -uroot -e "CREATE DATABASE IF NOT EXISTS \`$DB_NAME\` CHARACTER SET $DB_CHARSET;" 2>/dev/null
        "$MYSQL_CMD" -uroot -e "CREATE USER IF NOT EXISTS '$DB_USER'@'$DB_ACCESS' IDENTIFIED BY '$DB_PASSWORD';" 2>/dev/null
        "$MYSQL_CMD" -uroot -e "GRANT ALL PRIVILEGES ON \`$DB_NAME\`.* TO '$DB_USER'@'$DB_ACCESS'; FLUSH PRIVILEGES;" 2>/dev/null
        info "数据库已创建: $DB_NAME (用户: $DB_USER)"
    else
        warn "MySQL 不可用，请手动创建数据库"
    fi
fi

# 保存数据库凭据到文件，供安装向导参考
cat > "$SCRIPT_DIR/db-credentials.txt" << DBCRED
# 数据库凭据（由 deploy.sh 自动创建）
# 安装向导中填写以下信息：
数据库类型: MySQL
服务器: 127.0.0.1
端口: 3306
数据库名: $DB_NAME
用户名: $DB_USER
密码: $DB_PASSWORD
字符集: $DB_CHARSET
DBCRED

info "数据库凭据已保存: deploy/db-credentials.txt"

# ============================================================================
# 6. Storage 软连接
# ============================================================================
echo ""
echo -e "${BOLD}--- 6/8 Storage 软连接 ---${NC}"

DO_SYMLINK=$(ask_yes_if "SKIP_SYMLINKS" "是否创建 storage 软连接" "${GEN_SYMLINKS}")

if [ "$DO_SYMLINK" = "true" ]; then
    STORAGE_PATH="${APP_PATH}/wwwroot/storage"
    mkdir -p "$STORAGE_PATH"

    for target_dir in "$ADMIN_PATH" "$H5_PATH"; do
        [ ! -d "$target_dir" ] && continue

        link_path="${target_dir}/storage"

        # 已存在且不是软连接则删除
        if [ -e "$link_path" ] && [ ! -L "$link_path" ]; then
            rm -rf "$link_path"
        elif [ -L "$link_path" ]; then
            rm -f "$link_path"
        fi

        ln -sfn "$STORAGE_PATH" "$link_path"
        info "软连接: ${link_path} → ${STORAGE_PATH}"
    done
else
    info "跳过软连接"
fi

# ============================================================================
# 7. systemd 注册 + 启动
# ============================================================================
echo ""
echo -e "${BOLD}--- 7/8 systemd 服务 ---${NC}"

SERVICE_NAME="${APP_NAME}_api"
SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"

# 检测入口 DLL
ACTUAL_DLL=$(find "$APP_PATH" -maxdepth 1 -name "*.dll" \
    -not -name "SB.Framework.*" \
    -not -name "Microsoft.*" \
    -not -name "System.*" \
    -not -name "FreeSql.*" \
    -not -name "Serilog.*" \
    -not -name "Swashbuckle.*" \
    -not -name "Newtonsoft.*" \
    -not -name "SixLabors.*" \
    -not -name "StackExchange.*" \
    -not -name "Pomelo.*" \
    -not -name "Npgsql.*" \
    -not -name "BCrypt.*" \
    -not -name "Dapper.*" \
    | head -1)

if [ -n "$ACTUAL_DLL" ]; then
    DLL_NAME=$(basename "$ACTUAL_DLL" .dll)
    info "入口 DLL: $DLL_NAME"
else
    DLL_NAME="$APP_NAME"
    warn "未检测到入口 DLL，使用应用名: $DLL_NAME"
fi

cat > "$SERVICE_FILE" << SVCEOF
[Unit]
Description=${APP_NAME} API Server
After=network.target

[Service]
Type=notify
WorkingDirectory=${APP_PATH}
ExecStart=/usr/bin/dotnet ${APP_PATH}/${DLL_NAME}.dll --urls=http://0.0.0.0:${APP_PORT}
Restart=always
RestartSec=3
User=${RUN_AS}
Group=${RUN_AS}
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:${APP_PORT}
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1

[Install]
WantedBy=multi-user.target
SVCEOF

systemctl daemon-reload

BOOT=$(ask_yes_if "SKIP_SYSTEMD" "是否设置开机自启" "${ENABLE_ON_BOOT}")
if [ "$BOOT" = "true" ]; then
    systemctl enable "$SERVICE_NAME"
    info "已设置开机自启"
fi

info "systemd 服务已注册: $SERVICE_NAME"

# 启动
if systemctl is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
    info "服务已在运行，执行重启"
    systemctl restart "$SERVICE_NAME"
else
    systemctl start "$SERVICE_NAME"
fi

# 等待启动
i=0
while [ $i -lt 15 ]; do
    if curl -sf "http://127.0.0.1:${APP_PORT}/health" >/dev/null 2>&1 || \
       curl -sf "http://127.0.0.1:${APP_PORT}/api/setup/status" >/dev/null 2>&1; then
        info "服务已启动"
        break
    fi
    sleep 1
    i=$((i + 1))
done

if [ $i -ge 15 ]; then
    warn "健康检查未就绪（首次安装属正常）"
    systemctl is-active "$SERVICE_NAME" || warn "服务可能未正常启动: journalctl -u $SERVICE_NAME -f"
fi

# ============================================================================
# 8. 输出结果
# ============================================================================
echo ""
echo -e "${BOLD}========================================${NC}"
echo -e "${GREEN}  部署完成${NC}"
echo -e "${BOLD}========================================${NC}"
echo ""
echo -e "  ${BOLD}服务信息${NC}"
echo -e "    服务名称:   ${CYAN}${SERVICE_NAME}${NC}"
echo -e "    部署目录:   ${CYAN}${APP_PATH}${NC}"
echo -e "    监听端口:   ${CYAN}${APP_PORT}${NC}"
echo ""
echo -e "  ${BOLD}访问地址${NC}"
echo -e "    API:    ${CYAN}http://${API_DOMAIN}${NC}"
echo -e "    Admin:  ${CYAN}http://${ADMIN_DOMAIN}${NC}"
echo -e "    H5:     ${CYAN}http://${H5_DOMAIN}${NC}"
echo ""
echo -e "  ${BOLD}数据库信息${NC}"
echo -e "    类型:     MySQL"
echo -e "    主机:     127.0.0.1:3306"
echo -e "    库名:     ${CYAN}${DB_NAME}${NC}"
echo -e "    用户名:   ${CYAN}${DB_USER}${NC}"
echo -e "    密码:     ${CYAN}${DB_PASSWORD}${NC}"
echo ""
echo -e "  ${BOLD}常用命令${NC}"
echo -e "    查看状态:   systemctl status ${SERVICE_NAME}"
echo -e "    查看日志:   journalctl -u ${SERVICE_NAME} -f"
echo -e "    重启服务:   systemctl restart ${SERVICE_NAME}"
echo ""

if [ ! -f "$APP_PATH/installed.lock" ]; then
    echo -e "${YELLOW}  ========================================${NC}"
    echo -e "${YELLOW}  系统尚未安装${NC}"
    echo -e "${YELLOW}  ========================================${NC}"
    echo -e "  请访问安装向导完成初始化："
    echo -e ""
    echo -e "    ${BOLD}http://${API_DOMAIN}/setup/index.html${NC}"
    echo -e ""
    echo -e "  数据库信息已保存到: ${CYAN}deploy/db-credentials.txt${NC}"
    echo -e "  安装向导中填入上述数据库信息即可"
    echo -e "${YELLOW}  ========================================${NC}"
fi
echo ""
echo -e "${BOLD}========================================${NC}"
DEPLOY_SH

chmod +x "$DEPLOY_DIR/deploy.sh"

# ============================================================================
# 3. app.service — systemd 模板（参考）
# ============================================================================
cat > "$DEPLOY_DIR/app.service" << EOF
# ${NAME}_api.service — systemd 服务模板（参考）
# deploy.sh 会自动生成并注册

[Unit]
Description=${NAME} API Server
After=network.target

[Service]
Type=notify
WorkingDirectory=${APP_PATH}
ExecStart=/usr/bin/dotnet ${APP_PATH}/${NAME}.dll --urls=http://0.0.0.0:${PORT}
Restart=always
RestartSec=3
User=${RUN_AS}
Group=${RUN_AS}
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:${PORT}
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1

[Install]
WantedBy=multi-user.target
EOF

# ============================================================================
# 4. README
# ============================================================================
cat > "$DEPLOY_DIR/README.md" << 'README'
# 部署说明

## 理想流程

```
新服务器 → 安装宝塔 → 域名解析 → 上传包 → sudo ./deploy.sh → 一路回车 → 进入安装向导
```

## 快速部署

1. 上传 zip 包到服务器并解压
2. 进入 `deploy/` 目录
3. 修改 `deploy.conf`（填入宝塔 API 密钥，其余参数已预填）
4. 执行 `sudo ./deploy.sh`

## deploy.sh 自动完成

| 步骤 | 说明 |
|---|---|
| .NET 检测 | 检测运行时，未安装则自动安装 |
| 宝塔 API 创建网站 | 创建 API + Admin + H5 三个站点 |
| 宝塔 API 创建数据库 | 自动创建数据库 + 用户 + 密码 |
| Nginx 反代 | API 站点配置反向代理到本地端口 |
| Storage 软连接 | Admin/H5 → API 的 storage 目录 |
| systemd 服务 | 注册 + 开机自启 + 启动 |
| 安装向导 | 输出地址，用户访问完成数据库初始化 |

## deploy.conf 配置

### 宝塔 API（推荐配置）

配置后 deploy.sh 可通过宝塔 API 自动创建网站和数据库，宝塔后台可视化管理。

**获取步骤：**

1. 登录宝塔面板 → 左侧菜单"**设置**" → "**API接口**"
2. 开启"**API接口**"开关
3. 复制"**接口密钥**"（api_sk）
4. 在"**IP白名单**"中添加服务器本机 IP（或 `127.0.0.1`）
5. 点击"**保存**"
6. 将密钥填入 `deploy.conf` 的 `BT_API_SK` 字段

**常见问题：**

| 问题 | 原因 | 解决 |
|---|---|---|
| API 返回 403 | IP 不在白名单 | 添加 `127.0.0.1` 和服务器公网 IP |
| API 返回 401 | 密钥错误 | 重新复制 api_sk |
| 连接超时 | 面板端口不对 | 检查 `BT_PANEL_URL` 端口号 |
| 面板改过端口 | 非默认 8888 | 修改 `BT_PANEL_URL` 为实际端口 |

未配置时自动回退到直接写 Nginx 配置文件模式（功能相同，只是宝塔面板里看不到网站）。

### SKIP 标记

| 标记 | 默认 | 说明 |
|---|---|---|
| SKIP_APP | true | 应用名称/端口/目录 |
| SKIP_DOMAIN | true | 域名配置 |
| SKIP_DB | false | 数据库创建（服务端确认） |
| SKIP_DOTNET | false | .NET 安装（服务端确认） |
| SKIP_SYSTEMD | true | systemd 注册 |
| SKIP_NGINX | false | Nginx 配置 |
| SKIP_SYMLINKS | false | Storage 软连接 |

全设为 `true` = 无人值守静默部署。

## 文件说明

| 文件 | 用途 |
|---|---|
| deploy.sh | 交互式部署脚本 |
| deploy.conf | 配置文件（修改此文件） |
| app.service | systemd 模板（参考） |
| db-credentials.txt | 部署后生成，数据库凭据 |
README

info "部署脚本已生成: $DEPLOY_DIR/"
info "  deploy.sh          交互式部署脚本（宝塔API + 直接模式双支持）"
info "  deploy.conf        配置文件（含 SKIP 标记）"
info "  app.service        systemd 模板"
info "  README.md          说明文档"
