using Microsoft.AspNetCore.Mvc.Filters;

namespace SB.Feature.AccessToken.Services
{
    /// <summary>
    /// 声明某 Action/Controller 允许被"受限 API Key"访问,并指定所需 scope。
    /// 与 ScopeFilter 配合:
    ///   - 未标 [Scope] 的端点 → 受限 key 一律拒绝(默认 deny,least-privilege);
    ///   - 标了 [Scope("a","b")] → 受限 key 必须同时拥有 a、b 才放行。
    /// 密码登录 / FullAccess key 不受本属性约束。取代原 PM 的 [AgentScope]。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ScopeAttribute : Attribute, IFilterMetadata
    {
        public string RequiredScopes { get; }

        public ScopeAttribute(params string[] requiredScopes)
        {
            RequiredScopes = string.Join(",", requiredScopes ?? Array.Empty<string>());
        }
    }
}
