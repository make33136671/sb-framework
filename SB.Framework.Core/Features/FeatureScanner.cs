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
        // —— 包名(PackageId)与程序集名(AssemblyName)可以不同,这里必须用后者
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
