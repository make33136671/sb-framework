using FreeSql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SB.Feature.AccessToken.Models;
using SB.Feature.AccessToken.Services;
using SB.Framework.Core.Expand;
using SB.Framework.Models;

namespace SB.Feature.AccessToken.Controllers
{
    /// <summary>
    /// 访问令牌管理(后台 JwtAdmin)。签发 / 列表 / 详情 / 更新 / 吊销 / 审计。
    /// 提权防护:仅 super 可签发 FullAccess(god)key 或绑定到 super 账号;FullAccess 必须设有限 TTL。
    /// </summary>
    [ApiController]
    [Authorize(AuthenticationSchemes = "JwtAdmin")]
    [Route("/admin/AccessToken/[action]")]
    public class AccessTokenController : ControllerBase
    {
        private readonly ApiKeyService _keys;
        private readonly ServiceAccountService _accounts;
        private readonly ApiKeyAuditService _audit;
        private readonly IFreeSql _fsql;
        private readonly AccessTokenOptions _opt;

        public AccessTokenController(ApiKeyService keys, ServiceAccountService accounts,
            ApiKeyAuditService audit, IFreeSql fsql, IOptions<AccessTokenOptions> opt)
        {
            _keys = keys;
            _accounts = accounts;
            _audit = audit;
            _fsql = fsql;
            _opt = opt.Value;
        }

        /// <summary>签发新 key。明文仅在本响应返回一次。</summary>
        [HttpPost]
        public Result Create(
            [FromForm] string name,
            [FromForm] int? accountId,
            [FromForm] string? serviceAccount,
            [FromForm] string? scopes,
            [FromForm] bool fullAccess = false,
            [FromForm] string? expiresAt = null,
            [FromForm] int? rateLimitPerMinute = null,
            [FromForm] string? ipAllowlist = null,
            [FromForm] string? notes = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return Result.Fail("name 不能为空");

            var me = _fsql.Select<Admin>().Include(a => a.AdminRole).Where(a => a.Id == User.Id()).First();
            if (me == null) return Result.Fail("无法识别当前操作者");
            bool isSuper = me.AdminRole?.RoleKey == "super";

            DateTime? expires = ParseDate(expiresAt);

            // FullAccess(god)提权防护
            if (fullAccess)
            {
                if (!_opt.EnableFullAccess)
                    return Result.Fail("FullAccess 功能未启用(Features:AccessToken:EnableFullAccess=false)");
                if (!isSuper)
                    return Result.Fail("仅超级管理员可签发 FullAccess key");
                if (_opt.FullAccessMaxTtlDays > 0)
                {
                    if (expires == null)
                        return Result.Fail($"FullAccess key 必须设置过期时间(最长 {_opt.FullAccessMaxTtlDays} 天)");
                    if (expires.Value > SB.Framework.Core.Util.AppTime.Now.AddDays(_opt.FullAccessMaxTtlDays))
                        return Result.Fail($"FullAccess key 有效期不得超过 {_opt.FullAccessMaxTtlDays} 天");
                }
            }

            // 解析归属账号
            int targetAccountId;
            if (accountId.HasValue)
            {
                var target = _fsql.Select<Admin>().Include(a => a.AdminRole).Where(a => a.Id == accountId.Value).First();
                if (target == null) return Result.Fail("指定账号不存在");
                if (target.AdminRole?.RoleKey == "super" && !isSuper)
                    return Result.Fail("仅超级管理员可绑定到超级账号");
                targetAccountId = target.Id;
            }
            else if (!string.IsNullOrWhiteSpace(serviceAccount))
            {
                // 服务账号默认继承签发者角色(≤ 签发者权限),避免越权
                var acc = _accounts.EnsureServiceAccount(serviceAccount.Trim(), me.RoleId, me.Username);
                targetAccountId = acc.Id;
            }
            else
            {
                return Result.Fail("需指定 accountId(绑定已有账号)或 serviceAccount(服务账号用户名)");
            }

            var created = _keys.Create(targetAccountId, name, scopes, fullAccess, expires,
                rateLimitPerMinute, ipAllowlist, notes, me.Username);

            _audit.WriteAsync(new ApiKeyAuditLog
            {
                KeyId = created.Entity.Id,
                KeyName = created.Entity.Name,
                TokenPrefix = created.Entity.TokenPrefix,
                Action = "Created",
                Result = "Success",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Operator = me.Username,
                DenyReason = fullAccess ? "FullAccess" : null,
            });

            return Result.Success(new
            {
                created.Entity.Id,
                created.Entity.Name,
                token = created.Plaintext,           // 仅此一次
                created.Entity.TokenPrefix,
                accountId = targetAccountId,
                created.Entity.Scopes,
                created.Entity.FullAccess,
                created.Entity.ExpiresAt,
                created.Entity.RateLimitPerMinute,
            });
        }

