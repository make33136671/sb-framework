using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Core;
using SB.Framework.Core.Expand;
using SB.Framework.Models;
using SB.Framework.Models.Dto;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers
{
    /// <summary>
    /// 上传相关接口（直传 + 切片 + 配置）
    /// </summary>
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class UploadController : Controller
    {
        private readonly FileUploadService _fileUploadService;
        private readonly ChunkUploadService _chunkUploadService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// 注入直传 + 切片两个 Service 与 HttpContext 访问器
        /// </summary>
        public UploadController(
            FileUploadService fileUploadService,
            ChunkUploadService chunkUploadService,
            IHttpContextAccessor httpContextAccessor)
        {
            _fileUploadService = fileUploadService;
            _chunkUploadService = chunkUploadService;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// 获取上传配置（前端启动时拉一次缓存使用）
        /// </summary>
        [HttpGet]
        public Result Config()
        {
            var cfg = AppGlobal.AppSettings.UploadConfig;
            var host = _httpContextAccessor.HttpContext!.RequestHostWithScheme();
            return Result.Success(new UploadConfigDto
            {
                MaxFileSizeMB = cfg.MaxFileSizeMB,
                ChunkThresholdMB = cfg.ChunkThresholdMB,
                MaxConcurrent = cfg.MaxConcurrent,
                AllowedExtensions = cfg.AllowedExtensions,
                UrlPrefix = host
            });
        }

        /// <summary>
        /// 直传单个文件（小文件场景）
        /// </summary>
        [HttpPost]
        [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
        public Result Direct(IFormFile file)
        {
            return _fileUploadService.Upload(file);
        }

        /// <summary>
        /// 初始化分片上传：秒传检测 / 断点续传 / 新建会话
        /// </summary>
        [HttpPost]
        public Result ChunkInit([FromForm] string fileName, [FromForm] long fileSize, [FromForm] string fileMd5)
        {
            return _chunkUploadService.Init(fileName, fileSize, fileMd5);
        }

        /// <summary>
        /// 上传单个分片
        /// </summary>
        [HttpPost]
        [RequestSizeLimit(20 * 1024 * 1024)]
        public Result ChunkUpload([FromForm] string uploadId, [FromForm] int chunkIndex, IFormFile chunk)
        {
            return _chunkUploadService.Upload(uploadId, chunkIndex, chunk);
        }

        /// <summary>
        /// 合并所有分片为最终文件
        /// </summary>
        [HttpPost]
        public Result ChunkMerge([FromForm] string uploadId)
        {
            return _chunkUploadService.Merge(uploadId);
        }

        /// <summary>
        /// 查询上传进度（含已上传分片列表）
        /// </summary>
        [HttpGet]
        public Result ChunkProgress(string uploadId)
        {
            return _chunkUploadService.Progress(uploadId);
        }
    }
}
