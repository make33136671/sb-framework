using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SB.Framework.Core;
using SB.Framework.Core.Expand;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class DatabaseController : Controller
    {
        private readonly DatabaseBackupService _service;
        private readonly BackupUploader _uploader;
        private readonly IFreeSql _fsql;

        public DatabaseController(DatabaseBackupService service, BackupUploader uploader, IFreeSql fsql)
        {
            _service = service;
            _uploader = uploader;
            _fsql = fsql;
        }

        /// <summary>
        /// 检查备份工具可用性 + 数据库信息(工具按 DbType 自动选 mysqldump 或 pg_dump)
        /// </summary>
        [HttpGet]
        public Task<Result> CheckBackup(CancellationToken ct) => _service.CheckBackupToolAsync(ct);

        /// <summary>
        /// 触发数据库备份（Admin 后台）
        /// 走 BackupAndUploadAsync 统一流程：本地备份 + (启用了)云端上传 + 清理旧备份
        /// </summary>
        [HttpPost]
        public Task<Result> Backup(CancellationToken ct)
            => _service.BackupAndUploadAsync(User.Username(), ct);

        /// <summary>
        /// 备份记录列表
        /// </summary>
        [HttpGet]
        public Result List(string field = "", string order = "", int page = 1, int pageSize = 20)
            => _service.GetList(field, order, page, pageSize);

        /// <summary>
        /// 删除备份（文件 + 记录）
        /// id == 0 时按 filePath 删 disk_only 文件
        /// </summary>
        [HttpPost]
        public Result Del([FromForm] int id, [FromForm] string? filePath = null)
            => _service.Del(id, filePath);

        /// <summary>
        /// 从备份记录恢复数据库（高危：会覆盖整库）
        /// id == 0 时按 filePath 恢复 disk_only 文件
        /// </summary>
        [HttpPost]
        public Task<Result> Restore([FromForm] int id, [FromForm] string? filePath = null, CancellationToken ct = default)
            => _service.RestoreAsync(id, User.Username(), filePath, ct);

        /// <summary>
        /// 上传并导入备份文件（.zip 内含 .sql，或裸 .sql）。
        /// 只落盘 + 建记录，不执行导入；导入仍需走 Restore（高危操作需二次确认）。
        /// </summary>
        [HttpPost]
        [RequestSizeLimit(2L * 1024 * 1024 * 1024)] // 2 GB 单文件上限
        [RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024)]
        public async Task<Result> Import(IFormFile file, [FromForm] string? remark, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
                return Result.Fail("请选择要上传的备份文件");

            await using var stream = file.OpenReadStream();
            return await _service.ImportBackupAsync(
                stream, file.FileName, User.Username(), remark, ct);
        }

        /// <summary>
        /// 试运行校验一份备份（只读）：确认能解开、方言匹配、恢复工具可用
        /// </summary>
        [HttpPost]
        public Task<Result> Validate([FromForm] int id, CancellationToken ct)
            => _service.ValidateBackupFileAsync(id, ct);

        /// <summary>
        /// 触发数据库备份（自动化/定时 API,走 JwtAdmin:FullAccess 访问令牌或密码 JWT）
        /// 同样走 BackupAndUploadAsync 统一流程
        /// </summary>
        [HttpPost("/api/Database/Backup")]
        public Task<Result> ApiBackup(CancellationToken ct)
            => _service.BackupAndUploadAsync(User.Username(), ct);

        /// <summary>
        /// 手动上传/重试上传 一份本地备份到云端
        /// 已 success 的会拒绝（避免重复）；CloudConfig 未配也拒绝
        /// </summary>
        [HttpPost]
        public async Task<Result> UploadToCloud([FromForm] int id, CancellationToken ct)
        {
            var record = _fsql.Select<DatabaseBackup>().Where(c => c.Id == id).First();
            if (record == null) return Result.Fail("备份记录不存在");
            if (record.RemoteUploadStatus == "success")
                return Result.Fail("此备份已成功上传过，请勿重复");

            var url = AppGlobal.AppSettings?.GetBackupUploadUrl() ?? "";
            if (string.IsNullOrWhiteSpace(url))
                return Result.Fail("未配置云端中心地址 (CloudConfig.ServerUrl)");

            var (ok, error) = await _uploader.UploadAsync(url, record, ct);
            if (ok)
            {
                _fsql.Update<DatabaseBackup>()
                    .Set(c => c.RemoteUploadStatus, "success")
                    .Set(c => c.RemoteUploadTime, AppTime.UtcNow)
                    .Set(c => c.RemoteUploadUrl, url)
                    .Set(c => c.RemoteUploadError, null)
                    .Where(c => c.Id == id).ExecuteAffrows();
                return Result.Success("上传成功");
            }
            _fsql.Update<DatabaseBackup>()
                .Set(c => c.RemoteUploadStatus, "failed")
                .Set(c => c.RemoteUploadError, error)
                .Set(c => c.RemoteUploadUrl, url)
                .Where(c => c.Id == id).ExecuteAffrows();
            return Result.Fail($"上传失败: {error}");
        }
    }
}
