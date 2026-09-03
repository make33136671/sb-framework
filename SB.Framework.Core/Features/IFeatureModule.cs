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
/// DI/Builder，放这里零新增依赖。
/// </summary>
public interface IFeatureModule
{
    // ① 自我标识 —— 黑名单匹配、日志、去重的 key
    /// <summary>插件唯一名，约定 = 包名去 "SB.Feature." 前缀，如 "AccessToken" / "BackupReceive"。
    /// 用于 appsettings "Features:Disabled" 黑名单匹配、配置子节 key、启动日志。
    /// 必须全局唯一；重名时后发现的覆盖先发现的。</summary>
    string FeatureName { get; }

    /// <summary>展示名（菜单/日志可读），如 "云端备份接收"。</summary>
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
    /// 的插件可返回 false。最终是否加载 = EnabledByDefault &amp;&amp; !Features:Disabled.Contains(FeatureName)。</summary>
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
    /// 固定锚点：UseAuthorization 之后、MapControllers 之前。插件中间件不早于授权管线。</summary>
    void Configure(IApplicationBuilder app, FeatureContext ctx) { }
}
