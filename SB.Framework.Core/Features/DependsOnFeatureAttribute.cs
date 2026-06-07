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
