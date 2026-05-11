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

        private static readonly string BackupDir =
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "storage", "db");

        public DatabaseBackupService(IFreeSql fsql, ILogger<DatabaseBackupService> logger, BackupUploader uploader)
            : base(fsql, logger)
        {
            _fsql = fsql;
            _uploader = uploader;
        }

        /// <summary>
        /// 完整备份流程（统一入口，定时任务 / Admin 手动 / MasterKey API 都调它）：
        ///   1. CreateBackup 本地 dump + zip + insert
        ///   2. 启用了云端备份 → UploadLatestAsync 推到 CloudConfig.ServerUrl 并落 RemoteUpload* 字段
        ///   3. 按 backup_keep_local_count 清理超期本地备份
        /// 任意一步业务失败不抛异常（除非 CreateBackup 本身失败），返回 result.Code 给调用方
        /// </summary>
        public virtual async Task<Result> BackupAndUploadAsync(string createdBy, CancellationToken ct = default)
        {
            var result = CreateBackup(createdBy);
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
        /// 检查 mysqldump 是否可用 + 返回数据库详细信息
        /// 包含：服务器版本/字符集/表数量/总大小/总行数/最近备份时间 等
        /// </summary>
        public virtual Result CheckMysqldump()
        {
            var db = AppGlobal.AppSettings.DbConfig;

            // 1) 检查 mysqldump 客户端
            bool mysqldumpAvailable = false;
            string mysqldumpVersion = "";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "mysqldump",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                mysqldumpVersion = proc!.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                mysqldumpAvailable = proc.ExitCode == 0;
            }
            catch
            {
                mysqldumpAvailable = false;
            }

            // 2) 检查 mysql 客户端（恢复用）
            bool mysqlAvailable = false;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "mysql",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                proc!.WaitForExit();
                mysqlAvailable = proc.ExitCode == 0;
            }
            catch
            {
                mysqlAvailable = false;
            }

            // 3) 查询数据库元信息
            string serverVersion = "";
            string charSet = "";
            string collation = "";
            long tableCount = 0;
            long totalSize = 0;
            long totalRows = 0;
            string? lastBackupTime = null;

            try
            {
                serverVersion = (_fsql.Ado.ExecuteScalar("SELECT VERSION()")?.ToString() ?? "").Trim();

                using var dt = _fsql.Ado.ExecuteDataTable(
                    "SELECT DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME " +
                    "FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = ?dbName",
                    new { dbName = db.Database });
                if (dt.Rows.Count > 0)
                {
                    charSet = dt.Rows[0][0]?.ToString() ?? "";
                    collation = dt.Rows[0][1]?.ToString() ?? "";
                }

                using var stat = _fsql.Ado.ExecuteDataTable(
                    "SELECT COUNT(*) AS table_count, " +
                    "IFNULL(SUM(data_length + index_length), 0) AS total_size, " +
                    "IFNULL(SUM(table_rows), 0) AS total_rows " +
                    "FROM information_schema.TABLES WHERE TABLE_SCHEMA = ?dbName",
                    new { dbName = db.Database });
                if (stat.Rows.Count > 0)
                {
                    tableCount = Convert.ToInt64(stat.Rows[0]["table_count"]);
                    totalSize = Convert.ToInt64(stat.Rows[0]["total_size"]);
                    totalRows = Convert.ToInt64(stat.Rows[0]["total_rows"]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "查询数据库元信息失败");
            }

            // 4) 最近一次备份时间
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
                available = mysqldumpAvailable,
                version = mysqldumpVersion,
                mysqlClientAvailable = mysqlAvailable,
                serverVersion,
                charSet,
                collation,
                tableCount,
                totalSize,
                totalRows,
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
        /// 执行数据库备份
        /// </summary>
        public virtual Result CreateBackup(string createdBy, string? remark = null)
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
                // 1. 执行 mysqldump
                var args = $"--host={db.Server} --port={db.Port} --user={db.User} " +
                           $"--single-transaction --routines --triggers --set-gtid-purged=OFF " +
                           $"--result-file=\"{sqlFilePath}\" {db.Database}";

                var psi = new ProcessStartInfo
                {
                    FileName = "mysqldump",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                // 通过环境变量传递密码，避免命令行暴露
                psi.Environment["MYSQL_PWD"] = db.Password;

                using var proc = Process.Start(psi)!;
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                {
                    // 清理临时文件
                    if (File.Exists(sqlFilePath)) File.Delete(sqlFilePath);
                    _logger.LogError("mysqldump 失败: {Error}", stderr);
                    return Result.Fail($"mysqldump 执行失败: {stderr}");
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
        /// 从备份恢复数据库（支持 DB 记录或 disk_only 文件）
        /// 流程：先自动备份当前库（用作回退）→ 解压 zip 取 sql → mysql 客户端导入
        /// 注意：此操作覆盖目标库全部数据，调用方需做二次确认
        /// </summary>
        /// <param name="id">DB 记录 id；id == 0 表示按 filePath 走 disk_only 路径</param>
        /// <param name="filePath">磁盘文件相对路径（如 /storage/db/Saibo_xxx.zip），id == 0 时必填</param>
        public virtual Result Restore(int id, string operatorName, string? filePath = null)
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

            // 1. 检查 mysql 客户端可用性
            try
            {
                var versionPsi = new ProcessStartInfo
                {
                    FileName = "mysql",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var versionProc = Process.Start(versionPsi);
                versionProc!.WaitForExit();
                if (versionProc.ExitCode != 0)
                    return Result.Fail("mysql 客户端不可用，无法恢复");
            }
            catch
            {
                return Result.Fail("mysql 客户端未安装，无法恢复");
            }

            // 2. 恢复前自动备份当前数据库（用作回退）
            var safetyTag = id > 0 ? $"#{id} {fileName}" : $"磁盘文件 {fileName}";
            var safety = CreateBackup($"{operatorName} (恢复前自动备份)",
                $"恢复 {safetyTag} 之前的安全备份");
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

                // 4. mysql 导入
                var db = AppGlobal.AppSettings.DbConfig;
                var sw = Stopwatch.StartNew();
                var args = $"--host={db.Server} --port={db.Port} --user={db.User} {db.Database}";

                var psi = new ProcessStartInfo
                {
                    FileName = "mysql",
                    Arguments = args,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.Environment["MYSQL_PWD"] = db.Password;

                using var proc = Process.Start(psi)!;
                using (var fs = File.OpenRead(sqlPath))
                {
                    fs.CopyTo(proc.StandardInput.BaseStream);
                }
                proc.StandardInput.Close();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                sw.Stop();

                if (proc.ExitCode != 0)
                {
                    _logger.LogError("数据库恢复失败: {Error}", stderr);
                    return Result.Fail($"mysql 导入失败: {stderr}");
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
