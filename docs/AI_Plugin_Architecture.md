# SB.Feature 插件化架构设计

> 状态：设计稿 v2（2026-06-07）。本文取代 v1（v1 的「显式 `AddXxxFeature()` 注册 + `SB.Framework.Feature.*` 命名」已被推翻，理由见 §1.4）。
> 适用范围：`SB.Framework.*` 基础框架 + 其派生项目（PM / Nexus / vshop 等）。
> 本文是要长期遵循的地基文档。所有 `SB.Feature.*` 功能包按本文范式扩展。
>
> ⚠️ **一致性勘误（2026-08-05）**：§6.3 原文描述的 `MasterKeyMiddleware` / `X-Master-Key` 万能密钥 **已从源码移除**，由 `SB.Feature.AccessToken` 插件的 **FullAccess(god) API Key** 取代（可吊销/审计/IP 白名单）。§6.3 的"运维后门"归类结论保留，但实现范式改为 AccessToken，详见 §6.3 末尾修订。其余章节（发现层 / 清单层 / 数据装配层 / 装配时序 / 包发布）与源码一致。

---

## 目录

1. [架构总览](#1-架构总览)
2. [核心接口定义](#2-核心接口定义完整代码)
3. [程序集发现机制](#3-程序集发现机制完整代码)
4. [装配时序：改造后的 AddSbFramework / UseSbFramework](#4-装配时序改造后的-addsbframework--usesbframework)
5. [与 IModuleInitializer 的关系](#5-与-imoduleinitializer-的关系)
6. [主权功能边界](#6-主权功能边界)
7. [配置系统](#7-配置系统)
8. [包发布矩阵](#8-包发布矩阵)
9. [样板插件一：SB.Feature.BackupReceive](#9-样板插件一sbfeaturebackupreceive)
10. [样板插件二：SB.Feature.LicenseManage](#10-样板插件二sbfeaturelicensemanage)
11. [派生项目接入示例](#11-派生项目接入示例)
12. [未来新增一个插件的标准步骤](#12-未来新增一个插件的标准步骤)
13. [已决策 / 待实现时确认 汇总](#13-已决策--待实现时确认-汇总)
14. [分阶段实施清单](#14-分阶段实施清单)

---

## 1. 架构总览

### 1.1 一句话定义

把框架里用 `#if ENABLE_*` 条件编译塞进主包的可选功能（授权管理、备份接收端等），抽成**独立 NuGet 包 `SB.Feature.{Name}`**。**引包即启用**：框架启动时读 `.deps.json` 自动发现所有 `SB.Feature.*` 程序集并装配，无需派生项目手写任何注册代码；`appsettings` 的 `Features:Disabled` 黑名单可运行期关闭某个 feature。

### 1.2 三层职责分解

整个插件体系由三个正交的子系统协作完成，靠两个数据结构（`FeatureCatalog` / `FeatureDescriptor`）解耦：

| 子系统 | 回答的问题 | 核心产物 |
|---|---|---|
| **发现层** | "谁在场、来自哪个程序集、是否启用？" | `FeatureCatalog`（单一真相源，≈ Furion 的 `App.Assemblies`） |
| **接口/生命周期层** | "在场者怎么把自己装进去？" | `IFeatureModule` 清单接口 + `FeatureContext` |
| **数据装配层** | "在场者要建什么表/菜单/种子？" | 复用既有 `IModuleInitializer`（不动） |

**关键设计原则：清单（manifest）与执行体（service）分离。** Furion 的 `IServiceComponent` 把"我是谁、装什么、怎么装"全塞进一个一次性实例。本框架不能照抄，因为执行体（Service）必须进 DI 容器拿 `IFreeSql`/`ILogger`。所以：

- **清单 `IFeatureModule`**：轻量、无状态、`Activator.CreateInstance` 实例化、**不进容器**、装配完即弃。只负责"声明边界 + 挂钩子 + 指向自己的程序集"。
- **执行体 Service**：照旧走 `[AutoDependencyInjection]` 注册为 Scoped、构造注入。一字不改。
- **数据声明 `IModuleInitializer`**：照旧 `RuntimeHelpers.GetUninitializedObject` 无构造实例化、纯声明式。

### 1.3 整体数据流

```
┌─────────────────────────────────────────────────────────────────────┐
│ AddSbFramework() 内部                                                  │
│                                                                       │
│  ① FeatureScanner.Scan(options)                                       │
│       读 DependencyContext.Default.RuntimeLibraries (.deps.json 清单)  │
│       按 "SB.Feature." 前缀过滤 + Assembly.Load + 黑名单过滤           │
│       ↓                                                                │
│  ② FeatureCatalog (静态单一真相源 FeatureRuntime.Catalog)              │
│       ├─ FeatureAssemblies   : 仅 enabled feature 的程序集            │
│       ├─ AllScanAssemblies   : 框架Services → 各feature → 项目         │
│       └─ Features            : 全部(含禁用,带原因) 供日志              │
│       ↓                            ↓                          ↓        │
│  ③ DI注册              ④ Controller发现           ⑤ 清单装配钩子      │
│     AddBusinessServices   foreach FeatureAssemblies  foreach 已实例化  │
│     (遍历AllScan,            mvc.AddApplicationPart      IFeatureModule  │
│      [AutoDI]自动扫)        (框架硬编码,不交插件)      .ConfigureServices│
│                                                                       │
└─────────────────────────────────────────────────────────────────────┘
                                  ↓
┌─────────────────────────────────────────────────────────────────────┐
│ UseSbFramework() 内部                                                  │
│  ⑥ foreach IFeatureModule.Configure  (中间件,UseAuthorization之后)    │
│  ⑦ SeedDatabase → DiscoverModuleInitializers(catalog)                 │
│       遍历同一份 AllScanAssemblies → 建表/菜单/系统设置/种子           │
│  ⑧ FeatureCatalog.LogStartupSummary  (启动日志,含副作用清单)          │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.4 与 v1 的冲突裁决（为何推翻显式注册）

v1 的 §三明确否决了自动扫描，给了三条理由。本 v2 按用户拍板决策走自动扫描，逐条回应：

| v1 否决理由 | v2 的处理 |
|---|---|
| "懒加载玄学：`AppDomain.GetAssemblies()` 扫不全" | **已被消除**。v2 不用 `AppDomain`，用 `DependencyContext.Default.RuntimeLibraries`（**编译期清单**，列出全部引用无论是否已 JIT 加载）。实测 PM 的 `dist/api/Sb.Apps.Pm.WebApi.deps.json`：6 个 SB.* 包即便懒加载也全部在 runtime targets 里。**该顾虑在 deps.json 路线下技术上不成立。** |
| "隐式依赖难溯源" | **部分仍成立**，用启动日志打印 feature 副作用清单（菜单数/表名/路由前缀）+ 可选诊断端点补偿（§7.4）。这是"静态可审计性"代价，可接受。 |
| "控制权（引包但不启用）" | **黑名单 `Features:Disabled` 覆盖且更优**（不改代码、不重编即可关）。 |

包名从 v1 的 `SB.Framework.Feature.*` 改为 **`SB.Feature.*`**（更短，与用户命名约定一致）。

---

## 2. 核心接口定义（完整代码）

### 2.1 设计裁决摘要

评审在 4 份设计间收敛了接口形状，**已决策**如下：

- **接口名 `IFeatureModule`**（3:1，且减少改写面）。
- **程序集字段 `IReadOnlyList<Assembly>`，默认 `[GetType().Assembly]`**——容忍"清单在 A 包、实体/逻辑在 B 包"的分包插件，99% 单包插件零配置。
- **`FeatureContext` 封装上下文**，但**不含 `ApplicationPartManager`**——Controller 发现由框架发现器收归（§4 [F-4]），因此清单接口**不需要认识 MVC 类型**，可放 `SB.Framework.Core`（Core 已有 DI/Builder 类型但无 `Microsoft.AspNetCore.Mvc.Core`）。
- **`OptionsType` 成员**——框架据此自动 `Configure` 强类型配置（§7.2）。
- **`DependsOn` 用特性而非接口成员**——依赖是类型级元数据，可在实例化前反射读取用于排序；99% 无依赖插件不被迫写空集合。

### 2.2 `IFeatureModule`（清单接口，放 SB.Framework.Core）

```csharp
// 文件：SB.Framework.Core/Features/IFeatureModule.cs
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace SB.Framework.Features;

/// <summary>
/// SB.Feature 插件清单。每个 SB.Feature.* 包提供恰好一个实现，
/// 框架通过 .deps.json 扫描程序集后用 Activator.CreateInstance 实例化它（必须有公共无参构造）。
///
/// 重要约束：
///   1. 清单实例【不进 DI 容器】，是一次性安装器。ConfigureServices/Configure 执行完即弃。
///   2. 清单方法里【禁止】访问需要注入的状态——真正的业务逻辑在本插件程序集内的 Service 里，
///      由 [AutoDependencyInjection] 自动注册、构造注入。清单只负责"声明边界 + 挂钩子"。
///   3. 清单必须是【无状态薄壳】（无字段），以保证无参构造 + Activator.CreateInstance 零成本。
///
/// 为什么落 Core 而非 Models：ConfigureServices/Configure 的签名引用 IServiceCollection/
/// IApplicationBuilder（ASP.NET 抽象）。Models 是纯数据层，不该认识这些类型。Core 已引
/// DI/Builder（见 License/LicenseServiceExtensions.cs），放这里零新增依赖。
/// </summary>
public interface IFeatureModule
{
    // ① 自我标识 —— 黑名单匹配、日志、去重的 key
    /// <summary>插件唯一名，约定 = 包名去 "SB.Feature." 前缀，如 "LicenseManage" / "BackupReceive"。
    /// 用于 appsettings "Features:Disabled" 黑名单匹配、配置子节 key、启动日志。
    /// 必须全局唯一；重名时后发现的覆盖先发现的。</summary>
    string FeatureName { get; }

    /// <summary>展示名（菜单/日志可读），如 "授权管理"。</summary>
    string DisplayName { get; }

    // ② 本插件的业务程序集 —— 发现 Service / Controller / IModuleInitializer 的范围
    /// <summary>默认返回清单类所在程序集。99% 情况清单与 Service/实体同包，作者无需重写。
    /// 跨包（清单在 A、实体在 B）才显式返回多个。框架据此：
    ///   (a) 把这些程序集喂给统一的 IModuleInitializer 扫描器；
    ///   (b) 对这些程序集 AddApplicationPart 让跨程序集 Controller 被路由；
    ///   (c) 扫 [AutoDependencyInjection] 注册 Service（经统一 AllScanAssemblies）。</summary>
    IReadOnlyList<Assembly> FeatureAssemblies => new[] { GetType().Assembly };

    // ③ 是否默认启用
    /// <summary>默认启用（落实"自动发现+默认启用"决策）。极少数"装了但默认关、靠白名单显式开"
    /// 的插件可返回 false。最终是否加载 = EnabledByDefault && !Features:Disabled.Contains(FeatureName)。</summary>
    bool EnabledByDefault => true;

    // ④ 强类型配置类型（可选）
    /// <summary>本插件的强类型配置类。返回非 null 时，框架自动
    /// services.Configure(OptionsType, Configuration.GetSection($"Features:{FeatureName}"))。
    /// 插件 Service 里注入 IOptions&lt;TOptions&gt; 即可读。默认 null = 无私有配置。</summary>
    Type? OptionsType => null;

    // ⑤ 服务注册钩子（默认空实现）
    /// <summary>服务注册期：往 DI 加 [AutoDependencyInjection] 覆盖不到的东西。
    /// 大多数 Service 已被自动扫描注册（标 [AutoDependencyInjection] 即可），这里只处理"特例"：
    ///   - 接口→实现的手动绑定（自动 DI 只注册具体类型不绑接口，按接口注入的必须手动绑，
    ///     如 services.AddScoped&lt;IXxx, XxxImpl&gt;()）；
    ///   - Options / HttpClient / BackgroundService / 第三方库的 AddXxx。
    /// 【禁止】在这里 AddScoped 已标 [AutoDependencyInjection] 的常规 Service —— 会双重注册。
    ///
    /// ctx.IsInstalled = false（未安装）时，不要注册依赖 IFreeSql 的服务（IFreeSql 此时未注册，
    /// 会留下永远解析不了的 Scoped）。框架的常规 Service 注册本就裹在 isInstalled 分支里。</summary>
    void ConfigureServices(IServiceCollection services, FeatureContext ctx) { }

    // ⑥ 中间件钩子（默认空实现）
    /// <summary>中间件管线期：插件需要自己的中间件时用（如某 Feature 要 app.UseXxx）。
    /// 默认空——两个样板插件都不需要（控制器足矣）。
    /// 固定锚点：UseAuthorization 之后、MapControllers 之前（即所有插件中间件在 License 闸门内侧，
    /// 见 §6 主权边界）。插件【不得】插队到 License 校验之前。</summary>
    void Configure(IApplicationBuilder app, FeatureContext ctx) { }
}
```

### 2.3 `FeatureContext`（装配上下文，放 SB.Framework.Core）

```csharp
// 文件：SB.Framework.Core/Features/FeatureContext.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SB.Framework.Features;

/// <summary>装配上下文。一次性，随清单实例的 ConfigureServices/Configure 流程传递。
/// 不含 ApplicationPartManager —— Controller 发现由框架发现器收归，清单不碰 MVC 类型。</summary>
public sealed class FeatureContext
{
    /// <summary>读 appsettings（条件装配 / 连云地址 / 本 feature 私有配置）。</summary>
    public required IConfiguration Configuration { get; init; }

    /// <summary>IsDevelopment / ContentRootPath。</summary>
    public required IHostEnvironment Environment { get; init; }

    /// <summary>installed.lock 是否存在。决定 DB 相关注册是否跳过（对齐框架 isInstalled 门控）。</summary>
    public required bool IsInstalled { get; init; }

    /// <summary>已通过黑名单 + 拓扑排序的最终 feature 清单。
    /// 插件可据此做"另一个 feature 在场才注册某 decorator"的条件装配。</summary>
    public required IReadOnlyList<IFeatureModule> LoadedFeatures { get; init; }

    /// <summary>启动期 bootstrap logger（此时 DI 容器尚未 Build，用 Serilog 静态 logger 封装）。</summary>
    public required ILogger Logger { get; init; }
}
```

### 2.4 `DependsOnFeatureAttribute`（依赖声明，放 SB.Framework.Core）

```csharp
// 文件：SB.Framework.Core/Features/DependsOnFeatureAttribute.cs
namespace SB.Framework.Features;

/// <summary>声明本插件依赖另一个插件。框架按拓扑排序保证被依赖者的 ConfigureServices 先执行。
/// 用特性而非接口成员：依赖是类型级元数据，可在 Activator.CreateInstance 之前反射读取用于排序，
/// 且不强迫 99% 无依赖插件写空集合。
///
/// 用法（未来的报表插件）：
///   [DependsOnFeature(typeof(LicenseManageFeature))]
///   public sealed class LicenseReportFeature : IFeatureModule { ... }
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DependsOnFeatureAttribute : Attribute
{
    public Type FeatureType { get; }
    public DependsOnFeatureAttribute(Type featureType) => FeatureType = featureType;
}
```

> **DependsOn 排序规则（已决策）**：标准 Kahn 拓扑排序，被依赖者排前面，`ConfigureServices` 按拓扑序调用。
> - **平局打破**：同层级按 `FeatureName` 字典序稳定排序（保证多次启动顺序确定，日志可复现）。
> - **循环依赖**：检测到环立即抛 `InvalidOperationException`，消息列出环路径（如 `"Feature 循环依赖: A → B → A"`）。
> - **缺失依赖**：`[DependsOnFeature(typeof(X))]` 但 X 被黑名单关掉或没装 → 默认报错中止；留 `Features:IgnoreMissingDependencies` 开关给降级场景。
> - 与 `IModuleInitializer.InitOrder` 不冲突：前者排"代码装配序"（`ConfigureServices`），后者排"数据装配序"（建表/种子）。两者可不同向（A 的 Service 依赖 B 的 Service，但 A 的表要先于 B 建），故不强行统一。

---

## 3. 程序集发现机制（完整代码）

发现层是整个体系的地基，产出**单一真相源** `FeatureCatalog`。三套现有发现机制（`AddBusinessServices` / `DiscoverModuleInitializers` / Controller `ApplicationPart`）全部改成"遍历这个清单"，不再各自 `Assembly.Load` 写死的两个名字。

### 3.1 关键技术决策（评审收敛）

- **【已决策】用 `lib.GetDefaultAssemblyNames(ctx)` 取真实程序集名，绝不用 `lib.Name`（包名）。** 这是 4 份设计里 `discovery` 唯一处理对、其余三份会踩坑的点。实测：包 `SB.Framework.Core.NoLicense/0.2.1` 产出的**程序集名是 `SB.Framework.Core`**（`.NoLicense` 只是 NuGet PackageId，`AssemblyName` 不带后缀）。若按 `lib.Name` 去 `Assembly.Load("SB.Feature.X.NoLicense")`，而程序集叫 `SB.Feature.X`，**加载失败**。从 runtime assembly group 取的才是 `Assembly.Load` 能用的名字，也天然把双变体归一。
- **【已决策】黑名单过滤在"程序集清单层"，不在"FeatureName 装配层"。** 被 Disabled 的 feature 程序集**不进** `AllScanAssemblies`，于是装配侧（DI/Controller）和数据侧（`IModuleInitializer`）遍历同一份被过滤的清单，**一处过滤三处生效**。这是堵住"功能关了菜单还在"事故（§5.4）的决定性设计。
- **【已决策】三层兜底链**：(A) 派生项目显式清单（单文件/裁剪发布）→ (B) deps.json → (C) 已加载程序集。

### 3.2 `FeatureScanner` + `FeatureScannerOptions`

```csharp
// 文件：SB.Framework.Core/Features/FeatureScanner.cs
using System.Reflection;
using Microsoft.Extensions.DependencyModel;

namespace SB.Framework.Features;

public sealed class FeatureScannerOptions
{
    /// <summary>feature 包名前缀（含末尾点），决定"什么算 feature"。</summary>
    public string FeaturePrefix { get; init; } = "SB.Feature.";

    /// <summary>纳入扫描、但不一定是 feature 的框架/项目程序集前缀白名单。</summary>
    public string[] ScanPrefixes { get; init; } = { "SB.Framework.", "SB.Feature." };

    /// <summary>appsettings Features:Disabled，按 FeatureName 短名或完整程序集名匹配。</summary>
    public string[] Disabled { get; init; } = Array.Empty<string>();

    /// <summary>派生项目业务程序集名（沿用现有 ProjectAssemblyName，单独传入因为它不带 SB. 前缀）。</summary>
    public string? ProjectAssemblyName { get; init; }

    /// <summary>单文件/裁剪发布兜底：宿主显式提供的程序集清单（最高优先级）。</summary>
    public IReadOnlyList<Assembly>? SingleFilePublishAssemblies { get; init; }
}

public static class FeatureScannerDiagnostics
{
    /// <summary>load 失败的程序集名 → 失败原因，用于启动日志打 "skipped (load failed)"。</summary>
    public static readonly Dictionary<string, string> LoadFailures = new(StringComparer.OrdinalIgnoreCase);
}

public static class FeatureScanner
{
    public static FeatureCatalog Scan(FeatureScannerOptions options)
    {
        var assemblyNames = ResolveCandidateAssemblyNames(options);
        var loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in assemblyNames)
        {
            try { loaded[name] = Assembly.Load(new AssemblyName(name)); }
            catch (Exception ex)
            {
                // 不致命：记下来，启动日志里打 "skipped (load failed)"
                FeatureScannerDiagnostics.LoadFailures[name] = ex.Message;
            }
        }

        return FeatureCatalog.Build(loaded.Values, options);
    }

    /// <summary>读 .deps.json 清单，不依赖 AppDomain 已加载集合。三层兜底。</summary>
    private static IEnumerable<string> ResolveCandidateAssemblyNames(FeatureScannerOptions o)
    {
        // (A) 单文件/裁剪兜底优先：宿主已显式提供清单
        if (o.SingleFilePublishAssemblies is { Count: > 0 } provided)
            return provided.Select(a => a.GetName().Name!)
                           .Where(n => MatchesAnyPrefix(n, o) || IsProject(n, o))
                           .Distinct(StringComparer.OrdinalIgnoreCase);

        // (B) 常规：DependencyContext.Default 来自入口程序集的 .deps.json
        var ctx = DependencyContext.Default;
        if (ctx is null)
            return ResolveFromLoadedAssemblies(o); // (C) 再兜底：deps.json 缺失（极少见）

        // 关键：GetDefaultAssemblyNames 取真实程序集名而非包名 lib.Name
        // —— 双变体包 SB.Framework.Core.NoLicense 的 AssemblyName 仍是 SB.Framework.Core
        var names = ctx.RuntimeLibraries
            .SelectMany(lib => lib.GetDefaultAssemblyNames(ctx))
            .Select(an => an.Name!)
            .Where(n => !string.IsNullOrEmpty(n));

        return names.Where(n => MatchesAnyPrefix(n, o) || IsProject(n, o))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ResolveFromLoadedAssemblies(FeatureScannerOptions o)
        => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name!)
            .Where(n => !string.IsNullOrEmpty(n) && (MatchesAnyPrefix(n, o) || IsProject(n, o)))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool MatchesAnyPrefix(string asmName, FeatureScannerOptions o)
        => o.ScanPrefixes.Any(p => asmName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static bool IsProject(string asmName, FeatureScannerOptions o)
        => !string.IsNullOrEmpty(o.ProjectAssemblyName)
           && asmName.Equals(o.ProjectAssemblyName, StringComparison.OrdinalIgnoreCase);
}
```

### 3.3 `FeatureDescriptor` + `FeatureCatalog` + `FeatureRuntime`

```csharp
// 文件：SB.Framework.Core/Features/FeatureCatalog.cs
using System.Reflection;
using Serilog;

namespace SB.Framework.Features;

/// <summary>单个 feature 的元数据。发现层写，装配/数据/生命周期三层读。</summary>
public sealed record FeatureDescriptor(
    string FeatureName,          // 短名，"SB.Feature.LicenseManage" → "LicenseManage"
    Assembly Assembly,
    bool Enabled,                // 经黑名单过滤后的最终状态
    string? DisabledReason);     // "disabled by config" / "load failed: ..." / null

/// <summary>程序集发现的单一真相源。≈ Furion 的 App.Assemblies。
/// 区别：按 SB.Feature. 前缀 + deps.json 发现、按黑名单（程序集清单层）过滤。</summary>
public sealed class FeatureCatalog
{
    /// <summary>含被禁用的（带原因），供启动日志。</summary>
    public IReadOnlyList<FeatureDescriptor> Features { get; }

    /// <summary>仅 Enabled feature 的程序集，供 Controller AddApplicationPart。</summary>
    public IReadOnlyList<Assembly> FeatureAssemblies { get; }

    /// <summary>SB.Framework.Services（恒在）。</summary>
    public Assembly? FrameworkServicesAssembly { get; }

    /// <summary>派生项目业务程序集（可空）。</summary>
    public Assembly? ProjectAssembly { get; }

    /// <summary>三套发现机制统一遍历它。
    /// 顺序固定：框架Services → 各feature(按FeatureName排序,幂等) → 项目程序集。
    /// 项目永远排最后 ⇒ "后注册覆盖先注册"，项目子类仍覆盖 feature/框架基类。</summary>
    public IReadOnlyList<Assembly> AllScanAssemblies { get; }

    private FeatureCatalog(
        IReadOnlyList<FeatureDescriptor> features,
        Assembly? frameworkServices,
        Assembly? project)
    {
        Features = features;
        FrameworkServicesAssembly = frameworkServices;
        ProjectAssembly = project;
        FeatureAssemblies = features.Where(f => f.Enabled && f.Assembly is not null)
                                    .Select(f => f.Assembly).ToList();

        var all = new List<Assembly>();
        if (frameworkServices is not null) all.Add(frameworkServices);
        all.AddRange(features.Where(f => f.Enabled && f.Assembly is not null)
                             .OrderBy(f => f.FeatureName, StringComparer.Ordinal)
                             .Select(f => f.Assembly));
        if (project is not null) all.Add(project);
        AllScanAssemblies = all.Distinct().ToList();
    }

    internal static FeatureCatalog Build(IEnumerable<Assembly> loaded, FeatureScannerOptions o)
    {
        var disabled = new HashSet<string>(o.Disabled, StringComparer.OrdinalIgnoreCase);
        var features = new List<FeatureDescriptor>();
        Assembly? frameworkServices = null;
        Assembly? project = null;

        foreach (var asm in loaded)
        {
            var asmName = asm.GetName().Name!;

            if (asmName.Equals("SB.Framework.Services", StringComparison.OrdinalIgnoreCase))
            { frameworkServices = asm; continue; }

            if (!string.IsNullOrEmpty(o.ProjectAssemblyName)
                && asmName.Equals(o.ProjectAssemblyName, StringComparison.OrdinalIgnoreCase))
            { project = asm; continue; }

            if (!asmName.StartsWith(o.FeaturePrefix, StringComparison.OrdinalIgnoreCase))
                continue; // 其余框架程序集不算 feature

            var shortName = asmName[o.FeaturePrefix.Length..];           // "LicenseManage"
            bool isDisabled = disabled.Contains(shortName) || disabled.Contains(asmName);
            features.Add(new FeatureDescriptor(
                shortName, asm,
                Enabled: !isDisabled,
                DisabledReason: isDisabled ? "disabled by config" : null));
        }

        // 把 load 失败的补成 disabled 记录
        foreach (var (name, reason) in FeatureScannerDiagnostics.LoadFailures)
        {
            if (!name.StartsWith(o.FeaturePrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var shortName = name[o.FeaturePrefix.Length..];
            if (features.Any(f => f.FeatureName.Equals(shortName, StringComparison.OrdinalIgnoreCase)))
                continue;
            features.Add(new FeatureDescriptor(shortName, null!, Enabled: false,
                DisabledReason: $"load failed: {reason}"));
        }

        return new FeatureCatalog(features, frameworkServices, project);
    }

    /// <summary>启动日志：打印已加载 / 被禁用的 feature 清单（含原因）。</summary>
    public void LogStartupSummary()
    {
        var enabled = Features.Where(f => f.Enabled).Select(f => f.FeatureName)
                              .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var disabled = Features.Where(f => !f.Enabled).ToArray();

        Log.Information("[Features] loaded: {Features}",
            enabled.Length == 0 ? "(none)" : string.Join(", ", enabled));

        foreach (var f in disabled)
            Log.Warning("[Features] {Name} discovered but skipped: {Reason}",
                f.FeatureName, f.DisabledReason);
    }
}

/// <summary>进程级静态单一真相源。AddSbFramework 早期构建一次，UseSbFramework / SeedDatabase /
/// SetupService 静态方法都读这份缓存，不重扫。风格对齐既有 AppGlobal.AppSettings。</summary>
public static class FeatureRuntime
{
    public static FeatureCatalog Catalog { get; set; } = default!;
}
```

### 3.4 `IFeatureSingleFilePublish`（单文件/裁剪发布兜底）

```csharp
// 文件：SB.Framework.Core/Features/IFeatureSingleFilePublish.cs
using System.Reflection;

namespace SB.Framework.Features;

/// <summary>单文件/裁剪发布时，派生项目实现此接口显式提供 feature 程序集清单。
/// 常规发布无需实现 —— DependencyContext 路径即可。
/// 用法：派生项目在 Program.cs 显式 new XxxSingleFilePublish().IncludeAssemblies() 传入
/// FeatureScannerOptions.SingleFilePublishAssemblies（最稳，不依赖反射）。</summary>
public interface IFeatureSingleFilePublish
{
    Assembly[] IncludeAssemblies();
}
```

### 3.5 三套现有发现机制的改造

核心改造：**所有 `Assembly.Load("SB.Framework.Services")` + `Assembly.Load(projectAssemblyName)` 的成对硬编码，替换为遍历 `catalog.AllScanAssemblies`**。三套机制内部逻辑（`[AutoDependencyInjection]` 扫描 / `GetUninitializedObject` 实例化 / 基类工厂绑定）**完全不动**，只换"扫哪些程序集"。

```csharp
// 改造 1：ServiceSetup.AddBusinessServices（DI 扫描）
//   现状：RegisterServicesFromAssembly(services, "SB.Framework.Services") + 单 projectAssemblyName
public static IServiceCollection AddBusinessServices(this IServiceCollection services, FeatureCatalog catalog)
{
    // AllScanAssemblies 顺序 = 框架 → feature(短名排序) → 项目
    // 沿用"后注册覆盖先注册"：项目排最后 ⇒ 项目子类仍覆盖框架/feature 基类
    foreach (var asm in catalog.AllScanAssemblies)
        RegisterServicesFromAssembly(services, asm);   // 入参从 string 改 Assembly，省掉 Assembly.Load
    return services;
}
// RegisterServicesFromAssembly([AutoDependencyInjection] 扫描 + 基类工厂绑定) 内部逻辑完全不动。
// BackupReceiveService : BaseCrudService<T> 这种 feature 继承点，程序集进清单后自动被现有逻辑处理。
```

```csharp
// 改造 2：SetupService.DiscoverModuleInitializers（IModuleInitializer 发现）
//   现状：ScanModuleInitializers(modules, "SB.Framework.Services") + 单 projectAssemblyName
public static List<IModuleInitializer> DiscoverModuleInitializers(FeatureCatalog catalog)
{
    var modules = new Dictionary<string, IModuleInitializer>();   // ModuleName 去重，后扫覆盖先扫
    foreach (var asm in catalog.AllScanAssemblies)
        ScanModuleInitializers(modules, asm);                     // 入参 string → Assembly
    return modules.Values.OrderBy(m => m.InitOrder).ToList();
}
// ScanModuleInitializers 内部的 RuntimeHelpers.GetUninitializedObject 声明式实例化完全不动。
//
// 兼容缝：保留旧重载 DiscoverModuleInitializers(string?) 转发到 FeatureRuntime.Catalog 版，
// 避免现有调用点全炸；但新代码一律走 catalog 重载。
// DatabaseSeeder 的 4 个 Sync* 和 SetupService.Install 改读 FeatureRuntime.Catalog。
```

```csharp
// 改造 3：Controller 的 ApplicationPart（框架硬编码，不交插件 —— 见 §4 [F-4]）
//   现状：全框架零 AddApplicationPart，feature 包 Controller 必然 404
foreach (var asm in catalog.FeatureAssemblies)   // 仅 feature 程序集需补；框架/项目通常已被默认扫到
    mvc.AddApplicationPart(asm);                  // mvc = AddControllersWithViews() 返回的 IMvcBuilder
```

> **`AllScanAssemblies` 是本发现层对外的唯一公开面**，等价 Furion 的 `App.Assemblies`。一处改 `FeatureScanner.Scan`，三套机制 + 安装向导 + 未来任意第 4 套同时受益。

---

## 4. 装配时序：改造后的 AddSbFramework / UseSbFramework

只标注**新增/改动**点（`★` = 新插入的 feature 装配点；`▲` = 把写死扫描改为遍历清单），其余保持原样。锚点行号对齐当前 `SbFrameworkExtensions.cs`。

### 4.1 AddSbFramework 骨架

```csharp
public static WebApplicationBuilder AddSbFramework(this WebApplicationBuilder builder)
{
    var configuration = builder.Configuration;
    AppGlobal.AppSettings = configuration.Get<AppSettings>();
    ArchiveOldLogs();
    /* Serilog / UseSystemd 不变 */

    // ★[F-1] 发现 + 黑名单过滤 —— 必须最早，后续所有扫描都依赖它产出的 catalog
    var featureOptions = new FeatureScannerOptions {
        Disabled            = configuration.GetSection("Features:Disabled").Get<string[]>() ?? Array.Empty<string>(),
        ProjectAssemblyName = configuration.GetValue<string>("ProjectAssemblyName"),
        // 单文件发布时派生项目显式传 SingleFilePublishAssemblies（§3.4）
    };
    FeatureRuntime.Catalog = FeatureScanner.Scan(featureOptions);
    var catalog = FeatureRuntime.Catalog;

    // 实例化清单 + 应用 EnabledByDefault + 拓扑排序（被禁的程序集已不在 catalog，这里只排已启用的）
    var modules = FeatureModuleActivator.ActivateAndSort(catalog, configuration); // §4.3

    builder.Services.AddSetupServices();
    bool isInstalled = File.Exists(Path.Combine(builder.Environment.ContentRootPath, "installed.lock"));

#if !DISABLE_LICENSE
    builder.Services.AddLicenseService();      // ① 主权功能，独立于 feature 体系，原样不动
#else
    builder.Services.AddSingleton<ILicenseService>(new NullLicenseService());
#endif

    var featureCtx = new FeatureContext {
        Configuration = configuration, Environment = builder.Environment,
        IsInstalled = isInstalled, LoadedFeatures = modules,
        Logger = new SerilogLoggerFactory(Log.Logger).CreateLogger("Features"),
    };

    if (isInstalled)
    {
        builder.Services.AddFreeSql(configuration);
        builder.Services.AddJwtAuthentication(configuration);

        // ▲[F-2] 业务服务注册：遍历 catalog.AllScanAssemblies，[AutoDependencyInjection] 自动扫
        builder.Services.AddBusinessServices(catalog);

        builder.Services.AddCacheService();
        builder.Services.AddEmailServices();
        builder.Services.AddQdrantService(configuration);
        builder.Services.AddBackgroundTasks();
        builder.Services.AddLoginRateLimiter();
        builder.Services.AddDatabaseHealthCheck(configuration);

        // ★[F-3] feature 自定义 DI 安装 + 强类型 Options 自动绑定（按拓扑序）
        foreach (var m in modules)   // modules 已拓扑排序
        {
            if (m.OptionsType is not null)
                builder.Services.Configure(m.OptionsType,
                    configuration.GetSection($"Features:{m.FeatureName}"));   // §7.2 非泛型重载
            m.ConfigureServices(builder.Services, featureCtx);               // 只做接口绑定等特例
        }
    }
    else { builder.Services.AddAuthentication(); builder.Services.AddMemoryCache(); }

    /* Cors / Swagger / HttpContextAccessor / RazorPages / Kestrel limits 不变 */

    var mvc = builder.Services.AddControllersWithViews(options => { /* 原有 conventions 不变 */ })
                              .AddJsonOptions(/* 不变 */);

    // ★[F-4] Controller 跨程序集发现：框架硬编码，不交插件。必须在 AddControllersWithViews 返回的
    //   mvcBuilder 上链式调用（AddApplicationPart 是 IMvcBuilder 扩展）。这是整个插件化最易塌、
    //   塌了就是"feature Controller 全 404 且无报错"的一环。
    foreach (var asm in catalog.FeatureAssemblies)
        mvc.AddApplicationPart(asm);

    AppTime.Initialize(configuration["TimeZone"]);
    return builder;
}
```

### 4.2 UseSbFramework 骨架

```csharp
public static WebApplication UseSbFramework(this WebApplication app)
{
    bool isInstalled = File.Exists(Path.Combine(app.Environment.ContentRootPath, "installed.lock"));
    var catalog = FeatureRuntime.Catalog;
    var modules = FeatureModuleActivator.GetSorted();   // 复用 AddSbFramework 阶段排好的实例
    /* HSTS / Cors / RateLimiter / Serilog request logging 不变 */

#if !DISABLE_LICENSE
    if (isInstalled && !app.Environment.IsDevelopment())
        app.UseLicense();        // ★主权闸门：最外层，插件够不到。不动
#endif

    app.UseStaticFiles();
    app.UseMiddleware<SetupMiddleware>();
    app.UseRouting();
    // 旧 MasterKeyMiddleware 已移除。运维后门由 AccessToken 插件 FullAccess key 承担：
    // LicenseMiddleware 内部对 FullAccess key 直接放行（见 LicenseMiddleware.cs:80-91），无需额外中间件。
    app.UseAuthorization();

    // ★[F-5] feature 自定义中间件：固定锚点 = UseAuthorization 之后、MapControllers 之前
    //   （所有插件中间件在 License 闸门内侧）
    var featureCtx = FeatureModuleActivator.BuildAppContext(app, isInstalled, modules);
    foreach (var m in modules)
        m.Configure(app, featureCtx);

    if (isInstalled) app.MapHealthChecks("/health");
    app.MapControllers();        // feature Controller 已通过 [F-4] AddApplicationPart 进入路由表

    // ▲[F-6] 数据库同步：SeedDatabase 内部 Discover 改读 catalog（建表/菜单/系统设置/种子）
    //   feature 的 IModuleInitializer 天然合流（程序集已在 AllScanAssemblies）
    if (isInstalled) app.SeedDatabase();

    // ★[F-7] 启动日志：打印已加载 / 被禁用 feature 清单 + 副作用（菜单数/表名/路由）
    catalog.LogStartupSummary();

    return app;
}
```

### 4.3 时序硬约束（已决策）

1. **`[F-1]` 必须第一**——catalog 是 `[F-2/3/4]` 的输入。
2. **`[F-4]` AddApplicationPart 必须在 `AddControllersWithViews()` 之后链式调用**，因为它是 `IMvcBuilder` 扩展。现状 `AddControllersWithViews` 在方法末尾，feature 装配（`[F-2/3]`）在它之前，`[F-4]` 紧接其后。
3. **License DI（①）在 feature 发现之前**——保证插件即使想注入 `ILicenseService` 也拿得到。
4. **feature Service 注册（`[F-2/3]`）裹在 `isInstalled` 分支内**——未安装时不注册依赖 `IFreeSql` 的东西。
5. **`[F-5]` feature 中间件锚点固定在 `UseAuthorization` 之后、`MapControllers` 之前**——所有插件中间件在 License 闸门内侧，不得插队到 License 之前。
6. **`[F-6]` IModuleInitializer 同步落在 `SeedDatabase`**，时机不变（`MapControllers` 之后、仅 `isInstalled`）。

> **待实现时确认**：`FeatureModuleActivator`（实例化清单 + `Activator.CreateInstance` + 拓扑排序 + 缓存已排序实例供 Use 阶段复用）的具体形态。它要在 `[F-1]` 之后、`[F-3]` 之前完成实例化与排序，并把结果存进进程级缓存（同 `FeatureRuntime.Catalog` 风格）。`BuildAppContext` 把 `WebApplication` 的 `IApplicationBuilder` 面 + `IsInstalled` + 已排序 modules 组装成 `FeatureContext`。

---

## 5. 与 IModuleInitializer 的关系

### 5.1 裁决：组合并存 + 拆类，绝不合并到一个类型

**【已决策】`IFeatureModule`（管"代码装配"）与 `IModuleInitializer`（管"数据装配"）正交并存，靠 `FeatureAssemblies` 单点交汇。一个插件可只实现其一、或两个都实现。**

三选一的否决理由：

- ❌ **二选一替代**：`IModuleInitializer` 被 `RuntimeHelpers.GetUninitializedObject` 实例化（不调构造），`IFeatureModule` 被 `Activator.CreateInstance` 实例化（调无参构造）——**两种实例化语义不兼容**。且分属 Models / Core 两层，合并破坏分层。
- ❌ **新接口继承旧接口**（`IFeatureModule : IModuleInitializer`）：强迫纯逻辑插件写空 `GetEntityTypes()`，强迫 `IModuleInitializer` 认识 `IServiceCollection`。耦合错向。
- ✅ **组合并存 + 拆类**：见下。

### 5.2 三种实现姿态（作者按需选）

```
姿态 A（纯数据插件）：class XInit : IModuleInitializer        // 只建表/菜单，无新 Service
姿态 B（纯代码插件）：class XModule : IFeatureModule          // 只加 API/中间件，不建表
姿态 C（全功能插件）：清单 + Initializer + Service 各一类     // 推荐范式，两个样板都是
```

### 5.3 关键决策：拆类消除 GetUninitializedObject 矛盾

**这是 4 份设计里最重要的正确决策，评审一致采纳。**

矛盾真身：现状样板 `LicenseManageService` 把 `IModuleInitializer`（声明式，要无构造实例化读 `GetMenus()`）和 `[AutoDependencyInjection]`（注入式，要带 3 个依赖的构造函数跑 `GenerateLicense()`）**焊在同一个类**上。框架今天靠 `GetUninitializedObject`（造一个构造没跑、字段全 null 的半成品）+ "`GetMenus()` 里别碰 `_fsql`"的纪律走钢丝。`IModuleInitializer.cs` 注释明文警告这一点——这是个活雷。

**新范式把一身二/三职的类物理拆开**，矛盾从"运行期靠纪律规避"降级为"架构上不可能发生"：

```csharp
// 文件1：清单（薄,无状态,无字段）—— Activator.CreateInstance 实例化,不进容器
public sealed class LicenseManageFeature : IFeatureModule
{
    public string FeatureName => "LicenseManage";
    public string DisplayName => "授权管理";
    // FeatureAssemblies 默认 = 本程序集,不用写
}

// 文件2：声明式数据 —— 仍是 IModuleInitializer,仍被 GetUninitializedObject 实例化
//         但它现在【没有任何构造依赖】,纯声明 → GetUninitializedObject 对它绝对安全,雷被拆除
public sealed class LicenseManageInitializer : IModuleInitializer
{
    public string ModuleName => "授权管理";
    public int InitOrder => 70;
    public List<MenuDefinition> GetMenus() => new() { /* …原样搬过来… */ };
}

// 文件3：业务 Service —— 纯 [AutoDependencyInjection],构造注入 IFreeSql/ILogger/IHostEnvironment
//         不再实现 IModuleInitializer,构造注入与"无参实例化"的矛盾彻底消失
[AutoDependencyInjection]
public class LicenseManageService    // 不再 : IModuleInitializer
{
    public LicenseManageService(IFreeSql fsql, ILogger<LicenseManageService> logger, IHostEnvironment env) { … }
    // GenerateLicense / QuickGenerate / … 原样
}
```

**"只实现一套东西"的真义**：作者没为适配新体系重写业务逻辑，只是把原来挤在一个类里的三种关注点（清单元数据 / 声明式数据 / 注入式逻辑）**按职责拆开**。三者通过 `FeatureAssemblies` 自动被同一次扫描发现，作者无需手动登记任何东西。

### 5.4 黑名单必须同时作用于装配与数据两条链（重大盲区，已堵）

**【已决策】黑名单过滤放在"程序集清单层"（§3.1），不放"FeatureName 装配层"。**

盲区证据：`DatabaseSeeder.SeedDatabase` 独立调用 `DiscoverModuleInitializers` **4 次**（line 49/73/206/276），每次全量扫 `IModuleInitializer`，**不认 `Features:Disabled`**。若黑名单只在装配层按 FeatureName 过滤，会出现：
- 装配侧：`LicenseManage` 的 DI/Controller 不装 ✅
- 数据侧：`DatabaseSeeder.SyncModuleMenus` 照样把"授权管理"菜单同步进库 ❌
- 结果：**功能关了，菜单还在，点进去 404**——真实运维事故。

把过滤下沉到程序集清单层后："关一个 feature = 它的程序集从 `AllScanAssemblies` 消失 = DI/Controller/IModuleInitializer 同时扫不到它"，**一处过滤三处生效**，事故消失。

### 5.5 License 表归属（澄清一个假警报）

落地时有人担心"License 4 实体留 Models、建表声明在插件、验证侧无插件也要写表 → 崩溃"。**源码否掉这个崩溃前提**：`AdminService.cs:32`（核心模块，`InitOrder=0`，零 `#if`）的 `GetEntityTypes()` **已经把 4 个 License 实体放进去了**，注释写"授权验证实体（核心基础设施，始终需要建表）"。

**【已决策】**：
- 4 张 License 表（`LicenseClient`/`LicenseRecord`/`LicenseRequest`/`LicenseVerifyLog`）由**核心 `AdminService` 无条件建表，现状不动**，根本不依赖 LicenseManage 插件在不在。
- `LicenseManageInitializer.GetEntityTypes()` **必须留空**（默认实现），绝不重复声明这 4 个实体——否则语义上是"插件声明了不属于它的表"。
- 这条与"实体归属判据"（§8.4）一致：被核心反向引用的实体留 Models 且由核心建表。

---

## 6. 主权功能边界

主权功能 = 商业控制底线，客户不能关。**它独立于插件体系，两套机制并存，互不引用。** 关键区分线：

| 维度 | 主权（授权验证） | 插件（SB.Feature.*） |
|---|---|---|
| 控制方式 | 编译期 `#if DISABLE_LICENSE` + 双变体包 | 运行期 deps.json 扫描 + 黑名单 |
| 能否关 | **关不掉**（关 = 商业失守） | 可关（`Features:Disabled`） |
| 管线位置 | 管线最外层闸门 | 闸门内侧 |
| 程序集 | 常驻主包，无 `#if` 包裹 | 独立 `SB.Feature.*` 包 |

### 6.1 五条边界规则（已决策，经源码验证）

**规则 1 — 程序集发现对 License 变体完全不敏感。** deps.json 扫描按程序集名前缀 `SB.Feature.` 过滤；`NoLicense` 后缀只加在 4 个主包的 PackageId 上（程序集名不带后缀）。插件包名 `SB.Feature.LicenseManage` 不带 License 变体后缀。→ **NoLicense 主包下插件发现 100% 正常**，天然解耦，零特殊处理。

**规则 2 — 中间件管线位置写死在框架，插件不得插队到 License 之前。** 顺序：

```
UseStaticFiles
→ [#if !DISABLE_LICENSE] LicenseMiddleware     ← 主权闸门（最外层,插件够不到;FullAccess key 在此放行）
→ SetupMiddleware
→ UseRouting
→ UseAuthorization
→ ★ 插件中间件锚点 [F-5]（所有插件中间件在此,License 内侧）
→ MapControllers
```
`LicenseMiddleware` 是"先验证授权、再放行"的闸门，任何插件中间件必须在闸门**内侧**。

**规则 3 — 装配时序：插件发现整体早于"按发现结果干活"，但晚于 License DI 注册。** 见 §4.3。License DI（①）在插件发现之前；插件发现在 Service/Controller 注册之前。**这取代了 v1 要求派生项目手写顺序的做法**：Program.cs 回到 `builder.AddSbFramework(); app.UseSbFramework();` 两行。

**规则 4 — 白名单是主权资产，插件无权修改。** `LicenseMiddleware._whitelistPaths` 硬编码、不从配置读取（防篡改）。插件**不得**向白名单注入路径。后果：任何插件业务接口在生产 + 授权失效时会被 License 拦 403——**这是对的**，授权失效本就该全站停摆。唯一例外（插件的授权无关健康/回调端点需在授权失效时仍可达）必须说服框架维护者改主权代码加进硬编码白名单，不能自助。两个样板都不需要进白名单。

**规则 5 — `isInstalled` 门控对插件同样适用。** 未安装时框架不注册 `IFreeSql`。插件发现可照常扫描登记（轻量、无副作用），但**插件 Service 的 DI 注册必须裹在 `isInstalled` 分支里**（复用 `[F-2/3]` 的 `if (isInstalled)` 门）。安装向导阶段只用到 `IModuleInitializer`（`GetUninitializedObject` 创建，不进 DI、不碰 `IFreeSql`），故建表/菜单/种子在安装时正常工作。

### 6.2 授权管理插件的特殊性：NoLicense 项目能装 LicenseManage

**【已决策】能，且这是该插件最典型的部署形态。** "验证授权（客户端，拿公钥）"和"签发授权（服务端，拿私钥）"是反向两端。一个授权中心服务的正确姿态恰恰是：**自己不被授权束缚**（NoLicense 主包，`NullLicenseService`，永不 403）+ **同时能给下游签发**（装 `SB.Feature.LicenseManage`）。PM 实测就是 `.NoLicense` 主包。

实现障碍排查（逐项确认无障碍）：

| 潜在障碍 | 实际情况 | 结论 |
|---|---|---|
| 公钥被置空是否影响签发？ | NoLicense 的 `EmbedEmptyLicenseKey` 只置空**验证用的硬编码公钥**。签发用的是**私钥文件**（运行时从 `KeyDirectory`/ContentRoot 读），两套东西 | ✅ 无影响 |
| `NullLicenseService` 挡住签发接口？ | 它只让 `LicenseMiddleware` 永远放行。`EntitlementController`/`VerifyController` 是普通 admin 接口，不依赖 `ILicenseService` 验证结果 | ✅ 无影响 |
| 插件发现在 NoLicense 下工作？ | deps.json 扫 `SB.Feature.` 前缀，与主包 `.NoLicense` 后缀正交（规则1） | ✅ 正常 |
| 私钥从哪来？ | `Features.LicenseManage.KeyDirectory` 配置；空则 ContentRoot | ✅ 配置可控 |

唯一运维约束：签发后台须持有私钥（`license_private.key`），且该私钥须与下游项目内嵌的公钥配对——这是 License 体系本来的约定。

### 6.3 运维后门归类：第二档，实现已迁至 AccessToken FullAccess

> **【一致性修订 2026-08-05】**：旧 `MasterKeyMiddleware` / `AppSettings.MasterKey` / `X-Master-Key` **已从源码移除**。下述"运维后门"分类结论仍成立，但实现方式改为 `SB.Feature.AccessToken` 的 FullAccess key。

旧设计（已移除）：`AppSettings.MasterKey` 是配置字段（安装向导写入），中间件条件挂载（`if (!string.IsNullOrEmpty(MasterKey))`），空字符串 = 通道不挂载 = 后门关闭。它凌驾于授权验证之上（`LicenseMiddleware` 内部也认 `X-Master-Key` 绕过所有限制）。

**现行实现**（`SB.Feature.AccessToken`）：`ApiKey.FullAccess = true`（god key）——忽略 scope、穿透所有端点、旁路 License（`LicenseMiddleware` 对 FullAccess key 直接放行，见 `LicenseMiddleware.cs:80-91`）。能力上等价于旧 MasterKey，且**可吊销、可审计（`SB_ApiKey_Audit`）、可配 IP 白名单**。仅 `super` 角色可签发，且需功能开关 `EnableFullAccess=true`。

在"功能开关三档"里的精确归位：
- 第 1 档**主权**（编译期 `#if` + 双变体，客户不能关）= **授权验证 License**。
- 第 2 档**运维后门**（由框架作者/运维持有，不暴露给最终用户）= **AccessToken FullAccess key**。
- 第 3 档**纯功能性**（独立 NuGet 插件，引则有）= **`SB.Feature.*`**。

FullAccess key 配置走插件（`SB_ApiKey` 表 + `EnableFullAccess` 开关），**不进 `Features` 配置节**（不是插件静态配置）。

### 6.4 主权/插件经 LicensePayload 隐式耦合（须显式承认）

"两套机制并存互不干扰"在**运行机制**上成立，但在**代码物理边界**上：`LicensePayload`/`DomainMatcher`/`DomainExpander` 在 `SB.Framework.Core/License/`，是**签发端（插件）写、验证端（主权常驻）读的共享序列化契约**。`LicenseManage` 用私钥签 `LicensePayload`，主权 `LicenseService` 用公钥验同一份 Payload，RSA 签名的字段必须对齐。

**【已决策】**：`LicensePayload` 的任何字段变更是**跨越主权/插件边界的 breaking change**，必须主权侧与插件侧同步发版。文档显式承认这条耦合线。

---

## 7. 配置系统

### 7.1 `Features` 配置节完整结构

```jsonc
{
  // 派生项目业务程序集名（沿用现有，发现层用它把项目程序集归位到 AllScanAssemblies）
  "ProjectAssemblyName": "Sb.Apps.Pm.WebApi",

  "Features": {
    // 黑名单：按 FeatureName 短名禁用某个已引入的插件。默认全空 = 全部启用
    // 也支持完整程序集名 "SB.Feature.LicenseManage" 作精确兜底（短名歧义时）
    "Disabled": [ "BackupReceive" ],

    // 缺失依赖时是否容忍（默认 false = 报错中止）。降级场景用
    "IgnoreMissingDependencies": false,

    // 每个插件的私有配置子节，key = 插件的 FeatureName（= IModuleInitializer.ModuleName 风格）
    "LicenseManage": {
      "KeyDirectory": "",            // 私钥目录，空 = ContentRoot
      "DefaultValidDays": 365
    },
    "BackupReceive": {
      "StoragePath": "wwwroot/storage/remote-backups",  // 相对 ContentRoot
      "MaxFileSizeMB": 500,
      "RetentionDays": 90
    }
  }
}
```

设计要点（已决策）：
- `Disabled` 用 **FeatureName**（运维友好，与日志/`ModuleName` 一致）。
- 每个插件配置子节直接挂 `Features.{FeatureName}` 下，**不另起顶级节**：① 一眼看出是插件配置；② 卸载后残留集中好清理；③ 不污染主权 `AppSettings` 顶级类。
- 主权/云端配置**不进** `Features` 节：`CloudConfig.ServerUrl`、`LicenseConfig.Key` 保持顶级（它们属于客户端侧 License，与"是否装了某插件"无关）。旧 `MasterKey` 顶级字段**已移除**，运维后门能力由 AccessToken 插件的 FullAccess key 承担。

### 7.2 插件读自身配置：强类型 Options，框架自动绑定

**【已决策】用强类型 Options，但框架按约定自动 `Configure`，插件不写 `services.Configure<T>(...)`。**

机制：插件在 `IFeatureModule.OptionsType` 声明配置类型，框架发现循环里自动绑定（§4.1 [F-3]）：

```csharp
if (m.OptionsType is not null)
    services.Configure(m.OptionsType, configuration.GetSection($"Features:{m.FeatureName}"));
```

> `services.Configure(Type, IConfiguration)` 有**非泛型重载**（`OptionsConfigurationServiceCollectionExtensions`），所以"框架不知道插件 Options 具体类型"也能运行时绑定。这把"读哪个 section"收口到框架，插件无法把路径写错。

插件侧只需：

```csharp
public sealed class BackupReceiveOptions {
    public string StoragePath  { get; set; } = "wwwroot/storage/remote-backups";
    public int    MaxFileSizeMB { get; set; } = 500;
    public int    RetentionDays { get; set; } = 90;
}
public sealed class BackupReceiveFeature : IFeatureModule {
    public string FeatureName => "BackupReceive";
    public string DisplayName => "云端备份接收";
    public Type   OptionsType => typeof(BackupReceiveOptions);   // ★ 声明即自动绑定
}
// Service 里照常注入 IOptions<BackupReceiveOptions>
```

> **配置类放插件包内**（如 `BackupReceiveOptions` 在 `SB.Feature.BackupReceive` 程序集），因为只有该插件读它。实体（`LicenseClient` 等）留 Models（§8.4）。

### 7.3 启动日志

按 `[F-7]` 在 `UseSbFramework` 末尾调 `catalog.LogStartupSummary()`：

```
[Features] loaded: BackupReceive, LicenseManage
[Features] SomeFeature discovered but skipped: disabled by config
[Features] BrokenFeature discovered but skipped: load failed: Could not load file or assembly ...
```

用 `Serilog.Log` 静态 logger（与 `DatabaseSeeder`/`StartupSyncRunner` 一致，启动期无需 DI logger）。

### 7.4 可审计性补强（应对"自动发现难溯源"）

自动发现的代价是"静态可审计性"下降（review csproj diff 时，加一个 PackageReference 就悄悄启用了会建菜单/建表/挂接口的 feature）。deps.json 解决了运行可靠性，但溯源仍需补：

- **补强 A（强烈建议）**：启动日志不只打 feature 名，**打"这个 feature 引入了什么"**——新增菜单数、新增实体表名、新增路由前缀：
  ```
  [Features] LicenseManage: +5 menus, +0 tables, routes [/Entitlement/*, /api/verify/*]
  ```
  数据都已能拿到（`IModuleInitializer.GetMenus().Count` / `GetEntityTypes()` / 该程序集 Controller 路由）。这让"凭空多出的菜单/接口"在日志里有据可查、可溯源到具体 feature。
- **补强 B（建议）**：提供只读诊断端点 `/admin/features`（走 MasterKey 或 admin 鉴权），列出"已装载 feature + 各自菜单/表/路由 + 启用状态 + Disabled 原因"。把"翻日志"升级为"一个接口看全貌"。

> **待实现时确认**：补强 A/B 的具体实现（日志格式、诊断端点 DTO 形状）。两者均为可观测性增强，不阻塞核心装配。

---

## 8. 包发布矩阵

### 8.1 是否新建 SB.Framework.Abstractions？——否（已决策）

`IFeatureModule` 清单接口 + `FeatureContext` **放 `SB.Framework.Core`**。理由：
- Core 已具备 `IServiceCollection`/`IApplicationBuilder`/`IConfiguration`/`IHostEnvironment` 类型（见 `License/LicenseServiceExtensions.cs` 已 `using Microsoft.AspNetCore.Builder` + DI，定义 `AddLicense(IServiceCollection)`/`UseLicense(IApplicationBuilder)`）。放清单接口**零新增依赖**。
- **关键**：`FeatureContext` **不含 `ApplicationPartManager`**（Controller 发现收归框架发现器 [F-4]），所以清单接口**不需要 `Microsoft.AspNetCore.Mvc.Core`**。Core 现在不引 MVC.Core，无需为此加。
- 否决"新建 Abstractions"：两个样板都要 Core，未来纯逻辑插件占比低，为它单拆一个包收益不抵维护成本（YAGNI）。若未来真出现大量"零 Core 依赖"的纯逻辑插件，再抽不迟。

> 这同时化解了一个自相矛盾的设计：若既新建 Abstractions 又在 ctx 塞 `ApplicationPartManager`（MVC 类型），Abstractions 就得引 MVC.Core，"最底层轻包"立刻变重。把 AddApplicationPart 收归框架，两难同时消失。

### 8.2 主包是否还需双变体？——需要，保持现状（已决策）

License **客户端侧全部无条件常驻主包**（`LicenseMiddleware`/`LicenseClientService`/`LicenseHeartbeatTask`/`LicenseVerifyClient` 均无 `#if`）。`DISABLE_LICENSE` 在主包做两件**物理移除**：① `NullLicenseService` 顶掉真实验证；② csproj `EmbedEmptyLicenseKey` 把硬编码公钥置空。这两件嵌在主包代码/构建里，拆插件不动它们。→ 4 个主包继续各出双变体。

### 8.3 插件包是否需双变体？——不需要，永远单变体（已决策）

判定标准：*该包内是否存在"按 `DISABLE_LICENSE` 物理增删的授权验证代码或嵌入密钥"*。

- `SB.Feature.LicenseManage`：是**签发**端（私钥签 Key），不含任何"验证本进程自身授权"的代码，不挂 `LicenseMiddleware`，不嵌自校验公钥。`DISABLE_LICENSE` 对它无意义。→ 单变体。
- `SB.Feature.BackupReceive`：纯业务，与授权验证零交集。→ 单变体。

若错误地给插件出双变体，两个包**逐字节相同**（无 `#if` 可裁剪），纯冗余 + 增发布矩阵维度 + 让派生项目纠结引哪个。**单变体是正确的最小集。**

> 注意区分两个 License 符号：
> - `DISABLE_LICENSE`（控制客户端**验证**物理移除）→ 决定主包双变体。插件完全不碰。
> - `ENABLE_LICENSE_MANAGE` / `ENABLE_BACKUP_RECEIVE`（旧的服务端**签发** / 接收端编译开关）→ **拆插件后整个删除**。包存在与否就是开关。

### 8.4 实体归属判据（已决策）

**不一刀切"实体跟着 feature 走"。判据：实体是否被主权/核心侧反向引用。**

- **被核心引用 → 留 Models**：4 个 License 实体（`LicenseClient`/`LicenseRecord`/`LicenseRequest`/`LicenseVerifyLog`）被核心 `AdminService.GetEntityTypes()` 显式声明、且 `LicenseVerifyMode` 枚举被验证侧/心跳侧共享。搬进插件会让主权侧反依赖插件包（方向错误）。→ **留 `SB.Framework.Models`**。
- **无核心引用 → 进插件**：`BackupRemoteRecord` 现状被 `#if ENABLE_BACKUP_RECEIVE` 包裹，无任何核心侧引用。去掉 `#if` 后不该在 Models 留孤儿实体。→ **移入 `SB.Feature.BackupReceive`**。

### 8.5 BaseCrudService 下沉（已决策）

`BaseCrudService<T> : BaseRepository<T>` 全部依赖 = FreeSql（`BaseRepository`）+ `Microsoft.Extensions.Logging` + Models（`IEntity`/`Result`/`ISoftDelete`），**不碰 Services 包任何其它类型**。而 Core 已引 `FreeSql.Repository`（`BaseRepository` 在此），已依赖 Models。

→ **把 `BaseCrudService<T>` 下沉到 `SB.Framework.Core`，零新增依赖，编译即过。** 受影响 49 个继承点（框架 10 + 派生 39）改 `using`。**过渡方案**：在 Services 包留转发壳，派生项目零改动过渡：

```csharp
// SB.Framework.Services 留这个，下个大版本删
namespace SB.Framework.Services {
    [Obsolete("已下沉到 SB.Framework.Core，请改用 SB.Framework.Core.BaseCrudService<T>")]
    public class BaseCrudService<T> : SB.Framework.Core.BaseCrudService<T> where T : class, IEntity { }
}
```

为什么不让插件直接依赖 Services：Services 体量大（引 Qdrant/Redis/GoogleAuthenticator/ImageSharp），让每个 `SB.Feature.*` 拖这一坨是反模式。下沉一个零额外依赖的基类远优于此。

### 8.6 插件对框架包的依赖写法（已决策）

插件 csproj **引正常版框架包**（`SB.Framework.Core` 不带 `.NoLicense` 后缀）+ **浮动版本下限**（`Version="0.3.0"` 即 `[0.3.0,)`）。因为 NoLicense 包产出的 dll **同名**（`SB.Framework.Core.dll`），派生项目顶层的直接 PackageReference（如 PM 引 `.NoLicense`）的 transitive 解析会让最终 bin 里只有一份 dll，由派生项目顶层选变体统一收敛。PM 实测可行。

Web 类型来源：插件 Controller 需 `[ApiController]`/`IActionResult`/`IFormFile`。插件包**不能**引 `SB.Framework.WebApi`（循环），用 `<FrameworkReference Include="Microsoft.AspNetCore.App" />` 拿 MVC 类型。

### 8.7 完整发布矩阵表

| 包 | 角色 | License 客户端验证代码/嵌入公钥 | 变体数 | 包名 / 程序集名 |
|---|---|---|---|---|
| `SB.Framework.Models` | 主包·实体/DTO/`IModuleInitializer` | 无（随主包链出双变体保持依赖链一致） | **2** | `SB.Framework.Models` / `.NoLicense` → dll `SB.Framework.Models` |
| `SB.Framework.Core` | 主包·原语 + `BaseCrudService`(下沉) + `LicenseMiddleware` + **`IFeatureModule`/`FeatureCatalog`/`FeatureScanner`** | **有**（`LicenseMiddleware`/`LicenseService`，受 `DISABLE_LICENSE`） | **2** | `SB.Framework.Core` / `.NoLicense` → dll `SB.Framework.Core` |
| `SB.Framework.Services` | 主包·基础服务 + License 客户端侧 + `BaseCrudService` 转发壳 | **有**（`LicenseClientService` 等常驻） | **2** | `SB.Framework.Services` / `.NoLicense` |
| `SB.Framework.WebApi` | 主包·装配壳 + 嵌入公钥 + 挂 `LicenseMiddleware` + feature 装配 | **有**（`EmbedLicensePublicKey`/`UseLicense`） | **2** | `SB.Framework.WebApi` / `.NoLicense` |
| `SB.Framework.Analyzers` | 主包·分析器 | 无 | **2**（随主包链） | `SB.Framework.Analyzers` / `.NoLicense` |
| `SB.Framework.DevHost` | 开发工具 | 无 | **1** | `SB.Framework.DevHost` |
| **`SB.Feature.LicenseManage`** | **插件·签发后台** | **无** | **1** ✅ | `SB.Feature.LicenseManage` |
| **`SB.Feature.BackupReceive`** | **插件·备份接收端** | **无** | **1** ✅ | `SB.Feature.BackupReceive` |
| 未来 `SB.Feature.*` | 插件·纯功能 | 无（按定义） | **1**（永远单变体） | `SB.Feature.{Name}` |

> 新增包引用：`SB.Framework.Core.csproj` 加 `<PackageReference Include="Microsoft.Extensions.DependencyModel" />`（发现层读 `DependencyContext`）。

---

## 9. 样板插件一：SB.Feature.BackupReceive

> 形态：姿态 C。覆盖"**需要 `BaseCrudService` 基类**"的插件类型。

### 9.1 csproj

```xml
<!-- SB.Feature.BackupReceive.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>SB.Feature.BackupReceive</RootNamespace>
    <AssemblyName>SB.Feature.BackupReceive</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageId>SB.Feature.BackupReceive</PackageId>
    <Version>0.1.0</Version>
    <Description>SB Feature 云端备份接收端（服务端角色）</Description>
  </PropertyGroup>

  <ItemGroup>
    <!-- 只依赖 Core + Models。Core 提供下沉后的 BaseCrudService + IFeatureModule；
         Models 提供 IEntity/Result/IModuleInitializer。不引 Services、不引 WebApi。
         引正常版主包（dll 同名，由派生项目顶层选变体收敛）。-->
    <PackageReference Include="SB.Framework.Core" Version="0.3.0" />
    <PackageReference Include="SB.Framework.Models" Version="0.3.0" />
    <!-- 控制器要 [ApiController]/IActionResult/IFormFile → MVC 类型。
         用官方 framework-reference，不把整个 WebApi 包拉进来（避免循环）。-->
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
</Project>
```

### 9.2 实体（移入插件，去 `#if`）

```csharp
// 文件：SB.Feature.BackupReceive/BackupRemoteRecord.cs（从 Models 移来，删 #if ENABLE_BACKUP_RECEIVE）
using FreeSql.DataAnnotations;
using SB.Framework.Models;

namespace SB.Feature.BackupReceive;

[Table(Name = "backup_remote_record")]
public class BackupRemoteRecord : IEntity   // 只需 FreeSql.DataAnnotations + IEntity，插件都引得到
{
    [Column(IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }
    // …字段与现状逐字相同…
}
```

### 9.3 配置类

```csharp
// 文件：SB.Feature.BackupReceive/BackupReceiveOptions.cs
namespace SB.Feature.BackupReceive;

public sealed class BackupReceiveOptions
{
    public string StoragePath   { get; set; } = "wwwroot/storage/remote-backups";
    public int    MaxFileSizeMB { get; set; } = 500;
    public int    RetentionDays { get; set; } = 90;
}
```

### 9.4 清单

```csharp
// 文件：SB.Feature.BackupReceive/BackupReceiveFeature.cs
using SB.Framework.Features;

namespace SB.Feature.BackupReceive;

public sealed class BackupReceiveFeature : IFeatureModule
{
    public string FeatureName => "BackupReceive";
    public string DisplayName => "云端备份接收";
    public Type   OptionsType => typeof(BackupReceiveOptions);   // 自动绑定 Features:BackupReceive
    // FeatureAssemblies 默认 = 本程序集，不写
    // 无接口绑定特例、无中间件 → 不实现 ConfigureServices / Configure
    // Service 靠 [AutoDependencyInjection] 自动注册，清单【不】手动 AddScoped（避免双注册）
}
```

### 9.5 声明式数据（独立无状态，推荐拆出 — 与 LicenseManage 范式统一）

```csharp
// 文件：SB.Feature.BackupReceive/BackupReceiveInitializer.cs
using SB.Framework.Models;

namespace SB.Feature.BackupReceive;

public sealed class BackupReceiveInitializer : IModuleInitializer
{
    public string ModuleName => "云端备份接收";
    public int    InitOrder  => 80;
    public Type[] GetEntityTypes() => new[] { typeof(BackupRemoteRecord) };  // 这张表 feature 私有，由它建
    public List<MenuDefinition> GetMenus() => new() { /* 与现状逐字一致 */ };
}
```

### 9.6 Service（去 `#if`，继承下沉后的 Core.BaseCrudService，标 `[AutoDependencyInjection]`）

```csharp
// 文件：SB.Feature.BackupReceive/BackupReceiveService.cs
using FreeSql;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SB.Framework.Core;            // BaseCrudService 下沉到这里
using SB.Framework.Models;          // Result

namespace SB.Feature.BackupReceive;

[AutoDependencyInjection]
public class BackupReceiveService : BaseCrudService<BackupRemoteRecord>   // 不再实现 IModuleInitializer（已拆出）
{
    private readonly IFreeSql _fsql;
    private readonly IHostEnvironment _env;
    private readonly BackupReceiveOptions _opt;
    public BackupReceiveService(IFreeSql fsql, ILogger<BackupReceiveService> logger,
                                IHostEnvironment env, IOptions<BackupReceiveOptions> opt)
        : base(fsql, logger)
    { _fsql = fsql; _env = env; _opt = opt.Value; }

    // SaveUploadAsync / GetProductList / GetRecordList / GetById / DeleteByAdmin
    // —— 方法体与现状逐字相同，仅把硬编码路径/上限改读 _opt。
}
```

> **待实现时确认**：是否如上拆出 `BackupReceiveInitializer`。BackupReceive 的 `IModuleInitializer` 方法本就不碰注入字段，合体（Service 同时实现 `IModuleInitializer`）也安全。但**推荐拆**，与 LicenseManage 范式统一、并彻底规避 `GetUninitializedObject` 风险。

### 9.7 Controller（去 `#if`，移入插件，收敛为 ControllerBase）

```csharp
// 文件：SB.Feature.BackupReceive/BackupReceiveController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using SB.Framework.Models;

namespace SB.Feature.BackupReceive;   // 命名空间换了，但路由是绝对 [Route("/api/...")]，不受影响

[ApiController]
public class BackupReceiveController : ControllerBase   // 现状是 Controller(带Views)，纯API收敛成 ControllerBase
{
    private readonly BackupReceiveService _service;
    private readonly IHostEnvironment _env;
    public BackupReceiveController(BackupReceiveService service, IHostEnvironment env)
    { _service = service; _env = env; }

    [HttpPost, AllowAnonymous, Route("/api/BackupReceive/Upload")]
    [RequestSizeLimit(2L*1024*1024*1024), RequestFormLimits(MultipartBodyLengthLimit = 2L*1024*1024*1024)]
    public Task<Result> Upload(IFormFile file, [FromForm] string productEn, /* …逐字同现状… */ CancellationToken ct)
        => /* 同现状 */ throw new NotImplementedException();
    // ProductList / RecordList / Download / Delete —— 逐字同现状，全是绝对 [Route]，搬包安全
}
```

---

## 10. 样板插件二：SB.Feature.LicenseManage

> 形态：姿态 C 标准范式（清单 / Initializer / Service **三类分离**）。覆盖"**不需要基类、直接用 `IFreeSql`**"的插件类型 + 跨界依赖 `Core.License`。

### 10.1 csproj

```xml
<!-- SB.Feature.LicenseManage.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>SB.Feature.LicenseManage</RootNamespace>
    <AssemblyName>SB.Feature.LicenseManage</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageId>SB.Feature.LicenseManage</PackageId>
    <Version>0.1.0</Version>
    <Description>SB Feature 授权管理（签发端：管理客户/审批/签发/吊销）</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="SB.Framework.Core" Version="0.3.0" />   <!-- DomainExpander / LicensePayload -->
    <PackageReference Include="SB.Framework.Models" Version="0.3.0" /> <!-- 4实体 + DTO + 枚举 + IModuleInitializer -->
    <FrameworkReference Include="Microsoft.AspNetCore.App" />          <!-- EntitlementController/VerifyController -->
  </ItemGroup>
  <!-- LicenseManage 不依赖 BaseCrudService —— LicenseManageService 直接用 IFreeSql.Select<>，不继承基类。
       两个样板恰好覆盖"要基类(BackupReceive) / 不要基类(LicenseManage)"两种，验证 Core+Models 最小依赖面够用。 -->
</Project>
```

### 10.2 清单（独立薄壳）

```csharp
// 文件：SB.Feature.LicenseManage/LicenseManageFeature.cs
using Microsoft.Extensions.DependencyInjection;
using SB.Framework.Features;

namespace SB.Feature.LicenseManage;

public sealed class LicenseManageFeature : IFeatureModule
{
    public string FeatureName => "LicenseManage";
    public string DisplayName => "授权管理";
    public Type   OptionsType => typeof(LicenseManageOptions);   // KeyDirectory / DefaultValidDays

    public void ConfigureServices(IServiceCollection services, FeatureContext ctx)
    {
        // 接口绑定逃生口：自动 DI 只注册具体类型不绑接口，按接口注入的必须手动绑
        // （此处示例，若 LicenseManage 有按接口注入的依赖才需要；无则可删整个方法）
        // services.AddScoped<ILicenseVerifyClient, LicenseVerifyClient>();
        // 注意：不在这里 AddScoped<LicenseManageService> —— 它标了 [AutoDependencyInjection] 自动注册
    }
}
```

### 10.3 配置类

```csharp
// 文件：SB.Feature.LicenseManage/LicenseManageOptions.cs
namespace SB.Feature.LicenseManage;

public sealed class LicenseManageOptions
{
    public string KeyDirectory     { get; set; } = "";   // 空 = ContentRoot
    public int    DefaultValidDays { get; set; } = 365;
}
```

### 10.4 声明式数据（独立无状态，雷已拆除）

```csharp
// 文件：SB.Feature.LicenseManage/LicenseManageInitializer.cs
using SB.Framework.Models;

namespace SB.Feature.LicenseManage;

// 纯声明、无构造依赖 → GetUninitializedObject 对它绝对安全
public sealed class LicenseManageInitializer : IModuleInitializer
{
    public string ModuleName => "授权管理";
    public int    InitOrder  => 70;
    public List<MenuDefinition> GetMenus() => new() { /* 5个子菜单，逐字同现状 */ };
    // GetEntityTypes() 不实现 → 默认空。4张 License 表由核心 AdminService 无条件建（§5.5）
}
```

### 10.5 业务 Service（纯注入式，不再实现 IModuleInitializer）

```csharp
// 文件：SB.Feature.LicenseManage/LicenseManageService.cs
using FreeSql;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SB.Framework.Core.License;   // DomainExpander / LicensePayload（主权/插件共享契约 §6.4）
using SB.Framework.Models;         // 4实体 / Result
using SB.Framework.Models.Dto;     // IssueLicenseRequest / QuickGenerateRequest / HeartbeatRequest

namespace SB.Feature.LicenseManage;

[AutoDependencyInjection]
public class LicenseManageService   // 不再 : IModuleInitializer —— 矛盾消失
{
    private readonly IFreeSql _fsql;
    private readonly ILogger<LicenseManageService> _logger;
    private readonly IHostEnvironment _env;
    private readonly LicenseManageOptions _opt;
    public LicenseManageService(IFreeSql fsql, ILogger<LicenseManageService> logger,
                                IHostEnvironment env, IOptions<LicenseManageOptions> opt)
    { _fsql = fsql; _logger = logger; _env = env; _opt = opt.Value; }

    // GetPublicKeyXml / GenerateKeyPair / FindOrCreateClient / ClientList / QuickGenerate /
    // GenerateLicense(用 DomainExpander.Expand + new LicensePayload{...}) / CheckLicense /
    // RevokeLicense / Verify / ProcessHeartbeat …
    // —— 方法体与现状逐字相同，仅把 KeyDirectory 改读 _opt.KeyDirectory（空→ContentRoot）。
}
```

### 10.6 Controllers（去 `#if`，移入插件）

```csharp
// 文件：SB.Feature.LicenseManage/EntitlementController.cs（+ VerifyController.cs）
// 去掉 #if ENABLE_LICENSE_MANAGE，命名空间换 SB.Feature.LicenseManage，注入 LicenseManageService 不变。
// 路由是 [Route("[controller]/[action]")] / [Route("/api/verify/[action]")]：
//   [controller] token 解析成类名 Entitlement/Verify，与命名空间无关 → 搬包后路由不变，安全。
```

---

## 11. 派生项目接入示例

以 PM 为例。**核心收益：Program.cs 从 ~35 行降到 5 行，feature 启用从"失效的编译宏"改为"引包"。**

### 11.1 现状的真 bug（插件化的存在理由）

PM 现状 csproj 写 `<DefineConstants>$(DefineConstants);ENABLE_LICENSE_MANAGE;ENABLE_BACKUP_RECEIVE</DefineConstants>`，但这两个 feature 的源码在**框架包里**，被 `#if` 包裹。`Directory.Build.targets` 显示 `ENABLE_*` 在 **Debug 默认开、Release 默认关**。PM 引的是 `SB.Framework.WebApi.NoLicense` 0.2.2 **预编译 Release 包** → **这两个 feature 在 PM 生产包里大概率是空的**，PM csproj 那行常量是**死代码、误导性配置**。

> `#if`-in-package 对预编译 NuGet dll 是 no-op：PM 设 `DefineConstants` 只影响 PM 自己编译的代码，影响不了已编进 `SB.Framework.*.dll` 的 `#if` 块。**这正是必须做插件化的硬证据。**

### 11.2 新 Program.cs

```csharp
using SB.Framework.WebApi.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddSbFramework();   // 内部 deps.json 扫 SB.Feature.* + Sb.Apps.Pm.* → 自动装配每个 IFeatureModule

var app = builder.Build();
app.UseSbFramework();       // 内部按需 feature.Configure() 插中间件
await app.RunAsync();
```

> PM 自己的业务（PmServices / agent / webterm）**建议重构成它自己的一个 feature 程序集**（`Sb.Apps.Pm.Services` 实现 `IFeatureModule`，`FeatureName="PmCore"`），从而连 `AddPmServices()` 和 `installed.lock` 判断都被发现机制吸收；webterm 的 early-middleware 短路通过 `IFeatureModule.Configure()` 在合适顺序插入。**待实现时确认**：PM 业务是否一次性 feature 化，还是先保留 `ProjectAssemblyName` 走旧路径、后续再 feature 化。两者都能跑（`ProjectAssemblyName` 路径在 `AllScanAssemblies` 里保留）。

### 11.3 新 PM csproj

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- 删掉 ENABLE_LICENSE_MANAGE / ENABLE_BACKUP_RECEIVE 死常量。feature 改由引包决定。-->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SB.Framework.WebApi.NoLicense" Version="0.3.0" />
    <!-- 引哪个 feature 包 = 启用哪个 feature。这是"声明式装配"的物理体现。-->
    <PackageReference Include="SB.Feature.LicenseManage" Version="0.1.0" />
    <PackageReference Include="SB.Feature.BackupReceive" Version="0.1.0" />
    <ProjectReference Include="..\Sb.Apps.Pm.Services\Sb.Apps.Pm.Services.csproj" />
  </ItemGroup>
</Project>
```

### 11.4 简化对比

| 维度 | 现状 | 新架构 |
|---|---|---|
| Program.cs 行数 | ~35（手动 installed.lock + AddPmServices + Swagger 分组） | 5 |
| 启用 feature 方式 | csproj `DefineConstants` 编译宏（**对 NuGet 包无效**） | 引 `SB.Feature.*` 包 |
| 关闭 feature | 改宏重新构建整个框架 | `appsettings: Features:Disabled:[...]`，不重编 |
| 控制器发现 | 全靠 controllers 在入口 WebApi 程序集里 | 发现器 `AddApplicationPart` 每个 feature 程序集 |
| 跨项目模块数据 | 单个 `ProjectAssemblyName` 配置 | deps.json 扫全部 `SB.Feature.*` |

### 11.5 关闭某个 feature（运维操作示例）

PM 部署后想临时停用备份接收端，无需重新构建，改 `appsettings.json`：

```jsonc
{ "Features": { "Disabled": [ "BackupReceive" ] } }
```

重启即生效。启动日志：`[Features] loaded: LicenseManage` + `[Features] BackupReceive discovered but skipped: disabled by config`。DI/Controller/菜单**同时**消失（程序集清单层过滤，§5.4）。

---

## 12. 未来新增一个插件的标准步骤

以新增"授权统计报表"插件 `SB.Feature.LicenseReport`（依赖 `LicenseManage`）为例。

**步骤 1 — 建包。** 新建 `SB.Feature.LicenseReport.csproj`：
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>SB.Feature.LicenseReport</AssemblyName>
    <PackageId>SB.Feature.LicenseReport</PackageId>
    <Version>0.1.0</Version>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SB.Framework.Core" Version="0.3.0" />
    <PackageReference Include="SB.Framework.Models" Version="0.3.0" />
    <PackageReference Include="SB.Feature.LicenseManage" Version="0.1.0" />  <!-- 依赖另一插件 -->
    <FrameworkReference Include="Microsoft.AspNetCore.App" />                 <!-- 有 Controller 才需要 -->
  </ItemGroup>
</Project>
```

**步骤 2 — 写清单。** `LicenseReportFeature : IFeatureModule`：
```csharp
[DependsOnFeature(typeof(LicenseManageFeature))]   // 声明依赖 → 拓扑排序保证它后装配
public sealed class LicenseReportFeature : IFeatureModule
{
    public string FeatureName => "LicenseReport";
    public string DisplayName => "授权统计报表";
    public Type   OptionsType => typeof(LicenseReportOptions);   // 有私有配置才写
}
```

**步骤 3 — 写功能（按需）。**
- 有新表/菜单 → 写 `LicenseReportInitializer : IModuleInitializer`（无状态，纯声明 `GetEntityTypes()`/`GetMenus()`）。
- 有业务逻辑 → 写 `[AutoDependencyInjection]` Service（构造注入），**不**在清单 `AddScoped`。
- 有 API → 写 `ControllerBase` Controller（绝对 `[Route]`，命名空间随意）。
- 有按接口注入的依赖 / Options / HttpClient → 在清单 `ConfigureServices` 里手动绑。
- 有自己的中间件 → 在清单 `Configure` 里 `app.UseXxx`（自动落在 License 闸门内侧）。

**步骤 4 — 写配置类（按需）。** `LicenseReportOptions`（POCO，放插件包内），框架据 `OptionsType` 自动绑定 `Features:LicenseReport`。

**步骤 5 — 发包。** `dotnet pack` → 发到 baget（`nuget.saibo.club`）。**单变体**（`SB.Feature.*` 永远单变体）。

**步骤 6 — 派生项目接入。** 派生项目 csproj 加一行 `<PackageReference Include="SB.Feature.LicenseReport" Version="0.1.0" />`，**Program.cs 零改动**，重新发布即自动装配。

**步骤 7 — 验证。** 启动看日志 `[Features] loaded: ..., LicenseReport`；若依赖缺失会报 `Feature 循环依赖` 或缺失依赖错误。

> **不做的事**：不写 `AddLicenseReportFeature()` 扩展方法（违背引包即生效）；不出双变体；不在派生项目 Program.cs 登记；不改框架代码（除非要进 License 白名单 §6.1 规则4）。

---

## 13. 已决策 / 待实现时确认 汇总

### 13.1 已决策（用户拍板 3 条）

1. **自动发现 + 默认启用 + 配置可关**：deps.json 扫 `SB.Feature.*`，自动装配，`Features:Disabled` 黑名单可关，启动日志打印清单。
2. **主权功能（授权验证）独立于插件体系**：继续 `#if` 双变体，两套机制并存，不做成插件。
3. **派生项目无包袱**：纯从架构最优出发，Program.cs 回到两行，不为兼容旧写法妥协。

### 13.2 已决策（评审收敛）

| # | 决策点 | 结论 |
|---|---|---|
| C1 | 自动扫描 vs v1 显式注册 | 自动扫描（deps.json 消除 v1 否决理由）；本文取代 v1 |
| C2 | 清单接口名/形状 | `IFeatureModule` + `IReadOnlyList<Assembly>`(默认本程序集) + `FeatureContext`(不含 PartManager) + `OptionsType` |
| C3/8.4 | 实体归属 | 判据式：被核心引用留 Models（License 4 实体），无引用进插件（BackupRemoteRecord） |
| C4/8.1 | 清单接口放哪 | 放 `SB.Framework.Core`，**不**新建 Abstractions（YAGNI） |
| C5 | DI 注册路径 | `[AutoDependencyInjection]` 自动扫为主路径；`ConfigureServices` 只做接口绑定等特例；**禁止**双注册 |
| C6/5.3 | 清单 vs IModuleInitializer | 正交并存 + 拆类（清单/Initializer/Service 三分），**不**合并到一个类型 |
| D1/[F-4] | AddApplicationPart 时序 | 框架硬编码，在 `AddControllersWithViews()` 后链式调用，**不**交插件 |
| D2/5.4 | 黑名单作用范围 | 程序集清单层过滤（一处过滤三处生效），**不**在 FeatureName 装配层 |
| D5/5.5 | License 表归属 | 4 张表由核心 `AdminService` 无条件建（现状不动），插件 `GetEntityTypes()` 留空 |
| D6/3.1 | 包名≠程序集名 | 用 `GetDefaultAssemblyNames`，**禁止**用 `lib.Name` |
| D7/8.6 | 插件对框架包依赖 | 引正常版（dll 同名）+ 浮动版本，派生项目顶层选变体收敛 |
| 8.5 | BaseCrudService | 下沉到 Core，Services 留 `[Obsolete]` 转发壳过渡 |
| 8.3 | 插件变体数 | 永远单变体 |
| 6.3 | MasterKey 归类 | 第二档运维后门，配置开关条件挂载，新架构零改动 |
| 6.4 | 主权/插件耦合 | 经 Core.License 的 `LicensePayload` 共享契约耦合，字段变更是跨边界 breaking |

### 13.3 待实现时确认

| 点 | 说明 |
|---|---|
| `FeatureModuleActivator` 形态 | 实例化清单（`Activator.CreateInstance`）+ 拓扑排序 + 缓存已排序实例供 Use 阶段复用的具体类。在 [F-1] 后、[F-3] 前完成 |
| BackupReceive 是否拆 Initializer | 推荐拆（§9.5/9.6，与 LicenseManage 范式统一）；其 `IModuleInitializer` 方法不碰注入字段，合体也安全。需拍板 |
| PM 业务是否一次性 feature 化 | PmServices/agent/webterm 重构成 `Sb.Apps.Pm.Services` feature，还是先保留 `ProjectAssemblyName` 旧路径。两者都能跑 |
| 可审计性补强 A/B | 启动日志副作用清单格式 + `/admin/features` 诊断端点 DTO。可观测性增强，不阻塞核心 |
| `IFeatureSingleFilePublish` 落地方式 | 单文件发布时派生项目显式 `new XxxSingleFilePublish()` 传入，还是反射扫描。推荐显式（最稳） |
| `LicensePayload` 同步发版机制 | 主权侧与插件侧 Payload 字段对齐的版本协调流程（CI 检查/约定） |

---

## 14. 分阶段实施清单

三阶段（外加阶段 0），每阶段可独立验证、独立发版、独立回滚。

### 阶段 0：地基准备（不破坏现状，可单独合入）

| 任务 | 文件 | 验证 |
|---|---|---|
| 下沉 `BaseCrudService<T>` 到 Core，Services 留 `[Obsolete]` 转发壳 | `SB.Framework.Core/BaseCrudService.cs`（新）；`SB.Framework.Services/BaseCrudService.cs`（改转发壳） | 框架 + PM 全量编译通过，49 个继承点不报错 |
| `SB.Framework.Core.csproj` 加 `Microsoft.Extensions.DependencyModel` | `SB.Framework.Core.csproj` | restore 通过 |

**验证标准**：现状功能零行为变化，框架与 PM 照常构建运行。这一步不引入任何 feature 概念。

### 阶段 1：framework 地基改造（发现层 + 接口层 + 装配点）

| 任务 | 文件 |
|---|---|
| 新建 `IFeatureModule` / `FeatureContext` / `DependsOnFeatureAttribute` / `IFeatureSingleFilePublish` | `SB.Framework.Core/Features/*.cs` |
| 新建 `FeatureScanner` / `FeatureScannerOptions` / `FeatureScannerDiagnostics` | `SB.Framework.Core/Features/FeatureScanner.cs` |
| 新建 `FeatureDescriptor` / `FeatureCatalog` / `FeatureRuntime` / `LogStartupSummary` | `SB.Framework.Core/Features/FeatureCatalog.cs` |
| 新建 `FeatureModuleActivator`（实例化+拓扑排序+缓存） | `SB.Framework.Core/Features/FeatureModuleActivator.cs` |
| `AddBusinessServices(string?)` → `(FeatureCatalog)`，遍历清单 | `SB.Framework.WebApi/Extensions/ServiceSetup.cs` |
| `DiscoverModuleInitializers` 加 catalog 重载，`ScanModuleInitializers` 走 `Assembly` 入参 | `SB.Framework.Services/SetupService.cs`（:864-896） |
| `SeedDatabase` 4 个 `Sync*` 改读 `FeatureRuntime.Catalog`；`SetupService.Install` 同改 | `SB.Framework.WebApi/Extensions/DatabaseSeeder.cs`（:49/73/206/276）；`SetupService.cs`（:238） |
| 插 `[F-1..7]` 装配点 + `[F-4]` AddApplicationPart | `SB.Framework.WebApi/Extensions/SbFrameworkExtensions.cs`（:35/38/44/73/112-133） |
| 删除框架内 `#if ENABLE_LICENSE_MANAGE` / `#if ENABLE_BACKUP_RECEIVE` 相关 target | `Directory.Build.targets` |

**验证标准**（关键——此时还没有任何 feature 包，框架应"零 feature"正常跑）：
1. 框架自测项目启动，日志打 `[Features] loaded: (none)`。
2. 既有 `IModuleInitializer`（核心模块菜单/表）照常同步，核心功能（登录/CRUD）不变。
3. 故意建一个 dummy `SB.Feature.Dummy` 包引入，启动日志出现 `loaded: Dummy`，加进 `Features:Disabled` 后变 `skipped`，且其菜单不入库（验证 §5.4 一处过滤三处生效）。
4. dummy 包放一个 `[Route("/api/dummy/ping")]` Controller，验证 `AddApplicationPart` 生效（能路由），不引包时不存在。

发版：`SB.Framework.* 0.3.0`（双变体）。

### 阶段 2：两个样板插件包

| 任务 | 产物 |
|---|---|
| 建 `SB.Feature.BackupReceive`：移 `BackupRemoteRecord`(去`#if`) + Options + 清单 + Initializer + Service(`[AutoDI]`,继承Core基类) + Controller(`ControllerBase`) | `SB.Feature.BackupReceive 0.1.0`（单变体） |
| 建 `SB.Feature.LicenseManage`：清单 + Initializer(无状态) + Service(纯`[AutoDI]`) + Options + Entitlement/Verify Controller | `SB.Feature.LicenseManage 0.1.0`（单变体） |
| 从框架主包**删除**已抽出的源（`LicenseManageService`/`BackupReceiveService`/3 个 Controller/`BackupRemoteRecord`） | 框架主包瘦身 |
| 确认 4 个 License 实体**仍留** `SB.Framework.Models`，核心 `AdminService` 仍建表 | Models 不动 |

**验证标准**：
1. 一个**纯净测试宿主**（引 `SB.Framework.WebApi 0.3.0` + 两个 feature 包）启动，日志 `[Features] loaded: BackupReceive, LicenseManage`。
2. `/api/BackupReceive/Upload`、`/Entitlement/*`、`/api/verify/*` 三组路由可达（验证跨程序集 Controller 发现）。
3. 备份接收建表（`backup_remote_record`）、授权管理菜单入库；4 张 License 表由核心建（不依赖插件）。
4. `Features:Disabled:["LicenseManage"]` 后，授权管理菜单不入库、`/Entitlement/*` 404、签发 Service 不注册（验证黑名单全链生效）。
5. **NoLicense 宿主装 LicenseManage**（§6.2 场景）：用 `.NoLicense` 主包 + `SB.Feature.LicenseManage`，验证签发功能正常、自身永不 403。

### 阶段 3：派生项目适配（PM 先行）

| 任务 | 文件 |
|---|---|
| PM Program.cs 砍到两行 | `Sb.Apps.Pm.WebApi/Program.cs` |
| PM csproj 升 `0.3.0` 主包、加两个 feature 包引用、**删** `ENABLE_*` 死常量 | `Sb.Apps.Pm.WebApi.csproj` |
| （可选）PM 业务 feature 化：`Sb.Apps.Pm.Services` 实现 `IFeatureModule` | `Sb.Apps.Pm.Services/PmCoreFeature.cs` |
| Nexus / vshop 同样升级（PM 验证通过后） | 各派生项目 |

**验证标准**：
1. PM 全量构建（含 admin/h5 前端）通过，产物启动日志 `[Features] loaded: BackupReceive, LicenseManage`。
2. PM 既有功能（登录 admin/111111、CRUD、agent、webterm）行为不变。
3. PM 部署后改 `appsettings` 黑名单关掉 BackupReceive，重启验证运维侧热关闭（不重新构建）。
4. 对比迁移前后：菜单、API、数据库 schema 一致（无丢失、无重复建表）。

发版：`Sb.Apps.Pm 0.x`，按 saibo 容器部署流程发布到测试环境验证后推生产。

---

> **文档维护约定**：本文是地基文档。新增 `SB.Feature.*` 包时**无需改本文**（§12 步骤已覆盖）；只有"接口契约 / 发现机制 / 主权边界 / 配置结构"变更才更新本文，并同步 `docs/AI_Conventions.md` 的功能开关三档分级。`LicensePayload` 字段变更须按 §6.4 跨主权/插件边界协调。
