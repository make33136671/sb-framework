# License 授权系统

> 本文档为**授权子系统事实源**，逐项对照源码生成（`SB.Framework.Core/License/` + `SB.Framework.Services/` + `SB.Feature.LicenseManage/` + `VerificationController`）。
> ⚠️ 旧文档描述的 `Wuxing.LicenseGen`、`SB.LicenseGen` CLI、Obfuscar 混淆、`ENABLE_LICENSE_MANAGE` 编译开关 **均已不存在**，详见文末"已移除项"。

## 1. 架构

基于 **RSA-2048 非对称签名**：程序只含公钥（验签），私钥由授权签发端（LicenseManage 插件）保管。客户无法自行生成授权 Key。

| 组件 | 位置 | 作用 |
|------|------|------|
| `LicenseMiddleware` | `SB.Framework.Core/License/LicenseMiddleware.cs` | 请求拦截，过期/无效/时间回退/心跳超时返回 403 |
| `LicenseService`（+ `ILicenseService`） | `SB.Framework.Core/License/LicenseService.cs` | RSA 验签 + 身份校验 + 实时过期检测 |
| `LicenseResult` / `LicensePayload` | `SB.Framework.Core/License/LicenseResult.cs` | 解析结果 / 签名 Payload 模型 |
| `LicenseServiceExtensions` | `SB.Framework.Core/License/LicenseServiceExtensions.cs` | `AddLicense` / `UseLicense` / `AddHeartbeatChecker` |
| `TimeRegressionGuard` | `SB.Framework.Core/License/TimeRegressionGuard.cs` | 时间回退检测（HMAC 保护的持久化时间戳） |
| `ClientKeyService` / `HardwareIdService` | `SB.Framework.Core/License/` | 客户端标识生成 |
| `DomainMatcher` / `DomainExpander` | `SB.Framework.Core/License/` | 域名匹配（支持 `*.example.com`） |
| `ILicenseVerifyClient` | `SB.Framework.Core/License/ILicenseVerifyClient.cs` | 吊销查询接口（Core 层定义，避免引用 Services） |
| `LicenseClientService` | `SB.Framework.Services/LicenseClientService.cs` | 客户端：申请/查询/下载/离线应用授权 |
| `LicenseVerifyClient` | `SB.Framework.Services/LicenseVerifyClient.cs` | 节流异步吊销查询（1h 节流） |
| `LicenseHeartbeatTask` | `SB.Framework.Services/LicenseHeartbeatTask.cs` | 心跳后台任务 + `IHeartbeatGracePeriodChecker` 实现 |
| `LicenseManageService` | `SB.Feature.LicenseManage/`（插件） | 服务端：RSA 密钥管理 / 签发 / 吊销 / 审批 |

## 2. 配置（appsettings.json）

```jsonc
{
  "LicenseConfig": { "Key": "Base64(payload).Base64(signature)" },
  "CloudConfig": { "ServerUrl": "https://cloud.saibo.club" },   // 授权服务器根地址（授权/备份共用）
  "ProductEn": "your-product"                                     // 验签时校验产品名
}
```

- `LicenseConfig` **只有 `Key`**（无 `WhitelistPaths` 等项——中间件白名单已硬编码）。
- 授权服务器地址**统一走 `CloudConfig.ServerUrl`**，客户端拼接子路径：`{ServerUrl}/api/verify/*`。
- 心跳宽限期参数（`_intervalMinutes=5`、`_graceHours=48`）**硬编码在 `LicenseHeartbeatTask` 中，不从配置读取**（防用户禁用）。

## 3. 授权 Key 格式

```
Base64(JSON payload) . Base64(RSA-SHA256 PKCS1 签名)
```

Payload 字段（`LicensePayload`）：

| 字段 | 说明 |
|------|------|
| `Product`（JSON 名）→ `ProductEn` | 产品英文名，验签时校验 |
| `ProductCn` | 产品中文名 |
| `Domain` | **旧字段**，仅供老 Key 兼容解析；新 Key 用 `Domains` |
| `Domains` | 绑定域名列表（支持 `*.example.com` 通配）；null/空 = 不验证域名 |
| `ClientKey` / `HardwareId` | 绑定机器标识；null/空 = 不校验该项 |
| `VerifyMode` | **已废弃**，新验证逻辑不再读取（见 §5） |
| `IssuedAt` / `ExpiresAt` | 签发/到期时间 |
| `AllowedRoutes` | 到期后仍可访问的路由前缀列表 |
| `AllowAdminAfterExpiry` | 到期后是否放行 `/admin/*` |
| `AllowClientApiAfterExpiry` | 到期后是否放行 `/api/*` |

## 4. 中间件行为（`LicenseMiddleware`）

执行顺序（对每个请求）：

1. **硬编码白名单直接放行**（不可配置，防篡改）：
   `/health`、`/api/setup/`、`/api/verify/`、`/storage/`、`/admin/Verification/`、`/admin/Entitlement/`、`/admin/Setup/`、`/admin/Auth/Login`、`/admin/Auth/GetInfo`、`/admin/Menu/All`
