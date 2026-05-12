# SB Conventions（开发规范 / 已踩过的坑）

> 这份文档面向 AI 和派生项目开发者。所有从 sb 框架 fork 出去的下游项目继承同一套基座代码，必然继承同样的约定和坑——遵守这里的规范就能少走弯路。

---

## 1. API 响应 / 前端 toast

### 后端
- 所有 Controller 接口返回 `Result`：`Result.Success(data, message?)` / `Result.Fail(message)` / `Result.Page(items, total)`
- 序列化为 `{ code: 0|-1, message, data }`，`code === 0` 表示成功

### 前端 axios 拦截器（`apps/web-antd/src/api/request.ts`）
- `code === 0` 时**自动** `message.success(body.message || '操作成功')`
- 失败时**自动** `message.error(body.message || '操作失败')` 后 `throw`

### 业务代码约定
- **不要**在业务代码里写 `message.success(...)` / `message.error(...)` 跟 API 调用配对——会双 toast
- **不要**在 try/catch 里手动 `message.error(e.message)`——拦截器已经弹过 + throw，业务再弹一次重复
- 写新接口调用：只 `await api.xxx(...)`，让拦截器统一弹后端 message
- 后端 `Result.Success("准确文案")` 把成功文案写好，前端会自动展示

### 例外（保留 message.success/error）
1. **纯前端操作**：剪贴板复制（navigator.clipboard.writeText 后）、纯 UI 提示
2. **本地表单校验**：`message.error('请输入xxx')`
3. **silent 模式 API**：`api.post(url, data, { silent: true })` 跳过拦截器，业务自己弹
4. **业务事件提示**：非 API 直接产生的状态提示（如 LicenseUpdatePanel 的"授权已被吊销"）

---

## 2. UI 规范

### 按钮
- **禁用** `type="link"` 文字按钮 → 用实体按钮（`type="primary"` / `type="default"` / `danger`）
- 危险操作（删除/重置）→ `danger` + 二次确认（`Modal.confirm`）

### 表格
- **不展示 ID 列**：主键 ID 对用户无意义且占空间
- **表格高度链路**：vxe-grid / antd Table `height: auto` 时必须从 Page 到表格全链路 `height: 100% + min-height: 0`
  常见破坏点：Tabs / Drawer / wrapper 嵌套漏掉一环就会无限增高
- 行高自定义：`rowConfig: { isHover: true, height: 80 }`（行内多 Tag 竖排时用）

### 颜色（**重要**）
- **绝对不能硬编码颜色**：`#fff7e6` / `rgb(...)` / `yellow` / `red` 都禁止
- 必须走主题 token（CSS 变量），同时支持亮 / 暗主题切换

#### 项目用的 token 体系（混合）
| 场景 | 用什么 token | 范围 |
|------|------------|------|
| **antd 组件内部** + `:deep` 覆盖 | `var(--ant-color-*)` | antd ConfigProvider 作用域内 |
| **自研组件** / 自绘 div | `hsl(var(--muted))` / `hsl(var(--destructive))` 等 shadcn token | `:root` 全局 |
| antd Tag / Alert 状态色 | 用组件 prop：`<Tag color="warning">` / `<Alert type="warning">` | 不需要外部 CSS |

#### **重要踩坑**
> **`var(--ant-color-*)` 不挂在 `:root`**！只在 antd 组件内部作用域有定义。
> 在自定义 div 里用 `var(--ant-color-error)` → 浏览器解析不到 → 整个 `background` declaration 被丢弃 → **元素透明 / 看不见**。
> 自研组件要用项目 shadcn 体系的 `hsl(var(--muted))` / `hsl(var(--destructive))` / `hsl(var(--foreground))` 等（定义在 `packages/@core/base/design/src/design-tokens/`）。

