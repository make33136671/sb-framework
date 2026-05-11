namespace SB.Framework.Models
{
    /// <summary>
    /// 菜单定义（用于模块初始化自动注册菜单）
    /// </summary>
    public class MenuDefinition
    {
        /// <summary>挂载到哪个已存在菜单的 Path（null = 顶级菜单）</summary>
        public string? ParentPath { get; set; }

        /// <summary>路由名称</summary>
        public string Name { get; set; } = "";

        /// <summary>路由路径（也用作唯一标识，防重复创建）</summary>
        public string Path { get; set; } = "";

        /// <summary>组件路径（顶级菜单用 "LAYOUT"）</summary>
        public string Component { get; set; } = "";

        /// <summary>显示标题</summary>
        public string Title { get; set; } = "";

        /// <summary>图标</summary>
        public string Icon { get; set; } = "";

        /// <summary>排序</summary>
        public int OrderNo { get; set; }

        /// <summary>重定向路由</summary>
        public string? Redirect { get; set; }

        /// <summary>在菜单中隐藏</summary>
        public bool HideInMenu { get; set; }

        /// <summary>子菜单</summary>
        public List<MenuDefinition> Children { get; set; } = new();
    }
}
