using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Expand;
using SB.Framework.Core.Util;
using SB.Framework.Models;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class DeployRecordService : BaseCrudService<DeployRecord>, IModuleInitializer
    {
        private static readonly string BackupRoot = Path.Combine(AppContext.BaseDirectory, "deploy_backups");

        public DeployRecordService(IFreeSql fsql, ILogger<DeployRecordService> logger) : base(fsql, logger) { }

        #region IModuleInitializer
        public string ModuleName => "部署记录";
        public int InitOrder => 200;
        public Type[] GetEntityTypes() => new[] { typeof(DeployRecord) };
        #endregion

        /// <summary>
        /// 保存部署记录并备份 zip 文件
        /// </summary>
        public async Task<DeployRecord> SaveRecord(string target, string targetName, string tempZipPath, string fileName, long fileSize, string operatorName)
        {
            // 确保备份目录存在
            var targetDir = Path.Combine(BackupRoot, target);
            Directory.CreateDirectory(targetDir);

            // 备份文件名：时间戳_原始文件名
            var backupFileName = $"{AppTime.Now:yyyyMMdd_HHmmss}_{fileName}";
            var backupPath = Path.Combine(target, backupFileName);
            var fullBackupPath = Path.Combine(BackupRoot, backupPath);

            File.Copy(tempZipPath, fullBackupPath, true);

            var record = new DeployRecord
            {
                Target = target,
                TargetName = targetName,
                BackupPath = backupPath,
                FileName = fileName,
                FileSize = fileSize,
                Operator = operatorName,
            };

            await Orm.Insert(record).ExecuteAffrowsAsync();
            record.Id = (int)Orm.Select<DeployRecord>().Max(r => r.Id);

            return record;
        }

        /// <summary>
        /// 获取指定目标的部署记录
        /// </summary>
        public async Task<object> GetRecords(string? target, int page = 1, int pageSize = 20)
        {
            var query = Orm.Select<DeployRecord>()
                .WhereIf(!string.IsNullOrEmpty(target), r => r.Target == target)
                .OrderByDescending(r => r.Id);

            var total = await query.CountAsync();
            var items = await query.Page(page, pageSize).ToListAsync();

            return new { items, total };
        }

        /// <summary>
        /// 获取备份文件的完整路径
        /// </summary>
        public string? GetBackupFullPath(DeployRecord record)
        {
            var fullPath = Path.Combine(BackupRoot, record.BackupPath);
            return File.Exists(fullPath) ? fullPath : null;
        }
    }
}
