using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Core.Expand;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Services;
using System.IO;

namespace SB.Framework.WebApi.Controllers;

/// <summary>
/// appsettings.json 管理端点。
/// 所有 endpoint 要求 JwtAdmin（密码登录,或 FullAccess 的 sk_ 访问令牌;旧 X-Master-Key 通道已废），改动会写文件 + 历史 .bak。
/// 保存 / 上传 / 回滚后默认触发 AppShutdownHelper.GracefulStop，让 systemd 拉起新进程加载最新配置。
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = "JwtAdmin")]
[Route("[controller]/[action]")]
public class AppSettingsController : Controller
{
    private readonly AppSettingsService _service;
    private readonly IHostApplicationLifetime _appLifetime;

    public AppSettingsController(AppSettingsService service, IHostApplicationLifetime appLifetime)
    {
        _service = service;
        _appLifetime = appLifetime;
    }

    /// <summary>取当前 appsettings.json 原文 + 解析对象 + 元数据</summary>
    [HttpGet]
    public Result Get() => _service.Get();

    /// <summary>下载 appsettings.json（PhysicalFile，浏览器另存为）</summary>
    [HttpGet]
    public IActionResult Download()
    {
        var path = _service.AppSettingsPath;
        if (!System.IO.File.Exists(path))
            return NotFound(new { code = -1, message = "配置文件不存在" });

        return PhysicalFile(path, "application/json", "appsettings.json");
    }

    /// <summary>
    /// 保存新内容（接收 raw JSON 字符串）。
    /// restart=true 时调 AppShutdownHelper.GracefulStop 重启生效。
    /// </summary>
    [HttpPost]
    public Result Save([FromForm] SaveAppSettingsRequest req)
    {
        var op = HttpContext.User.Username();
        var result = _service.Save(req.RawText, op);
        if (result.Code == 0 && req.Restart)
        {
            AppShutdownHelper.GracefulStop(_appLifetime);
            return Result.Success(new
            {
                ((dynamic)result.Data!).path,
                ((dynamic)result.Data!).size,
                ((dynamic)result.Data!).savedAt,
                restartScheduled = true,
                message = "已保存，应用将在 1.5 秒后优雅重启",
            });
        }
        return result;
    }

    /// <summary>
    /// 上传 appsettings.json（multipart 整文件替换）。
    /// 内容会先 JSON 校验通过才接受。restart 参数同 Save。
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<Result> Upload(IFormFile file, [FromForm] bool restart = true)
    {
        if (file == null || file.Length == 0)
            return Result.Fail("请选择文件");

        if (!file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return Result.Fail("仅支持 .json 文件");

        string text;
        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            text = await reader.ReadToEndAsync();
        }

        var op = HttpContext.User.Username();
        var result = _service.Save(text, $"{op}（上传 {file.FileName}）");
        if (result.Code == 0 && restart)
        {
            AppShutdownHelper.GracefulStop(_appLifetime);
            return Result.Success(new { restartScheduled = true, message = "已上传保存，应用将在 1.5 秒后优雅重启" });
        }
        return result;
    }

    /// <summary>Diff 预览：返回当前原文 + 提交的新内容，前端做差异展示</summary>
    [HttpPost]
    public Result Diff([FromForm] SaveAppSettingsRequest req) => _service.Diff(req.RawText);

    /// <summary>历史 .bak 列表（时间倒序，最多 30 条）</summary>
    [HttpGet]
    public Result History() => _service.History();

    /// <summary>查看指定 .bak 内容</summary>
    [HttpGet]
    public Result HistoryContent(string fileName) => _service.HistoryContent(fileName);

    /// <summary>
    /// 回滚到指定 .bak。
    /// 当前 appsettings.json 会先存为新 .bak，然后覆盖回 .bak 内容。restart 参数同 Save。
    /// </summary>
    [HttpPost]
    public Result Rollback([FromForm] RollbackRequest req)
    {
        var op = HttpContext.User.Username();
        var result = _service.Rollback(req.FileName, op);
        if (result.Code == 0 && req.Restart)
        {
            AppShutdownHelper.GracefulStop(_appLifetime);
            return Result.Success(new { restartScheduled = true, message = $"已回滚到 {req.FileName}，应用将在 1.5 秒后优雅重启" });
        }
        return result;
    }
}

public class SaveAppSettingsRequest
{
    public string RawText { get; set; } = "";
    public bool Restart { get; set; } = true;
}

public class RollbackRequest
{
    public string FileName { get; set; } = "";
    public bool Restart { get; set; } = true;
}
