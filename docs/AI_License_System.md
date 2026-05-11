# License 授权系统

## 概述

基于 RSA 非对称加密的授权机制。项目内只含公钥（验签），私钥由开发者保管，客户无法自行生成授权 Key。

## 架构

| 组件 | 位置 | 作用 |
|------|------|------|
| `LicenseMiddleware` | `SB.Core/License/LicenseMiddleware.cs` | 请求拦截，过期/无效返回 403 |
| `LicenseService` | `SB.Core/License/LicenseService.cs` | 核心验签 + 身份验证逻辑 |
| `LicenseResult` / `LicensePayload` | `SB.Core/License/LicenseResult.cs` | 解析结果 + Payload 模型 |
| `HardwareIdService` | `SB.Core/License/HardwareIdService.cs` | 硬件指纹生成（MAC+CPU+主板+磁盘） |
| `ClientKeyService` | `SB.Core/License/ClientKeyService.cs` | ClientKey 生成 |
| `LicenseClientService` | `SB.Services/LicenseClientService.cs` | 客户端 Refresh/ApplyKey/CheckUpdate |
| `LicenseManageService` | `SB.Services/LicenseManageService.cs` | 授权管理服务器端生成与管理 |
| `LicenseRecord` | `SB.Models/LicenseRecord.cs` | 授权记录模型 + `LicenseVerifyMode` 枚举 |
| `Wuxing.LicenseGen` | `Wuxing.LicenseGen/Program.cs`（不交付） | 离线私钥签名，生成授权 Key |

## 配置

`appsettings.json`:

```json
{
  "LicenseConfig": {
    "Key": "Base64(payload).Base64(signature)"
  }
}
```

## 授权 Key 格式

```
Base64(JSON payload) . Base64(RSA-SHA256 signature)
```

Payload 内容:
```json
{
  "Product": "wuxing",
  "ExpiresAt": "2027-03-14 13:57:52",
  "IssuedAt": "2026-03-14 13:57:52",
  "VerifyMode": 2,
  "ClientKey": "",
  "HardwareId": ""
}
```

## 中间件行为

- 放行路径: `/health`, `/api/setup/*`, `LicenseConfig.WhitelistPaths` 配置项
- 授权无效或过期: 返回 HTTP 403
  ```json
  {"code": -1, "message": "系统授权已过期或无效，请联系管理员", "type": "error", "data": null}
  ```
- 仅在 `installed.lock` 存在时启用

## 生成授权 Key（开发者操作）

```bash
cd Wuxing.LicenseGen

# 首次：生成密钥对 + 1年有效期 Key
dotnet run

# 使用已有私钥，指定天数
dotnet run -- --key private.xml --days 7

# 使用已有私钥，指定到期日期
dotnet run -- --key private.xml --expires 2027-06-30
```

## 运维模式（短周期授权）

生成 7 天 Key，每 6 天更新，保证不停机：

| 天数 | 操作 |
|------|------|
| 第 0 天 | `--days 7` 生成 Key，发给客户 |
| 第 6 天 | 再次 `--days 7` 生成新 Key，客户替换后重启 |

## 安全说明

- 项目代码中仅包含 RSA 公钥，只能验证签名
- 私钥 `private.xml` 由开发者保管，不进入版本库
- `Wuxing.LicenseGen/` 已加入 `.gitignore`，不会被提交

---

## 安全防护路线图

### 当前现状

**已有的两个有效机制：**
1. RSA 签名验证（防止客户改 expiresAt 延期 —— 改了签名对不上）
2. 本地到期时间比较（`AppTime.UtcNow > expiresAt`）

### 已知绕过漏洞

| # | 攻击方式 | 难度 | 当前防护 |
|---|----------|------|----------|
| 1 | `scp -r` 产物到新服务器 | 低 | 无（key 不绑机器） |
| 2 | `date -s "2026-01-01"` 调回系统时间 | 低 | 无（依赖本地 OS 时间） |
| 3 | dnSpy 反编译 SB.API.dll 去掉 LicenseMiddleware 拦截 | 中 | 无（未混淆） |
| 4 | 替换 `license_public.key` 为自己的公钥 + 自签 | 中 | 无（公钥就是个文件） |
| 5 | `-p:DisableLicense=true` 构建 | 中 | 无（编译常量可被改） |
| 6 | 调 `/admin/License/ApplyKey` 强写 appsettings | 高 | 有限（依赖签名完整性） |

### 双验证漏洞说明

