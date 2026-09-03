using SB.Framework.Models;

namespace SB.Framework.WebTest;

/// <summary>
/// 测试模块声明式初始化(纯声明、无状态、无构造依赖 —— GetUninitializedObject 安全)。
/// 演示框架 IModuleInitializer 数据装配能力:安装向导/启动种子时自动建表 + 同步菜单。
/// InitOrder=90 排在核心模块(0)与备份接收(80)之后。
/// </summary>
public sealed class WebTestInitializer : IModuleInitializer
{
    public string ModuleName => "功能测试台";
    public int InitOrder => 90;

    /// <summary>声明 TEST_Order 表由本模块建(实体属于本模块,非核心反向引用)</summary>
    public Type[] GetEntityTypes() => new[] { typeof(TestOrder) };

    /// <summary>
    /// 声明"功能测试"菜单组(框架 admin 前端可见,同时验证菜单同步机制 ——
    /// 本模块被 Features:Disabled 黑名单关掉时,这组菜单会自动从库里清掉)。
    /// </summary>
    public List<MenuDefinition> GetMenus() => new()
    {
        new MenuDefinition
        {
            Name = "WebTest", Path = "/webtest", Component = "LAYOUT",
            Title = "功能测试台", Icon = "ion:flask-outline", OrderNo = 200,
            Children = new()
            {
                new() { Name = "TestOrder", Path = "/webtest/order", Component = "/webtest/order/List", Title = "测试订单", Icon = "ion:cart-outline", OrderNo = 1 },
                new() { Name = "TestConsole", Path = "/webtest/console", Component = "/webtest/console/Index", Title = "测试控制台", Icon = "ion:terminal-outline", OrderNo = 2 },
            }
        },
    };
}
