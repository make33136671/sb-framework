using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SB.Framework.Features;

/// <summary>
/// 清单实例化 + 拓扑排序器。职责：
///   1. 在 catalog.FeatureAssemblies（仅 enabled）里扫 IFeatureModule 实现类；
///   2. Activator.CreateInstance 实例化（清单是无状态薄壳，必须有公共无参构造）；
///   3. 应用 EnabledByDefault（实例级开关，补充程序集级黑名单）；
///   4. 按 [DependsOnFeature] 做 Kahn 拓扑排序（被依赖者在前），平局按 FeatureName 字典序稳定；
///   5. 缓存排序后的实例 —— AddSbFramework 的 ConfigureServices 阶段与 UseSbFramework 的
///      Configure 阶段【复用同一批实例】，保证两阶段顺序一致、且清单只实例化一次。
///
/// 实例化语义与 IModuleInitializer 的区别（见 §1.2）：
///   - 清单走 Activator.CreateInstance（调公共无参构造），因为它要被当对象正常用（读属性、调钩子）；
///   - IModuleInitializer 走 RuntimeHelpers.GetUninitializedObject（不调构造），因为它纯声明式。
///     两者是不同类型、不同实例化路径，不冲突。
/// </summary>
public static class FeatureModuleActivator
{
    /// <summary>进程级缓存：AddSbFramework 阶段排好，UseSbFramework 阶段复用。</summary>
    private static IReadOnlyList<IFeatureModule> _sorted = Array.Empty<IFeatureModule>();

    /// <summary>[F-1 后] 发现 + 实例化 + 应用 EnabledByDefault + 拓扑排序，结果缓存。
    /// 注：程序集级黑名单（Features:Disabled）已在 FeatureScanner/FeatureCatalog 过滤，
    /// catalog.FeatureAssemblies 只含 enabled 的。这里只补"实例自己声明 EnabledByDefault=false"的默认关。</summary>
    public static IReadOnlyList<IFeatureModule> ActivateAndSort(FeatureCatalog catalog, IConfiguration configuration)
    {
        // 1. 扫描 + 实例化清单。范围 = enabled feature 程序集 + 派生项目程序集。
        //    项目程序集纳入：让派生项目自己的 IFeatureModule（如 PmCoreFeature）也被发现，
        //    从而统一范式——项目业务也是一个 feature，彻底取代旧 ProjectAssemblyName 的 AddXxxServices 手动注册。
        var scanAssemblies = new List<System.Reflection.Assembly>(catalog.FeatureAssemblies);
        if (catalog.ProjectAssembly is not null && !scanAssemblies.Contains(catalog.ProjectAssembly))
            scanAssemblies.Add(catalog.ProjectAssembly);

        var instances = new Dictionary<Type, IFeatureModule>();
        foreach (var asm in scanAssemblies)
        {
            foreach (var type in SafeGetExportedTypes(asm))
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (!typeof(IFeatureModule).IsAssignableFrom(type)) continue;
                if (instances.ContainsKey(type)) continue;

                if (Activator.CreateInstance(type) is not IFeatureModule module) continue;

                // 2. 实例级开关：EnabledByDefault=false → 默认不装（未来可加 Features:Enabled 白名单反向豁免）
                if (!module.EnabledByDefault) continue;

                instances[type] = module;
            }
        }

        _sorted = TopologicalSort(instances);
        return _sorted;
    }

    /// <summary>[Use 阶段] 取 AddSbFramework 阶段排好的清单实例（复用，不重新实例化）。</summary>
    public static IReadOnlyList<IFeatureModule> GetSorted() => _sorted;

    /// <summary>[F-5] 组装 Use 阶段的 FeatureContext。
    /// 接收抽象（IConfiguration/IHostEnvironment/ILogger）而非 WebApplication，
    /// 使本类留在 Core 不必引完整 ASP.NET 框架。调用方（WebApi 层）把 app.Configuration 等传进来。</summary>
    public static FeatureContext BuildAppContext(
        IConfiguration configuration, IHostEnvironment environment,
        bool isInstalled, IReadOnlyList<IFeatureModule> modules, ILogger logger)
        => new()
        {
            Configuration = configuration,
            Environment = environment,
            IsInstalled = isInstalled,
            LoadedFeatures = modules,
            Logger = logger,
        };

    private static IEnumerable<Type> SafeGetExportedTypes(Assembly asm)
    {
        try { return asm.ExportedTypes; }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    /// <summary>Kahn 拓扑排序。被依赖者排前面，平局按 FeatureName 字典序稳定，检测环抛异常。</summary>
    private static List<IFeatureModule> TopologicalSort(Dictionary<Type, IFeatureModule> nodes)
    {
        // deps[node] = 它依赖的、且在场的 node 集合（边指向"必须先于我"的）
        var deps = new Dictionary<Type, HashSet<Type>>();
        foreach (var (type, _) in nodes)
        {
            var set = new HashSet<Type>();
            foreach (var attr in type.GetCustomAttributes<DependsOnFeatureAttribute>())
            {
                if (nodes.ContainsKey(attr.FeatureType))
                    set.Add(attr.FeatureType);
                // 缺失依赖（被黑名单关掉/没装）：当前默认忽略（保持发现层纯净）。
                // 未来可按 Features:IgnoreMissingDependencies 决定是否抛错。
            }
            deps[type] = set;
        }

        var result = new List<IFeatureModule>();
        var emitted = new HashSet<Type>();

        // 稳定迭代：每轮取依赖已满足的节点，按 FeatureName 字典序输出
        while (emitted.Count < nodes.Count)
        {
            var ready = nodes.Keys
                .Where(t => !emitted.Contains(t) && deps[t].All(emitted.Contains))
                .OrderBy(t => nodes[t].FeatureName, StringComparer.Ordinal)
                .ToList();

            if (ready.Count == 0)
            {
                var cycle = nodes.Keys.Where(t => !emitted.Contains(t))
                                      .Select(t => nodes[t].FeatureName)
                                      .OrderBy(x => x, StringComparer.Ordinal);
                throw new InvalidOperationException(
                    $"Feature 循环依赖，无法拓扑排序：{string.Join(" / ", cycle)}");
            }

            foreach (var t in ready)
            {
                result.Add(nodes[t]);
                emitted.Add(t);
            }
        }

        return result;
    }
}