#### antd 5 cssVar 模式常用 token 速查（覆盖 antd 组件用）
- 容器/卡片：`--ant-color-bg-container` / `--ant-color-bg-elevated` / `--ant-color-bg-layout`
- 次级填充：`--ant-color-fill-secondary` / `--ant-color-fill-tertiary`
- 警告（黄）：`--ant-color-warning-bg` / `--ant-color-warning`
- 错误（红）：`--ant-color-error-bg` / `--ant-color-error`
- 成功（绿）：`--ant-color-success-bg` / `--ant-color-success`
- 文字：`--ant-color-text` / `--ant-color-text-secondary` / `--ant-color-text-tertiary` / `--ant-color-text-quaternary`
- 边框：`--ant-color-border` / `--ant-color-border-secondary` / `--ant-color-split`

### 时间显示
- **时间组件用项目自研 `TimeTag`**（`apps/web-antd/src/components/TimeTag`）
- 后端时间是 UTC（带 Z 后缀）→ 前端按 `appTimeZone` 格式化（从 `/admin/Verification/Status` 拉）
- TimeTag `time` 为空或纯占位符（`-` / `--`）时不显示时区行（避免"-- + Asia/Shanghai +8"）
- TimeTag `trackColor` prop 自定义轨道色（用 shadcn `:root` token，不要传 `--ant-color-*`）

### 进度条
- 用项目自研 `TimeProgress`（`apps/web-antd/src/components/TimeProgress`）
- 默认 `mode="elapsed"`：进度 = (now - startTime) / (endTime - startTime)，进度条满 = 时间用完 = 紧迫
- `trackColor` 同理用 shadcn `:root` token

---

## 3. 后端规范

### 接口源约定(参数绑定,**强制**)

framework 全部 controller 参数用 **`[FromForm]`**(不是 `[FromBody]`),前端按 form-urlencoded 发请求。

| 场景 | 后端 | 前端 |
|------|------|------|
| 普通业务接口(login / CRUD / 设置等)| `[FromForm] T req` 或 `[FromForm] string foo, [FromForm] int bar` | `api.post(url, { foo, bar })` — plain object,axios v1 看到默认 form-urlencoded Content-Type **自动**序列化为 form 串(`foo=...&bar=...`)|
| 文件上传 | `IFormFile file`(implicit `[FromForm]`)+ 可选 `[FromForm] string key` | `new FormData() + formData.append('file', file)`,axios 自动设 `multipart/form-data` + boundary |
| **极少数 JSON 接口** | `[FromBody]` (e.g. Setup 走 fetch + JSON 旁路,不走 axios 拦截器)| 必须由 caller 显式 `headers: { 'Content-Type': 'application/json' }`,扩展 `api.ts` 接口加 headers 选项 |

**禁忌**:
- ❌ `[FromBody]` + axios 默认拦截器 → 后端拿到 form 串,model 全 null
- ❌ FormData 上传时硬编码 Content-Type=form-urlencoded → 浏览器仍发 multipart 但 header 错,`IFormFile` 解不到,"文件不能为空"
- ❌ 改 `apps/web-antd/src/api/request.ts` 全局拦截器把默认 Content-Type 改成 application/json → 直接破整个 framework 的 28+ `[FromForm]` 接口(login 首当其冲)

**踩雷史**(2026-05):AppSettings/Save 当时用 `[FromBody]` 期望 JSON,前端拦截器默认 form 报 415。一度改全局拦截器为 JSON → login + 文件上传 + 系统设置等所有 form 接口集体崩。最终修法是**统一后端用 `[FromForm]`**,axios 拦截器只为 FormData 加一个跳过分支(让浏览器自动设 multipart)。

### Controller 命名
- **不加 `Admin` 前缀**:直接业务名 → `MemberController` / `EmailConfigController` / `RoleController`
- 路由约定 `AdminRoutePrefixConvention` 自动给所有控制器加 `admin/` 前缀

| 类型 | 路由 | 最终 URL | Auth |
|------|------|---------|------|
| Admin 后台 | `[Route("[controller]/[action]")]` | `/admin/X/Y` | `JwtAdmin` |
| 客户端 API | `[Route("/api/[controller]/[action]")]` | `/api/X/Y`（绝对路由不受约定影响）| `JwtApi` |
| 匿名公开 | `[Route("/api/[controller]/[action]")] [AllowAnonymous]` | `/api/X/Y` | 无 |

