# API Reference

> 本文档为 **Controller 接口事实源**，逐条对照源码生成。若与旧文档冲突，以此为准。
>
> **路由约定**（`AdminRoutePrefixConvention`）：控制器级**相对** `[Route]` 模板自动拼接 `admin/` 前缀；以 `/` 或 `~/` 开头的**绝对**路由不受影响。下述"有效路由"已按此规则换算。

## 通用约定

- 除标注 `AllowAnonymous` 外，admin 接口需 `JwtAdmin`（Bearer token）；`/api/*` 公开接口需 `JwtApi`。
- 响应统一 `Result`：`{ code: 0|-1, message, data }`（`code === 0` 成功）。
- POST 参数默认 `[FromForm]`（SB0002 强制），标注 FromBody/FromQuery 的除外。
- 分页参数约定：`page`（默认 1）、`pageSize`（默认 20 或 10）。

## 已知缺陷 / 废弃标注（★）

1. **`SystemSettingController.List` 无 HTTP verb 属性**（源码 `SystemSettingController.cs:30`）——属性路由不可达，疑似漏标 `[HttpGet]`。⚠️ 不可用。
2. **`AppController` 4 个 `[Obsolete]` action**：`UploadApi`、`DeployApiByPath`、`UploadProject`、`DeployProjectByPath`——已废弃，改用 **`Upload` + `Deploy` 两步流程**（带冒烟预检）。
3. `BackupReceiveController` 控制器级无 `[Route]`，每个 action 显式绝对路由（不受 admin 前缀影响）。
4. `EntitlementController` / `VerifyController` 部分 POST action 参数无显式绑定属性，依赖 `[ApiController]` 简单类型默认 `FromQuery` 推断。

---

## 一、SB.Framework.WebApi

### 1. ApiController — `/api/[action]`（绝对）· `[Authorize(JwtApi)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| POST | `AuthLogin` | form: `username`, `password` | 登录（**AllowAnonymous**） |
| POST | `AuthRegister` | form: `username`, `password`, `name`, `inviteCode?` | 注册（**AllowAnonymous**） |
| POST | `AuthForgotPassword` | form: `username` | 找回密码（**AllowAnonymous**） |
| GET | `AuthInfo` | — | 当前用户信息 |
| POST | `AuthUpdateNickname` | form: `name` | 改昵称 |
| POST | `AuthUpdateAvatar` | form: `avatar` | 改头像 |
| POST | `AuthLogout` | — | 登出，固定返回成功 |
| POST | `AuthSubmitRealName` | form: `realName`, `idNumber`, `idFrontImage`, `idBackImage` | 提交实名认证 |
| GET | `AuthRealNameInfo` | — | 实名信息 |
| POST | `SecurityChangeLoginPassword` | form: `oldPassword`, `newPassword` | 改登录密码 |
| POST | `SecurityChangePaymentPassword` | form: `oldPassword`, `newPassword` | 改支付密码 |
| POST | `SendVerificationCode` | form: `email`, `type`(VerificationCodeType) | 发送验证码（**AllowAnonymous**） |
| POST | `VerifyCode` | form: `email`, `code`, `type` | 校验验证码（**AllowAnonymous**） |
| POST | `Upload` | form: `file`(IFormFile) | 文件上传（**AllowAnonymous**） |

