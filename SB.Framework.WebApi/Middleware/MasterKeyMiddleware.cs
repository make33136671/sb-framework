using System.Security.Claims;
using SB.Framework.Core;

namespace SB.Framework.WebApi.Middleware
{
    /// <summary>
    /// MasterKey 万能密钥认证中间件
    /// 请求携带 X-Master-Key 头且匹配配置值时，自动构造 Admin 身份，绕过 JWT 认证
    /// </summary>
    public class MasterKeyMiddleware
    {
        private readonly RequestDelegate _next;

        public MasterKeyMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var masterKey = AppGlobal.AppSettings?.MasterKey;

            if (!string.IsNullOrEmpty(masterKey)
                && context.Request.Headers.TryGetValue("X-Master-Key", out var headerKey)
                && string.Equals(masterKey, headerKey.ToString(), StringComparison.Ordinal))
            {
                var claims = new List<Claim>
                {
                    new("Id", "1"),
                    new("Username", "MasterKey"),
                    new("UserType", "Admin")
                };

                var identity = new ClaimsIdentity(claims, "JwtAdmin");
                context.User = new ClaimsPrincipal(identity);
            }

            await _next(context);
        }
    }
}
