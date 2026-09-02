using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers;

/// <summary>
/// 安装配置导出（/admin/SetupConfig/*，JwtAdmin 认证）。
/// 安装向导仅在安装时支持导入/导出配置；本控制器提供安装完成后的持久导出入口：
/// 导出值取自运行时生效配置（含安装后的修改），格式与安装向导「导入配置」完全兼容，
/// 重新安装时可直接导入预填。
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = "JwtAdmin")]
[Route("/admin/[controller]/[action]")]
public class SetupConfigController : Controller
{
    private readonly SetupService _setupService;

    public SetupConfigController(SetupService setupService)
    {
        _setupService = setupService;
    }

    /// <summary>导出系统安装配置（JSON，与安装向导导入格式 setup-config.json 兼容）。</summary>
    [HttpGet]
    public Result Export()
    {
        return _setupService.ExportInstalledConfig();
    }
}