### 2. AppController — `admin/App/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `Info` | — | 应用信息（版本/启动时间/内存/监听地址） |
| POST | `Restart` | — | 优雅重启 |
| POST | `UploadApi` ★[Obsolete] | form: `file`, `keepConfig`(默认true) | 上传 API zip 部署（200MB） |
| POST | `DeployApiByPath` ★[Obsolete] | form: `filePath`, `fileName`, `fileSize`, `clearCache`(默认false), `keepConfig`(默认true), `keepLock`(默认true) | 按分片路径部署 API |
| GET | `CacheInfo` | — | 缓存信息 |
| POST | `ClearAllCache` | — | 清空全部缓存 |
| POST | `ClearCacheByPrefix` | form: `prefix` | 按前缀清缓存 |
| GET | `PreviewDeploy` | query: `target`, `filePath`, `fileName?` | 部署预览（校验 zip） |
| POST | `CancelUpload` | form: `filePath` | 删除已上传临时 zip |
| GET | `DeployTargets` | — | 可部署前端项目列表 |
| POST | `UploadProject` ★[Obsolete] | form: `key`, `file`；`[Scope("deploy")]` | 上传前端项目 zip（200MB） |
| POST | `DeployProjectByPath` ★[Obsolete] | form: `key`, `filePath`, `fileName`, `fileSize` | 按路径部署前端项目 |
| POST | `Upload` | form: `target`, `file` | **统一上传**（api 或项目 key），创建 DeployJob 返回 jobId/uploadId |
| POST | `Deploy` | form: `uploadId`, `keepConfig`(默认true), `clearCache`(默认false), `keepLock`(默认true) | **统一部署**（冒烟预检→原子替换→重启） |
| GET | `UploadRecords` | query: `target?`, `status?`, `page`, `pageSize` | 部署作业列表（旧名） |
| GET | `DeployJobs` | 同上 | 部署作业列表（新名，等同 UploadRecords） |
| GET | `DeployRecords` | query: `target?`, `page`, `pageSize` | 部署历史 |
| POST | `Rollback` | form: `id` | 回滚到指定作业 |

### 3. AppSettingsController — `admin/AppSettings/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `Get` | — | 当前配置原文+解析对象+元数据 |
| GET | `Download` | — | 下载 appsettings.json |
| POST | `Save` | form: `req`(SaveAppSettingsRequest: RawText/Restart) | 保存配置（restart=true 优雅重启） |
| POST | `Upload` | form: `file`, `restart`(默认true) | 上传替换 appsettings.json（2MB） |
| POST | `Diff` | form: `req` | 变更预览 |
| GET | `History` | — | .bak 历史列表（≤30） |
| GET | `HistoryContent` | query: `fileName` | 查看 .bak 内容 |
| POST | `Rollback` | form: `req`(RollbackRequest: FileName/Restart) | 回滚到 .bak |

### 4. AuthController — `admin/Auth/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| POST | `Login` | form: `username`, `password`, `totpCode` | 管理员登录（**AllowAnonymous** + `EnableRateLimiting("login")`） |
| GET | `GetInfo` | — | 当前登录管理员信息 |
| POST | `Logout` | — | 登出，返回 `Content("2")`（**AllowAnonymous**） |
| POST | `ChangePassword` | form: `oldPassword`, `newPassword` | 修改密码 |
| POST | `ChangeAvatar` | form: `avatar` | 修改头像 |
| POST | `UpdateProfile` | form: `name`, `nickname` | 修改个人资料 |
| POST | `Upload` | form: `file` | 文件上传（**AllowAnonymous**） |
| GET | `GetGoogleCodeInfo` | — | 谷歌验证器绑定信息 |
| GET | `GetGoogleCodeQR` | — | 谷歌验证器二维码 |
| POST | `PostBindGoogleCode` | form: `code`, `password` | 绑定验证器 |
| POST | `UnbindGoogleCode` | form: `totpCode`, `password` | 解绑验证器 |

### 5. BackgroundTaskController — `admin/BackgroundTask/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `List` | — | 任务列表 |
| POST | `Start` | form: `name` | 启动任务 |
| POST | `Stop` | form: `name` | 停止任务 |
| POST | `Restart` | form: `name` | 重启任务 |
| POST | `RunOnce` | form: `name` | 立即执行一次 |
| GET | `GetLogs` | query: `name`, `limit`(默认100) | 任务日志（最新 N 条） |
| GET | `LogList` | query: `source?`, `taskName?`, `logLevel?`, `page`, `pageSize` | 分页日志 |
| POST | `ReloadParameters` | form: `name` | 重载任务参数 |

### 6. DashboardController — `admin/Dashboard/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `Overview` | — | 总览统计 |
| GET | `MemberTrend` | query: `days`(默认30) | 会员趋势 |
| GET | `MemberDistribution` | — | 会员分布 |
| GET | `RecentLogins` | query: `limit`(默认10) | 最近登录 |
| GET | `RecentMembers` | query: `limit`(默认10) | 最近会员 |
| GET | `PendingRealNames` | query: `limit`(默认10) | 待审实名 |
| GET | `LoginTrend` | query: `days`(默认7) | 登录趋势 |

