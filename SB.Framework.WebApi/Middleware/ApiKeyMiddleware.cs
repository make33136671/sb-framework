using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Authentication;
using System.Text.Json;

namespace SB.Framework.WebApi.Middleware;

/// <summary>
/// ApiKey 全局身份中间件。
/// 请求携带 Authorization: Bearer {sk_...} 且有效时，把 context.User 设为该 key 归属账号的 JwtAdmin 身份，
/// 使 /agent/* 和 /admin/* 接口用同一 Bearer Token 统一访问。
///
/// 禁止路径来源：
///   1. 框架硬编码基线（AccessToken/Manager/Menu — 防提权）
///   2. 派生项目通过 ApiKeyToAdminDeny.Add() 在启动代码中显式声明
///
/// 在 UseAuthorization() 之前注册。
/// </summary>
public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;
    private readonly List<DenyRule> _denyRules;

    /// <summary>框架硬编码基线（防提权）</summary>
    private static readonly DenyRule[] BaselineDenies =
    {
        new("/admin/accesstoken/", "ApiKeyOnly"),
        new("/admin/manager/",     "ApiKeyOnly"),
        new("/admin/menu/",        "All"),
    };

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _denyRules = new List<DenyRule>(BaselineDenies);
        _denyRules.AddRange(ApiKeyDeny.Rules);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var authenticator = context.RequestServices.GetService<IApiKeyAuthenticator>();
        if (authenticator == null)
        {
            await _next(context);
            return;
        }

        var result = authenticator.Resolve(context, "JwtAdmin");
        if (result.Outcome != ApiKeyOutcome.Authenticated || result.Principal == null)
        {
            await _next(context);
            return;
        }

        // 设置身份
        context.User = result.Principal;

        // 检查禁止路径
        var path = (context.Request.Path.Value ?? "").ToLowerInvariant();
        var matched = _denyRules.FirstOrDefault(r =>
            !string.IsNullOrWhiteSpace(r.Path) && path.StartsWith(r.NormalizedPath, StringComparison.Ordinal));

        if (matched != null)
        {
            _logger.LogWarning("ApiKey 被项目规则拒绝: {Path} (规则={Rule})", path, matched.Path);

            // 恢复到未认证状态
            context.User = new System.Security.Claims.ClaimsPrincipal();

            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new { code = -1, message = "ApiKey 无权访问此接口" }));
            return;
        }

        await _next(context);
    }
}

// ============================================================
// 公共 API：派生项目启动时声明禁止路径
// ============================================================

/// <summary>
/// ApiKey 禁止规则注册点。
/// 派生项目在 Program.cs / ConfigureServices 中调用
/// <c>ApiKeyDeny.Add("/admin/Secret/", "ApiKeyOnly")</c>。
/// 框架基线三条（AccessToken/Manager/Menu）已内置。
/// </summary>
public static class ApiKeyDeny
{
    internal static readonly List<DenyRule> Rules = new();

    /// <summary>
    /// 添加禁止规则。
    /// </summary>
    /// <param name="path">路径前缀（小写），如 "/admin/somesensitive/"</param>
    /// <param name="denyFor">"ApiKeyOnly"（默认：仅拒绝 ApiKey）| "All"（拒绝全部含 Admin JWT）</param>
    public static void Add(string path, string denyFor = "ApiKeyOnly")
    {
        if (!string.IsNullOrWhiteSpace(path))
            Rules.Add(new DenyRule(path.Trim(), denyFor));
    }
}

/// <summary>
/// 禁止规则。Path = 路径前缀（小写），DenyFor = ApiKeyOnly | All。
/// </summary>
public class DenyRule
{
    public string Path { get; set; } = "";
    public string DenyFor { get; set; } = "ApiKeyOnly";
    public string NormalizedPath => (Path ?? "").Trim().ToLowerInvariant();

    public DenyRule() { }
    public DenyRule(string path, string denyFor = "ApiKeyOnly")
    {
        Path = path;
        DenyFor = denyFor;
    }
}
