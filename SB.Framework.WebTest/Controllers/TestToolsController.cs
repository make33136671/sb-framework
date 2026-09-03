using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebTest.Controllers;

/// <summary>
/// 工具箱场景:框架基础能力直接映射 ——
/// 随机名生成 / AppTime 时区 / 电子表格解析(真实文件上传)。
/// </summary>
[ApiController, Route("[controller]/[action]")]
public class TestToolsController : ControllerBase
{
    private readonly IRandomNameGenerator _names;
    private readonly ISpreadsheetReader _sheets;

    public TestToolsController(IRandomNameGenerator names, ISpreadsheetReader sheets)
    {
        _names = names;
        _sheets = sheets;
    }

    /// <summary>随机中文名 / 生活服务备注名(派生项目常用)</summary>
    [HttpGet, AllowAnonymous]
    public Result RandomNames([FromQuery] int count = 5)
    {
        var c = Math.Clamp(count, 1, 50);
        return Result.Success("ok", new
        {
            ChineseNames = _names.ChineseNames(c),
            ServiceNames = _names.ServiceNames(c),
        });
    }

    /// <summary>AppTime 时区能力:存库用 UTC、业务用时区时间、时间戳互转</summary>
    [HttpGet, AllowAnonymous]
    public Result Time()
    {
        var now = SB.Framework.Core.Util.AppTime.Now;
        var utc = SB.Framework.Core.Util.AppTime.UtcNow;
        return Result.Success("ok", new
        {
            Zone = SB.Framework.Core.Util.AppTime.Zone.Id,
            Local = now,
            Utc = utc,
            Offset = SB.Framework.Core.Util.AppTime.Zone.GetUtcOffset(now).ToString(),
            Timestamp = SB.Framework.Core.Util.AppTime.ToTimestamp(utc),
            FromTimestamp = SB.Framework.Core.Util.AppTime.FromTimestamp(
                SB.Framework.Core.Util.AppTime.ToTimestamp(utc)),
        });
    }

    /// <summary>电子表格解析:上传 xlsx/xls/csv,返回每个 sheet 的行列内容</summary>
    [HttpPost, AllowAnonymous]
    public Result ParseExcel(IFormFile file)
    {
        if (file == null || file.Length == 0) return Result.Fail("请选择文件");
        using var stream = file.OpenReadStream();
        var sheets = _sheets.Read(stream, file.FileName);
        return Result.Success($"解析 {sheets.Count} 个 sheet", sheets.Select(s => new
        {
            s.Name,
            Rows = s.Rows,
        }));
    }
}
