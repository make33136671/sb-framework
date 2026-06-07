using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Core;

namespace SB.Framework.Services
{
    /// <summary>
    /// DeployJob CRUD + 状态流转 + 文件管理。
    /// 合并原 UploadRecordService 上传跟踪 + DeployRecordService 部署快照 / 回滚。
    /// 单一文件目录 deploy_archive/{target}/{ts}_{name}.zip,Status 决定它是 staging / 已部署 / 已回滚的文件。
    /// </summary>
    [AutoDependencyInjection]
    public class DeployJobService : BaseCrudService<DeployJob>, IModuleInitializer
    {
        private static readonly string ArchiveRoot = Path.Combine(AppContext.BaseDirectory, "deploy_archive");

        public DeployJobService(IFreeSql fsql, ILogger<DeployJobService> logger) : base(fsql, logger) { }

        #region IModuleInitializer
        public string ModuleName => "部署作业";
        public int InitOrder => 200;
        public Type[] GetEntityTypes() => new[] { typeof(DeployJob) };
        #endregion

        // ==================== 创建 ====================

        /// <summary>把已写入 sourceFilePath 的文件持久化到 archive 目录,创建 DeployJob(Uploaded)</summary>
        public async Task<DeployJob> CreateFromFile(
            string target, string targetName,
            string sourceFilePath, string fileName, long fileSize,
            string uploadedBy)
        {
            var storagePath = await PersistFile(target, sourceFilePath, fileName);
            return await InsertJob(target, targetName, fileName, fileSize, storagePath, uploadedBy);
        }

        /// <summary>把 stream 写入 archive 目录,创建 DeployJob(Uploaded)— 小文件直传路径</summary>
        public async Task<DeployJob> CreateFromStream(
            string target, string targetName,
            Stream fileStream, string fileName, long fileSize,
            string uploadedBy)
        {
            var storagePath = await PersistStream(target, fileStream, fileName);
            return await InsertJob(target, targetName, fileName, fileSize, storagePath, uploadedBy);
        }

        private async Task<string> PersistFile(string target, string sourceFilePath, string fileName)
        {
            var targetDir = Path.Combine(ArchiveRoot, target);
            Directory.CreateDirectory(targetDir);
            var storageName = $"{AppTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}_{fileName}";
            var storagePath = Path.Combine(target, storageName);
            File.Copy(sourceFilePath, Path.Combine(ArchiveRoot, storagePath), overwrite: true);
            await Task.CompletedTask;
            return storagePath;
        }

        private async Task<string> PersistStream(string target, Stream stream, string fileName)
        {
            var targetDir = Path.Combine(ArchiveRoot, target);
            Directory.CreateDirectory(targetDir);
            var storageName = $"{AppTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}_{fileName}";
            var storagePath = Path.Combine(target, storageName);
            using var dest = new FileStream(Path.Combine(ArchiveRoot, storagePath), FileMode.Create);
            await stream.CopyToAsync(dest);
            return storagePath;
        }

        private async Task<DeployJob> InsertJob(
            string target, string targetName, string fileName, long fileSize,
            string storagePath, string uploadedBy)
        {
            var job = new DeployJob
            {
                Target = target,
                TargetName = targetName,
                FileName = fileName,
                FileSize = fileSize,
                StoragePath = storagePath,
                Status = DeployJobStatus.Uploaded,
                UploadedBy = uploadedBy,
            };
            await Orm.Insert(job).ExecuteAffrowsAsync();
            job.Id = (int)Orm.Select<DeployJob>().Max(r => r.Id);
            return job;
        }

        // ==================== 状态流转 ====================

        public async Task MarkDeploying(int id, string deployedBy)
        {
            await Orm.Update<DeployJob>(id)
                .Set(r => r.Status, DeployJobStatus.Deploying)
                .Set(r => r.DeployedBy, deployedBy)
                .Set(r => r.DeployStartedAt, AppTime.Now)
                .ExecuteAffrowsAsync();
        }

