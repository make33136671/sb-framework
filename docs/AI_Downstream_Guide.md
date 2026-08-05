# 下游项目接入指南（Downstream Project Guide）

> ⚠️ 本指南替代旧的「Fork 指南」。派生项目**不再 fork 本仓库源码**，改为 **NuGet 包消费 + 子类覆盖 + 插件扩展**。前端基座才走 fork + merge（见 [sb-framework-admin](../../sb-framework-admin/README.md)）。

## Overview

```
sb-framework (NuGet, 只读)  ──引用──>  Sb.Apps.<App>.* (派生业务代码)
                                       │
                                       │ 继承覆盖 (后注册覆盖先注册)
                                       │ 实现扩展接口 (IFreeSqlConfigurator / IStartupSyncHook / IFeatureModule)
                                       ▼
                                   你的业务工程
```

**Key principle:** 框架以 NuGet 包为边界，派生项目**不修改框架 dll**。定制通过「继承 + DI 覆盖」或「实现扩展接口」，不改框架源码。

---

## Step 1: 创建工程

```bash
dotnet new webapi -n Sb.Apps.<App>.WebApi
```

在仓库根配 `NuGet.config`（私有 BaGet + nuget.org）：

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="saibo-baget" value="https://nuget.saibo.club/v3/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

## Step 2: 引用框架包

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SB.Framework.WebApi" Version="0.4.14" />
    <!-- 可选插件 -->
    <PackageReference Include="SB.Feature.AccessToken" Version="0.2.3" />
    <PackageReference Include="SB.Feature.BackupReceive" Version="0.1.1" />
    <PackageReference Include="SB.Feature.LicenseManage" Version="0.1.1" />
    <PackageReference Include="SB.FlowEngine" Version="0.2.1" />
  </ItemGroup>
</Project>
```

> 若需关闭授权验证，引用 `SB.Framework.WebApi.NoLicense`（同时 Core/Models/Services 也切 `.NoLicense` 变体）。

## Step 3: Program.cs（两行装配）

```csharp
using SB.Framework.WebApi.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddSbFramework();
var app = builder.Build();
app.UseSbFramework();
await app.RunAsync();
```

## Step 4: appsettings.json

框架会读 `AppSettings` 强类型配置。必须配置的核心节：

```json
{
  "TimeZone": "Asia/Shanghai",
  "ProductCn": "你的产品名",
  "ProductEn": "your-product",
  "DbConfig": { "DbType": "MySql", "Server": "", "Port": 3306, "Database": "sb_yourapp", "User": "", "Password": "", "AutoSyncStructure": true },
  "CacheConfig": { "Enabled": true, "Provider": "Memory" },
  "JwtAdmin": { "Key": "", "Issuer": "", "Audience": "", "Expires": 12 },
  "JwtApi": { "Key": "", "Issuer": "", "Audience": "", "Expires": 24 },
  "ProjectAssemblyName": "Sb.Apps.YourApp.WebApi",
  "Features": { "Disabled": [] }
}
```

> `ProjectAssemblyName` 是旧路径的扫描入口；推荐直接用 `IFeatureModule`（见 Step 9）让框架发现派生程序集。

## Step 5: 业务实体

```csharp
using FreeSql.DataAnnotations;

namespace Sb.Apps.YourApp.Models;

[Table(Name = "sb_yourapp_product")]   // 派生表前缀 sb_<app>_，必须显式声明
public class Product : IEntity, ISoftDelete
{
    public int Id { get; set; }
    [Column(StringLength = 200)]
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsDeleted { get; set; }
}
```

## Step 6: 业务服务

```csharp
using SB.Framework.Core.Attribute;
using SB.Framework.Core;

namespace Sb.Apps.YourApp.Services;

[AutoDependencyInjection]              // 自动 AddScoped
public class ProductService : BaseCrudService<Product>
{
    public ProductService(IFreeSql fsql, ILogger<ProductService> logger)
        : base(fsql, logger) { }

