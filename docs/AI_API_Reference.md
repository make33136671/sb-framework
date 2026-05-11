# API Reference

All admin endpoints require `JwtAdmin` auth (Bearer token) unless noted. All responses use unified `Result` wrapper: `{ code, message, data }`.

POST parameters are `[FromForm]` unless noted as JSON body.

---

## AuthController `/admin/Auth/[action]`

Authentication and account management.

| Method | Action | Params | Note |
|--------|--------|--------|------|
| POST | Login | `username`, `password`, `totpCode`(optional) | AllowAnonymous, rate-limited |
| GET | GetInfo | — | Current admin info + role + menus |
| POST | Logout | — | |
| POST | ChangePassword | `oldPassword`, `newPassword` | |
| POST | ChangeAvatar | `avatar` (URL) | |
| POST | UpdateProfile | `name`, `nickname` | |
| POST | Upload | `file` IFormFile | AllowAnonymous |
| GET | GetGoogleCodeInfo | — | `{ isBound, bindTime }` |
| GET | GetGoogleCodeQR | — | `{ qrBase64, manualEntryKey }` |
| POST | PostBindGoogleCode | `code`, `password` | |
| POST | UnbindGoogleCode | `totpCode`, `password` | |

---

## ManagerController `/admin/Manager/[action]`

Admin user and role management.

### Admin CRUD

| Method | Action | Params |
|--------|--------|--------|
| GET | List | query: `username`, `name`, `roleId`, `field`, `order`, `page`, `pageSize` |
| GET | Detail | query: `id` |
| POST | Add | form: Admin object |
| POST | Edit | form: Admin object |
| POST | Del | form: `id` (soft delete) |
| POST | ResetGoogleCode | form: `adminId` |

### Role CRUD

| Method | Action | Params |
|--------|--------|--------|
| GET | RoleList | query: `name`, `roleKey`, `field`, `order`, `page`, `pageSize` |
| GET | RoleDetail | query: `id` |
| GET | QueryAdminRole | query: `key` (search keyword) |
| POST | RoleAdd | form: AdminRole object |
| POST | RoleEdit | form: AdminRole object |
| POST | RoleDel | form: `id` |

### Role-Menu Permissions

| Method | Action | Params |
|--------|--------|--------|
| GET | GetMenuIds | query: `roleId` → int[] |
| GET | GetMenusDetail | query: `roleId` → Menu[] |
| POST | SetMenus | form: `roleId`, `menuIds` (comma-separated) |
| POST | AddMenu | form: `roleId`, `menuId` |
| POST | RemoveMenu | form: `roleId`, `menuId` |
| POST | CopyMenus | form: `fromRoleId`, `toRoleId` |
| GET | GetRolesByMenu | query: `menuId` → AdminRole[] |

---

## MenuController `/admin/Menu/[action]`

Menu management.

| Method | Action | Params | Note |
|--------|--------|--------|------|
| GET | All | — | Current admin's authorized menu tree |
| GET | TreeList | query: `name`, `title`, `field`, `order`, `page`, `pageSize` | Tree structure |
| GET | List | query: same as TreeList | Flat list |
| GET | Detail | query: `id` | |
| GET | QueryMenu | query: `key` | Search keyword |
| POST | Add | form/body: Menu object | |
| POST | Edit | form/body: Menu object | |
| POST | UpdateMenu | form: `id`, `field`, `value` | Update single field |
| POST | Del | form: `id` | |

---

## MemberController `/admin/Member/[action]`

Member (H5 user) management.

| Method | Action | Params |
|--------|--------|--------|
| GET | List | query: `id`, `keyword`, `status`, `field`, `order`, `page`, `pageSize` |
| GET | Detail | query: `id` |
| POST | Add | form: Member object |
| POST | Edit | form: Member object |
| POST | Del | form: `id` |
| POST | ResetPassword | form: `id`, `newPassword` |
| GET | Search | query: `keyword`, `limit` |
| GET | ReferralTree | query: `memberId` |
| GET | MemberTree | query: `memberId` (member + descendants) |
| GET | Children | query: `parentId` (direct children) |

---

## RealNameController `/admin/RealName/[action]`

KYC / real name verification review.

| Method | Action | Params |
|--------|--------|--------|
| GET | List | query: `status`, `keyword`, `field`, `order`, `page`, `pageSize` |
| GET | Detail | query: `id` (includes ID images) |
| POST | Approve | form: `id` |
| POST | Reject | form: `id`, `reason` |

---

## DashboardController `/admin/Dashboard/[action]`

Dashboard analytics.

| Method | Action | Params | Returns |
|--------|--------|--------|---------|
| GET | Overview | — | Member totals, today/yesterday new, active, verified, pending, banned |
| GET | MemberTrend | query: `days`(default 30) | `{ labels, values }` registration trend |
| GET | MemberDistribution | — | Status + real name distribution (pie chart) |
| GET | RecentLogins | query: `limit`(default 10) | Recent login logs |
| GET | RecentMembers | query: `limit`(default 10) | Recently registered members |
| GET | PendingRealNames | query: `limit`(default 10) | Pending KYC verifications |
| GET | LoginTrend | query: `days`(default 7) | `{ labels, values }` daily active trend |

