# Entity Models & Enums

> 本文档为**实体事实源**，逐类对照源码生成（21 个表实体）。若与旧文档冲突，以此为准。
> 所有表前缀 `SB_`（除 `BackgroundTaskLog` 为 `SYS_`）。实现 `ISoftDelete` 的实体自动应用全局过滤 `IsDeleted == false`。

## 核心接口与统一类型

### IEntity（`SB.Framework.Models/IEntity.cs`）

```csharp
public interface IEntity { int Id { get; set; } }
```

### ISoftDelete（`SB.Framework.Models/ISoftDelete.cs`）

```csharp
public interface ISoftDelete { bool IsDeleted { get; set; } }
```

实现 `ISoftDelete` 的实体：**Admin、AdminRole、Member、Menu、ApiKey**。

### IModuleInitializer（`SB.Framework.Models/IModuleInitializer.cs`）

模块声明式装配接口。`GetEntityTypes / GetSystemSettings / GetMenus` 必须**纯声明**（不访问实例字段），因为安装向导 `GetUninitializedObject` 创建实例时**不调构造函数**。

```csharp
public interface IModuleInitializer
{
    string ModuleName { get; }
    string ModuleKey => GetType().Name;          // Modules:Disabled 黑名单匹配
    Type[] GetOwnedTypes() => Array.Empty<Type>();
    string[] GetOwnedControllerNames() => Array.Empty<string>();
    int InitOrder => 0;                          // 越小越先执行
    Type[] GetEntityTypes() => Array.Empty<Type>();
    List<SystemSettingDefinition> GetSystemSettings() => new();
    List<MenuDefinition> GetMenus() => new();
    void SeedData(IFreeSql fsql) { }
}
```

### Result（`SB.Framework.Models/Result.cs`）

统一响应 `{ code, message, data }`（`Data` 为 null 时 JSON 忽略）：

| 静态方法 | 行为 |
|----------|------|
| `Success(object data)` | `{ Code=0, Message="ok", Data }` |
| `Success(string message, object data = null)` | `{ Code=0, Message=message, Data }` |
| `Fail(string message = "操作失败", int code = -1)` | `{ Code=code, Message=message }` |
| `Page(object items, long total, string message = "ok")` | `{ Code=0, Data={ items, total } }` |

### MenuDefinition（`SB.Framework.Models/MenuDefinition.cs`）

`ParentPath?`（挂载到已存在菜单 Path，null=顶级）、`Name`、`Path`（唯一标识）、`Component`（顶级用 "LAYOUT"）、`Title`、`Icon`、`OrderNo`、`Redirect?`、`HideInMenu`、`Children`。

---

## 一、Admin 域

### Admin — 表 `SB_Admin` · `IEntity, ISoftDelete`

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | int PK identity | 主键 |
| ParentId | int (=0) | 父级 Id（树） |
| Username | string(50) | 用户名 |
| Password | string(200) | BCrypt hash 或明文（按 PasswordConfig.StorageMode） |
| RoleId | int | 角色 Id |
| AdminRole | AdminRole | 导航 `[Navigate(RoleId)]` |
| Name | string | 名称 |
| Nickname | string?(50) | 客服昵称（对外展示，保护真实账号） |
| Avatar | string | 头像 |
| Status | AdminStatus (=Active) | 0=Active, 1=Disabled |
| AuthenticatorKey | string? | Google 2FA 秘钥 |
| AuthenticatorBindTime | DateTime? | 验证器绑定时间 |
| CreatedTime | DateTime | `now()`，不可更新 |
| LastOnlineTime | DateTime? | 最后登录时间 |
| AccountType | AccountType (=Human, InsertValueSql="0") | 0=Human(密码登录), 1=Service(仅 API Key) |
| IsDeleted | bool | 软删除 |

