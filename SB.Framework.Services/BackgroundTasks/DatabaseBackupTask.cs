using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.BackgroundTask;
using SB.Framework.Core.Util;
using SB.Framework.Models;

namespace SB.Framework.Services.BackgroundTasks
{
    /// <summary>
    /// 自动数据库备份任务
    ///
    /// 调度方式：1 分钟轮询，内部判断"距上次本地备份是否超过 backup_interval_hours"，
    /// 不依赖 cron。这样能容忍服务重启 / 任意时刻启动而不漂移备份时段。
    ///
    /// 流程：检查阈值 → 调用 DatabaseBackupService.CreateBackup → 配了 backup_upload_url 就上传到云端
    /// → 更新本次记录 RemoteUploadStatus → 按 backup_keep_local_count 清理旧本地文件
    /// </summary>
    public class DatabaseBackupTask : BaseBackgroundTask
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
        private readonly IServiceScopeFactory _scopeFactory;

        public DatabaseBackupTask(
            ILogger<DatabaseBackupTask> logger,
            IFreeSql fsql,
            IServiceScopeFactory scopeFactory)
            : base(logger, fsql)
        {
            _scopeFactory = scopeFactory;
        }

        public override string DisplayName => "数据库自动备份";

        protected override TimeSpan Interval => PollInterval;

        /// <summary>
        /// 下次执行时间 = 上次本地备份时间 + backup_interval_hours
        /// (节流条件，到时机才触发；轮询本身每分钟跑只是检查时机)
        /// </summary>
        public override DateTime? NextRunTime
        {
            get
            {
                if (Status != SB.Framework.Core.BackgroundTask.TaskStatus.Running) return null;
                using var scope = _scopeFactory.CreateScope();
                var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();
                var intervalHours = GetSettingInt(fsql, "backup_interval_hours", 12);
                if (intervalHours <= 0) return null; // 用户关闭定时备份
                var latest = fsql.Select<DatabaseBackup>()
                    .OrderByDescending(c => c.CreatedTime)
                    .First();
                if (latest == null) return AppTime.UtcNow; // 没备份过 → 下次轮询就备
                return latest.CreatedTime.ToUniversalTime().AddHours(intervalHours);
            }
        }

        /// <summary>
        /// 节流条件：backup_interval_hours <= 0 直接关；距上次备份 < interval 小时也跳过
        /// 手动 RunOnceAsync 绕过此判断 → 立即备份
        /// </summary>
        protected override Task<bool> ShouldRunNowAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();

            var intervalHours = GetSettingInt(fsql, "backup_interval_hours", 12);
            if (intervalHours <= 0) return Task.FromResult(false);

            var latest = fsql.Select<DatabaseBackup>()
                .OrderByDescending(c => c.CreatedTime)
                .First();
            if (latest != null)
            {
                var elapsedHours = (AppTime.UtcNow - latest.CreatedTime.ToUniversalTime()).TotalHours;
                if (elapsedHours < intervalHours) return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }

        protected override async Task ExecuteTaskAsync(CancellationToken cancellationToken)
        {
            // 完全复用 DatabaseBackupService.BackupAndUploadAsync —— 跟 admin 手动备份 / MasterKey API 走同一套
            using var scope = _scopeFactory.CreateScope();
            var backupService = scope.ServiceProvider.GetRequiredService<DatabaseBackupService>();
            var result = await backupService.BackupAndUploadAsync("auto-task", cancellationToken);
            if (result.Code != 0)
            {
                LogError($"自动备份失败: {result.Message}");
                WriteTaskLog(SB.Framework.Core.BackgroundTask.TaskLogLevel.Error, $"自动备份失败: {result.Message}");
            }
            else
            {
                WriteTaskLog(SB.Framework.Core.BackgroundTask.TaskLogLevel.Info, "本地备份 + 云端上传 + 清理 已执行");
            }
        }

        private static int GetSettingInt(IFreeSql fsql, string key, int def)
        {
            var s = fsql.Select<SystemSetting>().Where(c => c.Key == key).First();
            return s?.DataContent != null && int.TryParse(s.DataContent, out var n) ? n : def;
        }
    }
}
