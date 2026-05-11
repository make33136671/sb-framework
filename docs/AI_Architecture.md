# Architecture

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 8.0 ASP.NET Core |
| ORM | FreeSql + MySQL 5.7+ |
| Cache | Redis 6.0+ (optional) / MemoryCache / Null |
| Auth | Dual JWT (JwtAdmin + JwtApi) |
| Frontend | Vue 3 + Vite + Ant Design Vue (Vben Admin) |
| Package | pnpm monorepo |

## Project Structure

```
sb-api/
├── SB.API/          # Controller + DI + Middleware + Extensions
├── SB.Services/     # Business logic (Service + Repository)
├── SB.Models/       # Entity, DTO, Enum, Interface
├── SB.Core/         # Infrastructure (JWT, Util, FreeSql, License)
├── SB.LicenseGen/   # License generation CLI tool
├── SQL/             # Migration & seed SQL
└── docs/            # AI documentation (this directory)
```

## Routing / Auth / Service / Response

➜ 详见 [AI_Conventions.md §3](AI_Conventions.md) — 路由约定、JWT scheme、`[AutoDependencyInjection]`、Result 响应格式、软删除、中间件 (`MasterKeyMiddleware`、Rate Limiting) 都在那里统一维护。

## Database

- **ORM:** FreeSql with MySQL provider
- **Tables:** Prefixed `SB_` (e.g., `SB_Admin`, `SB_Member`)
- **Auto-sync:** Development mode auto-syncs entity → table structure
- **Config:** `appsettings.json` → `DbConfig` section

## Cache System

Config in `appsettings.json` → `CacheConfig`:

| Enabled | Provider | Injected | Behavior |
|---------|----------|----------|----------|
| false | — | `CacheNullService` | No-op, all Get returns null |
| true | Memory | `CacheMemoryCacheService` | In-memory |
| true | Redis | `CacheRedisService` | Redis-backed |

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

Seamless migration: enable encrypted mode → old plaintext passwords still work (auto-fallback) → new passwords are BCrypt → full migration as users change passwords.

## Email System

Pluggable provider architecture:

| Provider | Config |
|----------|--------|
| Google SMTP | SmtpServer, Port, Username, Password, SSL |
| Resend | API Key, From email |

Features: template-based sending, rate limiting per recipient, verification code service.

Related services: `IEmailService`, `EmailProviderFactory`, `EmailRateLimitService`, `VerificationCodeService`

## License System

### Client-side (LicenseController)
- Check license status, request authorization, download & apply license
- `LicenseMiddleware` validates license for protected routes
- Whitelist: setup, auth, health check paths

### Server-side (LicenseManageController) [conditional: `ENABLE_LICENSE_MANAGE`]
- RSA key pair management, client management
- Approve/reject license requests, generate/revoke licenses
- Verification logs

Build flags:
- Default: license validation **enabled**
- `-p:DisableLicense=true`: disable validation
- `-p:EnableLicenseManage=true`: include server-side management

## Background Tasks

- `BaseBackgroundTask` → base class, override `ExecuteAsync()`
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
- Max 10MB (configurable), allowed extensions whitelist
- Endpoint: `/admin/Auth/Upload` or `/api/Upload`

### Chunked Upload
- 10MB chunks, max 2GB total (configurable)
- Flow: Init (秒传检测) → Upload chunks → Merge
- 24h expiration, resume supported

## Database Backup

- `mysqldump`-based backup with versioned records
- Admin UI or MasterKey API trigger
- Records: file path, size, duration, created by

## Configuration Reference

`appsettings.json` key sections:

```json
{
  "AppName": "赛博管理系统",
  "DbConfig": { "Server": "", "Port": 3306, "Database": "", "User": "", "Password": "" },
  "CacheConfig": { "Enabled": false, "Provider": "Memory|Redis", "Redis": { "Connection": "", "InstanceName": "SB:" } },
  "PasswordConfig": { "StorageMode": "Encrypted|PlainText" },
  "UploadConfig": { "MaxFileSizeMB": 10, "AllowedExtensions": [...], "UploadPath": "uploads" },
  "ChunkUploadConfig": { "ChunkSizeMB": 10, "MaxFileSizeMB": 2048, "TempPath": "temp", "ExpireHours": 24 },
  "RateLimitConfig": { "WindowSeconds": 60, "PermitLimit": 5 },
  "CorsConfig": { "AllowAll": true, "Origins": [] },
  "JwtAdmin": { "Key": "", "Issuer": "", "Audience": "", "Expires": 1440 },
  "JwtApi": { "Key": "", "Issuer": "", "Audience": "", "Expires": 10080 },
  "EncryptionKey": "SB2024DefaultKey!",
  "ProjectAssemblyName": "",
  "ExcludeControllers": []
}
```

## Key Files

| Purpose | File |
|---------|------|
| Entry point | `SB.API/Program.cs` |
| Route convention | `SB.API/Extensions/AdminRoutePrefixConvention.cs` |
| Controller exclusion | `SB.API/Extensions/ControllerExclusionConvention.cs` |
| Service registration | `SB.API/Extensions/ServiceSetup.cs` |
| DI attribute | `SB.Core/Attribute/AutoDependencyInjection.cs` |
| CRUD base | `SB.Services/BaseCrudService.cs` |
| JWT token | `SB.Core/Authentication/Jwt/JwtToken.cs` |
| Password util | `SB.Core/Util/PasswordUtil.cs` |
| Encryption util | `SB.Core/Util/EncryptionUtil.cs` |
| Global exception | `SB.Core/Middleware/GlobalExceptionMiddleware.cs` |
| License middleware | `SB.Core/License/LicenseMiddleware.cs` |
