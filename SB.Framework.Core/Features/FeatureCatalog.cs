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
        var disabledList = Features.Where(f => !f.Enabled).ToArray();

        Log.Information("[Features] loaded: {Features}",
            enabled.Length == 0 ? "(none)" : string.Join(", ", enabled));

        foreach (var f in disabledList)
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