### 实体 / 表
- 表名前缀 `SB_`：`SB_DatabaseBackup` / `SB_Admin` / `SB_SystemSetting` 等
- **长文本字段一律 `DbType = "longtext"`**：富文本编辑器内容、公告内容、错误堆栈等
  禁用 `varchar` / `text` / `mediumtext`，避免后续因内容超长截断
- 软删除：`IsDeleted` 字段 + FreeSql 全局 filter 自动排除

### 时间（强制，编译期检查 SB0001）
- **禁用** `DateTime.Now` → 用 `AppTime.Now`（程序时区）
- **禁用** `DateTime.Today` → 用 `AppTime.Today`
- **禁用** `DateTime.UtcNow` → 用 `AppTime.UtcNow`
- 数据库存 UTC：`AppTime.UtcNow`
- 业务"现在"：`AppTime.Now`（按 appsettings.TimeZone）
- UTC → 显示：`AppTime.ToLocal(utc)`
- 前端倒计时：`AppTime.ToTimestamp(utcTarget)`，客户端做 `target - now`

### 服务
- `[AutoDependencyInjection]` 属性自动注册（默认 Scoped）
- `BaseCrudService<T>` 提供 Detail/Del/Insert/Select/ApplySorting/PagedResult 等

### IModuleInitializer 模块声明
所有模块通过实现 `IModuleInitializer` 声明：
```csharp
public class XxxService : BaseCrudService<XxxEntity>, IModuleInitializer
{
    public string ModuleName => "xxx 模块";
    public int InitOrder => 50;

    public Type[] GetEntityTypes() => new[] { typeof(XxxEntity) };

    public List<MenuDefinition> GetMenus() => new() { /* 菜单 */ };

    public List<SystemSettingDefinition> GetSystemSettings() => new() {
        new SystemSettingDefinition {
            Key = "xxx_yyy",
            Name = "xxx 名称",
            CategoryName = "xxx 分类",
            DisplayType = SystemSettingDisplayType.InputNumber,
            DefaultValue = "12",
            CantDelete = true,
        }
    };
}
```
启动时 `DatabaseSeeder` 扫描所有 IModuleInitializer，自动建表 + 同步菜单 + 同步系统设置（按 Key 唯一识别，已存在不覆盖用户改动）。

### FreeSql 已知踩坑
- `.Skip(N).ToList()` 在 MySQL 上 FreeSql 生成 `LIMIT -1 OFFSET N` 语法错 → 改成拉全部到内存再 LINQ Skip
- 长文本：实体加 `[Column(DbType = "longtext")]`

---

## 4. 后台任务（BackgroundTask）

### 注册
所有 sb-api 部署默认带的任务在 `BackgroundTaskRegistrationService.StartAsync` 注册：
```csharp
var task = new XxxTask(logger, fsql, scopeFactory);
_taskManager.RegisterTask("Xxx", task);
await _taskManager.StartTask("Xxx");
```
派生项目要加业务任务，可以自己写 `IHostedService` 调用 `IBackgroundTaskManager.RegisterTask`。

### 子类约定
```csharp
public class MyTask : BaseBackgroundTask
{
    private readonly IServiceScopeFactory _scopeFactory; // BackgroundService 是 singleton, scoped 依赖在 ExecuteTaskAsync 里 createScope 拿
    
    public override string DisplayName => "我的任务"; // 中文名（管理后台展示）

    protected override TimeSpan Interval => TimeSpan.FromMinutes(1); // 轮询周期

    /// 节流 hook：默认 true 每轮跑；override 加"距上次执行 / cron 命中"等条件
    /// 手动 RunOnceAsync 不调此 hook，可绕过节流强制执行
    protected override Task<bool> ShouldRunNowAsync(CancellationToken ct) { ... }

    /// 任务实际逻辑（手动 / 自动 都跑这个）
    protected override async Task ExecuteTaskAsync(CancellationToken ct) { ... }

    /// 业务"下次执行时间"，给管理后台展示
    public override DateTime? NextRunTime { ... }
}
```