2. **FullAccess(god) API Key 放行**：`IApiKeyAuthenticator` 鉴权通过且 `IsFullAccess` → 绕过所有限制（取代旧静态 MasterKey）。
3. **时间回退检测**：`TimeRegressionGuard.Check()` 触发 → 403 `{"code":-1,"message":"检测到系统时间回退...","type":"license_invalid"}`。
4. **授权有效**：继续检查
   - **域名校验**：`Domains` 非空时按 `Host` 匹配，不匹配 → 403；
   - **吊销检查（fire-and-forget）**：`ILicenseVerifyClient.TryVerifyAsync`，1h 节流、3s 超时、异常静默（不影响请求）；服务端返回 `revoked=true` → 清空本地 Key + 刷新；
   - **心跳宽限期**：`CloudServerUrl` 已配置且超过 48h 无成功心跳 → 403。
5. **授权过期**：按 `AllowAdminAfterExpiry` / `AllowClientApiAfterExpiry` / `AllowedRoutes` 策略部分放行，否则 403。
6. 拦截响应统一：`HTTP 403`，`{"code":-1,"message":...,"type":"license_invalid","data":null}`。

> 中间件仅在 `installed.lock` 存在（已安装）且非 Development 时挂载（`UseSbFramework` 中 `if (!env.IsDevelopment()) app.UseLicense()`）。`AddLicense` 注册了 `ILicenseService`。

## 5. 验签与身份校验（`LicenseService`）

- `Validate(licenseKey, expectedProduct)`：纯签名验证。
- `ValidateWithIdentity(licenseKey, clientKey, hardwareId, expectedProduct)`：签名 + 身份校验。
- **身份校验逻辑**（`VerifyIdentity`）：**Payload 中字段非空就校验、空则跳过**。`VerifyMode` 不再读取（废弃）。
- **实时过期**：`CurrentStatus` 每次访问时若 `AppTime.UtcNow > ExpiresAt` 动态转 `IsExpired=true`。
- **签名失败诊断**：返回公钥指纹 + Payload 解析 + 字节长度，帮助定位"签发端与当前端不是同一对密钥"。
- 公钥为空（授权启用但缺公钥）→ 注册 `MissingKeyLicenseService`，所有请求视为授权无效（`IsEnabled=true`）。

## 6. 时间回退检测（`TimeRegressionGuard`）

- 持久化文件（**双位置**）：主文件 `license_time.dat`（产物根目录）+ 备份文件 `%LOCALAPPDATA%/SB_Framework/license_time.dat`（取不到时回退产物目录 `license_time.backup.dat`）。内容均为 `{ticks}.{HMAC}`。
- HMAC 密钥 = SHA256(`公钥 + "|" + 本机HardwareId`)——即使拿到公钥也无法在别的机器伪造。
- 启动时校验：主/备份**任一**文件存储时间比当前时间快 >5 分钟 → 判定回退；**任一**文件 HMAC 校验失败/解析失败 → 视为篡改。
- 运行时：当前时间 < 内存最后时间 -5 分钟 → 回退 403；每 10 分钟双文件持久化一次。
- **双位置意义**：删主文件 + 回拨时钟无法绕过（备份仍在应用数据目录持有旧时间）。两处都删（且能找到备份位置）= 绕过，属于接受的残余风险。
- 文件读写失败不阻塞启动（降级）。

## 7. 客户端流程（`VerificationController` → `admin/Verification/*`，JwtAdmin，Status 除外）

| Action | 行为 |
|--------|------|
| `Status`（AllowAnonymous） | `LicenseClientService.GetStatus()`：授权状态 + 客户端标识（clientKey/hardwareId）+ 服务器/程序时区 + cloudServerUrl |
| `RequestLicense` | POST `{ServerUrl}/api/verify/Request`（form：domain/clientKey/hardwareId/serverIP） |
| `CheckUpdate` | GET `{ServerUrl}/api/verify/Check?clientKey&hardwareId[&actualDomain]`（允许传入 serverUrl 临时覆盖） |
| `ApplyLicense` | GET `{ServerUrl}/api/verify/Download?recordId` → 验签 → 写 `appsettings.json` → `Refresh` |
| `ApplyKey` | **离线**：`LicenseService.Validate` → 写 `appsettings.json` → `Refresh` |

## 8. 吊销与心跳

- **吊销**（`LicenseVerifyClient`）：每请求触发、成功后 1h 节流（失败 60s 冷却）；`POST {ServerUrl}/api/verify/Verify`，响应 `data.revoked=true` → 清空 `LicenseConfig.Key` 并 `Refresh("")`。
- **心跳**（`LicenseHeartbeatTask`）：每 5 分钟 `POST {ServerUrl}/api/verify/Heartbeat`（JSON）；`status=active` 写 `license_heartbeat.dat`（HMAC 保护）；`revoked` → Refresh；宽限期 48h（`IsGracePeriodExceeded`）。

## 9. 服务端签发（`SB.Feature.LicenseManage` 插件）

