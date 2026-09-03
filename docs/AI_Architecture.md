# Architecture

> ⚠️ 本文已按源码更新（.NET 10 + 插件化 + AccessToken）。若与其他文档冲突，以源码为准。

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 10.0 ASP.NET Core |
| ORM | FreeSql 3.5.x（MySQL 默认 + PostgreSQL，CodeFirst 自动建表） |
| Cache | Redis 6.0+ (optional) / MemoryCache / Null（`ICacheService` 三实现） |
| Auth | 双 JWT（JwtAdmin 后台 + JwtApi 客户端）+ API Key（`SB.Feature.AccessToken`）+ TOTP 2FA |
| Log | Serilog（NDJSON/CLEF 分级文件） |
| Frontend | Vue 3 + Vite + Ant Design Vue（Vben Admin，见 sb-framework-admin） |
| Package | 私有 BaGet（`https://nuget.saibo.club/v3/index.json`）+ nuget.org |

## Project Structure

```
sb-framework/
├── SB.Framework.Core/      # 基础设施 (JWT, Util, Middleware, 插件框架, BaseCrudService)
├── SB.Framework.Models/    # 实体、DTO、枚举、接口 (IEntity / IModuleInitializer / Result)
├── SB.Framework.Services/  # 应用服务 + 扩展点 (IFreeSqlConfigurator / IStartupSyncHook)
├── SB.Framework.WebApi/    # Controllers + Middleware + 一键装配 (AddSbFramework / UseSbFramework)
├── SB.Framework.Analyzers/ # Roslyn 编译期约束 (SB0001 / SB0002)
├── SB.Framework.DevHost/   # 本地调试启动壳（不发布）
├── SB.FlowEngine/          # 图执行引擎（独立 NuGet 包）
├── SB.Feature.AccessToken/     # 插件：统一访问令牌
├── SB.Feature.BackupReceive/   # 插件：云端备份接收端
├── scripts/                # 构建 / 打包 / 部署脚本
└── docs/                   # AI documentation (this directory)
```

### 依赖拓扑（关键）

```
Analyzers (netstandard2.0, 以 analyzer 形式被所有包引用)
    ↑
Models (只引 FreeSql)
    ↑
Core (引 Models + Analyzers)          ←── FlowEngine 只引 Core
    ↑
Services (引 Core + Models)
    ↑
WebApi (引 Services)                  ←── DevHost 引 WebApi + Feature 插件
```

Feature 插件（AccessToken / BackupReceive）**只引 Core + Models**，通过 `FrameworkReference: Microsoft.AspNetCore.App` 拿 MVC 类型 —— 避免循环依赖的关键设计。

## 一键装配（★ 核心）

