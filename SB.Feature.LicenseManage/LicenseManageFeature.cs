using SB.Framework.Features;

namespace SB.Feature.LicenseManage;

/// <summary>
/// SB.Feature.LicenseManage 插件清单（无状态薄壳）。
/// 仅声明边界 + 强类型配置类型。业务逻辑在 LicenseManageService（标 [AutoDependencyInjection] 自动注册）。
///
/// 不实现 ConfigureServices：LicenseManageService 无任何"按接口注入"的依赖
/// （只构造注入 IFreeSql / ILogger / IHostEnvironment / IOptions&lt;LicenseManageOptions&gt;，均由框架自带），
/// 也不在此手动 AddScoped LicenseManageService —— 它标了 [AutoDependencyInjection] 会自动注册，手动会双重注册。
/// 菜单/建表声明拆到独立的 LicenseManageInitializer。
/// </summary>
public sealed class LicenseManageFeature : IFeatureModule
{
    public string FeatureName => "LicenseManage";
    public string DisplayName => "授权管理";
    public Type OptionsType => typeof(LicenseManageOptions);   // KeyDirectory / DefaultValidDays
}