### 7. DatabaseController — `admin/Database/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `CheckBackup` | — | 备份工具可用性检查 |
| POST | `Backup` | — | 触发备份（本地+云端+清理） |
| GET | `List` | query: `field`, `order`, `page`, `pageSize` | 备份记录分页 |
| POST | `Del` | form: `id`, `filePath?` | 删除备份（文件+记录） |
| POST | `Restore` | form: `id`, `filePath?` | 恢复备份（**高危，覆盖整库**） |
| POST | **`/api/Database/Backup`**（绝对路由，无 admin） | — | 自动化/定时备份入口 |
| POST | `UploadToCloud` | form: `id` | 手动上传本地备份到云端 |

### 8. EmailConfigController — `admin/EmailConfig/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `GetConfig` | — | SMTP 配置（密码脱敏） |
| POST | `SaveConfig` | form: `dto`(EmailConfigSaveDto) | 保存 SMTP 配置 |
| GET | `GetTemplates` | query: `page`, `pageSize` | 模板分页 |
| GET | `GetTemplate` | query: `id` | 模板详情 |
| POST | `SaveTemplate` | form: `dto`(EmailTemplateSaveDto) | 新增/更新模板（Code 唯一） |
| GET | `GetLogs` | query: `page`, `pageSize`, `status?`(EmailStatus) | 邮件日志分页 |
| GET | `GetLog` | query: `id` | 日志详情 |
| GET | `GetResendConfig` | — | Resend 配置（ApiKey 脱敏） |
| POST | `SaveResendConfig` | form: `dto`(ResendConfigSaveDto) | 保存 Resend 配置 |
| GET | `GetProvider` | — | 邮件提供商（默认 GoogleSmtp） |
| POST | `SetProvider` | form: `provider` | 设置邮件提供商 |

### 9. LoginLogController — `admin/LoginLog/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `List` | query: `userType?`, `username?`, `status?`, `startDate?`, `endDate?`, `page`, `pageSize` | 登录日志分页 |

### 10. LogViewerController — `admin/LogViewer/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `Files` | — | 日志文件列表 |
| GET | `Read` | query: `fileName`, `tail`(默认500), `archive?` | 读日志内容 |
| GET | `Download` | query: `fileName`, `archive?` | 下载日志文件 |
| GET | `Diag` | query: `date?`, `level?`, `tail`(默认1000), `contains?` | **AI 诊断**：返回 NDJSON 流，摘要放 `X-Diag-*` 头 |

### 11. ManagerController — `admin/Manager/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `List` | query: `username`, `name`, `roleId?`, `field`, `order`, `page`, `pageSize`(默认10) | 管理员分页 |
| POST | `Add` | form: `admin`(Admin) | 添加管理员 |
| POST | `Edit` | form: `admin`(Admin) | 编辑管理员 |
| POST | `Del` | form: `id` | 删除管理员 |
| GET | `Detail` | query: `id` | 管理员详情 |
| POST | `ResetGoogleCode` | form: `adminId` | 超级管理员重置他人验证器 |
| GET | `RoleList` | query: `name`, `roleKey`, `field`, `order`, `page`, `pageSize`(默认10) | 角色分页 |
| GET | `QueryAdminRole` | query: `key` | 按 key 查询角色 |
| POST | `RoleAdd` | form: `adminRole`(AdminRole) | 添加角色 |
| POST | `RoleEdit` | form: `adminRole`(AdminRole) | 编辑角色 |
| POST | `RoleDel` | form: `id` | 删除角色 |
| GET | `RoleDetail` | query: `id` | 角色详情 |
| GET | `GetMenuIds` | query: `roleId` | 角色菜单 ID 列表 |
| GET | `GetMenusDetail` | query: `roleId` | 角色菜单详情 |
| POST | `SetMenus` | form: `roleId`, `menuIds`(逗号分隔) | 批量设置角色菜单 |
| POST | `AddMenu` | form: `roleId`, `menuId` | 角色添加菜单 |
| POST | `RemoveMenu` | form: `roleId`, `menuId` | 角色移除菜单 |
| POST | `CopyMenus` | form: `fromRoleId`, `toRoleId` | 复制角色菜单权限 |
| GET | `GetRolesByMenu` | query: `menuId` | 拥有指定菜单的角色列表 |