---

## SystemSettingController `/admin/SystemSetting/[action]`

Dynamic system settings (19 display types).

| Method | Action | Params |
|--------|--------|--------|
| GET | List | query: `key`, `field`, `order`, `page`, `pageSize` |
| GET | GetCategoryWithSettingList | query: `categoryId`, `field`, `order`, `page`, `pageSize` |
| GET | QuerySystemSetting | query: `key` |
| GET | Detail | query: `id` |
| POST | Add | form/body: SystemSetting object |
| POST | Edit | form/body: SystemSetting object |
| POST | SaveList | form: `settings` (JSON array string) |
| DELETE | Del | query: `id` |

---

## SystemSettingCategoryController `/admin/SystemSettingCategory/[action]`

Setting category management.

| Method | Action | Params |
|--------|--------|--------|
| GET | List | query: `key`, `name`, `field`, `order`, `page`, `pageSize` |
| GET | QuerySystemSettingCategory | query: `key` |
| GET | Detail | query: `id` |
| POST | Add | form/body: SystemSettingCategory object |
| POST | Edit | form/body: SystemSettingCategory object |
| POST | Del | form: `id` |

---

## EmailConfigController `/admin/EmailConfig/[action]`

Email configuration, templates, and logs.

### SMTP Config

| Method | Action | Params |
|--------|--------|--------|
| GET | GetConfig | — |
| POST | SaveConfig | form/body: EmailConfigSaveDto |

### Resend Config

| Method | Action | Params |
|--------|--------|--------|
| GET | GetResendConfig | — |
| POST | SaveResendConfig | form/body: ResendConfigSaveDto |
| GET | GetProvider | — |
| POST | SetProvider | form: `provider` |

### Templates

| Method | Action | Params |
|--------|--------|--------|
| GET | GetTemplates | query: `page`, `pageSize` |
| GET | GetTemplate | query: `id` |
| POST | SaveTemplate | form/body: EmailTemplateSaveDto |

### Logs

| Method | Action | Params |
|--------|--------|--------|
| GET | GetLogs | query: `page`, `pageSize`, `status` |
| GET | GetLog | query: `id` |

---

## LoginLogController `/admin/LoginLog/[action]`

Login audit logs.

| Method | Action | Params |
|--------|--------|--------|
| GET | List | query: `userType`, `username`, `status`, `startDate`, `endDate`, `page`, `pageSize` |

---

## BackgroundTaskController `/admin/BackgroundTask/[action]`

Background task management.

| Method | Action | Params |
|--------|--------|--------|
| GET | List | — |
| POST | Start | form: `name` |
| POST | Stop | form: `name` |
| POST | Restart | form: `name` |
| POST | ReloadParameters | form: `name` |
| GET | GetLogs | query: `name`, `limit`(default 100) |
| GET | LogList | query: `source`, `taskName`, `logLevel`, `page`, `pageSize` |

---

## ChunkUploadController `/admin/ChunkUpload/[action]`

Chunked file upload (20MB request limit per chunk).

| Method | Action | Params | Note |
|--------|--------|--------|------|
| POST | Init | form: `fileName`, `fileSize`, `fileMd5` | Returns uploadId, supports instant upload (秒传) |
| POST | Upload | form: `uploadId`, `chunkIndex`, `chunk` (IFormFile) | 20MB limit |
| POST | Merge | form: `uploadId` | Merge all chunks |
| GET | Progress | query: `uploadId` | Upload progress |

---

## DatabaseController `/admin/Database/[action]`

Database backup management.

| Method | Action | Params | Note |
|--------|--------|--------|------|
| GET | CheckBackup | — | Check mysqldump availability & DB info |
| POST | Backup | — | Create database backup |
| GET | List | query: `field`, `order`, `page`, `pageSize` | Backup records |
| POST | Del | form: `id` | Delete backup file & record |

**MasterKey API:**

| Method | Action | Route | Note |
|--------|--------|-------|------|
| POST | ApiBackup | `/api/Database/Backup` | Requires `X-Master-Key` header |

---

## AppController `/admin/App/[action]`

Application info, deployment, and cache management. Upload endpoints have 200MB request limit.

| Method | Action | Params | Note |
|--------|--------|--------|------|
| GET | Info | — | Version, uptime, environment |
| POST | Restart | — | Graceful restart (systemd auto-relaunch) |
| POST | UploadApi | `file` (zip) | Upload API package → extract → restart, 200MB limit |
| GET | CacheInfo | — | Cache provider & categories |
| POST | ClearAllCache | — | Clear all cache |
| POST | ClearCacheByPrefix | form: `prefix` | Clear cache by prefix |
| GET | DeployTargets | — | Get deployable frontend projects |
| POST | UploadProject | form: `key`, `file` (zip) | Deploy frontend project, 200MB limit |

---

## LicenseController `/admin/License/[action]`

Client-side license operations.

