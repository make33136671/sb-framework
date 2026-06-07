using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SB.Framework.Core;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using System.Text.RegularExpressions;

namespace SB.Feature.BackupReceive
{
    /// <summary>
    /// 云端备份接收服务（服务端角色）
    /// 接收客户端 multipart 上传 → 落盘到 {StoragePath} → 入库 BackupRemoteRecord
    /// 不暴露删除接口（防黑客通过应用层删云端备份；运维通过文件系统 / 服务端 admin 后台才能删）
    /// </summary>
    [AutoDependencyInjection]
    public class BackupReceiveService : BaseCrudService<BackupRemoteRecord>
    {
        private readonly IFreeSql _fsql;
        private readonly IHostEnvironment _env;
        private readonly ILogger<BackupReceiveService> _logger;
        private readonly BackupReceiveOptions _opt;

        public BackupReceiveService(IFreeSql fsql, ILogger<BackupReceiveService> logger, IHostEnvironment env, IOptions<BackupReceiveOptions> opt)
            : base(fsql, logger)
        {
            _fsql = fsql;
            _env = env;
            _logger = logger;
            _opt = opt.Value;
        }

        /// <summary>
        /// 接收上传：解码 logo + 落盘备份文件 + 入库
        /// </summary>
        public async Task<Result> SaveUploadAsync(
            IFormFile file,
            string productEn,
            string productCn,
            string? primaryDomain,
            string? logoBase64,
            string? backupUtcTime,
            string? md5,
            string clientIp,
            CancellationToken ct)
        {
            if (file == null || file.Length == 0)
                return Result.Fail("上传文件不能为空");
            if (string.IsNullOrWhiteSpace(productEn))
                return Result.Fail("productEn 不能为空");

            // 净化路径段，避免目录穿越
            var safeProductEn = SanitizePathSegment(productEn);
            var safeIp = SanitizePathSegment(clientIp);
            var safeFileName = SanitizePathSegment(Path.GetFileName(file.FileName));
            if (string.IsNullOrEmpty(safeFileName))
                safeFileName = $"backup_{AppTime.Now:yyyyMMddHHmmss}.zip";

            // 备份文件落盘 {StoragePath}/{productEn}/{ip}/{filename}
            var remoteBackupDir = Path.Combine(_env.ContentRootPath, _opt.StoragePath,
                safeProductEn, safeIp);
            Directory.CreateDirectory(remoteBackupDir);
            var diskPath = Path.Combine(remoteBackupDir, safeFileName);
            // 同名文件已存在 → 加时间戳后缀，确保只增不覆盖
            if (File.Exists(diskPath))
            {
                var stem = Path.GetFileNameWithoutExtension(safeFileName);
                var ext = Path.GetExtension(safeFileName);
                safeFileName = $"{stem}_{AppTime.Now:HHmmssfff}{ext}";
                diskPath = Path.Combine(remoteBackupDir, safeFileName);
            }
            using (var fs = File.Create(diskPath))
            {
                await file.CopyToAsync(fs, ct);
            }
            var relativePath = $"/storage/remote-backups/{safeProductEn}/{safeIp}/{safeFileName}";

            // Logo 落盘 wwwroot/storage/remote-logos/{productEn}.png（同 productEn 覆盖最新）
            string? logoRelPath = null;
            if (!string.IsNullOrWhiteSpace(logoBase64))
            {
                try
                {
                    var b64 = logoBase64;
                    var commaIdx = b64.IndexOf(',');
                    if (commaIdx > 0 && b64.StartsWith("data:")) b64 = b64[(commaIdx + 1)..];
                    var logoBytes = Convert.FromBase64String(b64);
                    var logoDir = Path.Combine(_env.ContentRootPath, "wwwroot", "storage", "remote-logos");
                    Directory.CreateDirectory(logoDir);
                    var logoDisk = Path.Combine(logoDir, $"{safeProductEn}.png");
                    await File.WriteAllBytesAsync(logoDisk, logoBytes, ct);
                    logoRelPath = $"/storage/remote-logos/{safeProductEn}.png";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "BackupReceive: logo 解码/写盘失败，跳过 logo 但接收备份");
                }
            }

            // 解析客户端备份时间（UTC）
            DateTime backupTime;
            if (!DateTime.TryParse(backupUtcTime, out backupTime))
                backupTime = AppTime.UtcNow;
            else
                backupTime = backupTime.ToUniversalTime();

