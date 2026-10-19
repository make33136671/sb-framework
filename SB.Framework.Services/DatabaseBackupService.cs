using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core;
using SB.Framework.Services.BackupTools;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;
using SB.Framework.Models;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class DatabaseBackupService : BaseCrudService<DatabaseBackup>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "数据库备份";
        public int InitOrder => 40;
        public Type[] GetEntityTypes() => new[] { typeof(DatabaseBackup) };
        #endregion

        private readonly IFreeSql _fsql;
        private readonly BackupUploader _uploader;
        private readonly IBackupTool _backupTool;

        private static readonly string BackupDir =
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "storage", "db");

        public DatabaseBackupService(IFreeSql fsql, ILogger<DatabaseBackupService> logger, BackupUploader uploader, IServiceProvider sp)
            : base(fsql, logger)
        {
            _fsql = fsql;
            _uploader = uploader;
            // 按 DbConfig.DbType 选 MySqlBackupTool / PostgreSqlBackupTool。tool 无状态,直接 new。
            _backupTool = BackupToolFactory.Create(sp);
        }

        /// <summary>
        /// 接收管理员上传的备份文件（.zip 内含 .sql，或裸 .sql），校验通过后落盘为可用备份：
        ///   1. 体积/扩展名校验
        ///   2. 解压取 .sql，按脚本头部判断方言（mysqldump / pg_dump）
        ///   3. 方言与当前数据库不符 → 直接拒绝（跨库 SQL 互不兼容，导入会半截失败）
        ///   4. 重命名成标准格式 &lt;dbName&gt;_&lt;yyyyMMdd_HHmmss&gt;.zip 落盘
        ///   5. 写 DB 记录（含 MD5），随后即可在列表里「恢复」
        /// 注意：这里**不执行导入**，导入是独立的高危操作，由 RestoreAsync 负责。
        /// </summary>
        /// <param name="stream">上传的文件流</param>
        /// <param name="originalFileName">原始文件名（保留原始库名/时间信息用于展示）</param>
        /// <param name="operatorName">操作人</param>
        /// <param name="remark">备注</param>
        /// <param name="ct">取消令牌</param>
        public virtual async Task<Result> ImportBackupAsync(
            Stream stream, string originalFileName, string operatorName,
            string? remark = null, CancellationToken ct = default)
        {
            var db = AppGlobal.AppSettings.DbConfig;
            var ext = Path.GetExtension(originalFileName ?? "").ToLowerInvariant();
            if (ext != ".zip" && ext != ".sql")
                return Result.Fail("只接受 .zip（内含 .sql 备份）或 .sql 文件");

            Directory.CreateDirectory(BackupDir);

            var tempDir = Path.Combine(Path.GetTempPath(), $"sb_import_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var uploadPath = Path.Combine(tempDir, $"upload{ext}");

            try
            {
                // 1. 落到临时目录（边写边算 MD5，供记录与完整性校验）
                string sourceMd5;
                long uploadedSize;
                await using (var fs = new FileStream(uploadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    await using var crypto = new System.Security.Cryptography.CryptoStream(
                        fs, md5, System.Security.Cryptography.CryptoStreamMode.Write);
                    await stream.CopyToAsync(crypto, ct);
                    await crypto.FlushFinalBlockAsync(ct);
                    uploadedSize = fs.Length;
                    sourceMd5 = Convert.ToHexString(md5.Hash!);
                }

                if (uploadedSize == 0)
                    return Result.Fail("上传文件为空");

                // 2. 取出 .sql 内容（zip 解压 / 裸 sql 直接用）
                var sqlPath = Path.Combine(tempDir, "content.sql");
                if (ext == ".zip")
                {
                    using var zip = ZipFile.OpenRead(uploadPath);
                    var entry = zip.Entries.FirstOrDefault(e =>
                        !string.IsNullOrEmpty(e.Name) &&
                        e.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase));
                    if (entry == null)
                        return Result.Fail("zip 内未找到 .sql 备份文件，请上传系统导出的备份包");
                    entry.ExtractToFile(sqlPath, overwrite: true);
                }
                else
                {
                    File.Copy(uploadPath, sqlPath, overwrite: true);
                }

                if (!File.Exists(sqlPath) || new FileInfo(sqlPath).Length == 0)
                    return Result.Fail("备份内的 .sql 文件为空");

                // 3. 方言校验（跨库导入必然失败，提前拦截并说明原因）
                var detected = DetectSqlDialect(sqlPath);
                if (detected == "unknown")
                    return Result.Fail("无法识别备份文件的数据库方言，请确认是 mysqldump 或 pg_dump 导出的备份");
                if (!string.Equals(detected, _backupTool.Dialect, StringComparison.OrdinalIgnoreCase))
                    return Result.Fail(
                        $"备份文件是 {detected} 格式，当前数据库是 {_backupTool.Dialect}，" +
                        $"两者 SQL 不兼容，无法恢复。请用 {_backupTool.DumpToolName} 导出的备份。");

                // 4. 以标准命名落盘：<dbName>_<yyyyMMdd_HHmmss>.zip
                // 时间优先取原文件名里的时间戳（保留「备份是什么时候产生的」），取不到再看文件系统时间
                var ts = ParseFromFileName(originalFileName ?? "").Timestamp
                         ?? File.GetLastWriteTime(sqlPath);
                var stamp = ts.ToString("yyyyMMdd_HHmmss");
                var zipFileName = $"{db.Database}_{stamp}.zip";
                var zipFullPath = Path.Combine(BackupDir, zipFileName);

                // 同一秒重复上传 → 加序号，绝不覆盖已有备份（覆盖等于丢数据）
                var seq = 1;
                while (File.Exists(zipFullPath))
                {
                    zipFileName = $"{db.Database}_{stamp}_{seq++}.zip";
                    zipFullPath = Path.Combine(BackupDir, zipFileName);
                }

                var innerSqlName = $"{db.Database}_{stamp}.sql";
                using (var zipOut = ZipFile.Open(zipFullPath, ZipArchiveMode.Create))
                {
                    zipOut.CreateEntryFromFile(sqlPath, innerSqlName, CompressionLevel.Optimal);
                }

                // 5. 重新计算落盘 zip 的 MD5（与上传的源文件不同：可能只是重新打包）
                string zipMd5;
                using (var md5 = System.Security.Cryptography.MD5.Create())
                using (var fs = File.OpenRead(zipFullPath))
                {
                    zipMd5 = Convert.ToHexString(md5.ComputeHash(fs));
                }

                var fi = new FileInfo(zipFullPath);
                var record = new DatabaseBackup
                {
                    DatabaseName = db.Database,
                    FileName = zipFileName,
                    FilePath = $"/storage/db/{zipFileName}",
                    FileSize = fi.Length,
                    Duration = 0,
                    CreatedBy = operatorName,
                    Remark = string.IsNullOrWhiteSpace(remark)
                        ? $"上传导入（原始文件名 {originalFileName}，方言 {detected}）"
                        : remark,
                    Md5 = zipMd5,
                };
                Insert(record);

                _logger.LogInformation(
                    "备份文件导入成功: {FileName}，原始名 {Original}，大小 {Size}bytes，方言 {Dialect}，操作人 {Operator}",
                    zipFileName, originalFileName, fi.Length, detected, operatorName);

                return Result.Success(new
                {
                    record.Id,
                    record.FileName,
                    record.FilePath,
                    record.FileSize,
                    record.CreatedBy,
                    record.CreatedTime,
                    record.Md5,
                    detectedDialect = detected,
                    sourceMd5,
                    message = "备份已导入备份记录，可在列表中点击「恢复」执行还原",
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "备份文件导入异常: {Original}", originalFileName);
                return Result.Fail($"备份导入失败: {ex.Message}");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { /* 临时目录清理失败不影响结果 */ }
            }
        }

        /// <summary>
        /// 识别 SQL 脚本来自哪种工具。只读文件头部若干 KB，足够覆盖注释/初始化语句区。
        /// MySQL: mysqldump 会写 `-- MySQL dump`；PG: pg_dump 写 `-- PostgreSQL database dump` 或 SET/SELECT pg_catalog 语句。
        /// </summary>
        private static string DetectSqlDialect(string sqlPath)
        {
            string head;
            using (var reader = new StreamReader(sqlPath, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                var buffer = new char[16 * 1024];
                var read = reader.ReadBlock(buffer, 0, buffer.Length);
                head = new string(buffer, 0, read);
            }

            if (head.Contains("PostgreSQL database dump", StringComparison.OrdinalIgnoreCase) ||
                head.Contains("pg_catalog", StringComparison.OrdinalIgnoreCase) ||
                head.Contains("SELECT pg_catalog.set_config", StringComparison.OrdinalIgnoreCase))
                return "postgresql";

            if (head.Contains("MySQL dump", StringComparison.OrdinalIgnoreCase) ||
                head.Contains("/*!40101", StringComparison.Ordinal) ||
                head.Contains("LOCK TABLES", StringComparison.OrdinalIgnoreCase) ||
                head.Contains("ENGINE=", StringComparison.OrdinalIgnoreCase))
                return "mysql";

            // 兜底：没有任何方言指纹，交给调用方拒绝
            return "unknown";
        }

        /// <summary>
        /// 完整备份流程（统一入口，定时任务 / Admin 手动 / 访问令牌 API 都调它）：
        ///   1. CreateBackup 本地 dump + zip + insert
        ///   2. 启用了云端备份 → UploadLatestAsync 推到 CloudConfig.ServerUrl 并落 RemoteUpload* 字段
        ///   3. 按 backup_keep_local_count 清理超期本地备份
        /// 任意一步业务失败不抛异常（除非 CreateBackup 本身失败），返回 result.Code 给调用方
        /// </summary>
        public virtual async Task<Result> BackupAndUploadAsync(string createdBy, CancellationToken ct = default)
        {
            // 直接 await CreateBackupAsync,IBackupTool.DumpAsync 内部已经全部走 async Process API
            // (WaitForExitAsync / ReadToEndAsync),不再有同步阻塞,无需 Task.Run wrapper。
            var result = await CreateBackupAsync(createdBy, null, ct);
            if (result.Code != 0) return result;

            await TryUploadLatestAsync(ct);
            TryCleanupOldBackups();
            return result;
        }

        /// <summary>
        /// 本地最新一条备份按系统设置自动上传到云端（启用 + 配了 URL 才传）
        /// 上传失败不影响本地备份，写 RemoteUploadStatus=failed 让用户能在 UI 重试
        /// </summary>
        public async Task TryUploadLatestAsync(CancellationToken ct = default)
        {
            var enabledSetting = _fsql.Select<SystemSetting>()
                .Where(s => s.Key == "backup_upload_enabled").First();
            var enabled = enabledSetting?.DataContent != null &&
                (enabledSetting.DataContent.Equals("true", StringComparison.OrdinalIgnoreCase)
                 || enabledSetting.DataContent == "1");
            if (!enabled) return;

            var url = AppGlobal.AppSettings?.GetBackupUploadUrl() ?? "";
            if (string.IsNullOrWhiteSpace(url)) return;

            var latest = _fsql.Select<DatabaseBackup>().OrderByDescending(c => c.Id).First();
            if (latest == null) return;

            var (ok, error) = await _uploader.UploadAsync(url, latest, ct);
            if (ok)
            {
                _fsql.Update<DatabaseBackup>()
                    .Set(c => c.RemoteUploadStatus, "success")
                    .Set(c => c.RemoteUploadTime, AppTime.UtcNow)
                    .Set(c => c.RemoteUploadUrl, url)
                    .Set(c => c.RemoteUploadError, null)
                    .Where(c => c.Id == latest.Id).ExecuteAffrows();
            }
            else
            {
                _fsql.Update<DatabaseBackup>()
                    .Set(c => c.RemoteUploadStatus, "failed")
                    .Set(c => c.RemoteUploadError, error)
                    .Set(c => c.RemoteUploadUrl, url)
                    .Where(c => c.Id == latest.Id).ExecuteAffrows();
            }
        }

        /// <summary>
        /// 按系统设置 backup_keep_local_count 清理超期本地备份（0 = 不清）
        /// 注：拉全部到内存再 Skip 而不是 fsql.Skip，绕开 FreeSql 在 MySQL 上 LIMIT -1 的语法 bug
        /// </summary>
        public void TryCleanupOldBackups()
        {
            var setting = _fsql.Select<SystemSetting>()
                .Where(s => s.Key == "backup_keep_local_count").First();
            var keepCount = setting?.DataContent != null && int.TryParse(setting.DataContent, out var n) ? n : 14;
            if (keepCount <= 0) return;

            var allOrdered = _fsql.Select<DatabaseBackup>()
                .OrderByDescending(c => c.CreatedTime)
                .ToList();
            foreach (var d in allOrdered.Skip(keepCount))
            {
                Del(d.Id, d.FilePath);
            }
        }

        /// <summary>
        /// 检查备份工具是否可用 + 返回数据库详细信息(版本/字符集/表数/大小/行数/最近备份时间)。
        /// 工具选取按 DbConfig.DbType:MySQL 用 mysqldump/mysql,PostgreSQL 用 pg_dump/psql。
        ///
        /// 返回 Result 字段:
        ///   - 新名:dumpToolName / restoreToolName / dumpToolAvailable / restoreToolAvailable / dumpToolVersion
        ///   - 旧名:available / mysqlClientAvailable / version(保留兼容旧前端调用,语义跟新名一致)
        /// </summary>
        public virtual async Task<Result> CheckBackupToolAsync(CancellationToken ct = default)
        {
            var db = AppGlobal.AppSettings.DbConfig;

            var (dumpAvailable, dumpVersion) = await _backupTool.CheckDumpToolAsync(ct);
            var restoreAvailable = await _backupTool.CheckRestoreToolAsync(ct);
            var meta = _backupTool.GetMetaInfo(_fsql, db);

            string? lastBackupTime = null;
            try
            {
                var latest = Select.OrderByDescending(r => r.CreatedTime).First();
                if (latest != null)
                    lastBackupTime = latest.CreatedTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch { /* 备份表不存在或异常时忽略 */ }

            return Result.Success(new
            {
                dbType = db.DbType,                       // 配置的类型 "MySql"/"PostgreSql"
                dialect = _fsql.Ado.DataType.ToString(),  // FreeSql 实际方言
                server = $"{db.Server}:{db.Port}",
                database = db.Database,
                // 新字段(跟具体 DB 解耦)
                dumpToolName = _backupTool.DumpToolName,
                restoreToolName = _backupTool.RestoreToolName,
                dumpToolAvailable = dumpAvailable,
                restoreToolAvailable = restoreAvailable,
                dumpToolVersion = dumpVersion,
                // 旧字段(向后兼容,语义跟新名一致;旧前端不改也能跑)
                available = dumpAvailable,
                version = dumpVersion,
                mysqlClientAvailable = restoreAvailable,
                serverVersion = meta.ServerVersion,
                charSet = meta.CharSet,
                collation = meta.Collation,
                tableCount = meta.TableCount,
                totalSize = meta.TotalSize,
                totalRows = meta.TotalRows,
                lastBackupTime,
            });
        }

        /// <summary>
        /// 备份历史列表（DB 记录 + 磁盘文件 双源合并）
        /// 每条记录附带 source 字段标识来源:
        ///  - both: DB 记录存在 + 磁盘文件也存在（正常）
        ///  - db_only: DB 有记录但磁盘文件丢失（异常）
        ///  - disk_only: 磁盘有 zip 但 DB 没记录（恢复后常见）
        /// 数据库恢复会把 SB_DatabaseBackup 表本身回滚，因此这里不做
        /// 自动补全，而是显式呈现状态让用户决定。
        /// </summary>
        public virtual Result GetList(string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            // 1) 扫磁盘上现有的 zip 文件
            var diskByPath = ScanDiskFiles();

            // 2) 拉所有 DB 记录
            var dbRecords = Select.OrderByDescending(r => r.CreatedTime).ToList();

            var merged = new List<object>();
            var seenDiskPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // DB 记录：判断对应磁盘文件是否还在
            foreach (var rec in dbRecords)
            {
                var inDisk = diskByPath.ContainsKey(rec.FilePath);
                if (inDisk)
                {
                    seenDiskPaths.Add(rec.FilePath);
                    var fi = diskByPath[rec.FilePath];
                    merged.Add(new
                    {
                        rec.Id,
                        rec.DatabaseName,
                        rec.FileName,
                        rec.FilePath,
                        FileSize = fi.Length, // 用磁盘真实大小
                        rec.Duration,
                        rec.CreatedBy,
                        rec.CreatedTime,
                        rec.Remark,
                        rec.Md5,
                        rec.RemoteUploadStatus,
                        rec.RemoteUploadTime,
                        rec.RemoteUploadError,
                        rec.RemoteUploadUrl,
                        source = "both",
                    });
                }
                else
                {
                    merged.Add(new
                    {
                        rec.Id,
                        rec.DatabaseName,
                        rec.FileName,
                        rec.FilePath,
                        rec.FileSize,
                        rec.Duration,
                        rec.CreatedBy,
                        rec.CreatedTime,
                        rec.Remark,
                        rec.Md5,
                        rec.RemoteUploadStatus,
                        rec.RemoteUploadTime,
                        rec.RemoteUploadError,
                        rec.RemoteUploadUrl,
                        source = "db_only",
                    });
                }
            }

            // 磁盘文件中没有对应 DB 记录的 → disk_only
            foreach (var (relPath, fi) in diskByPath)
            {
                if (seenDiskPaths.Contains(relPath)) continue;

                var (dbName, ts) = ParseFromFileName(fi.Name);
                merged.Add(new
                {
                    Id = 0, // disk_only 没有 DB 记录，用 0 标识
                    DatabaseName = dbName ?? "未知",
                    FileName = fi.Name,
                    FilePath = relPath,
                    FileSize = fi.Length,
                    Duration = 0,
                    CreatedBy = "(无记录)",
                    CreatedTime = ts ?? fi.LastWriteTime,
                    Remark = (string?)null,
                    Md5 = (string?)null,
                    RemoteUploadStatus = (string?)null,
                    RemoteUploadTime = (DateTime?)null,
                    RemoteUploadError = (string?)null,
                    RemoteUploadUrl = (string?)null,
                    source = "disk_only",
                });
            }

            // 3) 排序 + 分页
            var sorted = SortMerged(merged, field, order);
            var total = sorted.Count;
            var skip = Math.Max(0, (page - 1) * pageSize);
            var items = sorted.Skip(skip).Take(pageSize).ToList();

            return Result.Page(items, total);
        }

        /// <summary>
        /// 扫描备份目录，返回 <相对路径, FileInfo> 字典
        /// </summary>
        private static Dictionary<string, FileInfo> ScanDiskFiles()
        {
            var result = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(BackupDir)) return result;

            foreach (var p in Directory.GetFiles(BackupDir, "*.zip"))
            {
                var fi = new FileInfo(p);
                var relativePath = $"/storage/db/{fi.Name}";
                result[relativePath] = fi;
            }
            return result;
        }

        /// <summary>
        /// 合并后的列表排序：默认按 CreatedTime desc
        /// </summary>
        private static List<object> SortMerged(List<object> items, string field, string order)
        {
            // 实体使用反射读 CreatedTime 字段；其他排序需求暂时只支持时间倒序
            // 重要：DB 记录的 CreatedTime 是 UTC（kind=Utc，带 Z），disk_only 解析自文件名是本地无 kind，
            // 不统一转 UTC 直接比 ticks 会出错（本地 16:00 被当 UTC 16:00 排到 UTC 15:23 前）
            var asc = string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase);
            DateTime KeyFn(object x)
            {
                var prop = x.GetType().GetProperty("CreatedTime");
                if (prop?.GetValue(x) is not DateTime dt) return DateTime.MinValue;
                // kind=Utc 不变, Local 转 UTC, Unspecified 按系统 local 转 UTC
                return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
            }
            return asc
                ? items.OrderBy(KeyFn).ToList()
                : items.OrderByDescending(KeyFn).ToList();
        }

        /// <summary>
        /// 文件名格式 "<dbName>_yyyyMMdd_HHmmss.zip" 的解析正则
        /// </summary>
        private static readonly Regex BackupNameRegex =
            new(@"^(?<db>.+?)_(?<date>\d{8})_(?<time>\d{6})\.zip$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 从文件名解析数据库名 + 时间戳
        /// </summary>
        private static (string? DbName, DateTime? Timestamp) ParseFromFileName(string fileName)
        {
            var match = BackupNameRegex.Match(fileName);
            if (!match.Success) return (null, null);

            var dbName = match.Groups["db"].Value;
            var combined = $"{match.Groups["date"].Value}{match.Groups["time"].Value}";
            DateTime? ts = DateTime.TryParseExact(
                combined,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)
                ? parsed
                : null;
            return (dbName, ts);
        }

        /// <summary>
        /// 执行数据库备份。按 DbConfig.DbType 走 IBackupTool 对应实现(MySQL=mysqldump / PG=pg_dump)。
        /// </summary>
        public virtual async Task<Result> CreateBackupAsync(string createdBy, string? remark = null, CancellationToken ct = default)
        {
            var db = AppGlobal.AppSettings.DbConfig;
            var timestamp = AppTime.Now.ToString("yyyyMMdd_HHmmss");
            var sqlFileName = $"{db.Database}_{timestamp}.sql";
            var zipFileName = $"{db.Database}_{timestamp}.zip";

            // 确保目录存在
            Directory.CreateDirectory(BackupDir);

            var sqlFilePath = Path.Combine(BackupDir, sqlFileName);
            var zipFilePath = Path.Combine(BackupDir, zipFileName);

            var sw = Stopwatch.StartNew();

            try
            {
                // 1. 执行 dump(MySQL=mysqldump / PG=pg_dump)
                var (ok, stderr) = await _backupTool.DumpAsync(db, sqlFilePath, ct);
                if (!ok)
                {
                    if (File.Exists(sqlFilePath)) File.Delete(sqlFilePath);
                    _logger.LogError("{Tool} 失败: {Error}", _backupTool.DumpToolName, stderr);
                    return Result.Fail($"{_backupTool.DumpToolName} 执行失败: {stderr}");
                }

                // 2. 压缩为 zip
                if (File.Exists(zipFilePath)) File.Delete(zipFilePath);
                using (var zip = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(sqlFilePath, sqlFileName, CompressionLevel.Optimal);
                }

                // 3. 删除原始 sql 文件
                File.Delete(sqlFilePath);

                sw.Stop();

                var fileInfo = new FileInfo(zipFilePath);
                var relativePath = $"/storage/db/{zipFileName}";

                // 计算 md5（文件完整性校验 + 客户端云端去重用）
                string md5Hex;
                using (var md5 = System.Security.Cryptography.MD5.Create())
                using (var stream = File.OpenRead(zipFilePath))
                {
                    md5Hex = Convert.ToHexString(md5.ComputeHash(stream));
                }

                // 4. 写入数据库记录
                var record = new DatabaseBackup
                {
                    DatabaseName = db.Database,
                    FileName = zipFileName,
                    FilePath = relativePath,
                    FileSize = fileInfo.Length,
                    Duration = (int)sw.ElapsedMilliseconds,
                    CreatedBy = createdBy,
                    Remark = remark,
                    Md5 = md5Hex,
                };
                Insert(record);

                _logger.LogInformation("数据库备份成功: {FileName}, 大小: {Size}bytes, 耗时: {Duration}ms, 操作人: {CreatedBy}",
                    zipFileName, fileInfo.Length, sw.ElapsedMilliseconds, createdBy);

                return Result.Success(new
                {
                    record.Id,
                    record.FileName,
                    record.FilePath,
                    record.FileSize,
                    record.Duration,
                    record.CreatedBy,
                    record.CreatedTime
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                // 清理临时文件
                if (File.Exists(sqlFilePath)) File.Delete(sqlFilePath);
                if (File.Exists(zipFilePath)) File.Delete(zipFilePath);
                _logger.LogError(ex, "数据库备份异常");
                return Result.Fail($"备份失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 生成本次导入实际使用的 sql 文件。
        /// - PostgreSQL: pg_dump 用 --clean --if-exists 导出，脚本自带 DROP，直接原样导入。
        /// - MySQL: mysqldump 不生成 DROP TABLE，直接导入已有同名表会主键冲突/整批失败。
        ///   这里把脚本头的 DROP 语句换成「先查 information_schema 动态删掉当前库所有表」，
        ///   从而无论备份来自哪个版本、哪些表，都能干净覆盖当前库。
        /// 返回的路径可能与入参不同（MySQL 情况会写临时文件），调用方负责删除。
        /// </summary>
        private async Task<(string Path, bool Temp)> PrepareSqlForRestoreAsync(string sqlPath, string tempDir, CancellationToken ct)
        {
            if (_backupTool.Dialect != "mysql") return (sqlPath, false);

            var content = await File.ReadAllTextAsync(sqlPath, ct);
            // 备份脚本自身已经带 DROP TABLE（例如手工导出的 --add-drop-table）就不重复注入
            if (content.Contains("DROP TABLE IF EXISTS", StringComparison.OrdinalIgnoreCase))
                return (sqlPath, false);

            var db = AppGlobal.AppSettings.DbConfig;
            var prefix = string.Join(Environment.NewLine, new[]
            {
                "-- SB Framework: 导入前清空当前库所有表（外来备份可能不含 DROP TABLE）",
                "SET FOREIGN_KEY_CHECKS=0;",
                "SET @sb_drop = NULL;",
                "SELECT GROUP_CONCAT(CONCAT('DROP TABLE IF EXISTS `', table_name, '`;') SEPARATOR ' ')",
                $"  INTO @sb_drop FROM information_schema.tables WHERE table_schema = '{db.Database.Replace("'", "''")}';",
                "SET @sb_drop = IFNULL(@sb_drop, 'SELECT 1;');",
                "PREPARE sb_stmt FROM @sb_drop;",
                "EXECUTE sb_stmt;",
                "DEALLOCATE PREPARE sb_stmt;",
                "SET FOREIGN_KEY_CHECKS=1;",
                "",
            });

            var prepared = Path.Combine(tempDir, "restore_prepared.sql");
            await File.WriteAllTextAsync(prepared, prefix + content, ct);
            _logger.LogInformation("MySQL 导入前已注入清表语句（原脚本无 DROP TABLE）");
            return (prepared, true);
        }

        /// <summary>
        /// 试运行校验（只读，不碰数据库）：上传后的备份在真正恢复之前先确认「能解开、方言对、脚本非空」。
        /// 之所以需要：恢复是覆盖整库的高危操作，等到恢复那一刻才发现文件是坏的/方言不对，
        /// 此时自动安全备份已经跑完，用户白等一轮。提前校验能把问题挡在上传阶段。
        /// </summary>
        public virtual async Task<Result> ValidateBackupFileAsync(int id, CancellationToken ct = default)
        {
            var entity = Select.Where(c => c.Id == id).First();
            if (entity == null) return Result.Fail("备份记录不存在");

            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot",
                entity.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath)) return Result.Fail("备份文件不存在");

            var tempDir = Path.Combine(Path.GetTempPath(), $"sb_validate_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string sqlPath;
                using (var zip = ZipFile.OpenRead(fullPath))
                {
                    var entry = zip.Entries.FirstOrDefault(e =>
                        !string.IsNullOrEmpty(e.Name) &&
                        e.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase));
                    if (entry == null) return Result.Fail("备份 zip 内未找到 .sql 文件");
                    sqlPath = Path.Combine(tempDir, entry.Name);
                    entry.ExtractToFile(sqlPath, overwrite: true);
                }

                var size = new FileInfo(sqlPath).Length;
                if (size == 0) return Result.Fail("备份内的 .sql 文件为空");

                var detected = DetectSqlDialect(sqlPath);
                if (detected == "unknown") return Result.Fail("无法识别备份的数据库方言");
                if (!string.Equals(detected, _backupTool.Dialect, StringComparison.OrdinalIgnoreCase))
                    return Result.Fail($"备份是 {detected} 格式，当前数据库是 {_backupTool.Dialect}，不兼容");

                var restoreToolOk = await _backupTool.CheckRestoreToolAsync(ct);
                return Result.Success(new
                {
                    id,
                    fileName = entity.FileName,
                    sqlSize = size,
                    dialect = detected,
                    restoreToolName = _backupTool.RestoreToolName,
                    restoreToolAvailable = restoreToolOk,
                    message = "校验通过，可以执行恢复",
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "备份文件校验异常: {Id}", id);
                return Result.Fail($"校验失败: {ex.Message}");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// 从备份恢复数据库（支持 DB 记录或 disk_only 文件）
        /// 流程：先自动备份当前库（用作回退）→ 解压 zip 取 sql → mysql 客户端导入
        /// 注意：此操作覆盖目标库全部数据，调用方需做二次确认
        /// </summary>
        /// <param name="id">DB 记录 id；id == 0 表示按 filePath 走 disk_only 路径</param>
        /// <param name="filePath">磁盘文件相对路径（如 /storage/db/Saibo_xxx.zip），id == 0 时必填</param>
        public virtual async Task<Result> RestoreAsync(int id, string operatorName, string? filePath = null, CancellationToken ct = default)
        {
            string fileName;
            string fullPath;

            if (id > 0)
            {
                var entity = Select.Where(c => c.Id == id).First();
                if (entity == null)
                    return Result.Fail("备份记录不存在");
                filePath = entity.FilePath;
                fileName = entity.FileName;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return Result.Fail("disk_only 备份必须提供 filePath");
                fileName = Path.GetFileName(filePath);
            }

            fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot",
                filePath!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
                return Result.Fail("备份文件不存在，无法恢复");

            // 1. 检查 restore 工具可用性(MySQL=mysql / PG=psql)
            if (!await _backupTool.CheckRestoreToolAsync(ct))
                return Result.Fail($"{_backupTool.RestoreToolName} 客户端不可用,无法恢复");

            // 2. 恢复前自动备份当前数据库(用作回退)
            var safetyTag = id > 0 ? $"#{id} {fileName}" : $"磁盘文件 {fileName}";
            var safety = await CreateBackupAsync($"{operatorName} (恢复前自动备份)",
                $"恢复 {safetyTag} 之前的安全备份", ct);
            if (safety.Code != 0)
            {
                _logger.LogError("恢复前自动备份失败，中止恢复");
                return Result.Fail($"恢复前自动备份失败，已中止: {safety.Message}");
            }

            // 3. 解压 zip 拿 sql 文件到临时目录
            var tempDir = Path.Combine(Path.GetTempPath(), $"sb_restore_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string? sqlPath = null;

            try
            {
                using (var zip = ZipFile.OpenRead(fullPath))
                {
                    var entryFile = zip.Entries.FirstOrDefault(e =>
                        !string.IsNullOrEmpty(e.Name) &&
                        e.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase));
                    if (entryFile == null)
                        return Result.Fail("备份 zip 内未找到 .sql 文件");

                    sqlPath = Path.Combine(tempDir, entryFile.Name);
                    entryFile.ExtractToFile(sqlPath, overwrite: true);
                }

                // 4. 导入(MySQL=mysql / PG=psql)
                //    MySQL 备份不含 DROP TABLE，这里补一层清表前缀，保证覆盖当前库而不是撞主键
                var db = AppGlobal.AppSettings.DbConfig;
                var (effectiveSql, _) = await PrepareSqlForRestoreAsync(sqlPath!, tempDir, ct);
                var sw = Stopwatch.StartNew();
                var (ok, stderr) = await _backupTool.RestoreAsync(db, effectiveSql, ct);
                sw.Stop();

                if (!ok)
                {
                    _logger.LogError("数据库恢复失败: {Error}", stderr);
                    return Result.Fail($"{_backupTool.RestoreToolName} 导入失败: {stderr}");
                }

                _logger.LogWarning(
                    "数据库恢复成功: 从备份 {Tag}，耗时 {Duration}ms，操作人 {Operator}",
                    safetyTag, sw.ElapsedMilliseconds, operatorName);

                return Result.Success(new
                {
                    duration = (int)sw.ElapsedMilliseconds,
                    fromBackupId = id,
                    fromFileName = fileName,
                    safetyBackupMessage = "恢复前已自动备份当前数据库，可在备份记录中查看",
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据库恢复异常");
                return Result.Fail($"恢复失败: {ex.Message}");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { /* 临时目录清理失败不影响结果 */ }
            }
        }

        /// <summary>
        /// 删除备份（文件 + 数据库记录），支持三种状态
        /// - both: 删 DB 记录 + 磁盘文件
        /// - db_only: 只删 DB 记录（文件已不在）
        /// - disk_only: 只删磁盘文件（无记录），需要传 filePath
        /// </summary>
        public override Result Del(int id) => DelInternal(id, null);

        /// <summary>
        /// 带 filePath 的删除（兼容 disk_only 文件）
        /// </summary>
        public virtual Result Del(int id, string? filePath)
            => DelInternal(id, filePath);

        private Result DelInternal(int id, string? filePath)
        {
            string? targetPath = filePath;

            if (id > 0)
            {
                var entity = Select.Where(c => c.Id == id).First();
                if (entity == null && string.IsNullOrWhiteSpace(targetPath))
                    return Result.Fail("备份记录不存在!");
                if (entity != null) targetPath = entity.FilePath;
                Delete(c => c.Id == id);
            }
            else if (string.IsNullOrWhiteSpace(targetPath))
            {
                return Result.Fail("disk_only 删除必须提供 filePath");
            }

            // 删除磁盘文件（如果还在）
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot",
                    targetPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath))
                {
                    try { File.Delete(fullPath); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "删除磁盘文件失败: {Path}", fullPath);
                    }
                }
            }

            return Result.Success("删除成功!");
        }
    }
}
