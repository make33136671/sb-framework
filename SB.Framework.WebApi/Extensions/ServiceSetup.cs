using SB.Framework.Core.Attribute;
using SB.Framework.Features;
using SB.Framework.Models;
using SB.Framework.Services;
using System.Reflection;

namespace SB.Framework.WebApi.Extensions
{
    public static class ServiceSetup
    {
        /// <summary>
        /// 收集被禁模块声明拥有的全部 Service/Provider 类型与 Controller 全名，
        /// 用于跳过服务注册与从路由移除 Controller。
        /// </summary>
        /// <param name="catalog">当前 feature catalog（含所有可扫程序集）</param>
        /// <param name="disabledModuleKeys">Modules:Disabled 黑名单（大小写不敏感）</param>
        /// <returns>(serviceTypes: 被禁 Service/Provider 类型集, controllerNames: 被禁 Controller 全名集)</returns>
        public static (IReadOnlySet<Type> serviceTypes, IReadOnlySet<string> controllerNames)
            CollectDisabledOwnedTypes(FeatureCatalog catalog, string[] disabledModuleKeys)
        {
            if (disabledModuleKeys == null || disabledModuleKeys.Length == 0)
                return (new HashSet<Type>(), new HashSet<string>());

            // 取全部模块(不带黑名单过滤),挑出命中黑名单的,收其 owned types + controller names
            var all = SetupService.DiscoverModuleInitializers(catalog);
            var ban = new HashSet<string>(disabledModuleKeys, StringComparer.OrdinalIgnoreCase);
            var serviceTypes = new HashSet<Type>();
            var controllerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in all)
            {
                if (ban.Contains(m.ModuleKey) || ban.Contains(m.GetType().Name))
                {
                    foreach (var t in m.GetOwnedTypes()) serviceTypes.Add(t);
                    foreach (var n in m.GetOwnedControllerNames()) controllerNames.Add(n);
                }
            }
            return (serviceTypes, controllerNames);
        }

        /// <summary>
        /// 注册业务服务（feature 化主路径）。
        /// 遍历 catalog.AllScanAssemblies（框架Services → 各feature(短名排序) → 项目程序集），
        /// 对每个程序集扫 [AutoDependencyInjection] 注册。顺序保证"后注册覆盖先注册"：
        /// 项目排最后 ⇒ 项目子类仍覆盖 feature/框架基类。
        /// </summary>
        /// <param name="bannedTypes">被禁模块声明的 Service/Provider 类型，跳过注册；null/空时行为与旧版完全相同。</param>
        public static IServiceCollection AddBusinessServices(
            this IServiceCollection services,
            FeatureCatalog catalog,
            IReadOnlySet<Type>? bannedTypes = null)
        {
            foreach (var asm in catalog.AllScanAssemblies)
                RegisterServicesFromAssembly(services, asm, bannedTypes);
            return services;
        }

        /// <summary>
        /// 注册业务服务（旧字符串重载，兼容缝）。
        /// 新代码一律走 AddBusinessServices(FeatureCatalog)。
        /// </summary>
        /// <param name="services">服务容器</param>
        /// <param name="projectAssemblyName">项目 Service 程序集名称（如 "Sb.Apps.Pm.Services"），不传则只注册框架 Service</param>
        public static IServiceCollection AddBusinessServices(
            this IServiceCollection services,
            string? projectAssemblyName = null)
        {
            // 1. 注册框架自带的 Service
            RegisterServicesFromAssembly(services, Assembly.Load("SB.Framework.Services"));

            // 2. 注册项目的 Service（后注册覆盖先注册）
            if (!string.IsNullOrEmpty(projectAssemblyName))
            {
                RegisterServicesFromAssembly(services, Assembly.Load(projectAssemblyName));
            }

            return services;
        }

        /// <summary>
        /// 扫描指定程序集，注册所有标记了 [AutoDependencyInjection] 的 Service。
        /// bannedTypes 非空时跳过其中的类型（被禁模块 owned types）。
        /// </summary>
        private static void RegisterServicesFromAssembly(
            IServiceCollection services, Assembly assembly,
            IReadOnlySet<Type>? bannedTypes = null)
        {
            foreach (var type in assembly.ExportedTypes)
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                if (bannedTypes != null && bannedTypes.Contains(type)) continue;

                var attr = type.GetCustomAttribute<AutoDependencyInjectionAttribute>();
                if (attr == null || !attr.AutoDependencyInjection)
                    continue;

                // 注册自身类型
                services.AddScoped(type);

                // 如果继承了框架 Service（标记了 [AutoDependencyInjection] 的基类），
                // 同时注册为基类类型，使框架 Controller 注入基类时拿到项目子类实例
                var baseType = type.BaseType;
                while (baseType != null && baseType != typeof(object))
                {
                    var baseAttr = baseType.GetCustomAttribute<AutoDependencyInjectionAttribute>();
                    if (baseAttr != null && baseAttr.AutoDependencyInjection)
                    {
                        // 用工厂方法确保 DI 解析到的是同一个实例
                        services.AddScoped(baseType, sp => sp.GetRequiredService(type));
                        break;
                    }
                    baseType = baseType.BaseType;
                }
            }
        }

        /// <summary>
        /// 发现所有模块初始化器（旧字符串重载，兼容缝）
        /// </summary>
        public static List<IModuleInitializer> DiscoverModuleInitializers(string? projectAssemblyName = null)
            => SetupService.DiscoverModuleInitializers(projectAssemblyName);

        /// <summary>
        /// 发现所有模块初始化器（feature 化主路径）
        /// </summary>
        public static List<IModuleInitializer> DiscoverModuleInitializers(FeatureCatalog catalog)
            => SetupService.DiscoverModuleInitializers(catalog);
    }
}
