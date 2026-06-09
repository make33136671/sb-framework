namespace SB.Framework.Core.Authentication
{
    /// <summary>
    /// 声明某 Action/Controller 允许被"受限 API Key"访问,并指定所需 scope。
    /// 属性本身在核心层(不依赖 MVC / 功能包),仅作端点元数据;实际强制由功能包
    /// SB.Feature.AccessToken 的 ScopeFilter 读取并执行 —— 未加载功能包的 app 中本属性是惰性元数据。
    /// 与 ScopeFilter 配合:
    ///   - 未标 [Scope] 的端点 → 受限 key 一律拒绝(默认 deny,least-privilege);
    ///   - 标了 [Scope("a","b")] → 受限 key 必须同时拥有 a、b 才放行,且该端点变为 key 专用(密码登录 JWT 被拒)。
    /// FullAccess key 不受本属性约束。取代原 PM 的 [AgentScope]。
    /// </summary>
    [global::System.AttributeUsage(
        global::System.AttributeTargets.Method | global::System.AttributeTargets.Class,
        AllowMultiple = true, Inherited = true)]
    public sealed class ScopeAttribute : global::System.Attribute
    {
        public string RequiredScopes { get; }

        public ScopeAttribute(params string[] requiredScopes)
        {
            RequiredScopes = string.Join(",", requiredScopes ?? global::System.Array.Empty<string>());
        }
    }
}