### 12. MemberController — `admin/Member/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `List` | query: `id?`, `keyword?`, `status?`(UserStatus), `field`, `order`, `page`, `pageSize` | 会员分页 |
| GET | `Detail` | query: `id` | 会员详情 |
| POST | `Add` | form: `member`(Member) | 新增会员 |
| POST | `Edit` | form: `member`(Member) | 编辑会员 |
| POST | `Del` | form: `id` | 删除会员 |
| POST | `ResetPassword` | form: `id`, `newPassword` | 重置密码 |
| GET | `Search` | query: `keyword?`, `limit`(默认20) | 会员搜索 |
| GET | `ReferralTree` | query: `memberId` | 推荐关系树 |
| GET | `MemberTree` | query: `memberId` | 会员树 |
| GET | `Children` | query: `parentId?` | 下级会员 |

### 13. MenuController — `admin/Menu/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `All` | — | 当前管理员权限菜单（前端路由来源） |
| POST | `UpdateMenu` | form: `id`, `field`, `value` | 更新菜单属性 |
| GET | `TreeList` | query: `name`, `title`, `field`, `order`, `page`, `pageSize`(默认10) | 权限菜单树列表 |
| GET | `List` | 同上 | 权限菜单列表 |
| GET | `QueryMenu` | query: `key` | 按 key 查询菜单 |
| POST | `Add` | form: `menu`(Menu) | 添加菜单 |
| POST | `Edit` | form: `menu`(Menu) | 更新菜单 |
| POST | `Del` | form: `id` | 删除菜单 |
| GET | `Detail` | query: `id` | 菜单详情 |

### 14. RealNameController — `admin/RealName/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `List` | query: `status?`(RealNameStatus), `keyword?`, `field`, `order`, `page`, `pageSize` | 实名认证分页 |
| GET | `Detail` | query: `id` | 实名详情 |
| POST | `Approve` | form: `id` | 审核通过 |
| POST | `Reject` | form: `id`, `reason?` | 审核拒绝 |

### 15. SetupController — `/api/Setup/[action]`（绝对）· `[AllowAnonymous]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `Status` | — | 安装状态（`{installed}` 布尔） |
| GET | `DeployHints` | — | 推断默认部署路径 |
| POST | `TestDatabase` | **FromBody** `config`(DatabaseConfigDto) | 测试数据库连接 |
| POST | `TestRedis` | **FromBody** `config`(RedisConfigDto) | 测试 Redis 连接 |
| POST | `Install` | **FromBody** `request`(SetupRequest) | 执行安装（成功后延迟优雅重启） |

### 16. SqlController — `admin/Sql/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| POST | `Execute` | form: `sql` | 执行 SQL |
| POST | `ExportCsv` | form: `sql` | 导出查询结果为 CSV |

### 17. SystemSettingCategoryController — `admin/SystemSettingCategory/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `List` | query: `key`, `name`, `field`, `order`, `page`, `pageSize`(默认10) | 设置分类分页 |
| GET | `QuerySystemSettingCategory` | query: `key` | 按 key 查询分类 |
| POST | `Add` | form: `systemSettingCategory` | 添加分类 |
| POST | `Edit` | form: `systemSettingCategory` | 更新分类 |
| POST | `Del` | form: `id` | 删除分类 |
| GET | `Detail` | query: `id` | 分类详情 |

### 18. SystemSettingController — `admin/SystemSetting/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| **⚠️ 无** | `List` | — | ★**缺陷：无 HTTP verb 属性，属性路由不可达** |
| GET | `GetCategoryWithSettingList` | query: `categoryId?`, `field`, `order`, `page`, `pageSize` | 分类+设置项列表 |
| GET | `QuerySystemSetting` | query: `key` | 按 key 查询 |
| GET | `GetByKey` | query: `key` | 按 Key 精确查询单条 |
| POST | `Add` | form: `systemSetting` | 添加设置 |
| POST | `Edit` | form: `systemSetting` | 更新设置 |
| POST | `SaveList` | form: `settings`(JSON 数组文本) | 批量保存 |
| POST | `Del` | form: `id` | 删除设置 |
| GET | `Detail` | query: `id` | 设置详情 |