### AdminRole — 表 `SB_AdminRole` · `IEntity, ISoftDelete`

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | int PK | 主键 |
| Name | string(50) | 角色名称 |
| RoleKey | string(50) | 角色标识 |
| Admins | List\<Admin\> | 导航 `[Navigate(ManyToMany=typeof(AdminRole))]` |
| RoleMenu | List\<AdminRoleMenu\> | 导航 `[Navigate(AdminRoleMenu.RoleId)]` |
| IsDeleted | bool | 软删除 |

### AdminRoleMenu — 表 `SB_AdminRoleMenu` · **无接口**（主键仍为 Id）

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | int PK | 主键 |
| RoleId | int | 角色 ID |
| MenuId | int | 菜单 ID |
| AdminRole / Menu | 导航 | `[Navigate]` |

### Member — 表 `SB_Member` · `IEntity, ISoftDelete`

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | int PK | 主键 |
| Username | string | 用户名 |
| Password | string | 密码 |
| Avatar | string | 头像 |
| Name | string | 名称 |
| Status | UserStatus（**以字符串存储** MapType=string） | 0=Active, 1=Banned |
| CreatedTime | DateTime | `now()`，不可更新 |
| LastOnlineTime | DateTime | `now()` |
| PaymentPassword | string? | 支付密码 |
| CreditScore | int (=100) | 信用分 |
| RealNameStatus | RealNameStatus (=Unverified) | 实名状态 |
| InvitationCode | string? | 邀请码 |
| ParentId | int? | 邀请人 ID |
| AncestryPath | string?(500) | 祖先路径 `/1/5/` |
| IsDeleted | bool | 软删除 |

### MemberRealName — 表 `SB_MemberRealName` · `IEntity`

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | int PK | 主键 |
| MemberId | int | 会员 Id |
| RealName | string | 真实姓名 |
| IdNumber | string | 身份证号 |
| IdFrontImage / IdBackImage | string | 证件照 |
| Status | RealNameStatus（字符串存储, =Reviewing） | 0=Unverified,1=Reviewing,2=Verified,3=Rejected |
| RejectReason | string? | 拒绝原因 |
| SubmitTime | DateTime | `now()`，不可更新 |
| ReviewTime | DateTime? | 审核时间 |
| ReviewedBy | int? | 审核人 |

---

## 二、Menu 域

### Menu — 表 `SB_Menu` · `IEntity, ISoftDelete`

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | int PK | 主键 |
| ParentId | int | 父级 Id |
| ParentMenu | Menu | 导航 |
| Name | string(100) | 名称 |
| Path | string(200) | 路由路径 |
| Component | string | 组件路径 |
| Redirect | string | 重定向 |
| Title | string(100) | 显示标题 |
| Icon | string | 图标 |
| OrderNo | int | 排序 |
| HideInMenu | bool | 菜单中隐藏 |
| IgnoreKeepAlive | bool | 忽略 keep-alive |
| IsDeleted | bool | 软删除 |

---

## 三、系统设置域

### SystemSetting — 表 `SB_SystemSetting` · `IEntity`

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | int PK | 主键 |
| CategoryId | int | 分类 |
| Category | SystemSettingCategory? | 导航 |
| Key | string(100) | 设置系统键 |
| Name | string(100) | 显示名 |
| DisplayType | SystemSettingDisplayType | 见枚举 |
| CantDelete | bool | 禁止删除 |
| DataContent | string?(max) | 设置值 |
| DataContentConfig | string(max) | 选项配置 JSON |
| Sort | int | 排序 |

**`SystemSettingDefinition`**（非表，供 `GetOrCreate` 自动注册）：Key/Name/CategoryName/CategoryIcon/CategorySort/DisplayType/DefaultValue/DataContentConfig/CantDelete/Sort。

### SystemSettingCategory — 表 `SB_SystemSetting_Category` · `IEntity`

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | int PK | 主键 |
| Name | string(50) | 设置名称 |
| Icon | string | 图标 |
| Sort | int | 排序 |
| SystemSettingList | List\<SystemSetting\> | 导航 |

---

## 四、邮件域