    // 继承即可获得 Detail / Del / 排序 / 分页
    public async Task<Result> GetByCategoryAsync(int categoryId, int page = 1, int pageSize = 20)
    {
        var query = Orm.Select<Product>().Where(p => p.CategoryId == categoryId);
        return await PagedResultAsync(query, page, pageSize);
    }
}
```

## Step 7: 控制器

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sb.Apps.YourApp.WebApi.Controllers;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ProductController : ControllerBase
{
    private readonly ProductService _service;
    public ProductController(ProductService service) => _service = service;

    [HttpGet]
    public async Task<Result> List(string field, string order, int page = 1, int pageSize = 20)
        => await _service.GetPageAsync(page, pageSize, field, order);

    [HttpPost]
    public async Task<Result> Add([FromForm] Product product)   // admin 约定 [FromForm]（SB0002）
        => await _service.AddAsync(product);
}
```

> `AdminRoutePrefixConvention` 自动给控制器加 `admin/` 前缀，前端 baseURL 已含 `/admin`。
> 公开 API 控制器用 `JwtApi` scheme，路由 `/api/*`。

## Step 8: 覆盖框架服务

框架扫描顺序为「框架 → feature（字典序）→ 项目程序集（永远最后）」。派生项目子类自动覆盖框架注册：

```csharp
[AutoDependencyInjection]
public class CustomAdminService : AdminService
{
    // 同名构造函数透传
    public override Result GetList(string username = "", ...)
    {
        // Custom logic
        return base.GetList(username, ...);
    }
}
```

DI 系统会同时以 `AdminService` 类型注册它，框架控制器注入的 `AdminService` 将拿到你的自定义版本。

## Step 9: 模块装配（IModuleInitializer）

声明建表 / 菜单 / 系统设置 / 种子数据。框架启动时自动同步：

```csharp
namespace Sb.Apps.YourApp.Services;

public class YourModuleInitializer : IModuleInitializer
{
    public string ModuleName => "YourApp";
    public string ModuleKey => "yourapp";
    public int InitOrder => 100;   // 排在框架(0-50)之后

    public IEnumerable<Type> GetEntityTypes() => new[]
    {
        typeof(Product), typeof(Order),
    };

    public IEnumerable<MenuDefinition> GetMenus() => new[]
    {
        new MenuDefinition
        {
            Name = "产品管理",
            Path = "/products",
            Component = "products/List",
            Title = "产品管理",
            Icon = "lucide:package",
            OrderNo = 100,
        },
    };

    public async Task SeedDataAsync(IFreeSql fsql, CancellationToken ct) { ... }
}
```

> 若把业务程序集做成 feature（`SB.Feature.` 前缀 + 实现 `IFeatureModule`），框架也能通过插件机制发现它。

## Step 10: 其他扩展点

| 需求 | 做法 |
|------|------|
| 配置 FreeSql（GlobalFilter/Mapper） | 实现 `IFreeSqlConfigurator`（`Order` 100+）注册到 DI |
| 启动后同步任务 | 实现 `IStartupSyncHook`（按 `Order` 串行，schema+seed 完成后执行） |
| 后台任务 | 继承 `BaseBackgroundTask`，override `ExecuteTaskAsync`/`Interval`/`ShouldRunNowAsync` |
| 排除框架 Controller | `appsettings` `ExcludeControllers: ["Dashboard","Member"]` |
| 禁用模块/插件 | `Modules:Disabled` / `Features:Disabled` 黑名单 |
| 禁止 API Key 访问路径 | `ApiKeyDeny.Add("/admin/secret/", "ApiKeyOnly")` |
| 单文件发布 | 实现 `IFeatureSingleFilePublish` |

## Step 11: 升级框架包

升级只需 bump NuGet 包版本（`SB.Framework.WebApi 0.4.14 → 0.4.x`），无源码冲突。参考 [CHANGELOG.md](../CHANGELOG.md) 判断兼容性：

```bash
dotnet add package SB.Framework.WebApi --version <new>
```

---

## Frontend（对应派生）

前端基座 `sb-framework-admin` 才是 fork + merge 模式（clone 基座 → `git remote rename origin upstream` → `git merge upstream/main` 同步）。业务页写在 `apps/web-antd/src/views/<module>/`。菜单由后端 `IModuleInitializer.GetMenus()` 驱动，前端无需手写路由。

详见 [sb-framework-admin README](../../sb-framework-admin/README.md) 与 [AI_Conventions.md](AI_Conventions.md) §1-§3。
