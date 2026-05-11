# SB Framework

> 赛博科技自研 .NET 后端基础设施 — 给所有派生项目（PM / ERP / CRM / ...）共享的 NuGet 包合集。

## 这是什么

`sb-framework` 包含 5 个 .NET 8 工程，全部以 NuGet 包形式提供给派生项目消费：

| Package | 内容 | 文件位置 |
|---------|------|---------|
| `Sb.Framework.Core` | 基础工具（Auth / License / Util / Middleware / Expand） | `SB.Framework.Core/` |
| `Sb.Framework.Models` | 基础领域模型（Admin / Menu / Role / License / etc.） | `SB.Framework.Models/` |
| `Sb.Framework.Services` | 基础应用服务 + 扩展点（IModuleInitializer / IFreeSqlConfigurator / IStartupSyncHook） | `SB.Framework.Services/` |
| `Sb.Framework.WebApi` | 基础 Controllers + 中间件 + 一键装配（AddSbFramework / UseSbFramework） | `SB.Framework.WebApi/` |
| `Sb.Framework.Analyzers` | Roslyn 编译期约束（SB0001 等） | `SB.Framework.Analyzers/` |

另含开发期调试启动壳：

| 工程 | 用途 |
|------|------|
| `Sb.Framework.DevHost` | 本地跑基座用，**不发布**，仅 sb-framework 仓库内部调试 |

## 派生项目怎么用

**1. 配 NuGet 源**（仓库根 `NuGet.config`）：

```xml
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="saibo-framework" value="https://gitlab.saibo.club/api/v4/groups/63/-/packages/nuget/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <saibo-framework>
      <add key="Username" value="%GITLAB_USER%" />
      <add key="ClearTextPassword" value="%GITLAB_TOKEN%" />
    </saibo-framework>
  </packageSourceCredentials>
</configuration>
```

**2. csproj 引用**：

```xml
<ItemGroup>
  <PackageReference Include="Sb.Framework.WebApi" Version="0.1.0-preview.1" />
</ItemGroup>
```

**3. Program.cs**：

```csharp
using SB.Framework.WebApi.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddSbFramework();
var app = builder.Build();
app.UseSbFramework();
await app.RunAsync();
```

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
- 表前缀必须 `sb_*`（基座表）— 派生表前缀由派生项目定义
- 严禁基座代码引用任何 PM/ERP/CRM 等派生业务关键字（CI 自动检查）
- `DateTime.Now` 编译期禁用（用 `AppTime.Now`）
- commit message 用中文 + Conventional Commits 风格

完整命名规范：[docs/naming-conventions.md](./docs/naming-conventions.md)

## 文档

- [命名规范](./docs/naming-conventions.md) — 12 个维度完整规范
- [架构设计](./docs/architecture.md) — 工程拓扑 / hook 机制 / DI 流程
- [Fork 指南](./docs/fork-guide.md) — 派生项目如何接入 NuGet 包

## License

Proprietary — 赛博科技内部使用。