### EmailConfig — 表 `SB_EmailConfig` · `IEntity`
SmtpServer / SmtpPort / Username / Password / FromEmail / FromName / EnableSsl / IsActive / CreatedAt(`now()`不可更新) / UpdatedAt

### EmailLog — 表 `SB_EmailLog` · `IEntity`
TemplateId / ToEmail / Subject / Body(text) / Status(EmailStatus, **字符串存储**) / ErrorMessage? / Provider?(50) / SentAt? / CreatedAt(`now()`不可更新)

### EmailTemplate — 表 `SB_EmailTemplate` · `IEntity`
Code / Name / Subject / Body(text) / IsActive / CreatedAt(`now()`不可更新) / UpdatedAt

### EmailVerificationCode — 表 `SB_EmailVerificationCode` · `IEntity`
Email / Code / Type(VerificationCodeType) / VerifyCount / IsUsed / VerifiedAt? / IpAddress?(50) / ExpireAt / CreatedAt(`now()`不可更新)

---

## 五、上传域

### ChunkUpload — 表 `SB_ChunkUpload` · `IEntity`

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | int PK | 主键 |
| UploadId | string(32) | 上传标识（GUID） |
| FileName | string(500) | 原始文件名 |
| FileSize | long | 字节 |
| FileMd5 | string(32) | 文件 MD5 |
| ChunkSize | int | 每片字节 |
| TotalChunks | int | 总分片数 |
| Status | ChunkUploadStatus (=Uploading) | 0=上传中,1=已完成,2=已过期 |
| FilePath | string(500) | 合并后相对路径 |
| CreatedAt | DateTime | `now()` |
| CompletedAt | DateTime? | 完成时间 |

### ChunkUploadPart — 表 `SB_ChunkUpload_Part` · `IEntity`
UploadId(32) / ChunkIndex / ChunkSize / UploadedAt(`now()`)

---

## 六、备份域

### DatabaseBackup — 表 `SB_DatabaseBackup` · `IEntity`

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | int PK | 主键 |
| DatabaseName | string(100) | 数据库名 |
| FileName | string(200) | 备份文件名 |
| FilePath | string(500) | 相对路径 `/storage/db/xxx.zip` |
| FileSize | long | 字节 |
| Duration | int | 耗时（毫秒） |
| CreatedBy | string(100) | 操作人（管理员用户名或访问令牌账号） |
| CreatedTime | DateTime | `now()`，不可更新 |
| Remark | string?(500) | 备注 |
| Md5 | string?(64) | 备份 MD5（完整性校验/防篡改/去重） |
| RemoteUploadStatus | string?(16) | null=未上传 / "success" / "failed" |
| RemoteUploadTime | DateTime? | 云端上传成功时间 (UTC) |
| RemoteUploadError | string?(max) | 失败原因 |
| RemoteUploadUrl | string?(500) | 实际上传 URL |

### BackupRemoteRecord — 表 `SB_BackupRemoteRecord` · `IEntity`（`SB.Feature.BackupReceive` 项目根）

客户端经 `POST /api/BackupReceive/Upload` 上传时落盘+写记录。**不开放删除接口**。
ProductEn(50) / ProductCn(100) / PrimaryDomain?(200) / LogoPath?(500) / ClientIp(64, 经 X-Forwarded-For) / FileName(200) / FilePath(500) / FileSize / Md5?(64) / BackupTime(UTC) / ReceivedTime(`now()`不可更新, UTC)

---

## 七、后台任务 / 日志域

### BackgroundTaskLog — 表 `SYS_BackgroundTaskLog` · `IEntity`（⚠️ 前缀是 `SYS_` 非 `SB_`）

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | int PK | 主键 |
| Source | LogSource (=Task) | 0=定时任务, 1=系统异常 |
| TaskName | string(200) | 任务名 |
| LogLevel | TaskLogLevel | 0=Error,1=Warning,2=Info |
| Message | string(2000) | 消息 |
| Exception | string?(text) | 异常堆栈 |
| CreatedTime | DateTime | `now()`，不可更新 |
| ExecutionRecordId | string?(50) | 执行记录 |