        [HttpGet]
        public Result List(string? keyword = null, int? accountId = null, int page = 1, int pageSize = 20)
        {
            var (items, total) = _keys.List(keyword, accountId, page, pageSize);
            // 不返回 TokenHash
            var data = items.Select(k => new
            {
                k.Id, k.AccountId, k.Name, k.TokenPrefix, k.Scopes, k.FullAccess,
                k.IpAllowlist, k.ExpiresAt, k.LastUsedAt, k.RateLimitPerMinute,
                k.Notes, k.CreatedBy, k.ModifiedBy, k.RevokeReason, k.CreatedTime, k.UpdatedTime,
            }).ToList();
            return Result.Page(data, total);
        }

        [HttpGet]
        public Result Detail(int id)
        {
            var k = _keys.Detail(id);
            if (k == null) return Result.Fail("不存在");
            return Result.Success(new
            {
                k.Id, k.AccountId, k.Name, k.TokenPrefix, k.Scopes, k.FullAccess,
                k.IpAllowlist, k.ExpiresAt, k.LastUsedAt, k.RateLimitPerMinute,
                k.Notes, k.CreatedBy, k.ModifiedBy, k.RevokeReason, k.CreatedTime, k.UpdatedTime,
            });
        }

        [HttpPost]
        public Result Update(
            [FromForm] int id,
            [FromForm] string? name = null,
            [FromForm] string? scopes = null,
            [FromForm] string? expiresAt = null,
            [FromForm] int? rateLimitPerMinute = null,
            [FromForm] string? ipAllowlist = null,
            [FromForm] string? notes = null)
        {
            var me = _fsql.Select<Admin>().Where(a => a.Id == User.Id()).First();
            var ok = _keys.Update(id, name, scopes, ParseDate(expiresAt), rateLimitPerMinute, ipAllowlist, notes, me?.Username);
            if (!ok) return Result.Fail("不存在");
            _audit.WriteAsync(new ApiKeyAuditLog { KeyId = id, Action = "Updated", Result = "Success", Operator = me?.Username });
            return Result.Success("已更新");
        }

        [HttpPost]
        public Result Revoke([FromForm] int id, [FromForm] string? reason = null)
        {
            var me = _fsql.Select<Admin>().Where(a => a.Id == User.Id()).First();
            var k = _keys.Detail(id);
            if (k == null) return Result.Fail("不存在");
            _keys.Revoke(id, reason, me?.Username);
            _audit.WriteAsync(new ApiKeyAuditLog
            {
                KeyId = id, KeyName = k.Name, TokenPrefix = k.TokenPrefix,
                Action = "Revoked", Result = "Success", Operator = me?.Username, DenyReason = reason,
            });
            return Result.Success("已吊销");
        }

        [HttpGet]
        public Result Audit(int? keyId = null, string? action = null, int page = 1, int pageSize = 20)
        {
            var (items, total) = _audit.Query(keyId, action, null, null, null, page, pageSize);
            return Result.Page(items, total);
        }

        private static DateTime? ParseDate(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : (DateTime.TryParse(s, out var d) ? d : null);
    }
}