        /// <summary>标 Deployed,同时把该 target 之前 Deployed 的 job 标 RolledBack(被新 job 覆盖)</summary>
        public async Task MarkDeployed(int id, string target)
        {
            var now = AppTime.Now;
            var previous = await Orm.Select<DeployJob>()
                .Where(r => r.Target == target && r.Status == DeployJobStatus.Deployed && r.Id != id)
                .OrderByDescending(r => r.Id)
                .FirstAsync();

            await Orm.Update<DeployJob>(id)
                .Set(r => r.Status, DeployJobStatus.Deployed)
                .Set(r => r.DeployFinishedAt, now)
                .Set(r => r.PreviousJobId, previous?.Id)
                .ExecuteAffrowsAsync();

            if (previous != null)
            {
                await Orm.Update<DeployJob>(previous.Id)
                    .Set(r => r.Status, DeployJobStatus.RolledBack)
                    .Set(r => r.RolledBackAt, now)
                    .ExecuteAffrowsAsync();
            }
        }

        public async Task MarkFailed(int id, string error)
        {
            await Orm.Update<DeployJob>(id)
                .Set(r => r.Status, DeployJobStatus.Failed)
                .Set(r => r.DeployFinishedAt, AppTime.Now)
                .Set(r => r.ErrorMessage, error)
                .ExecuteAffrowsAsync();
        }

        // ==================== 回滚 ====================

        /// <summary>根据被回滚的 jobId,创建一个新 DeployJob(指向旧 job 的 StoragePath),复制状态为 Uploaded 等待 Deploy 触发</summary>
        public async Task<DeployJob> CreateRollbackJob(int targetJobId, string operatorName)
        {
            var target = await Orm.Select<DeployJob>().Where(r => r.Id == targetJobId).FirstAsync()
                ?? throw new InvalidOperationException($"DeployJob {targetJobId} 不存在");

            var rollbackJob = new DeployJob
            {
                Target = target.Target,
                TargetName = target.TargetName,
                FileName = target.FileName,
                FileSize = target.FileSize,
                StoragePath = target.StoragePath,  // 共用旧文件,不复制
                Status = DeployJobStatus.Uploaded,
                UploadedBy = operatorName,
                RollbackOfJobId = targetJobId,
                Remark = $"回滚到 #{targetJobId}",
            };
            await Orm.Insert(rollbackJob).ExecuteAffrowsAsync();
            rollbackJob.Id = (int)Orm.Select<DeployJob>().Max(r => r.Id);

            // 在被回滚的 job 上记操作人
            await Orm.Update<DeployJob>(targetJobId)
                .Set(r => r.RolledBackBy, operatorName)
                .ExecuteAffrowsAsync();

            return rollbackJob;
        }

        // ==================== 查询 ====================

        public async Task<object> GetJobs(string? target = null, string? status = null, int page = 1, int pageSize = 20)
        {
            var query = Orm.Select<DeployJob>()
                .WhereIf(!string.IsNullOrEmpty(target), r => r.Target == target)
                .WhereIf(!string.IsNullOrEmpty(status), r => r.Status == status)
                .OrderByDescending(r => r.Id);

            var total = await query.CountAsync();
            var items = await query.Page(page, pageSize).ToListAsync();

            return new { items, total };
        }

        public string GetFullPath(DeployJob job)
            => Path.Combine(ArchiveRoot, job.StoragePath);

        // ==================== 清理 ====================

        /// <summary>清理超过 retentionDays 天的 RolledBack 状态文件(已被覆盖且过期的备份)</summary>
        public async Task Cleanup(int retentionDays = 30)
        {
            var cutoff = AppTime.Now.AddDays(-retentionDays);
            var old = await Orm.Select<DeployJob>()
                .Where(r => r.Status == DeployJobStatus.RolledBack && r.RolledBackAt < cutoff)
                .ToListAsync();

            foreach (var r in old)
            {
                try
                {
                    var fullPath = GetFullPath(r);
                    if (File.Exists(fullPath)) File.Delete(fullPath);
                }
                catch { /* 忽略文件删除失败 */ }
            }
        }
    }
}