            var record = new BackupRemoteRecord
            {
                ProductEn = productEn,
                ProductCn = productCn ?? "",
                PrimaryDomain = string.IsNullOrWhiteSpace(primaryDomain) ? null : primaryDomain,
                LogoPath = logoRelPath,
                ClientIp = clientIp,
                FileName = safeFileName,
                FilePath = relativePath,
                FileSize = file.Length,
                Md5 = md5,
                BackupTime = backupTime,
            };
            Insert(record);

            _logger.LogInformation("BackupReceive: 接收备份 {Product} from {Ip} ({Size} bytes) → {Path}",
                productEn, clientIp, file.Length, relativePath);

            return Result.Success(new { record.Id, record.FilePath, record.FileSize });
        }

        /// <summary>按产品聚合：每行一个产品 + 备份次数 / 总大小 / 最近备份时间 / Logo</summary>
        public Result GetProductList()
        {
            // 同一 productEn 可能多次上传不同 logo / productCn，简化按 productEn 分组，
            // 详情字段（logo, productCn, ip 列表）用聚合后再补一次查询取最新一条
            var grouped = _fsql.Select<BackupRemoteRecord>()
                .GroupBy(c => c.ProductEn)
                .ToList(g => new
                {
                    productEn = g.Key,
                    backupCount = g.Count(),
                    totalSize = g.Sum(g.Value.FileSize),
                    latestBackupTime = g.Max(g.Value.BackupTime),
                    latestReceivedTime = g.Max(g.Value.ReceivedTime),
                });

            // 对每个产品取最新一条记录拿 logo / productCn / ip 列表
            var data = grouped.Select(g =>
            {
                var latest = _fsql.Select<BackupRemoteRecord>()
                    .Where(c => c.ProductEn == g.productEn)
                    .OrderByDescending(c => c.ReceivedTime)
                    .First();
                var clientIps = _fsql.Select<BackupRemoteRecord>()
                    .Where(c => c.ProductEn == g.productEn)
                    .GroupBy(c => c.ClientIp)
                    .ToList(x => x.Key);
                return new
                {
                    g.productEn,
                    productCn = latest?.ProductCn ?? "",
                    logoPath = latest?.LogoPath,
                    primaryDomain = latest?.PrimaryDomain,
                    g.backupCount,
                    g.totalSize,
                    g.latestBackupTime,
                    g.latestReceivedTime,
                    clientIps,
                };
            }).ToList();

            return Result.Success(data);
        }

        /// <summary>单产品的备份记录列表（分页，按接收时间倒序）</summary>
        public Result GetRecordList(string productEn, int page = 1, int pageSize = 20)
        {
            var data = _fsql.Select<BackupRemoteRecord>()
                .Where(c => c.ProductEn == productEn)
                .OrderByDescending(c => c.ReceivedTime)
                .Count(out var total)
                .Page(page, pageSize)
                .ToList();
            return Result.Page(data, total);
        }

        /// <summary>按 Id 取单条（供下载接口拿文件路径）</summary>
        public BackupRemoteRecord? GetById(int id)
        {
            return _fsql.Select<BackupRemoteRecord>().Where(c => c.Id == id).First();
        }

        /// <summary>
        /// 服务端管理员手动删除一份云端备份（连同磁盘文件）
        /// **重要：本方法只能由 [Authorize JwtAdmin] 接口调用，绝不能给客户端 Anonymous 上传通道暴露删除入口**
        /// 文件删除失败仍删 DB 记录（运维用户自己清理孤儿文件）
        /// </summary>
        public Result DeleteByAdmin(int id)
        {
            var record = _fsql.Select<BackupRemoteRecord>().Where(c => c.Id == id).First();
            if (record == null) return Result.Fail("备份记录不存在");

            // 删磁盘文件（失败不影响 DB 删除）
            try
            {
                var diskPath = Path.Combine(_env.ContentRootPath, "wwwroot",
                    record.FilePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(diskPath)) File.Delete(diskPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BackupReceive 删除文件失败但仍删 DB: {Path}", record.FilePath);
            }

            _fsql.Delete<BackupRemoteRecord>().Where(c => c.Id == id).ExecuteAffrows();
            _logger.LogInformation("BackupReceive: admin 删除备份 Id={Id} {Product}/{File}",
                id, record.ProductEn, record.FileName);
            return Result.Success("删除成功");
        }

        private static readonly Regex SafeSegmentRegex = new(@"[^a-zA-Z0-9._-]");
        private static string SanitizePathSegment(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var safe = SafeSegmentRegex.Replace(s, "_");
            // 避免 . / .. / 空字符串
            return safe.Trim('.', '_') is var t && string.IsNullOrEmpty(t) ? "_" : t;
        }
    }
}