| Method | Action | Params | Note |
|--------|--------|--------|------|
| GET | Status | — | AllowAnonymous. License status + client info |
| POST | RequestLicense | form: `domain` | Request authorization from server |
| POST | CheckUpdate | — | Check for new authorization |
| POST | ApplyLicense | form: `recordId` | Download and apply license |

---

## LicenseManageController `/admin/LicenseManage/[action]`

Server-side license management. **Conditional: build with `-p:EnableLicenseManage=true`**

### Key Management

| Method | Action | Params |
|--------|--------|--------|
| GET | GetPublicKey | — |
| POST | RegenerateKeyPair | — |

### Client Management

| Method | Action | Params |
|--------|--------|--------|
| GET | ClientList | query: `page`, `pageSize`, `keyword` |
| GET | ClientDetail | query: `id` |
| POST | ClientEdit | form: `id`, `name`, `remark`, `status` |

### License Requests

| Method | Action | Params |
|--------|--------|--------|
| GET | RequestList | query: `page`, `pageSize`, `status` |
| POST | ApproveRequest | form: `requestId`, `expiresAt`, `verifyMode`, `allowedRoutes` |
| POST | RejectRequest | form: `requestId`, `reason` |

### License Records

| Method | Action | Params |
|--------|--------|--------|
| GET | RecordList | query: `page`, `pageSize`, `clientId` |
| POST | GenerateLicense | form: `clientId`, `domain`, `clientKey`, `hardwareId`, `expiresAt`, `verifyMode`, `allowedRoutes` |
| POST | RevokeLicense | form: `recordId` |

### Verification Logs

| Method | Action | Params |
|--------|--------|--------|
| GET | VerifyLogList | query: `page`, `pageSize`, `clientId` |

---

## ApiController `/api/[action]`

Public API for mobile/external clients. Auth: JwtApi (except noted).

### Authentication (AllowAnonymous)

| Method | Action | Params |
|--------|--------|--------|
| POST | AuthLogin | form: `username`, `password` |
| POST | AuthRegister | form: `username`, `password`, `name`, `inviteCode` |
| POST | AuthForgotPassword | form: `username` |

### User Profile (JwtApi)

| Method | Action | Params |
|--------|--------|--------|
| GET | AuthInfo | — |
| POST | AuthUpdateNickname | form: `name` |
| POST | AuthUpdateAvatar | form: `avatar` |
| POST | AuthLogout | — |

### Real Name Verification (JwtApi)

| Method | Action | Params |
|--------|--------|--------|
| POST | AuthSubmitRealName | form: `realName`, `idNumber`, `idFrontImage`, `idBackImage` |
| GET | AuthRealNameInfo | — |

### Security (JwtApi)

| Method | Action | Params |
|--------|--------|--------|
| POST | SecurityChangeLoginPassword | form: `oldPassword`, `newPassword` |
| POST | SecurityChangePaymentPassword | form: `oldPassword`, `newPassword` |

### Verification Code (AllowAnonymous)

| Method | Action | Params |
|--------|--------|--------|
| POST | SendVerificationCode | form: `email`, `type` |
| POST | VerifyCode | form: `email`, `code`, `type` |

### File Upload (AllowAnonymous)

| Method | Action | Params |
|--------|--------|--------|
| POST | Upload | `file` IFormFile |

---

## SetupController `/api/Setup/[action]`

System installation wizard. All AllowAnonymous. Returns error if already installed.

| Method | Action | Body | Note |
|--------|--------|------|------|
| GET | Status | — | Check installation status |
| POST | TestDatabase | JSON: `{ Server, Port, Database, User, Password }` | Test DB connection |
| POST | TestRedis | JSON: `{ Connection, InstanceName }` | Test Redis connection |
| POST | Install | JSON: SetupRequest (see below) | Execute installation |

### SetupRequest Schema

```json
{
  "Database": { "Server": "", "Port": "3306", "Database": "Saibo", "User": "root", "Password": "" },
  "EnableCache": false,
  "CacheProvider": "Memory|Redis",
  "Redis": { "Connection": "localhost:6379", "InstanceName": "SB:" },
  "AppName": "赛博管理系统",
  "AdminUsername": "admin",
  "AdminPassword": "123456",
  "AdminDisplayName": "超级管理员",
  "CorsAllowAll": true,
  "CorsOrigins": [],
  "PasswordStorageMode": "Encrypted|PlainText"
}
```

Installation process: create tables → seed data → update appsettings.json → create `installed.lock` → requires restart.

---

## LicenseApiController `/api/license/[action]`

Public license API for client instances. AllowAnonymous. **Conditional: `ENABLE_LICENSE_MANAGE`**

| Method | Action | Params |
|--------|--------|--------|
| POST | Request | form: `domain`, `clientKey`, `hardwareId`, `serverIP` |
| POST | Verify | form: `domain`, `clientKey`, `hardwareId`, `serverIP`, `licenseValid`, `message` |
| GET | Check | query: `clientKey`, `hardwareId` |
| GET | Download | query: `recordId` |
