#if ENABLE_BACKUP_RECEIVE
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers
{
    /// <summary>
    /// 云端备份接收
    ///
    /// **重要安全设计：本 Controller 只暴露 Upload + 查询 + Download 三类接口，
    /// 没有任何 Delete/Update/Replace HTTP 方法**。客户端通过应用层无法删除云端备份。
    /// 服务端运维如需清理，只能通过文件系统直接操作。
    /// </summary>
    [ApiController]
    public class BackupReceiveController : Controller
    {
        private readonly BackupReceiveService _service;
        private readonly IHostEnvironment _env;

        public BackupReceiveController(BackupReceiveService service, IHostEnvironment env)
        {
            _service = service;
            _env = env;
        }

        /// <summary>
        /// 接收客户端上传（无鉴权，凭元数据归类）
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        [Route("/api/BackupReceive/Upload")]
        [RequestSizeLimit(2L * 1024 * 1024 * 1024)] // 2 GB 单文件上限
        [RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024)]
        public async Task<Result> Upload(
            IFormFile file,
            [FromForm] string productEn,
            [FromForm] string productCn,
            [FromForm] string? primaryDomain,
            [FromForm] string? logoBase64,
            [FromForm] string? backupUtcTime,
            [FromForm] string? md5,
            CancellationToken ct)
        {
            // X-Forwarded-For 优先（兼容 nginx 反代），否则用 RemoteIpAddress
            var clientIp = HttpContext.Request.Headers["X-Forwarded-For"].ToString();
            if (string.IsNullOrWhiteSpace(clientIp))
                clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            else
                clientIp = clientIp.Split(',')[0].Trim();

            return await _service.SaveUploadAsync(file, productEn, productCn, primaryDomain,
                logoBase64, backupUtcTime, md5, clientIp, ct);
        }

        /// <summary>
        /// 服务端管理员：按产品聚合的列表
        /// </summary>
        [HttpGet]
        [Authorize(AuthenticationSchemes = "JwtAdmin")]
        [Route("/admin/BackupReceive/ProductList")]
        public Result ProductList() => _service.GetProductList();

        /// <summary>
        /// 服务端管理员：单产品的备份记录分页
        /// </summary>
        [HttpGet]
        [Authorize(AuthenticationSchemes = "JwtAdmin")]
        [Route("/admin/BackupReceive/RecordList")]
        public Result RecordList(string productEn, int page = 1, int pageSize = 20)
        {
            if (string.IsNullOrWhiteSpace(productEn))
                return Result.Fail("productEn 不能为空");
            return _service.GetRecordList(productEn, page, pageSize);
        }

        /// <summary>
        /// 服务端管理员：下载备份文件
        /// </summary>
        [HttpGet]
        [Authorize(AuthenticationSchemes = "JwtAdmin")]
        [Route("/admin/BackupReceive/Download/{id:int}")]
        public IActionResult Download(int id)
        {
            var record = _service.GetById(id);
            if (record == null) return NotFound();

            var diskPath = Path.Combine(_env.ContentRootPath, "wwwroot",
                record.FilePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(diskPath)) return NotFound("文件已不存在");

            return PhysicalFile(diskPath, "application/zip", record.FileName);
        }

        /// <summary>
        /// 服务端管理员：删除一份云端备份（仅 admin 接口，客户端 Anonymous 通道永远没有删除能力）
        /// </summary>
        [HttpPost]
        [Authorize(AuthenticationSchemes = "JwtAdmin")]
        [Route("/admin/BackupReceive/Delete")]
        public Result Delete([FromForm] int id) => _service.DeleteByAdmin(id);
    }
}
#endif
