# Build & Deploy

> 本文档为**构建/部署事实源**，已按源码更新。若与旧文档冲突，以此为准。

## Requirements

- **.NET 10.0 SDK**（所有框架工程 `net10.0`；Analyzers `netstandard2.0`）
- MySQL 5.7+（或 PostgreSQL）
- Redis 6.0+（可选）
- Node.js >=20 / pnpm >=10（前端，见 sb-framework-admin）

## Port Allocation

项目预留范围：**6010 - 6019**

| 组件 | 端口 |
|------|------|
| 后端（DevHost） | 6010 |
| 前端（web-antd） | 6012 |

## 一、框架本身构建

### 编译整个解决方案

```bash
dotnet build sb-framework.sln
```

### 打包推 NuGet（`scripts/pack-push.sh`）

把框架子项目编译成 NuGet 包（单一包，无变体），推送到 BaGet 私有源。改完框架必须走这步，派生项目才能 restore 到新版本。

```bash
./scripts/pack-push.sh --bump                              # 所有可打包项目 patch+1 后推送
./scripts/pack-push.sh --project SB.Framework.WebApi --bump # 只发 WebApi
./scripts/pack-push.sh --project SB.Framework.Core --version 0.5.0
./scripts/pack-push.sh --project SB.Framework.WebApi --no-push  # 只打包不推送
```

- 可打包项目：Models / Core / Services / WebApi / Analyzers / Feature.AccessToken / Feature.BackupReceive（**DevHost 不打包**）。
- **铁律**（来自 CHANGELOG 0.4.3 事故）：每次发包**必须删 bin/obj 干净重建**，否则包版本 bump 但内部程序集版本未变，派生项目运行时 `FileNotFoundException` 崩溃。脚本默认强制清理。
- 源：默认公网 `https://nuget.saibo.club/v3/index.json`；内网用 `--source-http`。API key 优先取环境变量 `SB_BAGET_API_KEY`。

### 本地调试

```bash
cd SB.Framework.DevHost
dotnet run --urls=http://localhost:6010
curl http://localhost:6010/api/Setup/Status   # {"code":0,"data":{"installed":false}}
```

## 二、派生项目构建（`scripts/sb-build.sh`）

`sb-build.sh` 是赛博体系所有 .NET 项目的标准化构建入口（派生项目可直接复用）：

```bash
./sb-build.sh --project <csproj路径> [options]
# 常用：
./sb-build.sh --project src/App.WebApi                 # 默认: 清dist + 删config + 打zip
./sb-build.sh --project src/App.WebApi --version 2.0.0
./sb-build.sh --project src/App.WebApi --rid linux-x64
./sb-build.sh --project src/App.WebApi --deploy /opt/apps/pm/api --restart   # 直接部署
```

关键默认：清空 dist、删除 appsettings*.json 与 wwwroot（防覆盖线上）、保留 `installed.lock`、打 zip。

### 派生项目 SemVer 自动版本号（推荐）

在派生应用仓库根放 `build-publish.sh`，让 `/admin/App/Info` 返回真实版本：

```bash
PREFIX=${VERSION_PREFIX:-1.0}
PATCH=$(git rev-list --count HEAD 2>/dev/null || echo 0)
VERSION="$PREFIX.$PATCH"

dotnet publish <App.WebApi.csproj> -c Release -o dist \
  -p:Version=$VERSION \
  -p:IncludeSourceRevisionInInformationalVersion=false \
  --nologo -v minimal
```

参考实现：`sb-apps-pm/build-publish.sh`、`sb-apps-nexus/build-publish.sh`。

## 三、安装向导（首次运行）

`installed.lock` 不存在时进入安装模式：

1. 访问 `http://<host>:<port>/setup/index.html`
2. 配置：产品信息 → 数据库 → 缓存 → 部署 → 密码 → 管理员 → 确认
3. 安装 → 建表、种子数据、更新 `appsettings.json`、创建 `installed.lock`
4. **重启后生效**（Install 成功后延迟优雅重启）

**重装**：删除 `installed.lock` 后重启。

### Setup API（`/api/Setup/[action]`，AllowAnonymous）

| Action | Method | Body |
|--------|--------|------|
| Status | GET | — |
| TestDatabase | POST | FromBody `{ DbType, Server, Port, Database, User, Password }` |
| TestRedis | POST | FromBody `{ Connection, InstanceName }` |
| Install | POST | FromBody SetupRequest（见 AI_Models.md） |

## 四、部署

### Linux（systemd）

```bash
./scripts/sb-build.sh --project <App.WebApi.csproj> --rid linux-x64
# 上传 dist/ 到服务器，创建 systemd 服务：
```

