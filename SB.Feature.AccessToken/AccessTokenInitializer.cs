using SB.Feature.AccessToken.Models;
using SB.Framework.Models;

namespace SB.Feature.AccessToken
{
    /// <summary>
    /// 访问令牌功能的声明式初始化(建表 + 菜单)。纯声明、无构造依赖。
    /// 菜单挂在「系统管理 → 开发工具」下,与运维工具/后台任务并列。
    /// </summary>
    public sealed class AccessTokenInitializer : IModuleInitializer
    {
        public string ModuleName => "访问令牌";
        public int InitOrder => 60;

        public Type[] GetEntityTypes() => new[] { typeof(ApiKey), typeof(ApiKeyAuditLog) };

        public List<MenuDefinition> GetMenus() => new()
        {
            // Path 保持不变(菜单同步按 Path 匹配存量记录),只改 ParentPath 把它重新挂到
            // 开发工具下 —— 同步会 Update 旧记录的 ParentId,不会新增重复菜单。
            new MenuDefinition
            {
                Name = "AccessToken",
                ParentPath = "/system/devtools",
                Path = "/system/access-token",
                Component = "/access-token/index",
                Title = "访问令牌",
                Icon = "lucide:key-round",
                OrderNo = 30,
            },
            new MenuDefinition
            {
                Name = "AccessTokenAudit",
                ParentPath = "/system/devtools",
                Path = "/system/access-token-audit",
                Component = "/access-token/audit",
                Title = "令牌审计",
                Icon = "lucide:scroll-text",
                OrderNo = 31,
                HideInMenu = true,
            },
        };
    }
}
