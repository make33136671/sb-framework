using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SB.Framework.Core.Authentication;

namespace SB.Framework.WebApi.Middleware
{
    /// <summary>
    /// API Key 全局身份中间件(取代静态 MasterKeyMiddleware)。
    /// 请求携带 Authorization: Bearer sk_… 且有效时,把 context.User 设为该 key 归属账号的身份,
    /// 这样即便目标是 AllowAnonymous 端点,既有"按 User.Username() 写操作人"的逻辑也能拿到身份。
    ///
    /// [Authorize(JwtAdmin/JwtApi)] 端点的认证另由 JwtBearer scheme 的 HandleApiKey 处理(同一 IApiKeyAuthenticator,
    /// 结果按请求缓存,不会重复查库)。功能包未加载时本中间件无副作用。
    /// </summary>
    public class ApiKeyMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiKeyMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var authenticator = context.RequestServices.GetService<IApiKeyAuthenticator>();
            if (authenticator != null)
            {
                var result = authenticator.Resolve(context, "JwtAdmin");
                if (result.Outcome == ApiKeyOutcome.Authenticated && result.Principal != null)
                    context.User = result.Principal;
            }

            await _next(context);
        }
    }
}