### LoginLog — 表 `SB_LoginLog` · `IEntity`

UserId / UserType(0=H5会员,1=Admin) / Username(100) / IpAddress?(50) / UserAgent?(500) / Status(0=成功,1=失败) / CreateTime(`now()`不可更新)

### DeployJob — 表 `SB_DeployJob` · `IEntity`

> 状态机：`Uploaded → Deploying → Deployed | Failed`，`Deployed → RolledBack`。回滚产生新行，`RollbackOfJobId` 指向被回滚 job。

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | int PK | 主键 |
| Target | string(50) | api 或 DeployConfig key |
| TargetName | string(100) | 目标显示名 |
| FileName | string(200) / FileSize long / StoragePath string(500) | 文件信息 |
| Status | string(20, =“Uploaded”) | 字符串常量（非枚举），见下 |
| ErrorMessage | string?(1000) | 失败原因 |
| UploadedBy / DeployedBy / RolledBackBy | string(100)? | 操作人 |
| UploadedAt / DeployStartedAt? / DeployFinishedAt? / RolledBackAt? | DateTime | 时间轴 |
| PreviousJobId | int? | 本次覆盖的 job（回滚目标） |
| RollbackOfJobId | int? | 本 job 是哪个 job 的回滚 |
| Remark | string?(500) | 备注 |

`DeployJobStatus`（static class 常量）：`Uploaded / Deploying / Deployed / Failed / RolledBack`。

---

## 八、插件域（SB.Feature.AccessToken）

### ApiKey — 表 `SB_ApiKey` · `IEntity, ISoftDelete` · 带 `[Index]`

索引：`idx_apikey_token_hash`(唯一)、`idx_apikey_account`、`idx_apikey_name`、`idx_apikey_updated`。

> API Key 挂在某个账号（SB_Admin）上的凭据。**明文只在签发时回显一次**，库中只存 sha256/HMAC hash。

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | int PK | 主键 |
| AccountId | int | 归属账号 SB_Admin.Id |
| Name | string(100) | 名称 |
| TokenHash | string(64) | sha256/HMAC(明文) hex；明文不入库 |
| TokenPrefix | string(16) | 明文前 12 位，list 显示用 |
| Scopes | string?(500) | 逗号分隔 scope；null=所有 `[Scope]` 端点放行 |
| FullAccess | bool | **god**：忽略 scope、穿透所有端点；仅 super 可签发 |
| IpAllowlist | string?(500) | 允许 IP/CIDR |
| ExpiresAt | DateTime? | 过期时间 |
| LastUsedAt | DateTime? | 最后使用 |
| RateLimitPerMinute | int? | 每分钟上限（0/null=不限） |
| Notes | string?(2000) | 备注 |
| CreatedBy / ModifiedBy | string(50)? | 操作人 |
| RevokeReason | string?(500) | 吊销原因 |
| CreatedTime / UpdatedTime? | DateTime | 时间轴 |
| IsDeleted | bool | 软删除 |

### ApiKeyAuditLog — 表 `SB_ApiKey_Audit` · `IEntity` · 带 `[Index]`

索引：`idx_apikey_audit_key` / `idx_apikey_audit_time` / `idx_apikey_audit_action`。

> 记录签发/吊销/更新/鉴权失败/限流等关键事件；成功的每次 "Used" 不逐条写（仅 LastUsedAt 节流更新）。

KeyId（未命中为 0） / KeyName(100) / TokenPrefix(16) / Action(20: Created/Revoked/Updated/AuthFailed/Denied/RateLimited/Throttled/Expired) / Result(10: Success/Denied) / IpAddress?(45) / RequestPath?(500) / DenyReason?(200) / Operator?(50) / CreatedTime

---

## 九、DTO（非表，`SB.Framework.Models/Dto/`）