### 三个执行入口
| 入口 | 走哪些 hook |
|------|------|
| 自动轮询 | ShouldRunNowAsync → ExecuteTaskAsync |
| 管理后台「立即执行」(`POST /admin/BackgroundTask/RunOnce`) | 直接 ExecuteTaskAsync（绕过 ShouldRunNowAsync） |
| 子类业务方法 | 自行决定 |

---

## 5. 系统设置

### 19 种 DisplayType
0=Input / 1=InputTextArea / 2=RadioGroup / 3=Select / 4=CheckboxGroup / 5=Switch / 6=ImageUpload / 7=InputNumber / 8=InputPassword / 9=DatePicker / 10=TimePicker / 11=RangePicker / 12=Rate / 13=AutoComplete / 14=Mentions / 15=TreeSelect / 16=IconPicker / 17=MultiImageUpload / 18=FileUpload

### 前端读取
- 按 key 取单条：`GET /admin/SystemSetting/GetByKey?key=xxx` → 返回 `SystemSetting` 对象，值在 `dataContent` 字段
- 业务代码：
  ```ts
  const s = await api.get('/SystemSetting/GetByKey', { params: { key: 'xxx_yyy' } });
  const value = s?.dataContent ?? defaultValue;
  ```

---

## 6. 云端中心架构（CloudConfig）

### 设计
所有云端服务（在线授权 / 数据库备份接收 / 未来其他）共用一个根地址 `CloudConfig.ServerUrl`，安装向导填一次，运行时各功能拼子路径调用。

### 字段
- `appsettings.json` 加 `"CloudConfig": { "ServerUrl": "https://cloud.example.com" }`
- 安装向导第一步「产品信息」有「云端中心地址」输入

### Helper（业务代码用）
```csharp
// AppSettings.cs
public string GetLicenseServerUrl()  // 拼 {ServerUrl}/api/license
public string GetBackupUploadUrl()   // 拼 {ServerUrl}/api/BackupReceive/Upload
```

新增云端功能：在 AppSettings 加同样的 helper 拼子路径，业务调 helper 而不是直接拼字符串。

### 向后兼容
`LicenseConfig.LicenseServerUrl` 字段保留，新部署留空，由 `CloudConfig.ServerUrl + /api/license` 自动拼接。

---

## 7. 数据库备份

### 统一入口
**三个备份触发点**（定时任务 / Admin 手动 / MasterKey API）共用 `DatabaseBackupService.BackupAndUploadAsync`：
1. `CreateBackup` 本地 dump + zip + insert
2. 启用了 `backup_upload_enabled` + `CloudConfig.ServerUrl` → `TryUploadLatestAsync` 推到云端
3. 按 `backup_keep_local_count` 清理超期本地

派生项目不要单独写"备份后上传"逻辑，直接调 `BackupAndUploadAsync`。

### 实体扩展
`SB_DatabaseBackup` 含 `Md5` / `RemoteUploadStatus` / `RemoteUploadTime` / `RemoteUploadError` / `RemoteUploadUrl`。

---

## 8. 前端品牌资源

### Logo / Favicon / app-info.json
- 安装向导上传 Logo → 后端 `FaviconGenerator` 生成各尺寸图标 + `app-info.json` 写入 `wwwroot/storage/`
- 前端 `bootstrap.ts` 拉 `/storage/app-info.json` → `updatePreferences({ app: { name }, logo: { source }, copyright })`
- vite `/storage` 已代理到后端 6010，前端**不要保留** `public/storage/` 副本（vite 优先返回 public 静态文件覆盖代理）

### 不要硬编码品牌名
- 不要写"赛博管理系统" / "Saibo Management System" 等具体品牌字面量
- 安装向导兜底默认值改成"管理系统" / 留空
- 运行时所有显示一律走 `__APP_INFO__.nameCn` / `preferences.app.name`