- **签发入口**：`EntitlementController`（`admin/Entitlement/*`，JwtAdmin）：`GetPublicKey` / `RegenerateKeyPair` / `ClientList` / `ClientEdit` / `RequestList` / `ApproveRequest` / `RejectRequest` / `RecordList` / `IssueLicense`(FromBody) / `QuickGenerate`(FromBody) / `DeleteClient` / `RevokeLicense` / `VerifyLogList`。
- **公开验证 API**：`VerifyController`（`/api/verify/*`，AllowAnonymous）：`Heartbeat` / `Request` / `Verify` / `Check` / `Download`。
- **私钥目录优先级**：`SB_LICENSE_KEY_DIR` 环境变量 > 插件 Options.KeyDirectory > ContentRoot。密钥对自动生成（`GenerateLicenseKeyFile`），私钥不入库不提交（`.gitignore` 忽略 `license_*.key`）。
- **典型部署**：`LicenseManage` 装在 **NoLicense 主包**上（自己不被授权束缚 + 能给下游签发）。
- **⚠️ 强制身份绑定**（`GenerateLicense` 校验）：签发时**至少绑定一项身份**（`Domains` / `ClientKey` / `HardwareId` 任一非空），否则拒绝签发。防"空白授权"——无任何校验字段的 Key 可在任意机器原样使用。`QuickGenerate` 本就要求 ClientKey/HardwareId 至少一项。

## 10. 公钥嵌入与 NoLicense 变体

- `SB.Framework.WebApi.csproj` 的 `EmbedLicensePublicKey` Target 在**构建时**从 `license_public.key` 读公钥，写入 `BuildInfo.LicensePublicKey`（`GenerateLicenseKeyFile` Task 首次自动生成 RSA-2048 密钥对）。
- **运行时优先用内嵌公钥**（`LicenseSetup.AddLicenseService`）：`BuildInfo.LicensePublicKey` 非空即使用编译进 DLL 的公钥；仅内嵌为空（旧产物/缺密钥）才回退读产物目录 `license_public.key` 文件。→ **替换 `license_public.key` 文件 + 自签无法绕过**（验签用内嵌公钥）。
- `.NoLicense` 变体：`EmbedEmptyLicenseKey` 置空公钥；`SbFrameworkExtensions` 走 `#if !DISABLE_LICENSE → NullLicenseService()` 分支（`IsEnabled=false`），不挂中间件，授权逻辑物理关闭。`LicenseSetup.AddLicenseService` 的 `#if DISABLE_LICENSE` 分支同样注册 `NullLicenseService`（一致语义）。
- 正常版 + 缺公钥（内嵌与文件均为空）→ 注册 `MissingKeyLicenseService`，**全站 403**（保护生效）。

## 11. 已知缺口（安全现状）

> 目标威胁模型：**防"顺手党"**（改文件 / 改时间 / 复制授权 / AI 几分钟绕过），不防逆向工程 IL 的开发者。

| # | 缺口 | 现状 | 说明 |
|---|------|------|------|
| 1 | 机器不绑定 | ✅ 已封 | `GenerateLicense` 强制绑定至少一项身份（域名/ClientKey/HardwareId），无"空白授权"可签发；签发绑定后跨机复制过不了身份校验 |
| 2 | 系统时间调回 | ✅ 已封 | `TimeRegressionGuard`（5 分钟容差 + 双文件持久化）；删主文件回拨时钟会被备份文件拦住 |
| 3 | 反编译改 DLL | 接受 | 非目标威胁模型（逆向党）；若需提高可恢复 Obfuscar 混淆 |
| 4 | 替换授权证书/自签 | ✅ 已封 | 运行时优先用**内嵌公钥**（编译进 DLL），替换 `license_public.key` 文件无效 |
| 5 | `-p:DisableLicense=true` 构建 | 固有 | NoLicense 变体有意关闭授权（商业上受控，按项目授予） |
| 6 | 心跳被屏蔽（拔网/hosts） | 已防护 | 48h 宽限期后 403（需配置 `CloudServerUrl`） |
| 7 | 删除主+备份时间文件再回拨 | 接受 | 残余风险：需找到 `%LOCALAPPDATA%/SB_Framework/` 并删两处，已超出"顺手党"范畴 |

## 12. 已移除项（旧文档误留）

| 旧描述 | 现状 |
|--------|------|
| `Wuxing.LicenseGen` CLI | **不存在**（命名来自更早 fork） |
| `SB.LicenseGen` 离线签名 CLI | **目录不存在**（`.gitignore` 保留条目，实际已移除）；签发改由 `LicenseManage` 插件的 `QuickGenerate`/`IssueLicense` |
| Obfuscar 混淆（层4） | **已移除**，无 `obfuscar.xml` / `obfuscate.sh` |
| `-p:EnableLicenseManage=true` / `-p:EnableBackupReceive=true` 编译开关 | **已移除**，改 NuGet 插件引用即启用 |
| `LicenseConfig.WhitelistPaths` 配置 | **已移除**，白名单硬编码于中间件 |
| `X-Master-Key` / `MasterKeyMiddleware` | **已移除**，由 AccessToken 插件 FullAccess key 取代 |
| `VerifyMode` 双验证逻辑 | **已废弃**，`VerifyIdentity` 改为"非空即校验、空则跳过"；旧"双验证漏洞"描述已不适用 |
