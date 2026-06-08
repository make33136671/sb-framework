using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SB.Feature.AccessToken.Services
{
    /// <summary>
    /// 全局 scope 闸门。语义(账号即 token + 框架后端无 RBAC 的前提下,防止 scoped key 变全权):
    ///
    ///   未声明 [Scope] 的端点:
    ///     · 受限 key       → 403(默认 deny,受限 key 只能进显式开放的端点);
    ///     · 密码登录 / FullAccess key → 放行。
    ///
    ///   声明了 [Scope] 的端点(= 该端点为 API Key 专用,opt-in):
    ///     · 无 key(密码登录 / 匿名)→ 403,要求用 API Key 访问
    ///         —— 既保留原 /agent/* 的 key-only 语义,又保证下游注入的 IApiKeyContext 必然存在;
    ///     · FullAccess key → 放行;
    ///     · 受限 key       → 必须满足全部 scope,否则 403。
    /// </summary>
    public sealed class ScopeFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var keyCtx = context.HttpContext.Items[ApiKeyContext.HttpItemKey] as IApiKeyContext;

            // 取端点上的 [Scope](action 级在后 → LastOrDefault 让 action 覆盖 controller)
            var scopeAttr = context.ActionDescriptor.EndpointMetadata
                .OfType<ScopeAttribute>()
                .LastOrDefault();

            if (scopeAttr == null)
            {
                // 未声明 [Scope]:受限 key 拒绝;密码登录 / FullAccess key 放行
                if (keyCtx != null && !keyCtx.FullAccess)
                {
                    Deny(context, "该端点不对受限 API Key 开放");
                    return;
                }
                await next();
                return;
            }

            // 声明了 [Scope] = API Key 专用端点
            if (keyCtx == null)
            {
                Deny(context, "该端点需通过 API Key 访问");
                return;
            }
            if (keyCtx.FullAccess)
            {
                await next();
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
