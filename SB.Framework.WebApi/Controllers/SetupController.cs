using Microsoft.AspNetCore.Authorization;
using SB.Framework.Core.Attribute;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.WebApi.Middleware;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Models.Dto;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers;

/// <summary>
/// 系统安装向导
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("/admin/[controller]/[action]")]
public class SetupController : Controller
{
    private readonly SetupService _setupService;
    private readonly IHostApplicationLifetime _appLifetime;

    public SetupController(SetupService setupService, IHostApplicationLifetime appLifetime)
    {
        _setupService = setupService;
        _appLifetime = appLifetime;
    }

    /// <summary>
    /// 获取安装状态
    /// </summary>
    [HttpGet]
    public Result Status()
    {
        return Result.Success(new { installed = _setupService.IsInstalled() });
    }

    /// <summary>
    /// 推断默认部署路径（admin / h5），供安装向导第 4 步预填
    /// </summary>
    [HttpGet]
    public Result DeployHints()
    {
        if (_setupService.IsInstalled())
            return Result.Fail("系统已安装");
        return _setupService.GetDeployHints();
    }

    /// <summary>
    /// 测试数据库连接
    /// </summary>
    [HttpPost]
    [AllowJsonBody]
        public Result TestDatabase([FromBody] DatabaseConfigDto config)
    {
        if (_setupService.IsInstalled())
            return Result.Fail("系统已安装");

        return _setupService.TestDatabaseConnection(config);
    }

    /// <summary>
    /// 测试 Redis 连接
    /// </summary>
    [HttpPost]
    [AllowJsonBody]
        public Result TestRedis([FromBody] RedisConfigDto config)
    {
        if (_setupService.IsInstalled())
            return Result.Fail("系统已安装");

        return _setupService.TestRedisConnection(config);
    }

    /// <summary>
    /// 生成品牌 Logo（仅未安装时可用）。
    /// 按产品名/单字标/配色渲染品牌图标，返回 base64 预览图（与安装后的 images/logo.png 一致）。
    /// 安装向导据此预览：用户可用生成结果、改传上传图，或留空（安装时自动生成默认品牌图标）。
    /// </summary>
    [HttpPost]
    [AllowJsonBody(Reason = "嵌套对象 + 前端 setupFetch 统一 JSON 提交，与 TestDatabase/TestRedis/Install 一致")]
        public Result GenerateLogo([FromBody] SetupGenerateLogoRequest request)
    {
        if (_setupService.IsInstalled())
            return Result.Fail("系统已安装");

        return _setupService.GenerateLogo(request);
    }

    /// <summary>
    /// 执行系统安装
    /// </summary>
    [HttpPost]
    [AllowJsonBody]
        public Result Install([FromBody] SetupRequest request)
    {
        if (_setupService.IsInstalled())
            return Result.Fail("系统已安装");

        var result = _setupService.Install(request);

        if (result.Code == 0)
        {
            // 安装成功后延迟重启应用，让响应先返回给客户端
            // systemd Restart=always 会自动重新拉起进程，届时注册全部服务
            AppShutdownHelper.GracefulStop(_appLifetime);
        }

        return result;
    }
}
