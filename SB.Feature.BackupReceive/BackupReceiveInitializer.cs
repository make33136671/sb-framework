using SB.Framework.Models;

namespace SB.Feature.BackupReceive
{
    /// <summary>
    /// 云端备份接收端的声明式初始化数据（建表 + 菜单）。
    /// 纯声明、无构造依赖、不访问实例字段 —— 安装向导反射创建实例时不会调用构造函数。
    /// 从原 BackupReceiveService 实现的 IModuleInitializer 拆出。
    /// </summary>
    public sealed class BackupReceiveInitializer : IModuleInitializer
    {
        public string ModuleName => "云端备份接收";
        public int InitOrder => 80;

        public Type[] GetEntityTypes() => new[] { typeof(BackupRemoteRecord) };

        public List<MenuDefinition> GetMenus() => new()
        {
            // 一级菜单（与「授权管理」平级）
            new MenuDefinition
            {
                Name = "BackupReceive",
                Path = "/backup-receive",
                Component = "LAYOUT",
                Title = "云端备份",
                Icon = "ion:cloud-download-outline",
                OrderNo = 95,
                Children = new()
                {
                    new() { Name = "BackupReceiveProductList", Path = "/backup-receive/product", Component = "/backup-receive/ProductList", Title = "产品列表", Icon = "ion:apps-outline", OrderNo = 1 },
                    new() { Name = "BackupReceiveDetail", Path = "/backup-receive/detail", Component = "/backup-receive/RecordList", Title = "备份详情", Icon = "ion:document-attach-outline", OrderNo = 2, HideInMenu = true },
                },
            },
        };
    }
}