派生项目 `Program.cs` 只需：

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddSbFramework();      // 装配 DI / 中间件 / 插件
var app = builder.Build();
app.UseSbFramework();          // 接管线
await app.RunAsync();
```

- **`AddSbFramework`**（`SB.Framework.WebApi/Extensions/SbFrameworkExtensions.cs`）：读配置 → Serilog → `FeatureScanner.Scan`（插件发现，最早）→ 实例化 feature 清单 → **`installed.lock` 门控**（已安装：FreeSql + Jwt + 业务服务 + 缓存/邮件/后台任务 + feature DI + Controller 跨程序集发现；未安装：仅 Authentication + MemoryCache）→ `AdminRoutePrefixConvention`。
- **`UseSbFramework`** 管线顺序：HSTS → CORS → RateLimiter → Serilog → `GlobalException` → QueryCount → StaticFiles → `SetupMiddleware`(安装门控) → Routing → `ApiKeyMiddleware` → Authorization → Swagger → feature 中间件 → HealthChecks → MapControllers → SeedDatabase。
- **冒烟模式**：`SB_SMOKE_TEST=1` 跳过 DB 迁移与后台任务，只探活 `/health`（部署预检）。

## 插件框架（SB.Feature.\*）

三层架构：

| 层 | 接口/组件 | 实例化方式 | 职责 |
|----|-----------|-----------|------|
| 发现 | `FeatureScanner`（读 `.deps.json` 按 `SB.Feature.` 前缀） | — | 产出 `FeatureCatalog`（单一真相源） |
| 清单/生命周期 | `IFeatureModule` | `Activator.CreateInstance`（不进 DI） | `ConfigureServices` / `Configure` |
| 数据装配 | `IModuleInitializer` | `GetUninitializedObject`（不调构造） | 建表 / 菜单 / 系统设置 / 种子 |

**声明式数据装配**：模块实现 `IModuleInitializer`，声明 `GetEntityTypes()` / `GetMenus()` / `GetSystemSettings()` / `SeedData()`；`DatabaseSeeder` 启动时自动建表（FreeSql `CodeFirst.SyncStructure`，无迁移文件）并同步菜单/设置/种子。

完整设计见 [AI_Plugin_Architecture.md](AI_Plugin_Architecture.md)。

## Routing / Auth / Service / Response

➜ 详见 [AI_Conventions.md §3](AI_Conventions.md) — 路由约定、JWT scheme、`[AutoDependencyInjection]`、Result 响应格式、软删除、中间件（Rate Limiting）都在那里统一维护。

- 路由约定：`AdminRoutePrefixConvention` 自动加 `admin/` 前缀；公开 API `/api/*`。
- 认证：`JwtAdmin`（后台 `/admin/*`）/ `JwtApi`（客户端 `/api/*`，带单点登录校验）。API Key（`Authorization: Bearer sk_...`）由 `SB.Feature.AccessToken` 插件提供，在 JWT 验证前拦截。
- 响应：统一 `Result { code, message, data }`。

## Database

- **ORM:** FreeSql（`IFreeSql` 单例），MySQL 默认 + PostgreSQL。
- **仓储模式:** `BaseCrudService<T> : BaseRepository<T>`（约束 `T : class, IEntity`），提供 `Detail` / `Del`（自动软删）/ 排序 / 分页。
- **Tables:** 前缀 `sb_`（e.g., `SB_Admin`, `SB_Member`）；派生表 `sb_<app>_`。
- **Auto-sync:** 启动时 `CodeFirst.SyncStructure` 自动建表（无迁移文件）。
- **全局软删过滤:** Admin / Menu / AdminRole / Member 自动 `Apply("IsDeleted", ...)`。
- **慢查询监控:** `Aop.CommandAfter` 阈值 `SlowQueryThresholdMs`（默认 200ms）→ Warning 日志。
- **扩展点:** `IFreeSqlConfigurator`（派生项目配置 GlobalFilter / Mapper / Converter，按 `Order` 执行）。
- **Config:** `appsettings.json` → `DbConfig` section。

## Cache System

Config in `appsettings.json` → `CacheConfig`:

| Enabled | Provider | Injected | Behavior |
|---------|----------|----------|----------|
| false | — | `CacheNullService` | No-op, all Get returns null |
| true | Memory | `CacheMemoryCacheService` | In-memory |
| true | Redis | `CacheRedisService` | Redis-backed（`AllowAdmin` 支持 `GetStats()` 诊断） |

```csharp
// ICacheService interface
T Get<T>(string key);
void Set<T>(string key, T value, TimeSpan? expiration = null);
void Remove(string key);
void RemoveByPrefix(string keyPrefix);
```

## Password System

Config: `appsettings.json` → `PasswordConfig.StorageMode`

| Mode | Storage | Use Case |
|------|---------|----------|
| Encrypted | BCrypt hash | Production |
| PlainText | Raw string | Dev/test |

`PasswordUtil` methods: `EncryptPassword()`, `VerifyPassword()`, `IsEncryptedMode()`

## Email System

Pluggable provider architecture（`EmailProviderFactory`）：Google SMTP / Resend。含模板发送、按收件人限流、验证码服务（`VerificationCodeService`）。

Related services: `IEmailService`, `EmailProviderFactory`, `EmailRateLimitService`, `VerificationCodeService`

## AccessToken（SB.Feature.AccessToken 插件）

统一访问令牌：账号即 token（密码 / API Key 两种认证）。
- `ApiKey`（表 `SB_ApiKey`）：AccountId、TokenHash(sha256)、TokenPrefix、Scopes(RBAC)、FullAccess(god)、IpAllowlist、ExpiresAt、RateLimitPerMinute；明文只签发时回显一次。
- 实现 `IApiKeyAuthenticator` 核心缝，注册 `ScopeFilter`（端点 `[Scope]` 声明，默认 deny）。

## Background Tasks

- `BaseBackgroundTask` → base class，override `ExecuteTaskAsync()` / `Interval` / `ShouldRunNowAsync()`
- `BackgroundTaskManager` → lifecycle management (start/stop/restart)
- `BackgroundTaskHostedService` → scheduler
- `BackgroundTaskRegistrationService` → auto-discovery via `IModuleInitializer`
- Logs persisted to `SB_BackgroundTaskLog`

## Google 2FA (TOTP)

- Bind: generate QR → user scans → verify code + password → bind
- Login: if bound, `totpCode` required
- Unbind: verify TOTP code + password
- Admin can reset another admin's 2FA

## File Upload

### Regular Upload
- Max 10MB（configurable via `UploadConfig`），allowed extensions whitelist
- `GET /Upload/Config` 返回配置（前端 SmartUpload 读取）

### Chunked Upload
- 分片阈值可配（`ChunkThresholdMB`），最大文件大小可配
- Flow: Init（秒传检测 new/resume/instant）→ Upload chunks → Merge
- 前端 spark-md5 计算文件 MD5，XHR 分片带进度

## Database Backup

- `mysqldump`-based backup with versioned records
- 统一入口 `DatabaseBackupService.BackupAndUploadAsync`（定时任务 / Admin 手动 / API）
- 可选推送到云端（`CloudConfig.ServerUrl` + BackupReceive 插件接收）

## Configuration Reference

`appsettings.json` key sections:

```json
{
  "TimeZone": "Asia/Shanghai",
  "ProductCn": "",
  "ProductEn": "",
  "DbConfig": { "DbType": "MySql|PostgreSQL", "Server": "", "Port": 3306, "Database": "", "User": "", "Password": "", "AutoSyncStructure": true, "SlowQueryThresholdMs": 200 },
  "CacheConfig": { "Enabled": false, "Provider": "Memory|Redis", "Redis": { "Connection": "", "InstanceName": "SB:" } },
  "PasswordConfig": { "StorageMode": "Encrypted|PlainText" },
  "UploadConfig": { "MaxFileSizeMB": 50, "ChunkThresholdMB": 5, "AllowedExtensions": [], "MaxConcurrent": 3 },
  "RateLimitConfig": { "WindowSeconds": 60, "PermitLimit": 5 },
  "CorsConfig": { "AllowAll": true, "Origins": [] },
  "JwtAdmin": { "Key": "", "Issuer": "", "Audience": "", "Expires": 12 },
  "JwtApi": { "Key": "", "Issuer": "", "Audience": "", "Expires": 24 },
  "ProjectAssemblyName": "",
  "ExcludeControllers": [],
  "PrimaryDomain": "",
  "CloudConfig": { "ServerUrl": "" },
  "DeployConfig": { "Projects": [] },
  "Features": { "Disabled": [] },
  "Modules": { "Disabled": [] }
}
```

## Key Files

| Purpose | File |
|---------|------|
| 一键装配入口 | `SB.Framework.WebApi/Extensions/SbFrameworkExtensions.cs` |
| 插件发现 | `SB.Framework.Core/Features/FeatureScanner.cs` |
| 插件清单 | `SB.Framework.Core/Features/IFeatureModule.cs` |
| 数据装配声明 | `SB.Framework.Models/IModuleInitializer.cs` |
| 全局配置 | `SB.Framework.Core/AppSettings.cs` |
| Route convention | `SB.Framework.WebApi/Extensions/AdminRoutePrefixConvention.cs` |
| Service registration | `SB.Framework.WebApi/Extensions/ServiceSetup.cs` |
| DI attribute | `SB.Framework.Core/Attribute/AutoDependencyInjection.cs` |
| CRUD base | `SB.Framework.Core/BaseCrudService.cs` |
| JWT token | `SB.Framework.Core/Authentication/Jwt/JwtToken.cs` |
| Password util | `SB.Framework.Core/Util/PasswordUtil.cs` |
| 时间工具（强制） | `SB.Framework.Core/Util/AppTime.cs` |
| Global exception | `SB.Framework.Core/Middleware/GlobalExceptionMiddleware.cs` |
