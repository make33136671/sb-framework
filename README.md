# SB Framework

> 赛博科技自研 .NET 10 后端基础设施 — 给所有派生项目（PM / ERP / CRM / ...）共享的 NuGet 包合集。
> 核心理念：**引包即启用，两行代码装配** + **插件化（SB.Feature.\*）** + **声明式数据装配（IModuleInitializer）**。

## 这是什么

`sb-framework` 以 NuGet 包形式提供给派生项目消费。核心包（`net10.0`）：

| Package | 版本 | 内容 | 位置 |
|---------|------|------|------|
| `SB.Framework.Core` | 0.4.6 | 基础工具/原语（Auth / License / Util / Middleware / **插件框架** / BaseCrudService） | `SB.Framework.Core/` |
| `SB.Framework.Models` | 0.4.7 | 基础领域模型（实体 / DTO / 枚举 / `IEntity` / `IModuleInitializer` / `Result`） | `SB.Framework.Models/` |
| `SB.Framework.Services` | 0.4.13 | 基础应用服务 + 扩展点（`IFreeSqlConfigurator` / `IStartupSyncHook`） | `SB.Framework.Services/` |
| `SB.Framework.WebApi` | 0.4.14 | Controllers + 中间件 + 一键装配（`AddSbFramework` / `UseSbFramework`） | `SB.Framework.WebApi/` |
| `SB.Framework.Analyzers` | 0.4.6 | Roslyn 编译期约束（SB0001 禁 `DateTime.Now` / SB0002 禁 `[FromBody]`） | `SB.Framework.Analyzers/` |

> 以上包均有 `.NoLicense` 变体（如 `SB.Framework.WebApi.NoLicense`），通过 `dotnet pack -p:DisableLicense=true` 构建，用于关闭授权验证的部署。

流程引擎（独立包，只依赖 Core）：

| Package | 版本 | 内容 |
|---------|------|------|
| `SB.FlowEngine` | 0.2.1 | 图执行引擎（前端 flow-engine 的服务端对称实现，业务节点经 `IFlowNodeExecutor` 接入） |

功能插件（只引 Core + Models，`FrameworkReference: Microsoft.AspNetCore.App`）：

| Package | 版本 | 职责 |
|---------|------|------|
| `SB.Feature.AccessToken` | 0.2.3 | 统一访问令牌（API Key + 密码，scoped key + FullAccess 旁路 License） |
| `SB.Feature.BackupReceive` | 0.1.1 | 云端备份接收端 |
| `SB.Feature.LicenseManage` | 0.1.1 | 授权签发端（管理客户/审批/签发/吊销） |

另含开发期调试启动壳：

| 工程 | 用途 |
|------|------|
| `SB.Framework.DevHost` | 本地跑基座用，**不发布**，仅 sb-framework 仓库内部调试（默认监听 6010） |

## 派生项目怎么用

**1. 配 NuGet 源**（仓库根 `NuGet.config`）：

```xml
<configuration>
  <packageSources>
    <clear />
    <!-- saibo 私有 BaGet：托管 SB.Framework.*，同时 Mirror 透传 nuget.org -->
    <add key="saibo-baget" value="https://nuget.saibo.club/v3/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

**2. csproj 引用**：

```xml
<ItemGroup>
  <PackageReference Include="SB.Framework.WebApi" Version="0.4.14" />
  <!-- 可选插件 -->
  <PackageReference Include="SB.Feature.AccessToken" Version="0.2.3" />
</ItemGroup>
```

**3. Program.cs（两行装配）**：

```csharp
using SB.Framework.WebApi.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddSbFramework();
var app = builder.Build();
app.UseSbFramework();
await app.RunAsync();
```

派生项目接入与扩展的完整指南见 [docs/AI_Downstream_Guide.md](./docs/AI_Downstream_Guide.md)。

## 本地开发

```bash
git clone git@gitlab.saibo.club:saibo/framework/sb-framework.git
cd sb-framework

# 编译
dotnet build sb-framework.sln

# 调试启动（默认监听 6010，仅 dev）
cd SB.Framework.DevHost && dotnet run --urls=http://localhost:6010

# 验证启动
curl http://localhost:6010/api/Setup/Status
# 期望: {"code":0,"message":"ok","data":{"installed":false}}
```

## 贡献

**在 PR 前必读**：[CONTRIBUTING.md](./CONTRIBUTING.md)

强制规范：
- 命名空间不可改：基座代码必须在 `SB.Framework.*` 命名空间下
- 表前缀必须 `sb_*`（基座表）— 派生表前缀 `sb_<app>_` 由派生项目定义
- 严禁基座代码引用任何 PM/ERP/CRM 等派生业务关键字（CI 自动检查）
- `DateTime.Now` 编译期禁用（用 `AppTime.Now`，SB0001）
- `[FromBody]` 编译期禁用（admin 约定 `[FromForm]`，SB0002）
- commit message 用中文 + Conventional Commits 风格

完整命名规范：[docs/naming-conventions.md](./docs/naming-conventions.md)

## 文档

- [文档索引](./docs/AI_Index.md) — 全部 AI 文档入口
- [架构设计](./docs/AI_Architecture.md) — 工程拓扑 / 插件框架 / 一键装配 / 子系统
- [下游项目接入指南](./docs/AI_Downstream_Guide.md) — 派生项目如何消费 NuGet 包并扩展
- [插件架构](./docs/AI_Plugin_Architecture.md) — 插件化设计 v2（权威）
- [构建与部署](./docs/AI_Build_Deploy.md)

## License

Proprietary — 赛博科技内部使用。
