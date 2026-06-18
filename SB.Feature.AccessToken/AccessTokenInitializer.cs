using SB.Feature.AccessToken.Models;
using SB.Framework.Models;

namespace SB.Feature.AccessToken
{
    /// <summary>
    /// 访问令牌功能的声明式初始化(建表 + 菜单)。纯声明、无构造依赖。
    /// 菜单挂在「系统管理」区,与管理员账号并列 —— 呼应"账号即 token,同一处管理"。
    /// </summary>
    public sealed class AccessTokenInitializer : IModuleInitializer
    {
        public string ModuleName => "访问令牌";
        public int InitOrder => 60;

        public Type[] GetEntityTypes() => new[] { typeof(ApiKey), typeof(ApiKeyAuditLog) };

        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition
            {
                Name = "AccessToken",
                ParentPath = "/system",
                Path = "/system/access-token",
                Component = "/access-token/index",
                Title = "访问令牌",
                Icon = "lucide:key-round",
                OrderNo = 30,
            },
            new MenuDefinition
            {
                Name = "AccessTokenAudit",
                ParentPath = "/system",
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
