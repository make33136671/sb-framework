using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Feature.AccessToken.Models;

namespace SB.Feature.AccessToken.Services
{
    /// <summary>
    /// API Key 审计日志服务。写入不阻塞主流程;支持分页查询。
    /// </summary>
    public class ApiKeyAuditService
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<ApiKeyAuditService> _logger;

        public ApiKeyAuditService(IFreeSql fsql, ILogger<ApiKeyAuditService> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        public void WriteAsync(ApiKeyAuditLog entry)
        {
            if (entry.CreatedTime == default)
                entry.CreatedTime = SB.Framework.Core.Util.AppTime.Now;

            _ = Task.Run(() =>
            {
                try { _fsql.Insert(entry).ExecuteAffrows(); }
                catch (Exception ex) { _logger.LogWarning(ex, "ApiKey 审计写入失败: Action={Action}", entry.Action); }
            });
        }

        public (List<ApiKeyAuditLog> items, long total) Query(
            int? keyId = null, string? action = null, string? result = null,
            DateTime? startTime = null, DateTime? endTime = null,
            int page = 1, int pageSize = 20)
        {
            var query = _fsql.Select<ApiKeyAuditLog>()
                .WhereIf(keyId.HasValue, a => a.KeyId == keyId!.Value)
                .WhereIf(!string.IsNullOrEmpty(action), a => a.Action == action)
                .WhereIf(!string.IsNullOrEmpty(result), a => a.Result == result)
                .WhereIf(startTime.HasValue, a => a.CreatedTime >= startTime!.Value)
                .WhereIf(endTime.HasValue, a => a.CreatedTime <= endTime!.Value)
                .OrderByDescending(a => a.CreatedTime)
                .Page(page, pageSize);

            var total = query.Count();
            var items = query.ToList();
            return (items, total);
        }

        /// <summary>清理超期审计(由清理 HostedService 调用)。</summary>
        public int CleanupOlderThan(DateTime threshold)
        {
            return _fsql.Delete<ApiKeyAuditLog>().Where(a => a.CreatedTime < threshold).ExecuteAffrows();
        }
    }
}
