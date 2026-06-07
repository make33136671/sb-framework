using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SB.Feature.AccessToken.Services
{
    /// <summary>
    /// 全局 scope 闸门。仅对"API Key 认证"的请求生效,密码登录 / 普通 JWT 请求直接放行。
    ///
    /// 关键安全语义(账号即 token + 框架后端无 RBAC 的前提下,防止 scoped key 变全权):
    ///   - 非 key 请求(HttpContext.Items 无 ApiKeyContext)→ 放行;
    ///   - FullAccess(god)key → 放行;
    ///   - 受限 key:
    ///       · 端点未声明 [Scope] → 403 拒绝(默认 deny,受限 key 只能进显式开放的端点);
    ///       · 端点声明 [Scope("…")] → 必须满足全部 scope,否则 403。
    /// </summary>
    public sealed class ScopeFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var keyCtx = context.HttpContext.Items[ApiKeyContext.HttpItemKey] as IApiKeyContext;

            // 非 API Key 请求(密码登录 / 普通 JWT)→ 不归本闸门管
            if (keyCtx == null)
            {
                await next();
                return;
            }

            // FullAccess(god)key → 全放行
            if (keyCtx.FullAccess)
            {
                await next();
                return;
            }

            var scopeAttr = context.ActionDescriptor.EndpointMetadata
                .OfType<ScopeAttribute>()
                .LastOrDefault();

            // 受限 key + 端点未声明 [Scope] → 默认拒绝
            if (scopeAttr == null)
            {
                Deny(context, "该端点不对受限 API Key 开放");
                return;
            }

            var requiredScopes = scopeAttr.RequiredScopes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var scope in requiredScopes)
            {
                if (!keyCtx.HasScope(scope))
                {
                    Deny(context, $"缺少 scope 权限: {scope}");
                    return;
                }
            }

            await next();
        }

        private static void Deny(ActionExecutingContext context, string message)
        {
            context.Result = new ObjectResult(new { code = -1, message })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
