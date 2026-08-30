namespace SB.Framework.Models
{
    /// <summary>
    /// 模块初始化接口 — 各模块 Service 实现此接口声明自己需要的初始化数据
    /// 安装向导自动扫描所有实现，统一执行：建表 → 系统设置 → 菜单 → 种子数据
    ///
    /// 安装向导通过 RuntimeHelpers.GetUninitializedObject 实例化，不调用构造函数。
    /// 实现类的 GetEntityTypes/GetSystemSettings/GetMenus/SeedData 不应依赖注入的实例字段。
    /// </summary>
    public interface IModuleInitializer
    {
        /// <summary>模块名称</summary>
        string ModuleName { get; }

        /// <summary>模块英文标识 — 用于 Modules:Disabled 黑名单匹配。默认取实现类名。</summary>
        string ModuleKey => GetType().Name;

        /// <summary>模块拥有的本程序集可见类型(Service / Provider)— 黑名单关闭时跳过这些服务的 DI 注册。默认空。</summary>
        Type[] GetOwnedTypes() => Array.Empty<Type>();

        /// <summary>模块拥有的 Controller 全名(跨程序集,Services 无法 typeof WebApi 的 Controller)— 黑名单关闭时按全名从路由移除。默认空。</summary>
        string[] GetOwnedControllerNames() => Array.Empty<string>();

        /// <summary>初始化顺序（越小越先执行，默认 0）</summary>
        int InitOrder => 0;

        /// <summary>需要同步的实体类型（建表）</summary>
        Type[] GetEntityTypes() => Array.Empty<Type>();

        /// <summary>需要注册的系统设置</summary>
        List<SystemSettingDefinition> GetSystemSettings() => new();

        /// <summary>需要注册的菜单</summary>
        List<MenuDefinition> GetMenus() => new();

        /// <summary>种子数据（建表后执行，fsql 由安装向导提供）</summary>
        void SeedData(IFreeSql fsql) { }
    }
}
