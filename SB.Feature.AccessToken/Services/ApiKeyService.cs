using FreeSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SB.Feature.AccessToken.Models;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace SB.Feature.AccessToken.Services
{
    /// <summary>
    /// API Key 业务服务。明文 key 仅在 Create 返回一次,库里只存 sha256(明文) 的 hex。
    /// 注:刻意使用纯 sha256(不加 HMAC/pepper),以便从 PM agent token(同样 sha256)平滑迁移 —— 直接搬 TokenHash。
    /// key 自身为 32 字节(256bit)高熵随机,sha256 存储不可被离线爆破;在线爆破由 ApiKeyAuthenticator 的 IP 节流挡。
    /// </summary>
    public class ApiKeyService
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<ApiKeyService> _logger;
        private readonly AccessTokenOptions _opt;
        private readonly ConcurrentDictionary<int, DateTime> _touchCache = new();

        public ApiKeyService(IFreeSql fsql, ILogger<ApiKeyService> logger, IOptions<AccessTokenOptions> opt)
        {
            _fsql = fsql;
            _logger = logger;
            _opt = opt.Value;
        }

        public record CreateResult(ApiKey Entity, string Plaintext);

        public CreateResult Create(int accountId, string name, string? scopes, bool fullAccess,
            DateTime? expiresAt, int? rateLimitPerMinute, string? ipAllowlist, string? notes, string? createdBy)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name 不能为空", nameof(name));
            if (accountId <= 0)
                throw new ArgumentException("accountId 非法", nameof(accountId));

            // 32 字节随机 → hex(纯 0-9a-f,shell 安全无特殊字符)
            var bytes = RandomNumberGenerator.GetBytes(32);
            var rand = Convert.ToHexString(bytes).ToLowerInvariant();
            var prefix = string.IsNullOrEmpty(_opt.KeyPrefix) ? "sk_" : _opt.KeyPrefix;
            var plaintext = $"{prefix}{rand}";
            var hash = Sha256Hex(plaintext);
            var tokenPrefix = plaintext.Substring(0, Math.Min(12, plaintext.Length));

            var entity = new ApiKey
            {
                AccountId = accountId,
                Name = name,
                TokenHash = hash,
                TokenPrefix = tokenPrefix,
                Scopes = scopes,
                FullAccess = fullAccess,
                ExpiresAt = expiresAt,
                RateLimitPerMinute = rateLimitPerMinute,
                IpAllowlist = ipAllowlist,
                Notes = notes,
                CreatedBy = createdBy ?? "unknown",
            };
            entity.Id = (int)_fsql.Insert(entity).ExecuteIdentity();

            // FullAccess 列有 InsertValueSql="false"（FreeSql 服务端默认值），INSERT 会忽略 C# 属性值。
            // 若需要 FullAccess=true，INSERT 后立即用 Set() 覆盖写入。
            if (fullAccess && entity.Id > 0)
            {
                _fsql.Update<ApiKey>()
                    .Where(k => k.Id == entity.Id)
                    .Set(k => k.FullAccess, true)
                    .ExecuteAffrows();
            }

            _logger.LogInformation("created api key: id={Id} name={Name} account={Account} full={Full}",
                entity.Id, name, accountId, fullAccess);

            return new CreateResult(entity, plaintext);
        }

        /// <summary>按明文校验。返回有效 key 实体(未过期、未吊销);否则 null。账号状态校验在 Authenticator 里做。</summary>
        public ApiKey? Verify(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return null;
            var hash = Sha256Hex(plaintext);
            var key = _fsql.Select<ApiKey>().Where(t => t.TokenHash == hash).First();
            if (key == null) return null;
            if (key.ExpiresAt.HasValue && key.ExpiresAt.Value < SB.Framework.Core.Util.AppTime.Now) return null;
            // ISoftDelete 已自动过滤 IsDeleted=false,被吊销的查不出来
            return key;
        }

        /// <summary>更新 LastUsedAt,节流到最多每 60 秒落库一次,避免高频写。</summary>
        public void TouchLastUsed(int id)
        {
            var now = SB.Framework.Core.Util.AppTime.Now;
            if (_touchCache.TryGetValue(id, out var last) && (now - last).TotalSeconds < 60) return;
            _touchCache[id] = now;
            _ = Task.Run(() =>
            {
                try { _fsql.Update<ApiKey>(id).Set(t => t.LastUsedAt, now).ExecuteAffrows(); }
                catch (Exception ex) { _logger.LogWarning(ex, "更新 LastUsedAt 失败 id={Id}", id); }
            });
        }

        public void Revoke(int id, string? reason = null, string? modifiedBy = null)
        {
            _fsql.Update<ApiKey>(id)
                .Set(t => t.IsDeleted, true)
                .Set(t => t.RevokeReason, reason)
                .Set(t => t.ModifiedBy, modifiedBy)
                .Set(t => t.UpdatedTime, SB.Framework.Core.Util.AppTime.Now)
                .ExecuteAffrows();
            _logger.LogInformation("revoked api key: id={Id} reason={Reason}", id, reason);
        }

        public bool Update(int id, string? name = null, string? scopes = null, DateTime? expiresAt = null,
            int? rateLimitPerMinute = null, string? ipAllowlist = null, string? notes = null, string? modifiedBy = null)
        {
            var key = _fsql.Select<ApiKey>().Where(t => t.Id == id).First();
            if (key == null) return false;

            if (name != null) key.Name = name;
            key.Scopes = scopes;
            key.ExpiresAt = expiresAt;
            key.RateLimitPerMinute = rateLimitPerMinute;
            key.IpAllowlist = ipAllowlist;
            key.Notes = notes;
            if (modifiedBy != null) key.ModifiedBy = modifiedBy;
            key.UpdatedTime = SB.Framework.Core.Util.AppTime.Now;

            // SetSource 全列更新,允许把可空列清空
            _fsql.Update<ApiKey>().SetSource(key).ExecuteAffrows();
            return true;
        }

        public ApiKey? Detail(int id) => _fsql.Select<ApiKey>().Where(t => t.Id == id).First();

        public (List<ApiKey> items, long total) List(string? keyword = null, int? accountId = null,
            int page = 1, int pageSize = 20)
        {
            var query = _fsql.Select<ApiKey>()
                .WhereIf(!string.IsNullOrWhiteSpace(keyword), t => t.Name.Contains(keyword!) || t.TokenPrefix.Contains(keyword!))
                .WhereIf(accountId.HasValue, t => t.AccountId == accountId!.Value)
                .OrderByDescending(t => t.CreatedTime)
                .Page(page, pageSize);
            var total = query.Count();
            return (query.ToList(), total);
        }

        public static string Sha256Hex(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
