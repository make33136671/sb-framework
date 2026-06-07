using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SB.Feature.AccessToken.Models;
using SB.Framework.Core.Authentication;
using SB.Framework.Models;
using System.Security.Claims;

namespace SB.Feature.AccessToken.Services
{
    /// <summary>
    /// IApiKeyAuthenticator 实现(功能包注册到 DI,框架核心缝调用)。
    /// 取代原静态 X-Master-Key:sk_ 提取 → sha256 查库 → 账号状态校验 → FullAccess 开关 → IP 白名单 → 速率,
    /// 并对"无效 key 尝试"按来源 IP 节流 + 拉黑(防爆破)。每请求只评估一次(Eval 缓存在 HttpContext.Items)。
    /// </summary>
    public sealed class ApiKeyAuthenticator : IApiKeyAuthenticator
    {
        private const string EvalItemKey = "__sb_apikey_eval";

        private readonly ApiKeyService _keys;
        private readonly ApiKeyRateLimiter _limiter;
        private readonly ApiKeyAuditService _audit;
        private readonly IFreeSql _fsql;
        private readonly AccessTokenOptions _opt;
        private readonly ILogger<ApiKeyAuthenticator> _logger;

        public ApiKeyAuthenticator(ApiKeyService keys, ApiKeyRateLimiter limiter, ApiKeyAuditService audit,
            IFreeSql fsql, IOptions<AccessTokenOptions> opt, ILogger<ApiKeyAuthenticator> logger)
        {
            _keys = keys;
            _limiter = limiter;
            _audit = audit;
            _fsql = fsql;
            _opt = opt.Value;
            _logger = logger;
        }

        private sealed record Eval(ApiKeyOutcome Outcome, ApiKey? Key, Admin? Account, bool FullAccess, string? Reason);

        public ApiKeyAuthResult Resolve(HttpContext context, string schemeName)
        {
            Eval eval;
            if (context.Items.TryGetValue(EvalItemKey, out var cached) && cached is Eval e)
            {
                eval = e;
            }
            else
            {
                eval = Evaluate(context);
                context.Items[EvalItemKey] = eval;
                if (eval.Outcome == ApiKeyOutcome.Authenticated && eval.Key != null)
                    context.Items[ApiKeyContext.HttpItemKey] = new ApiKeyContext(eval.Key);
            }

            return eval.Outcome switch
            {
                ApiKeyOutcome.Authenticated => ApiKeyAuthResult.Authenticated(BuildPrincipal(eval, schemeName), eval.FullAccess),
                ApiKeyOutcome.Invalid => ApiKeyAuthResult.Invalid(eval.Reason ?? "无效的 API Key"),
                _ => ApiKeyAuthResult.NotPresent,
            };
        }

