# Build & Deploy

## Requirements

- .NET 8.0 SDK
- MySQL 5.7+
- Redis 6.0+ (optional)
- Node.js 18+ / pnpm 10.22+ (frontend)

## Port Allocation

Project reserved range: **6010 - 6019**

| Component | Port |
|-----------|------|
| sb-api | 6010 |
| sb-admin | 6012 |

## Backend Commands

```bash
# Build
dotnet build                              # Debug
dotnet build -c Release                   # Release

# Development
dotnet run --project SB.API               # HTTP:6010, HTTPS:7292
dotnet watch run --project SB.API         # Hot reload

# Publish
dotnet publish SB.API -c Release -o ./dist

# Production run
dotnet SB.API.dll --urls='http://127.0.0.1:6010'
```

## Build Parameters

➜ 详见 [AI_Conventions.md §10](AI_Conventions.md) — `-p:DisableLicense` / `-p:EnableLicenseManage` / `-p:EnableBackupReceive` 三个开关 + 常见组合命令。

## 派生项目 SemVer 自动版本号(推荐脚本)

派生 application 项目(`sb-apps-pm` / `sb-apps-nexus` / 等)应该在自己仓库根目录放一个 `build-publish.sh`,让 `dotnet publish` 自动生成语义化版本号,使 `/admin/App/Info` 返回的 `version` 字段真实反映"代码改了多少次",而不是永远 `1.0.0+<sha>`。

**版本号规则**:`Version = <PREFIX>.<git commit 总数>`,例如 `1.0.157`。每次新 commit → patch 自动 +1。bump minor/major 用 `VERSION_PREFIX=1.1 ./build-publish.sh` 覆盖默认 PREFIX。

**关键 dotnet 参数**:`-p:Version=$VERSION` + `-p:IncludeSourceRevisionInInformationalVersion=false`(关掉 SourceLink 默认追加 commit sha,让 `InformationalVersion` 干净等于 `Version`)。

**模板脚本**:

```bash
#!/bin/bash
# <App> API 构建发布脚本
set -e
cd "$(dirname "$0")"

PREFIX=${VERSION_PREFIX:-1.0}
PATCH=$(git rev-list --count HEAD 2>/dev/null || echo 0)
VERSION="$PREFIX.$PATCH"

echo "=== <App> publish version $VERSION ==="

DIST=../dist/api
rm -rf $DIST && mkdir -p $DIST

dotnet publish <App.WebApi.csproj 路径> \
  -c Release -o $DIST \
  -p:Version=$VERSION \
  -p:IncludeSourceRevisionInInformationalVersion=false \
  --nologo -v minimal

# 默认过滤:appsettings + wwwroot(线上配置/资源不要覆盖)
cd $DIST
rm -f appsettings.json appsettings.*.json
rm -rf wwwroot

# 打 zip(zip 内直接是产物,无外层目录)
cd .. && rm -f api.zip && (cd api && zip -qr ../api.zip .)

echo "✓ Done. Version: $VERSION"
```

