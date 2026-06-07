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
