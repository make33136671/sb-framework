using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Core;
using SB.Framework.Features;
using SB.Framework.Models;
using SB.Framework.Services;
using SB.Framework.WebTest.Services;

namespace SB.Framework.WebTest.Controllers;

/// <summary>
/// 测试台首页与系统信息。GET / → 重定向测试台 UI(wwwroot/test.html)。
/// </summary>
[ApiController, Route("/test/home/[action]")]
public class TestHomeController : ControllerBase
{
    [HttpGet("/"), AllowAnonymous]
    public IActionResult Index() => RedirectPermanent("/test.html");

    /// <summary>框架与测试台元信息(验证配置绑定/Swagger/时区初始化)</summary>
    [HttpGet, AllowAnonymous]
    public Result Info() => Result.Success("ok", new
    {
        Product = AppGlobal.AppSettings?.ProductCn,
        ProductEn = AppGlobal.AppSettings?.ProductEn,
        TimeZone = SB.Framework.Core.Util.AppTime.Zone.Id,
        ServerNow = SB.Framework.Core.Util.AppTime.Now,
        LoadedFeatures = FeatureRuntime.Catalog?.Features
            .Select(f => new { f.FeatureName, f.Enabled }).ToList(),
        Swagger = "/swagger",
    });
}

/// <summary>
/// 认证场景:演示 JWT 登录、身份解析、鉴权对照、API Key 通道识别。
/// 登录走框架 AdminService(含 2FA / 登录限流,与生产同一条代码路径)。
/// </summary>
[ApiController, Route("/test/auth/[action]")]
public class TestAuthController : ControllerBase
{
    private readonly AdminService _admin;
    private readonly IHttpContextAccessor _http;

    public TestAuthController(AdminService admin, IHttpContextAccessor http)
    {
        _admin = admin;
        _http = http;
    }

    /// <summary>登录(默认账号 admin/111111;连接触发 20 次/分钟会吃 429,本身就是限流测试点)</summary>
    [HttpPost, AllowAnonymous]
    public Task<Result> Login([FromForm] string username, [FromForm] string password, [FromForm] string totpCode = "")
        => _admin.LoginAsync(username, password, totpCode);

    /// <summary>受保护端点:无/错 token → 401;有效 token → 解析 Claims(验证 JwtAdmin 通道)</summary>
    [HttpGet, Authorize(AuthenticationSchemes = "JwtAdmin")]
    public Result WhoAmI() => Result.Success("JWT 鉴权通过", new
    {
        Username = User.FindFirst("Username")?.Value,
        Claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList(),
    });

    /// <summary>
    /// 认证通道探针:AllowAnonymous 端点上 JWT 是被动认证,这里演示主动 AuthenticateAsync ——
    /// 带 JWT → admin;带 sk_ key → key:xxx;都不带 → anonymous。
    /// </summary>
    [HttpGet, AllowAnonymous]
    public async Task<Result> ApiKeyProbe()
    {
        var http = _http.HttpContext!;
        string channel;
        string operatorTag;
        if (http.Items.TryGetValue(SB.Feature.AccessToken.Services.ApiKeyContext.HttpItemKey,
                out var ctx) && ctx is SB.Feature.AccessToken.Services.IApiKeyContext key)
        {
            channel = "AccessToken 插件(sk_ key)";
            operatorTag = key.OperatorTag;
        }
        else
        {
            var auth = await http.AuthenticateAsync("JwtAdmin");
            if (auth.Succeeded)
            {
                channel = "JwtAdmin(JWT)";
                operatorTag = auth.Principal.FindFirst("Username")?.Value ?? "unknown";
            }
            else
            {
                channel = "匿名";
                operatorTag = "anonymous";
            }
        }
        var authHeader = http.Request.Headers.Authorization.ToString();
        return Result.Success("请求已到达", new
        {
            AuthorizationHeader = authHeader.Length > 12 ? $"{authHeader[..10]}..." : (authHeader == "" ? "(未携带)" : authHeader),
            Channel = channel,
            OperatorTag = operatorTag,
        });
    }
}