参考实现:
- [sb-apps-pm/build-publish.sh](https://gitlab.saibo.club/saibo/apps/sb-apps-pm/-/blob/main/build-publish.sh)
- [sb-apps-nexus/build-publish.sh](https://gitlab.saibo.club/saibo/apps/sb-apps-nexus/-/blob/main/build-publish.sh)

## Frontend Commands

```bash
cd sb-admin
pnpm install                   # Install dependencies
pnpm dev:antd                  # Dev server (http://localhost:5173)
pnpm build:antd                # Production build → dist/
```

## Setup Wizard (First Run)

When `installed.lock` does not exist, the system enters setup mode.

1. Access `http://<host>:<port>/setup/index.html`
2. Configure: database connection, Redis (optional), admin account, CORS, password mode
3. Install → creates tables, seeds data, updates `appsettings.json`, creates `installed.lock`
4. **Restart required** to load new config

**Re-install:** delete `installed.lock` and restart.

### Setup API

All at `/api/Setup/[action]`, AllowAnonymous:

| Action | Method | Body |
|--------|--------|------|
| Status | GET | — |
| TestDatabase | POST | `{ Server, Port, Database, User, Password }` |
| TestRedis | POST | `{ Connection, InstanceName }` |
| Install | POST | SetupRequest JSON (see AI_API_Reference.md) |

## Deployment

### Linux (systemd)

1. Publish: `dotnet publish SB.API -c Release -o ./dist`
2. Upload `dist/` to server
3. Create systemd service:

```ini
[Unit]
Description=SB API
After=network.target

[Service]
WorkingDirectory=/opt/sb-api
ExecStart=/usr/bin/dotnet SB.API.dll --urls=http://127.0.0.1:6010
Restart=always
RestartSec=10
User=www-data

[Install]
WantedBy=multi-user.target
```

4. `systemctl enable sb-api && systemctl start sb-api`

### In-App Deployment

The `AppController` supports hot deployment:

- **UploadApi**: Upload zip → extract → replace files → auto-restart (200MB limit)
- **UploadProject**: Upload frontend zip → extract to configured directory (200MB limit)
- **DeployTargets**: List deployable frontend projects
- **Restart**: Graceful app restart

### Database Backup

- Admin UI: Database Management page
- API: `POST /api/Database/Backup` with `X-Master-Key` header
- Requires `mysqldump` available in PATH

## Logging

Serilog rolling file configuration:
- `logs/error-{Date}.txt` — Error level
- `logs/info-{Date}.txt` — Info level
- `logs/warning-{Date}.txt` — Warning level
- Old logs auto-archived to timestamped subfolder on startup
- Development: additional console output

## New Feature Workflow

### 1. Entity

```csharp
// SB.Models/YourEntity.cs
[Table(Name = "SB_YourEntity")]
public class YourEntity : IEntity, ISoftDelete
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Column(StringLength = 100)]
    public string Name { get; set; }

    [Column(DbType = "timestamp", InsertValueSql = "now()")]
    public DateTime CreatedTime { get; set; }

    public bool IsDeleted { get; set; }
}
```

### 2. Service

```csharp
// SB.Services/YourEntityService.cs
[AutoDependencyInjection]
public class YourEntityService : BaseCrudService<YourEntity>
{
    public YourEntityService(IFreeSql fsql, ILogger<YourEntityService> logger)
        : base(fsql, logger) { }
}
```

### 3. Controller

```csharp
// SB.API/Controllers/YourEntityController.cs
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class YourEntityController : Controller
{
    private readonly YourEntityService _service;

    public YourEntityController(YourEntityService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List(string field, string order, int page = 1, int pageSize = 20)
        => Ok(await _service.GetPageAsync(page, pageSize, field, order));

    [HttpGet]
    public async Task<IActionResult> Detail(int id)
        => Ok(Result.Success(await _service.GetAsync(id)));

    [HttpPost]
    public async Task<IActionResult> Add([FromForm] YourEntity entity)
        => Ok(await _service.AddAsync(entity));

    [HttpPost]
    public async Task<IActionResult> Edit([FromForm] YourEntity entity)
        => Ok(await _service.EditAsync(entity));

    [HttpPost]
    public async Task<IActionResult> Del([FromForm] int id)
        => Ok(await _service.DeleteAsync(id));
}
```

### Key Conventions

- English names only
- `[AutoDependencyInjection]` for singleton registration
- `[FromForm]` for form data, `[FromBody]` for JSON
- JWT claims: `User.UserId()`, `User.UserName()`, `User.UserType()`, `User.Role()`
- Sensitive data: `EncryptionUtil` with AES-256
- Table prefix: `SB_`

## Code Obfuscation

To protect license-related logic from reverse engineering (e.g., dnSpy), the project uses [Obfuscar](https://github.com/obfuscar/obfuscar) for build-time obfuscation of `SB.Core.dll` and `SB.API.dll`.

### Install

```bash
dotnet tool install --global Obfuscar.GlobalTool
```

### Usage

```bash
./obfuscate.sh
```

This script:
1. Runs `dotnet publish SB.API -c Release -o ./dist`
2. Runs Obfuscar against the published output using `obfuscar.xml`
3. Replaces `SB.Core.dll` and `SB.API.dll` in `./dist` with obfuscated versions
4. Cleans up the temporary `dist_obfuscated` directory

### What It Does

- **Renames** internal types, methods, fields, properties, and events to unreadable Unicode names
- **Encrypts strings** (including license keys, validation logic strings) in both assemblies
- **Preserves** controllers, middleware, extensions, DI-registered types, and config classes so the app runs correctly

### Configuration

The obfuscation config is in `obfuscar.xml` at the project root. Key skip rules:
- `SB.API.Controllers`, `SB.API.Middleware`, `SB.API.Extensions` — routing and DI rely on type names
- `SB.Core.AppSettings`, `SB.Core.AppGlobal`, `SB.Core.LicenseConfig` — used by DI and config binding
- Types matching `*Config$` in `SB.Core` — deserialized from JSON config

### Limitations

Obfuscation raises the barrier for casual reverse engineering but is not absolute protection. A determined attacker with sufficient time can still deobfuscate .NET assemblies. It is one layer in a defense-in-depth strategy.
