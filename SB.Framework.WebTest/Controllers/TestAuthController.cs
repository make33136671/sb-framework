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
[ApiController, Route("[controller]/[action]")]
public class TestHomeController : ControllerBase
{
    private readonly FeatureCatalog _catalog;

    public TestHomeController(FeatureCatalog catalog) => _catalog = catalog;

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
        LoadedFeatures = _catalog.Features.Select(f => new { f.FeatureName, f.Enabled }).ToList(),
        Swagger = "/swagger",
    });
}

/// <summary>
/// 认证场景:演示 JWT 登录、身份解析、鉴权对照、API Key 通道识别。
/// 登录走框架 AdminService(含 2FA / 登录限流,与生产同一条代码路径)。
/// </summary>
[ApiController, Route("[controller]/[action]")]
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
        Identity = User.Identity?.Name,
        Claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList(),
        OperatorTag = Operator(),
    });

    /// <summary>API Key 通道验证:用 sk_ key 访问本端点,操作人应显示 key:xxx(验证 AccessToken 插件全链)</summary>
    [HttpGet, AllowAnonymous]
    public Result ApiKeyProbe() => Result.Success("请求已到达(请看 data.operatorTag 判断认证通道)", new
    {
        AuthorizationHeader = _http.HttpContext?.Request.Headers.Authorization.ToString() switch
        {
            { Length: > 12 } h => $"{h[..10]}...",
            { Length: > 0 } h => h,
            _ => "(未携带)",
        },
        OperatorTag = Operator(),
    });

    private string Operator()
    {
        var http = _http.HttpContext;
        if (http != null && http.Items.TryGetValue(
                SB.Feature.AccessToken.Services.ApiKeyContext.HttpItemKey, out var ctx)
            && ctx is SB.Feature.AccessToken.Services.IApiKeyContext key)
            return key.OperatorTag;
        return http?.User.Identity?.Name ?? "anonymous";
    }
}