---

## 9. 安装向导

### 流程顺序（重要）
**产品 → 数据库 → 缓存 → 部署 → 密码 → 管理员 → 确认**

产品信息（productEn / productCn / Logo / 时区 / CORS / 主域名 / 云端中心地址）放最前，**后续步骤的默认值都从产品名推断**：
- `productEn` 改 → 自动同步 `dbName` 首字母大写、`cachePrefix=productEn:`、`redisInstanceName=productEn:`
- `dbServer` 改 → 自动同步 `redisConnection=dbServer:6379`
- `userEdited` 跟踪：用户改过的字段不再被自动推断覆盖

### MasterKey
- 默认关闭（`EnableMasterKey: false`）
- 启用时安装成功页一次性显示 GUID，写入 `appsettings.MasterKey`
- 客户端通过 `X-Master-Key` 请求头调任意接口绕过 JWT
- **生产环境建议关闭**

### 部署项目
- 进入「部署」步骤自动预填两个：admin / h5
- 路径从后端 `GET /api/Setup/DeployHints` 推断：API 在 `*_api` 目录 → admin = `*_admin` / h5 = `*_h5`
- 推断不出留空让用户填

---

## 10. 构建参数

### 默认（客户交付）
- 授权验证 ON（不传 `-p:DisableLicense=true`）
- 授权管理 OFF（不传 `-p:EnableLicenseManage=true`）
- 备份接收端 OFF（不传 `-p:EnableBackupReceive=true`）

### 编译开关
| Flag | 作用 |
|------|------|
| `-p:DisableLicense=true` | 关闭授权验证 |
| `-p:EnableLicenseManage=true` | 包含授权管理（License 服务端角色） |
| `-p:EnableBackupReceive=true` | 包含备份接收（备份服务端角色） |

Debug 模式默认开启所有 `Enable*`，方便联调。

### 默认过滤
构建产物时**默认排除**：
- `appsettings.json` / `appsettings.*.json`（避免覆盖线上配置）
- `wwwroot/`（生产环境定制的静态资源目录）

显式覆盖：用户说"构建带 wwwroot" / "构建带配置" 时不删。

---

## 11. 提交 / 发布

### Git
- 修复一个 bug / 完成一个功能 → 自动 `git add` + `git commit` + `git push`
- commit message 中文 + Conventional Commit：`feat: xxx` / `fix: xxx` / `refactor: xxx` / `chore: xxx`
- **不**用 `git add -A` / `git add .`，按文件名显式 add（避免误提交 `.env` / `*.key` 等敏感文件）
- 多个不相关改动可拆 2-3 个 commit
- **不**强推 main、**不** `reset --hard`、**不**改 `git config`、**不**跳 hooks（`--no-verify`）

### 后端代码改动后
**立即重启 API**（不要询问用户）。

### 调试
**Development 环境跳过授权验证**（`if (!app.Environment.IsDevelopment()) app.UseLicense();`），避免授权服务器不可达打断调试。生产构建不受影响。

---

## 12. 其他重要约定

### 测试账号
- Admin：`admin` / `111111`(种子数据统一)

### 端口范围
- API: 6010
- Admin: 6012
- 范围 6010-6019 预留

### 文档命名
- `docs/` 下所有 AI 专用文档以 `AI_` 前缀
- 给 AI 看，不需要给人看


---

## 13. 自研可复用资产清单（**优先使用 · 抽完必须更新**）

> 全系统视觉/交互/工具方法都应优先复用项目自研资产，避免到处轮子。
>
> **核心约定**（包括派生项目）：
> 1. **写新业务前先查清单** — 时间/金额/上传 / Util / Extension 已有的别重写
> 2. **找不到合适的就抽出来** — 跨页/跨服务复用 ≥ 2 处 → 抽到 `components/` 或 `Util/Expand/`，加 index.ts / using
> 3. **抽完立即追加到本清单** — 派生项目自己抽的也要在 13.1/13.2 末尾追加（同 commit 更新清单）
> 4. **改组件本身全系统受益** — 不要在某个业务页 fork 一份魔改

