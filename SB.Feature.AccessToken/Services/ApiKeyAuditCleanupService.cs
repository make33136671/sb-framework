using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SB.Feature.AccessToken.Services
{
    /// <summary>
    /// 审计日志超期清理。每 24 小时删一次早于 AuditRetentionDays 的记录。
    /// </summary>
    public sealed class ApiKeyAuditCleanupService : BackgroundService
    {
        private readonly ApiKeyAuditService _audit;
        private readonly AccessTokenOptions _opt;
        private readonly ILogger<ApiKeyAuditCleanupService> _logger;

        public ApiKeyAuditCleanupService(ApiKeyAuditService audit, IOptions<AccessTokenOptions> opt,
            ILogger<ApiKeyAuditCleanupService> logger)
        {
            _audit = audit;
            _opt = opt.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 启动后等 1 分钟再首跑,避开启动高峰
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_opt.AuditRetentionDays > 0)
                    {
                        var threshold = DateTime.Now.AddDays(-_opt.AuditRetentionDays);
                        var n = _audit.CleanupOlderThan(threshold);
                        if (n > 0) _logger.LogInformation("ApiKey 审计清理: 删除 {N} 条早于 {T:yyyy-MM-dd} 的记录", n, threshold);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ApiKey 审计清理失败");
                }

                try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
