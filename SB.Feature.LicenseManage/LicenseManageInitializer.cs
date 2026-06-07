using SB.Framework.Models;

namespace SB.Feature.LicenseManage;

/// <summary>
/// 授权管理模块初始化声明（纯声明、无构造依赖 → 反射 GetUninitializedObject 对它绝对安全）。
/// 仅声明菜单。GetEntityTypes 不实现（默认空）：4 张 License 表
/// (LicenseClient / LicenseRecord / LicenseRequest / LicenseVerifyLog) 由核心 AdminService 无条件建。
/// </summary>
public sealed class LicenseManageInitializer : IModuleInitializer
{
    public string ModuleName => "授权管理";
    public int InitOrder => 70;

    public List<MenuDefinition> GetMenus() => new()
    {
        new MenuDefinition
        {
            Name = "LicenseManage", Path = "/license-manage", Component = "LAYOUT",
            Title = "授权管理", Icon = "ion:shield-checkmark-outline", OrderNo = 90,
            Children = new()
            {
                new() { Name = "LicenseQuickGenerate", Path = "/license-manage/quick-generate", Component = "/license-manage/QuickGenerate", Title = "授权签发", Icon = "ion:key-outline", OrderNo = 0 },
                new() { Name = "LicenseClientList", Path = "/license-manage/client", Component = "/license-manage/ClientList", Title = "产品列表", Icon = "ion:globe-outline", OrderNo = 1 },
                new() { Name = "LicenseRequestList", Path = "/license-manage/request", Component = "/license-manage/RequestList", Title = "授权申请", Icon = "ion:document-attach-outline", OrderNo = 2 },
                new() { Name = "LicenseRecordList", Path = "/license-manage/record", Component = "/license-manage/RecordList", Title = "授权记录", Icon = "ion:ribbon-outline", OrderNo = 3 },
                new() { Name = "LicenseVerifyLog", Path = "/license-manage/verify-log", Component = "/license-manage/VerifyLogList", Title = "验证日志", Icon = "ion:pulse-outline", OrderNo = 4 },
            }
        },
    };
}