        private Eval Evaluate(HttpContext context)
        {
            var token = ExtractBearer(context.Request);
            // 只接管 sk_ 前缀的凭据;JWT(eyJ…)/无凭据 → 交回常规鉴权
            if (string.IsNullOrEmpty(token) || !token.StartsWith("sk_", StringComparison.Ordinal))
                return new Eval(ApiKeyOutcome.NotPresent, null, null, false, null);

            var ip = ClientIp(context);
            var path = context.Request.Path.Value;

            // 防爆破:该 IP 已被拉黑 → 直接拒,不查库
            if (_limiter.IsIpBlocked(ip))
            {
                Audit("Throttled", "Denied", 0, "", "", ip, path, "IP 暂时被拉黑");
                return new Eval(ApiKeyOutcome.Invalid, null, null, false, "尝试过于频繁,请稍后再试");
            }

            var key = _keys.Verify(token);
            if (key == null)
            {
                // 记录失败 + 计数拉黑;对外统一模糊原因,不区分"不存在/过期/吊销"
                _limiter.RecordIpFailure(ip, _opt.FailThreshold, _opt.FailWindowSeconds, _opt.FailBlockSeconds);
                Audit("AuthFailed", "Denied", 0, "", "", ip, path, "key 无效/过期/吊销");
                return new Eval(ApiKeyOutcome.Invalid, null, null, false, "无效的 API Key");
            }

            // 归属账号状态校验(账号即 token:账号停用/删除则 key 立即失效)
            var account = _fsql.Select<Admin>().Where(a => a.Id == key.AccountId).First();
            if (account == null || account.IsDeleted || account.Status != AdminStatus.Active)
            {
                Audit("Denied", "Denied", key.Id, key.Name, key.TokenPrefix, ip, path, "归属账号停用/不存在");
                return new Eval(ApiKeyOutcome.Invalid, null, null, false, "无效的 API Key");
            }

            // IP 白名单
            if (!string.IsNullOrWhiteSpace(key.IpAllowlist) && !IpAllowed(key.IpAllowlist!, ip))
            {
                Audit("Denied", "Denied", key.Id, key.Name, key.TokenPrefix, ip, path, "来源 IP 不在白名单");
                return new Eval(ApiKeyOutcome.Invalid, null, null, false, "无效的 API Key");
            }

            // FullAccess(god)总开关:关闭时 god key 不予认证
            if (key.FullAccess && !_opt.EnableFullAccess)
            {
                Audit("Denied", "Denied", key.Id, key.Name, key.TokenPrefix, ip, path, "FullAccess 功能已禁用");
                return new Eval(ApiKeyOutcome.Invalid, null, null, false, "无效的 API Key");
            }

            // 单 key 速率限制
            if (!_limiter.AllowKey(key))
            {
                Audit("RateLimited", "Denied", key.Id, key.Name, key.TokenPrefix, ip, path,
                    $"速率超限 {key.RateLimitPerMinute}/min");
                return new Eval(ApiKeyOutcome.Invalid, null, null, false, "速率超限");
            }

            _keys.TouchLastUsed(key.Id);
            // 成功不逐条审计(量大),仅失败/拒绝/生命周期事件入审计
            return new Eval(ApiKeyOutcome.Authenticated, key, account, key.FullAccess, null);
        }

        private static ClaimsPrincipal BuildPrincipal(Eval eval, string schemeName)
        {
            var acc = eval.Account!;
            var key = eval.Key!;
            var claims = new List<Claim>
            {
                new("Id", acc.Id.ToString()),
                new("Username", acc.Username),
                new("UserType", "Admin"),
                new("AuthVia", "ApiKey"),
                new("ApiKeyId", key.Id.ToString()),
                new("ApiKeyName", key.Name),
                new("FullAccess", key.FullAccess ? "true" : "false"),
            };
            if (!string.IsNullOrWhiteSpace(key.Scopes))
                claims.Add(new Claim("Scopes", key.Scopes));

            var identity = new ClaimsIdentity(claims, schemeName);
            return new ClaimsPrincipal(identity);
        }

        private void Audit(string action, string result, int keyId, string keyName, string prefix,
            string ip, string? path, string reason)
        {
            _audit.WriteAsync(new ApiKeyAuditLog
            {
                KeyId = keyId,
                KeyName = keyName,
                TokenPrefix = prefix,
                Action = action,
                Result = result,
                IpAddress = ip,
                RequestPath = path,
                DenyReason = reason,
            });
        }

        private static string? ExtractBearer(HttpRequest request)
        {
            var header = request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(header)) return null;
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
            return header.Substring("Bearer ".Length).Trim();
        }

        private static string ClientIp(HttpContext context)
        {
            var fwd = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(fwd))
                return fwd.Split(',')[0].Trim();
            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        /// <summary>IP 白名单匹配:精确相等或前缀(简单 CIDR 近似,如 "192.168.10." 命中网段);完整 CIDR 后续补。</summary>
        private static bool IpAllowed(string allowlist, string ip)
        {
            foreach (var raw in allowlist.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(raw, ip, StringComparison.OrdinalIgnoreCase)) return true;
                if (raw.EndsWith(".") && ip.StartsWith(raw, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