```ini
[Unit]
Description=SB App API
After=network.target

[Service]
WorkingDirectory=/opt/apps/<app>/api
ExecStart=/usr/bin/dotnet <App>.WebApi.dll --urls=http://127.0.0.1:6010
Restart=always
RestartSec=10
User=www-data

[Install]
WantedBy=multi-user.target
```

### 应用内远程部署（`scripts/deploy-via-api.sh`）

不用 ssh/rsync，直接把 zip POST 给已运行的框架，框架原子替换 + 优雅重启：

```bash
# 两步流程（AppController）：Upload → Deploy
./deploy-via-api.sh --url https://pm.saibo.club --token sk_xxx --zip api.zip
./deploy-via-api.sh --url https://pm.saibo.club --token sk_xxx --zip api.zip --clear-cache --wait
```

- 认证：`Authorization: Bearer <sk_ 访问令牌>`。部署接口未声明 Scope，**需要 FullAccess key 或密码 JWT**。
- `keepConfig=true`（默认）时 `appsettings*.json` / `installed.lock` 不被覆盖。
- **冒烟预检**（零宕机拦坏包）：`AppController.Deploy` 先解压到 staging、以 `SB_SMOKE_TEST=1` 冒烟模式拉起新包轮询 `/health`（最长 45s），不通过直接拒绝部署。可用 `SB_DEPLOY_SMOKE=0` 关闭。
- 回滚：admin → 部署作业列表，对 Deployed 记录回滚；或 `POST /App/Deploy` 传旧 jobId。

> `[Obsolete]` 的单接口（`UploadApi` / `DeployApiByPath` / `UploadProject` / `DeployProjectByPath`）已废弃，请走 `Upload` + `Deploy` 两步流程。

### 数据库备份

- 统一入口 `DatabaseBackupService.BackupAndUploadAsync`（定时任务 / Admin 手动 / API `POST /api/Database/Backup`）。
- 需 `mysqldump` 在 PATH 中（`CheckBackup` 可检测）。
- 可选推送云端（`CloudConfig.ServerUrl` + BackupReceive 插件接收）。

## 五、日志

Serilog NDJSON（CLEF 格式，`Serilog.Formatting.Compact`）：

| 文件 | 级别 |
|------|------|
| `logs/error-{yyyyMMdd}.ndjson` | Error |
| `logs/info-{yyyyMMdd}.ndjson` | Info |
| `logs/warning-{yyyyMMdd}.ndjson` | Warning |

- 启动前旧日志自动归档到时间戳子目录。
- 字段：`@t`(UTC) / `@mt` / `@l` / `@x` / `SourceContext` / 自定义属性。
- Development 额外控制台输出。
- AI 诊断：`GET /admin/LogViewer/Diag` 返回 NDJSON 流（`date/level/tail/contains`）。

## 六、新功能工作流

### 1. 实体（派生项目 `Sb.Apps.<App>.Models`）

```csharp
[Table(Name = "sb_<app>_entity")]        // 派生表前缀 sb_<app>_
public class YourEntity : IEntity, ISoftDelete
{
    public int Id { get; set; }
    [Column(StringLength = 100)]
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}
```

### 2. 服务

```csharp
[AutoDependencyInjection]
public class YourEntityService : BaseCrudService<YourEntity>
{
    public YourEntityService(IFreeSql fsql, ILogger<YourEntityService> logger)
        : base(fsql, logger) { }
}
```

### 3. 控制器

```csharp
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class YourEntityController : ControllerBase
{
    private readonly YourEntityService _service;
    public YourEntityController(YourEntityService service) => _service = service;

    [HttpGet]
    public async Task<Result> List(int page = 1, int pageSize = 20) => ...;

    [HttpPost]
    public async Task<Result> Add([FromForm] YourEntity entity) => ...;  // admin 用 [FromForm]（SB0002）
}
```

### 4. 模块装配

实现 `IModuleInitializer`（`GetEntityTypes` / `GetMenus` / `GetSystemSettings` / `SeedData`），框架启动自动建表 + 同步菜单/设置/种子。

### 关键约定

- 英文命名；`[AutoDependencyInjection]` 自动注册（Scoped）。
- **禁止 `[FromBody]`**（SB0002，除非 `[AllowJsonBody]`）；**禁止 `DateTime.Now`**（SB0001，用 `AppTime.Now`）。
- JWT claims：`User.Id()` / `User.Username()` / `User.UserType()` / `User.Role()`。
- 敏感数据：`EncryptionUtil`（AES-256）。
- 表前缀：基座 `sb_`，派生 `sb_<app>_`。

## 七、冒烟模式（部署预检）

环境变量 `SB_SMOKE_TEST=1`：跳过 `SeedDatabase`（DB 迁移）与后台任务运行器，但保留 `IBackgroundTaskManager` 的 DI 注册（否则注入它的控制器解析失败）。临时实例只读探活 `/health`，不污染生产。