---

### 13.1 前端组件（`sb-admin/apps/web-antd/src/components/<目录>`）

```ts
import { TimeTag } from '#/components/TimeTag';
```

| 组件 | 场景 | 关键 props / 注意 |
|------|------|------------------|
| **TimeTag** | 时间戳 + 时区标签 | `time` / `color` / `timezone` / `placeholder` (默认 `--`)；`time` 为空或纯占位符时不显示时区行 |
| **TimeProgress** | 时间区间进度可视化（授权剩余、备份倒数等） | `startTime` / `endTime` / `mode` (默认 `elapsed`) / `color` / `trackColor` / `variant` / `striped`；color 必须用 shadcn `:root` token (`hsl(var(--destructive))`)，不要传 `var(--ant-color-*)` |
| **TimezoneDisplay** | 单独展示时区文本 | `timezone` (IANA ID，空值显示 `--`) |
| **AmountDisplay** | 所有金额展示（充值/订单/提现） | `amount` / `currency` / `currencyIcon` / `mode` (`icon`/`text`) / `decimals` |
| **SmartUpload / DirectUpload / ChunkUpload** | 文件上传 | 三组件 props/emits/slots 一致；Smart 默认推荐自动选直传/分片，强制场景用 Direct/Chunk |
| **LicenseExpiryReminder** | 授权到期弹窗 | 已挂 `basic.vue` `#extra`，业务页面不要重复挂载 |
| **PermissionDescription** | 用户/角色的"我有什么权限"卡片 | 自动读 `usePermission()`，按 `userType` 渲染 |

**禁忌**（替换后看似工作但失去全系统统一）：
- 写 `{{ row.createdTime }}` 直接展示时间 → 用 TimeTag
- `{{ amount.toFixed(2) }} USDT` 拼字符串 → 用 AmountDisplay
- 自己包 antd `<Upload>` → 用 SmartUpload（丢失分片/秒传能力）
- if/else 判 `userType` 写权限说明文案 → 用 PermissionDescription

---

### 13.2 后端可复用资产（`sb-api/SB.Core/`）

#### `SB.Core/Util/`（工具类）

| 类 | 用途 |
|----|------|
| `AppTime` | **强制使用**。`AppTime.Now/UtcNow/Today/Zone/ToLocal/ToUtc/ToTimestamp`。禁用 `DateTime.Now`（编译期 `SB0001` 报错） |
| `AppShutdownHelper` | 应用优雅停机（安装向导触发 systemd 重启用） |
| `EncryptionUtil` | AES/RSA 加密 |
| `PasswordUtil` | 密码哈希（BCrypt）+ 校验，配 `PasswordConfig.StorageMode` |
| `SecurityUtil` | 通用安全工具 |
| `StringUtil` | 字符串工具 |
| `HttpUtil` | HttpClient 帮助 |
| `ImageUtil` | 图像处理 |
| `FaviconGenerator` | Logo / Favicon / app-info.json 生成（安装向导用） |

#### `SB.Core/Expand/`（C# Extension methods）

```csharp
using SB.Core.Expand;
```

| 扩展 on | 关键方法 |
|---------|---------|
| `ClaimsPrincipal` | `User.Id()` / `User.Username()` / `User.Avatar()` / `User.UserRole()` / `User.UserType()` / `User.Role()` |
| `DateTime` / `long` / `int` | `dt.GetTimestamp()` / `dt.GetTimestampMillisecond()` / `ts.GetDatetime()` |
| `HttpContext` / `HttpRequest` | `request.GetClientIP()` / `ctx.RequestHost()` / `ctx.RequestHostWithScheme()` / `ctx.RequestOrigin()` |
| `string` (image url) | `path.ToAbsoluteUrl(ctx)` / `url.ToRelativePath()` |
| `string` | `str.TrimStartsPrefix(prefix)` |

