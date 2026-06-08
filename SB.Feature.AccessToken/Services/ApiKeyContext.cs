using SB.Feature.AccessToken.Models;

namespace SB.Feature.AccessToken.Services
{
    /// <summary>
    /// 当前请求命中的 API Key 上下文。由 ApiKeyAuthenticator 在认证成功时注入 HttpContext.Items,
    /// ScopeFilter / 业务层据此判 scope、FullAccess、写操作人。
    /// </summary>
    public interface IApiKeyContext
    {
        int KeyId { get; }
        string KeyName { get; }
        int AccountId { get; }
        bool FullAccess { get; }
        ApiKey Key { get; }

        /// <summary>RBAC 通配 scope 检查。Scopes=null 时对任意 required 永真(限"已声明 [Scope] 的端点")。</summary>
        bool HasScope(string scope);

        /// <summary>写审计 / 操作人字段用,格式 "key:&lt;name&gt;"。</summary>
        string OperatorTag => $"key:{KeyName}";
    }

    /// <inheritdoc/>
    public sealed class ApiKeyContext : IApiKeyContext
    {
        public const string HttpItemKey = "__sb_apikey_ctx";

        private readonly string[]? _scopes;

        public ApiKeyContext(ApiKey key)
        {
            Key = key;
            KeyId = key.Id;
            KeyName = key.Name;
            AccountId = key.AccountId;
            FullAccess = key.FullAccess;
            _scopes = string.IsNullOrWhiteSpace(key.Scopes)
                ? null
                : key.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        public int KeyId { get; }
        public string KeyName { get; }
        public int AccountId { get; }
        public bool FullAccess { get; }
        public ApiKey Key { get; }

        /// <summary>
        /// RBAC 通配匹配:
        /// - FullAccess → 永真(god);
        /// - Scopes==null → 永真(向后兼容 agent null=全权,仍只限已声明 [Scope] 的端点);
        /// - 持有 "*" → 永真;持有完全相同(忽略大小写)→ 通过;
        /// - "action:resource" 形式:持有 "action:*" → 通过(如 required=read:task,持有 read:* → 允许)。
        /// </summary>
        public bool HasScope(string required)
        {
            if (FullAccess) return true;
            if (_scopes == null) return true;
            if (string.IsNullOrWhiteSpace(required)) return true;

            foreach (var s in _scopes)
            {
                if (string.Equals(s, "*", StringComparison.Ordinal)) return true;
                if (string.Equals(s, required, StringComparison.OrdinalIgnoreCase)) return true;

                var colon = required.IndexOf(':');
                if (colon > 0)
                {
                    var action = required.Substring(0, colon);
                    if (string.Equals(s, $"{action}:*", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }
    }
}