| DTO | 字段 |
|-----|------|
| DatabaseConfigDto | DbType="MySql" / Server="127.0.0.1" / Port="3306" / Database="Saibo" / User="root" / Password |
| RedisConfigDto | Connection="localhost:6379" / InstanceName="SB:" |
| DeployProjectDto | Name / Key / Path |
| SetupRequest | Database / WipeDatabase=false / EnableCache=false / CacheProvider="Memory" / Redis / PasswordStorageMode="Encrypted" / ProductCn="管理系统" / ProductEn="sb" / PrimaryDomain? / CachePrefix="sb:" / LogoBase64? / TimeZone="Asia/Shanghai" / CorsAllowAll=true / CorsOrigins / AdminUsername="admin" / AdminPassword / AdminDisplayName="超级管理员" / CloudServerUrl? / DeployProjects |
| UploadConfigDto | MaxFileSizeMB / ChunkThresholdMB / MaxConcurrent / AllowedExtensions / UrlPrefix |
| VbenMenu + Meta | 前端菜单树：Id/ParentId/Path/Name/Component(空→"PARENT")/Redirect/Meta(Title/Icon/OrderNo/HideInMenu/IgnoreKeepAlive)/Title/OrderNo |

---

## 十、枚举总表

### 全局枚举（`SB.Framework.Models/Enums.cs`）

| 枚举 | 成员 |
|------|------|
| UserStatus | Active=0, Banned=1 |
| RealNameStatus | Unverified=0, Reviewing=1, Verified=2, Rejected=3 |
| EmailStatus | Pending=0, Sent=1, Failed=2 |
| VerificationCodeType | Register=0, ResetPassword=1 |

### 文件内联枚举

| 枚举 | 定义 | 所在文件 |
|------|------|---------|
| AdminStatus | Active=0, Disabled=1 | Admin.cs |
| AccountType | Human=0, Service=1 | Admin.cs |
| SystemSettingDisplayType | Input=0..FileUpload=18（共 19 项：Input/InputTextArea/RadioGroup/Select/CheckboxGroup/Switch/ImageUpload/InputNumber/InputPassword/DatePicker/TimePicker/RangePicker/Rate/AutoComplete/Mentions/TreeSelect/IconPicker/MultiImageUpload/FileUpload） | SystemSetting.cs |
| ChunkUploadStatus | Uploading=0, Completed=1, Expired=2 | ChunkUpload.cs |
| TaskLogLevel | Error=0, Warning=1, Info=2 | BackgroundTaskLog.cs |
| LogSource | Task=0, Exception=1 | BackgroundTaskLog.cs |
| DeployJobStatus | static class 常量（字符串） | DeployJob.cs |

### Qdrant 类型（非表，`SB.Framework.Models/Qdrant/`）

- `QdrantDistance`：Cosine=0, Euclid=1, Dot=2, Manhattan=3
- `QdrantOptions`（配置段 `Sb:Qdrant`）：Host="localhost", Port=6334, UseHttps, ApiKey?
- `SearchHit`：Id(Guid), Score, Payload
- `VectorPoint`：Id(Guid), Vector, Payload?

---

## 关键事实

1. 共 **21 个表实体**：17 个在 Models 根目录，2 个在 AccessToken 插件，1 个 BackupRemoteRecord（BackupReceive 项目根），LoginLog。
2. 实现 `ISoftDelete`：Admin、AdminRole、Member、Menu、ApiKey。
3. `AdminRoleMenu` 未实现 `IEntity`（主键仍为 Id）。
4. **实体/属性无 `[Obsolete]` 标记**（仅 AppController 4 个 action 有）。
5. `StringLength=-2`（max/text）：SystemSetting.DataContent、SystemSetting.DataContentConfig、DatabaseBackup.RemoteUploadError。
6. 字符串存储枚举列：Member.Status、MemberRealName.Status、EmailLog.Status。
7. ⚠️ `BackgroundTaskLog` 表前缀是 **`SYS_`**（非 `SB_`）——写 SQL/文档时注意。