写新业务时先 grep `SB.Core/Expand/` 看有没有现成扩展，避免重复造轮子。

#### `SB.Core/Attribute/`

| 属性 | 用途 |
|------|------|
| `[AutoDependencyInjection]` | Service 自动 DI 注册（默认 Scoped），配 `BaseCrudService<T>` 用 |

#### `SB.Core/BackgroundTask/`

| 类 | 用途 |
|----|------|
| `BaseBackgroundTask` | 后台任务基类（详见 §4） |
| `TaskExecutionRecord` / `TaskLogLevel` / `TaskStatus` | 任务执行模型 |

#### `SB.Core/FreeSql/BaseRepositoryExtensions.cs`
FreeSql 仓储扩展（`OrderByPropertyName` 等 ApplySorting helper）。

#### `SB.Core/License/`
License 子系统 — 详见 [AI_License_System.md](AI_License_System.md)。
业务一般通过 `AppGlobal.AppSettings.GetLicenseServerUrl()` helper 调用，不直接碰内部类。

#### `SB.Core/Middleware/` + `SB.API/Middleware/`

| 中间件 | 用途 |
|-------|------|
| `GlobalExceptionMiddleware` | 全局异常拦截 → 统一 `Result.Fail` |
| `QueryCountMiddleware` | 数据库查询计数（debug header 显示 N 次 SQL） |
| `MasterKeyMiddleware` | `X-Master-Key` 请求头校验 |
| `SetupMiddleware` | 未安装时把请求重定向到 `/setup/index.html` 安装向导 |

#### `SB.API/Extensions/`（启动配置 Setup 类）
框架内部基础设施，业务一般不动。Fork 时若需扩展见 [AI_Fork_Guide.md](AI_Fork_Guide.md)。
关键：`AdminRoutePrefixConvention`（自动 `/admin/` 前缀）、`ControllerExclusionConvention`（编译时排除 controller）、`ServiceSetup`（`[AutoDependencyInjection]` 扫描）、`DatabaseSeeder`（启动 seed 菜单/设置/种子数据）。

#### `SB.Analyzers`

| 分析器 | 作用 |
|--------|------|
| `BanDateTimeNowAnalyzer` + `CodeFix` | 编译期禁用 `DateTime.Now`（错误码 `SB0001`，自动修成 `AppTime.Now`） |

#### 框架基类（`SB.Services/`）

| 类 | 用途 |
|----|------|
| `BaseCrudService<T>` | Service CRUD 基类（Detail / Del / Insert / Select / ApplySorting / PagedResult） |
| `IModuleInitializer` | 模块声明（GetEntityTypes / GetMenus / GetSystemSettings / SeedData，详见 §3） |

#### CloudConfig helper（`AppSettings.cs`）

```csharp
AppGlobal.AppSettings.GetLicenseServerUrl()    // 拼 {ServerUrl}/api/license
AppGlobal.AppSettings.GetBackupUploadUrl()     // 拼 {ServerUrl}/api/BackupReceive/Upload
```
新增云端服务必须**在 AppSettings 加同样的 helper**（详见 §6），业务调 helper 不直接拼字符串。

---

### 13.3 派生项目维护本清单

派生项目 fork 这套 docs 后，**在 13.1 / 13.2 末尾追加自己抽出来的组件 / Util**，例如：

```md
### 13.1 前端组件
... (上游已有)

#### 本项目（xxx）追加
- `OrderStatusTag` — 订单状态 Tag（pending/paid/shipped/...）
- `KycVerifyForm` — 实名认证表单
```

合并上游时只需关心上游章节的更新，自己追加的部分不会冲突。

#### 抽组件清单更新流程
1. 业务里发现重复模式（≥ 2 处）→ 抽到 `components/` 或 `Util/Expand/`
2. **同 commit 更新本文件 §13.1 / §13.2 的清单**（commit msg 加 "+ 组件清单"）
3. PR 描述里强调"已加入清单"
