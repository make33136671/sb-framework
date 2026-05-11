#!/bin/bash
# 本地开发用：仿 systemd Restart=always
# 进程退出后 2s 内自动拉起。用于：
#  - 安装向导跑完 Install 后框架主动 StopApplication 优雅退出，本地没守护进程
#  - 调试中改完 dll 想"重启拿新版"，kill 当前进程即可让脚本拉起新的
#
# 自动检测：同目录下 *WebApi / *DevHost / *Server 后缀的 csproj 子目录
# 或显式传 binary 路径 / csproj 名作第一个参数：
#     bash dev-restart-on-exit.sh                       # 自动检测
#     bash dev-restart-on-exit.sh Sb.Apps.Pm.WebApi     # 指定 csproj 名
#     bash dev-restart-on-exit.sh /path/to/Foo.exe      # 直接传 binary
#
# 监听端口由 appsettings.json 的 "Urls" 字段决定（ASP.NET Core 默认行为），
# 脚本启动 binary 不传 --urls 覆盖，让配置文件成为端口的唯一来源。
# 如需临时改端口：编辑 appsettings.json 的 "Urls" 字段后 kill 进程即可（watcher 自动拉起）。
#
# 环境变量：LOG_FILE（默认 /tmp/<projname>.log）。
# 生产环境请用宝塔守护进程 / systemd unit，不要用这个脚本。

set -u

PROJ_ROOT="$(cd "$(dirname "$0")" && pwd)"

if [ "${1:-}" != "" ] && [ -x "$1" ]; then
    BIN="$1"
    WORK_DIR="$(dirname "$BIN")"
elif [ "${1:-}" != "" ]; then
    NAME="$1"
    WORK_DIR="$PROJ_ROOT/$NAME"
    BIN="$WORK_DIR/bin/Debug/net8.0/$NAME"
else
    CANDIDATES=$(find "$PROJ_ROOT" -maxdepth 2 -type d \( -name "*WebApi" -o -name "*DevHost" -o -name "*Server" \) 2>/dev/null | head -3)
    if [ -z "$CANDIDATES" ]; then
        echo "[守护] 未能自动检测到 WebApi/DevHost/Server csproj 目录" >&2
        echo "[守护] 显式传参：bash $(basename "$0") <csproj_name 或 binary 绝对路径>" >&2
        exit 1
    fi
    WORK_DIR=$(echo "$CANDIDATES" | head -1)
    NAME=$(basename "$WORK_DIR")
    BIN="$WORK_DIR/bin/Debug/net8.0/$NAME"
fi

PROJ_NAME=$(basename "$WORK_DIR")
LOG_FILE="${LOG_FILE:-/tmp/${PROJ_NAME}.log}"

trap 'echo "[守护] 收到 SIGTERM，停止"; exit 0' TERM INT

if [ ! -x "$BIN" ]; then
    echo "[守护] binary 不存在或不可执行：$BIN" >&2
    echo "[守护] 请先 dotnet build" >&2
    exit 1
fi

# cd 到 csproj 目录让 ASP.NET Core ContentRoot 落到 appsettings.json 同级，
# 否则 SetupService 读 appsettings 时会因路径错抛 "Could not find file"。
# 监听端口由该目录下 appsettings.json 的 "Urls" 字段决定。
cd "$WORK_DIR" || exit 1

echo "[守护] watcher 启动  binary=$BIN  cwd=$WORK_DIR  log=$LOG_FILE  port=由 appsettings.json Urls 决定" | tee -a "$LOG_FILE"

while true; do
    "$BIN" >> "$LOG_FILE" 2>&1
    EXIT=$?
    echo "[守护] API 进程退出 code=$EXIT，2 秒后重启" | tee -a "$LOG_FILE"
    sleep 2
done