### 19. UploadController — `admin/Upload/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `Config` | — | 上传配置（大小上限/并发/扩展名/UrlPrefix） |
| POST | `Direct` | form: `file`（2GB） | 直传单文件 |
| POST | `ChunkInit` | form: `fileName`, `fileSize`, `fileMd5` | 分片初始化（秒传/断点续传检测） |
| POST | `ChunkUpload` | form: `uploadId`, `chunkIndex`, `chunk`(20MB) | 上传单分片 |
| POST | `ChunkMerge` | form: `uploadId` | 合并分片 |
| GET | `ChunkProgress` | query: `uploadId` | 上传进度查询 |

### 20. VerificationController — `admin/Verification/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `Status` | — | 授权状态+客户端标识（**AllowAnonymous**） |
| POST | `RequestLicense` | query: `domain`(推断) | 向授权服务器申请 |
| POST | `CheckUpdate` | query: `serverUrl?`, `actualDomain?` | 查询新授权 |
| POST | `ApplyLicense` | query: `recordId`, `serverUrl?` | 下载并应用新授权 |
| POST | `ApplyKey` | query: `licenseKey?` | 离线应用授权 Key |

---

## 二、SB.Feature.AccessToken

### 21. AccessTokenController — `/admin/AccessToken/[action]`（绝对，显式含 admin）· `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| POST | `Create` | form: `name`, `accountId?`, `serviceAccount?`, `scopes?`, `fullAccess`(默认false), `expiresAt?`, `rateLimitPerMinute?`, `ipAllowlist?`, `notes?` | 签发 key（**明文仅返回一次**；FullAccess 需 super + `EnableFullAccess`） |
| GET | `List` | query: `keyword?`, `accountId?`, `page`, `pageSize` | key 分页（不含 TokenHash） |
| GET | `Detail` | query: `id` | key 详情 |
| POST | `Update` | form: `id`, `name?`, `scopes?`, `expiresAt?`, `rateLimitPerMinute?`, `ipAllowlist?`, `notes?` | 更新 key |
| POST | `Revoke` | form: `id`, `reason?` | 吊销 key |
| GET | `Audit` | query: `keyId?`, `action?`, `page`, `pageSize` | 审计日志分页 |

---

## 三、SB.Feature.BackupReceive

### 22. BackupReceiveController — 控制器级无 `[Route]`，action 级绝对路由

| Verb | Action | 路由 | 参数 | 行为 |
|------|--------|------|------|------|
| POST | `Upload` | `/api/BackupReceive/Upload` | form: `file`, `productEn`, `productCn`, `primaryDomain?`, `logoBase64?`, `backupUtcTime?`, `md5?` | 客户端上传备份（**AllowAnonymous**，2GB，取 X-Forwarded-For IP） |
| GET | `ProductList` | `/admin/BackupReceive/ProductList` | — | 按产品聚合的备份列表 |
| GET | `RecordList` | `/admin/BackupReceive/RecordList` | query: `productEn`, `page`, `pageSize` | 单产品备份记录分页 |
| GET | `Download` | `/admin/BackupReceive/Download/{id:int}` | route: `id` | 下载备份 zip |
| POST | `Delete` | `/admin/BackupReceive/Delete` | form: `id` | 管理员删除备份 |

> 设计：客户端 Anonymous 上传通道**无删除能力**；删除仅存在于 admin 通道。

---

## 四、SB.Feature.LicenseManage

### 23. EntitlementController — `admin/Entitlement/[action]` · `[Authorize(JwtAdmin)]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| GET | `GetPublicKey` | — | 公钥 XML + 是否有密钥对 |
| POST | `RegenerateKeyPair` | — | 重新生成密钥对（旧授权失效） |
| GET | `ClientList` | query: `page`, `pageSize`, `keyword?` | 客户分页 |
| GET | `ClientDetail` | query: `id` | 客户详情 |
| POST | `ClientEdit` | query(推断): `id`, `remark?`, `status?` | 编辑客户 |
| GET | `RequestList` | query: `page`, `pageSize`, `status?` | 授权申请分页 |
| POST | `ApproveRequest` | query(推断): `requestId`, `expiresAt`, `verifyMode`(默认Both) + **FromBody**: `allowedRoutes?` | 审批申请 |
| POST | `RejectRequest` | query(推断): `requestId`, `reason?` | 拒绝申请 |
| GET | `RecordList` | query: `page`, `pageSize`, `clientId?` | 授权记录分页 |
| POST | `IssueLicense` | **FromBody** `req`(IssueLicenseRequest) | 统一签发授权 |
| POST | `QuickGenerate` | **FromBody** `request`(QuickGenerateRequest) | 快速签发（粘贴客户端信息） |
| POST | `DeleteClient` | query(推断): `id` | 软删除客户 |
| POST | `RevokeLicense` | query(推断): `recordId` | 吊销授权 |
| GET | `VerifyLogList` | query: `page`, `pageSize`, `clientId?` | 验证日志分页 |

