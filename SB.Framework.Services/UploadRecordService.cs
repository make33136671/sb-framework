using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;
using SB.Framework.Models;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class UploadRecordService : BaseCrudService<UploadRecord>, IModuleInitializer
    {
        private static readonly string StagingRoot = Path.Combine(AppContext.BaseDirectory, "upload_staging");

        public UploadRecordService(IFreeSql fsql, ILogger<UploadRecordService> logger) : base(fsql, logger) { }

        #region IModuleInitializer
        public string ModuleName => "上传记录";
        public int InitOrder => 201;
        public Type[] GetEntityTypes() => new[] { typeof(UploadRecord) };
        #endregion

        /// <summary>
        /// 把已写入 sourceFilePath 的文件持久化到 staging 目录，创建上传记录并返回
        /// </summary>
        public async Task<UploadRecord> SaveUpload(
            string target, string targetName,
            string sourceFilePath, string fileName, long fileSize,
            string uploadedBy)
        {
            var targetDir = Path.Combine(StagingRoot, target);
            Directory.CreateDirectory(targetDir);

            var storageName = $"{AppTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}_{fileName}";
            var storagePath = Path.Combine(target, storageName);
            var fullStoragePath = Path.Combine(StagingRoot, storagePath);

            File.Copy(sourceFilePath, fullStoragePath, overwrite: true);

            var record = new UploadRecord
            {
                Target = target,
                TargetName = targetName,
                FileName = fileName,
                FileSize = fileSize,
                StoragePath = storagePath,
                Status = UploadRecordStatus.Pending,
                UploadedBy = uploadedBy,
            };

            await Orm.Insert(record).ExecuteAffrowsAsync();
            record.Id = (int)Orm.Select<UploadRecord>().Max(r => r.Id);

            return record;
        }

        /// <summary>
        /// 把 IFormFile 流直接写入 staging 并创建记录（小文件直传路径）
        /// </summary>
        public async Task<UploadRecord> SaveUploadFromStream(
            string target, string targetName,
            Stream fileStream, string fileName, long fileSize,
            string uploadedBy)
        {
            var targetDir = Path.Combine(StagingRoot, target);
            Directory.CreateDirectory(targetDir);

            var storageName = $"{AppTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}_{fileName}";
            var storagePath = Path.Combine(target, storageName);
            var fullStoragePath = Path.Combine(StagingRoot, storagePath);

            using (var dest = new FileStream(fullStoragePath, FileMode.Create))
            {
                await fileStream.CopyToAsync(dest);
            }

            var record = new UploadRecord
            {
                Target = target,
                TargetName = targetName,
                FileName = fileName,
                FileSize = fileSize,
                StoragePath = storagePath,
                Status = UploadRecordStatus.Pending,
                UploadedBy = uploadedBy,
            };

            await Orm.Insert(record).ExecuteAffrowsAsync();
            record.Id = (int)Orm.Select<UploadRecord>().Max(r => r.Id);

            return record;
        }

        public async Task MarkDeployed(int id)
        {
            await Orm.Update<UploadRecord>(id)
                .Set(r => r.Status, UploadRecordStatus.Deployed)
                .Set(r => r.DeployTime, AppTime.Now)
                .ExecuteAffrowsAsync();
        }

        public async Task MarkFailed(int id, string error)
        {
            await Orm.Update<UploadRecord>(id)
                .Set(r => r.Status, UploadRecordStatus.Failed)
                .Set(r => r.DeployTime, AppTime.Now)
                .Set(r => r.DeployError, error)
                .ExecuteAffrowsAsync();
        }

        public async Task<object> GetRecords(string? target, string? status, int page = 1, int pageSize = 20)
        {
            var query = Orm.Select<UploadRecord>()
                .WhereIf(!string.IsNullOrEmpty(target), r => r.Target == target)
                .WhereIf(!string.IsNullOrEmpty(status), r => r.Status == status)
                .OrderByDescending(r => r.Id);

            var total = await query.CountAsync();
            var items = await query.Page(page, pageSize).ToListAsync();

            return new { items, total };
        }

        public string GetFullPath(UploadRecord record)
            => Path.Combine(StagingRoot, record.StoragePath);

        /// <summary>
        /// 清理超过 retentionDays 天且状态为 Deployed 的记录及对应文件
        /// </summary>
        public async Task Cleanup(int retentionDays = 7)
        {
            var cutoff = AppTime.Now.AddDays(-retentionDays);
            var old = await Orm.Select<UploadRecord>()
                .Where(r => r.Status == UploadRecordStatus.Deployed && r.UploadTime < cutoff)
                .ToListAsync();

            foreach (var r in old)
            {
                try
                {
                    var fullPath = GetFullPath(r);
                    if (File.Exists(fullPath)) File.Delete(fullPath);
                }
                catch { /* 文件删除失败不影响记录清理 */ }

                await Orm.Delete<UploadRecord>(r.Id).ExecuteAffrowsAsync();
            }
        }
    }
}
