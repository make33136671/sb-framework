namespace SB.Framework.Models
{
    /// <summary>
    /// 模块初始化接口 — 各模块 Service 实现此接口声明自己需要的初始化数据
    /// 安装向导自动扫描所有实现，统一执行：建表 → 系统设置 → 菜单 → 种子数据
    ///
    /// 注意：GetEntityTypes / GetSystemSettings / GetMenus 方法必须是纯声明式的（不访问实例字段），
    /// 因为安装向导通过反射创建实例时不会调用构造函数。
    /// </summary>
    public interface IModuleInitializer
    {
        /// <summary>模块名称</summary>
        string ModuleName { get; }

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