### 24. VerifyController — `/api/verify/[action]`（绝对）· `[AllowAnonymous]`

| Verb | Action | 参数 | 行为 |
|------|--------|------|------|
| POST | `Heartbeat` | **FromBody** `request`(HeartbeatRequest) | 客户端心跳上报 |
| POST | `Request` | query(推断): `domain`, `productEn?`, `productCn?`, `clientKey?`, `hardwareId?`, `machineName?`, `osDescription?`, `processorCount?`, `macAddresses?`, `serverIP?` | 客户端申请授权 |
| POST | `Verify` | query(推断): `domain?`, `actualDomain?`, `productEn?`, `productCn?`, `clientKey?`, `hardwareId?`, `currentKey?`, `serverIP?`, `licenseValid`(默认false), `message?` | 验证上报+吊销查询（返回 `{revoked}`） |
| GET | `Check` | query(推断): `domain?`, `actualDomain?`, `productEn?`, `productCn?`, `clientKey?`, `hardwareId?`, `machineName?`, `osDescription?`, `processorCount?`, `macAddresses?`, `currentKey?` | 查询可下载新授权 |
| GET | `Download` | query: `recordId` | 下载授权文件 |

---

## 五、SB.FlowEngine

**无 Controller**（纯图执行引擎，业务节点经 `IFlowNodeExecutor` 接入）。

---

## 汇总

| # | 工程 | Controller | Action 数 |
|---|------|-----------|-----------|
| 1 | SB.Framework.WebApi | ApiController | 14 |
| 2 | SB.Framework.WebApi | AppController | 18 |
| 3 | SB.Framework.WebApi | AppSettingsController | 8 |
| 4 | SB.Framework.WebApi | AuthController | 11 |
| 5 | SB.Framework.WebApi | BackgroundTaskController | 8 |
| 6 | SB.Framework.WebApi | DashboardController | 7 |
| 7 | SB.Framework.WebApi | DatabaseController | 7 |
| 8 | SB.Framework.WebApi | EmailConfigController | 11 |
| 9 | SB.Framework.WebApi | LoginLogController | 1 |
| 10 | SB.Framework.WebApi | LogViewerController | 4 |
| 11 | SB.Framework.WebApi | ManagerController | 19 |
| 12 | SB.Framework.WebApi | MemberController | 10 |
| 13 | SB.Framework.WebApi | MenuController | 9 |
| 14 | SB.Framework.WebApi | RealNameController | 4 |
| 15 | SB.Framework.WebApi | SetupController | 5 |
| 16 | SB.Framework.WebApi | SqlController | 2 |
| 17 | SB.Framework.WebApi | SystemSettingCategoryController | 6 |
| 18 | SB.Framework.WebApi | SystemSettingController | 9（含 1 个不可达） |
| 19 | SB.Framework.WebApi | UploadController | 6 |
| 20 | SB.Framework.WebApi | VerificationController | 5 |
| 21 | SB.Feature.AccessToken | AccessTokenController | 6 |
| 22 | SB.Feature.BackupReceive | BackupReceiveController | 5 |
| 23 | SB.Feature.LicenseManage | EntitlementController | 14 |
| 24 | SB.Feature.LicenseManage | VerifyController | 5 |
| — | SB.FlowEngine | （无） | 0 |

**合计：24 个 Controller，194 个 Action**（其中 `SystemSettingController.List` 因缺 verb 属性，属性路由不可达）。

**AllowAnonymous 汇总**：
- `ApiController`：AuthLogin / AuthRegister / AuthForgotPassword / SendVerificationCode / VerifyCode / Upload
- `AuthController`：Login / Logout / Upload
- `SetupController`：全部
- `VerificationController`：Status
- `VerifyController`：全部
- `BackupReceiveController`：Upload
