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
        /// 注册业务服务（feature 化主路径）。
        /// 遍历 catalog.AllScanAssemblies（框架Services → 各feature(短名排序) → 项目程序集），
        /// 对每个程序集扫 [AutoDependencyInjection] 注册。顺序保证"后注册覆盖先注册"：
        /// 项目排最后 ⇒ 项目子类仍覆盖 feature/框架基类。
        /// </summary>
        public static IServiceCollection AddBusinessServices(
            this IServiceCollection services,
            FeatureCatalog catalog)
        {
            foreach (var asm in catalog.AllScanAssemblies)
                RegisterServicesFromAssembly(services, asm);
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
        /// 扫描指定程序集，注册所有标记了 [AutoDependencyInjection] 的 Service
        /// </summary>
        private static void RegisterServicesFromAssembly(IServiceCollection services, Assembly assembly)
        {
            foreach (var type in assembly.ExportedTypes)
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

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
