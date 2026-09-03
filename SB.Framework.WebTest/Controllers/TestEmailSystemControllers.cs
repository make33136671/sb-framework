using FreeSql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Features;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebTest.Controllers;

/// <summary>
/// 邮件场景:模板渲染预览(不出网)/ 真实发送(需在系统设置里配好 SMTP)/ 验证码全流程。
/// 框架无 DryRun 模式,渲染预览用模板替换逻辑等价演示,真发送走 IEmailService 同一代码路径。
/// </summary>
[ApiController, Route("/test/email/[action]")]
public class TestEmailController : ControllerBase
{
    private readonly IEmailService _email;
    private readonly IVerificationCodeService _codes;

    public TestEmailController(IEmailService email, IVerificationCodeService codes)
    {
        _email = email;
        _codes = codes;
    }

    /// <summary>渲染预览:验证码模板占位符替换,不实际发送(无 SMTP 也能测)</summary>
    [HttpPost, AllowAnonymous]
    public Result RenderPreview([FromForm] string code = "123456", [FromForm] string appName = "框架功能测试台")
    {
        var parameters = new Dictionary<string, string> { ["code"] = code, ["appName"] = appName };
        // 模板存库(EmailTemplate 表),占位符 {{key}} 由 EmailService 替换
        var fsql = (IFreeSql)HttpContext.RequestServices.GetService(typeof(IFreeSql))!;
        var template = fsql.Select<EmailTemplate>()
            .Where(t => t.Code == "RegisterVerify" && t.IsActive)
            .First();
        if (template == null)
            return Result.Fail("未找到激活的 RegisterVerify 模板(可在 admin 前端 → 邮件模板里录入)");

        var body = parameters.Aggregate(template.Body,
            (acc, kv) => acc.Replace($"{{{{{kv.Key}}}}}", kv.Value));
        return Result.Success("渲染成功(未发送)", new
        {
            template.Subject,
            Raw = template.Body,
            Rendered = body,
        });
    }

    /// <summary>真实发送(依赖 SMTP 配置;结果落 EmailLog 表)</summary>
    [HttpPost, AllowAnonymous]
    public Task<Result> Send([FromForm] string to, [FromForm] string subject = "", [FromForm] string body = "")
        => _email.SendEmailAsync(to,
            string.IsNullOrWhiteSpace(subject) ? "【测试台】发送测试" : subject,
            string.IsNullOrWhiteSpace(body) ? $"<p>SB Framework 测试台发送时间:{SB.Framework.Core.Util.AppTime.Now}</p>" : body);

    /// <summary>发送验证码(6 位/5 分钟/频控)</summary>
    [HttpPost, AllowAnonymous]
    public Task<Result> SendCode([FromForm] string to)
        => _codes.SendVerificationCodeAsync(to, VerificationCodeType.Register);

    /// <summary>校验验证码</summary>
    [HttpPost, AllowAnonymous]
    public Task<Result> VerifyCode([FromForm] string to, [FromForm] string code)
        => _codes.VerifyCodeAsync(to, code, VerificationCodeType.Register);
}

/// <summary>
/// 系统设施场景:插件发现清单 / 健康检查 / 登录日志 / SQL 直查(演示 SqlController 能力面)。
/// </summary>
[ApiController, Route("/test/system/[action]")]
public class TestSystemController : ControllerBase
{
    private readonly IFreeSql _fsql;

    public TestSystemController(IFreeSql fsql) => _fsql = fsql;

    /// <summary>Feature 插件体系:发现/启用/禁用全清单(黑名单关掉某插件后这里同步变化)</summary>
    [HttpGet, AllowAnonymous]
    public Result Features() => Result.Success("ok", new
    {
        AllScanAssemblies = FeatureRuntime.Catalog?.AllScanAssemblies
            .Select(a => a.GetName().Name).ToList(),
        Features = FeatureRuntime.Catalog?.Features.Select(f => new
        {
            f.FeatureName,
            f.Enabled,
            f.DisabledReason,
            Assembly = f.Assembly?.GetName().Name,
        }),
    });

    /// <summary>数据库连通 + 核心表计数(验证 FreeSql 装配与迁移结果)</summary>
    [HttpGet, AllowAnonymous]
    public Result DbCheck()
    {
        var counts = new
        {
            Admin = _fsql.Select<Admin>().Count(),
            Menu = _fsql.Select<Menu>().Count(),
            TestOrder = _fsql.Select<TestOrder>().Count(),
            BackgroundTaskLog = _fsql.Select<BackgroundTaskLog>().Count(),
        };
        return Result.Success("数据库连通正常", counts);
    }

    /// <summary>最近登录日志(验证登录审计链路)</summary>
    [HttpGet, AllowAnonymous]
    public Result LoginLogs(int pageSize = 10)
    {
        var logs = _fsql.Select<LoginLog>().OrderByDescending(l => l.Id).Take(pageSize).ToList();
        return Result.Page(logs, logs.Count);
    }
}
