using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace SB.Framework.Core.Authentication
{
    /// <summary>
    /// API Key 认证结果。
    /// </summary>
    public enum ApiKeyOutcome
    {
        /// <summary>请求没带 sk_ bearer —— 交回正常 JWT 校验。</summary>
        NotPresent = 0,
        /// <summary>带了 key 但无效/过期/吊销/账号停用/超限 —— 应判 401。</summary>
        Invalid = 1,
        /// <summary>key 校验通过。</summary>
        Authenticated = 2,
    }

    /// <summary>
    /// 单次请求的 API Key 解析结果(每请求只真正校验一次,缓存在 HttpContext.Items)。
    /// </summary>
    public sealed class ApiKeyAuthResult
    {
        public ApiKeyOutcome Outcome { get; init; }

        /// <summary>认证通过时,以 key 归属账号构造的身份(AuthenticationType = 传入的 schemeName)。</summary>
        public ClaimsPrincipal? Principal { get; init; }

        /// <summary>该 key 是否为 FullAccess(god)——穿透所有 scope 限制。</summary>
        public bool IsFullAccess { get; init; }

        /// <summary>失败原因(仅服务端日志/审计用,不对外泄露)。</summary>
        public string? FailReason { get; init; }

        public static readonly ApiKeyAuthResult NotPresent = new() { Outcome = ApiKeyOutcome.NotPresent };

        public static ApiKeyAuthResult Invalid(string reason) =>
            new() { Outcome = ApiKeyOutcome.Invalid, FailReason = reason };

        public static ApiKeyAuthResult Authenticated(ClaimsPrincipal principal, bool isFullAccess) =>
            new() { Outcome = ApiKeyOutcome.Authenticated, Principal = principal, IsFullAccess = isFullAccess };
    }

    /// <summary>
    /// API Key 认证器 —— 框架核心只声明这条"缝",真正实现由 SB.Feature.AccessToken 功能包提供并注册到 DI。
    /// 未加载该功能包时 DI 取不到本服务,所有调用点静默退回到 JWT/密码鉴权(即"无 key 能力")。
    ///
    /// 取代原静态 X-Master-Key:实现里做 sk_ 提取 → sha256/HMAC 查库 → 账号状态校验 → 失败节流 + 审计。
    /// </summary>
    public interface IApiKeyAuthenticator
    {
        /// <summary>
        /// 解析请求里的 Authorization: Bearer sk_… 凭据。每请求只真正校验一次(结果缓存在 HttpContext.Items),
        /// 含失败按 IP 节流 / 审计 / 归属账号状态校验。schemeName 决定生成身份的 AuthenticationType。
        /// 本方法不写 context.User —— 由调用方(中间件 / JwtBearer handler)按需取用。
        /// </summary>
        ApiKeyAuthResult Resolve(HttpContext context, string schemeName);
    }
}