`LicensePayload.VerifyMode` 默认值硬编码为 `2`（`SB.Core/License/LicenseResult.cs:90`），但 `Wuxing.LicenseGen` 生成 payload 时未写入 `ClientKey` / `HardwareId`。反序列化后 VerifyMode 保留默认值 2 → 前端显示"双验证"，而验证逻辑 `LicenseService.VerifyIdentity` 对空值直接跳过 → 实际不校验。

### 威胁矩阵

| 场景 | 当前能否阻止 | 需要哪层防护 |
|------|-------------|-------------|
| 客户拿到 root，把程序移植到其他服务器 | 不能 | 层 1: 机器绑定 |
| 客户拿到 root，你失去服务器控制权 | 不能 | 层 2: 在线激活/心跳/吊销 |
| 调系统时间回退 | 不能 | 层 3: 时间回退检测 |
| 反编译去掉 LicenseMiddleware | 不能 | 层 4: 代码混淆 |

---

### 层 1: 机器绑定（最高优先级） ✅ 已完成

**目标**: 挡住 scp 移植（场景 A）

**实施方案**:
- `SB.LicenseGen/Program.cs` CLI 工具，支持 `--client-key`、`--hardware-id`、`--verify-mode`、`--domain`、`--days`、`--expires`、`--output` 等参数
- 生成 payload 时把身份字段写入 JSON，签名算法与 `LicenseManageService.GenerateLicense()` 一致
- 依赖现有 `HardwareIdService`（`SB.Core/License/HardwareIdService.cs`，已实现 MAC+CPU+主板+磁盘采集）
- 前端离线授权流程：客户复制 client info JSON → 管理员用 LicenseGen 生成绑定机器的 key → 客户导入
- 验证逻辑已存在（`LicenseService.VerifyIdentity`），不用改后端

**完成时间**: 2026-04-13

---

### 层 2: 在线激活 + 心跳 + 吊销（最强控制力） ✅ 已完成

**目标**: 挡住失控场景（客户拿到 root 且你无法远程访问）

**实施方案**:
- 部署独立授权服务器（`-p:EnableLicenseManage=true` 构建 + 独立域名 + 独立 DB）
- 客户端后台任务 `LicenseHeartbeatTask` 按配置间隔心跳 `/api/license/Heartbeat`
- 服务端 `LicenseManageService.ProcessHeartbeat()` 校验客户端状态，返回 active / revoked / unknown
- 心跳失败超过宽限期（默认 48h）→ `LicenseMiddleware` 拦截返回 403
- 授权服务器管理后台已有"吊销"按钮 → 下次心跳收到停服指令 → 客户端自动 Refresh 失效
- 客户拔网 / hosts 屏蔽 → 宽限期后自动失效
- 配置项：`LicenseConfig.HeartbeatIntervalMinutes`（默认 0 禁用）、`HeartbeatGraceHours`（默认 48）

**完成时间**: 2026-04-13

---

### 层 3: 时间回退检测 ✅ 已完成

**目标**: 挡住 `date -s` 调时间

**实施方案**:
- `TimeRegressionGuard`（`SB.Core/License/TimeRegressionGuard.cs`）启动时加载 `license_time.dat`（存储 UTC ticks）
- 当前时间 < 上次记录超过 5 分钟容差 → 判定时间回退 → 403 拦截
- 每 10 分钟自动更新存储的时间戳
- 集成到 `LicenseMiddleware`，在授权检查之前执行
- 在 `LicenseServiceExtensions.AddLicense()` 中初始化（仅授权启用时）

**完成时间**: 2026-04-13

---

### 层 4: 代码混淆（提高反编译门槛） ✅ 已完成

**目标**: 挡住 dnSpy 直接改 DLL

**实施方案**:
- 使用 Obfuscar（免费开源 .NET 混淆器）
- 配置文件 `obfuscar.xml`：对 `SB.Core.dll` 和 `SB.API.dll` 进行重命名 + 字符串加密
- 一键脚本 `obfuscate.sh`：构建 → 混淆 → 替换 → 清理
- 跳过规则：保留 Controllers、Middleware、Extensions、Config 类（DI 和路由依赖名称）
- 安装：`dotnet tool install --global Obfuscar.GlobalTool`

**完成时间**: 2026-04-13

---

### 实施完成情况

| 阶段 | 层级 | 状态 | 完成时间 |
|------|------|------|----------|
| 第 1 阶段 | 层 1 机器绑定 | ✅ 已完成 | 2026-04-13 |
| 第 2 阶段 | 层 3 时间检测 | ✅ 已完成 | 2026-04-13 |
| 第 3 阶段 | 层 2 在线激活 + 心跳 | ✅ 已完成 | 2026-04-13 |
| 第 4 阶段 | 层 4 代码混淆 | ✅ 已完成 | 2026-04-13 |
