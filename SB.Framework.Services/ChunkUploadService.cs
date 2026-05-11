using Microsoft.AspNetCore.Http;
using SB.Framework.Core;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Expand;
using SB.Framework.Core.Util;
using SB.Framework.Models;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class ChunkUploadService : IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "分片上传";
        public int InitOrder => 50;
        public Type[] GetEntityTypes() => new[] { typeof(ChunkUpload), typeof(ChunkUploadPart) };
        #endregion

        private readonly IFreeSql _fsql;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ChunkUploadService(IFreeSql fsql, IHttpContextAccessor httpContextAccessor)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// 初始化上传：秒传检测 / 断点续传 / 新建会话
        /// </summary>
        public virtual Result Init(string fileName, long fileSize, string fileMd5)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return Result.Fail("文件名不能为空");
            if (fileSize <= 0)
                return Result.Fail("文件大小无效");
            if (string.IsNullOrWhiteSpace(fileMd5) || fileMd5.Length != 32)
                return Result.Fail("文件MD5无效");

            var config = AppGlobal.AppSettings.UploadConfig;

            // 校验文件大小
            var maxBytes = config.MaxFileSizeMB * 1024L * 1024L;
            if (fileSize > maxBytes)
                return Result.Fail($"文件大小不能超过 {config.MaxFileSizeMB}MB");

            // 校验文件扩展名
            var extension = Path.GetExtension(fileName);
            if (config.AllowedExtensions != null && config.AllowedExtensions.Count > 0)
            {
                if (!config.AllowedExtensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase)))
                    return Result.Fail($"不允许上传 {extension} 类型的文件");
            }

            var md5Lower = fileMd5.ToLower();

            // 1. 秒传检测：查找同MD5 + 同大小的已完成上传
            var completed = _fsql.Select<ChunkUpload>()
                .Where(x => x.FileMd5 == md5Lower && x.FileSize == fileSize && x.Status == ChunkUploadStatus.Completed)
                .OrderByDescending(x => x.CompletedAt)
                .First();

            if (completed != null && !string.IsNullOrEmpty(completed.FilePath))
            {
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), $"wwwroot{completed.FilePath}");
                if (File.Exists(fullPath))
                {
                    var host = _httpContextAccessor.HttpContext.RequestHostWithScheme();
                    return Result.Success("秒传", new
                    {
                        status = "instant",
                        fileName = completed.FilePath,
                        absfileName = $"{host}{completed.FilePath}",
                        name = completed.FileName,
                        size = completed.FileSize
                    });
                }
            }

            // 2. 断点续传：查找同MD5 + 同大小的未完成上传
            var inProgress = _fsql.Select<ChunkUpload>()
                .Where(x => x.FileMd5 == md5Lower && x.FileSize == fileSize && x.Status == ChunkUploadStatus.Uploading)
                .OrderByDescending(x => x.CreatedAt)
                .First();

            if (inProgress != null)
            {
                var uploadedChunks = _fsql.Select<ChunkUploadPart>()
                    .Where(x => x.UploadId == inProgress.UploadId)
                    .ToList(x => x.ChunkIndex);

                return Result.Success("续传", new
                {
                    status = "resume",
                    uploadId = inProgress.UploadId,
                    chunkSize = inProgress.ChunkSize,
                    totalChunks = inProgress.TotalChunks,
                    uploadedChunks
                });
            }

            // 3. 新建上传会话
            var chunkSizeBytes = config.ChunkSizeMB * 1024 * 1024;
            var totalChunks = (int)Math.Ceiling((double)fileSize / chunkSizeBytes);
            var uploadId = Guid.NewGuid().ToString("N");

            _fsql.Insert(new ChunkUpload
            {
                UploadId = uploadId,
                FileName = fileName,
                FileSize = fileSize,
                FileMd5 = md5Lower,
                ChunkSize = chunkSizeBytes,
                TotalChunks = totalChunks,
                Status = ChunkUploadStatus.Uploading
            }).ExecuteAffrows();

            // 创建临时目录
            var tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"wwwroot{config.ChunkTempPath}{uploadId}");
            Directory.CreateDirectory(tempDir);

            return Result.Success("新上传", new
            {
                status = "new",
                uploadId,
                chunkSize = chunkSizeBytes,
                totalChunks,
                uploadedChunks = Array.Empty<int>()
            });
        }

        /// <summary>
        /// 上传单个分片
        /// </summary>
        public virtual Result Upload(string uploadId, int chunkIndex, IFormFile chunk)
        {
            if (string.IsNullOrWhiteSpace(uploadId))
                return Result.Fail("uploadId 不能为空");
            if (chunk == null || chunk.Length == 0)
                return Result.Fail("分片数据为空");

            var debugThrottleMs = AppGlobal.AppSettings.UploadConfig.DebugThrottleMs;
            if (debugThrottleMs > 0)
                Thread.Sleep(debugThrottleMs);

            var upload = _fsql.Select<ChunkUpload>()
                .Where(x => x.UploadId == uploadId && x.Status == ChunkUploadStatus.Uploading)
                .First();

            if (upload == null)
                return Result.Fail("上传会话不存在或已过期");

            if (chunkIndex < 0 || chunkIndex >= upload.TotalChunks)
                return Result.Fail($"分片序号无效，有效范围: 0-{upload.TotalChunks - 1}");

            // 幂等检查：分片已存在则直接返回成功
            var exists = _fsql.Select<ChunkUploadPart>()
                .Where(x => x.UploadId == uploadId && x.ChunkIndex == chunkIndex)
                .Any();
            if (exists)
                return Result.Success("分片已存在");

            // 保存分片文件
            var config = AppGlobal.AppSettings.UploadConfig;
            var tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"wwwroot{config.ChunkTempPath}{uploadId}");
            if (!Directory.Exists(tempDir))
                Directory.CreateDirectory(tempDir);

            var chunkPath = Path.Combine(tempDir, chunkIndex.ToString());
            using (var stream = File.Create(chunkPath))
            {
                chunk.CopyTo(stream);
            }

            // 记录分片
            _fsql.Insert(new ChunkUploadPart
            {
                UploadId = uploadId,
                ChunkIndex = chunkIndex,
                ChunkSize = (int)chunk.Length
            }).ExecuteAffrows();

            return Result.Success("上传成功");
        }

        /// <summary>
        /// 合并所有分片
        /// </summary>
        public virtual Result Merge(string uploadId)
        {
            if (string.IsNullOrWhiteSpace(uploadId))
                return Result.Fail("uploadId 不能为空");

            var upload = _fsql.Select<ChunkUpload>()
                .Where(x => x.UploadId == uploadId)
                .First();

            if (upload == null)
                return Result.Fail("上传会话不存在");

            if (upload.Status == ChunkUploadStatus.Completed)
            {
                // 已合并过，直接返回结果
                var host2 = _httpContextAccessor.HttpContext.RequestHostWithScheme();
                return Result.Success("上传成功", new
                {
                    fileName = upload.FilePath,
                    absfileName = $"{host2}{upload.FilePath}",
                    name = upload.FileName,
                    size = upload.FileSize
                });
            }

            // 校验所有分片是否齐全
            var uploadedCount = (int)_fsql.Select<ChunkUploadPart>()
                .Where(x => x.UploadId == uploadId)
                .Count();

            if (uploadedCount < upload.TotalChunks)
                return Result.Fail($"分片不完整，已上传 {uploadedCount}/{upload.TotalChunks}");

            var config = AppGlobal.AppSettings.UploadConfig;
            var uploadConfig = AppGlobal.AppSettings.UploadConfig;
            var tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"wwwroot{config.ChunkTempPath}{uploadId}");

            // 最终文件路径（与现有 Upload 接口格式一致）
            var extension = Path.GetExtension(upload.FileName);
            var finalFileName = Guid.NewGuid().ToString("N") + extension;
            var finalRelativePath = $"{uploadConfig.UploadPath}{finalFileName}";
            var finalDir = Path.Combine(Directory.GetCurrentDirectory(), $"wwwroot{uploadConfig.UploadPath}");

            if (!Directory.Exists(finalDir))
                Directory.CreateDirectory(finalDir);

            var finalPath = Path.Combine(finalDir, finalFileName);

            // 按顺序合并分片
            using (var finalStream = File.Create(finalPath))
            {
                for (int i = 0; i < upload.TotalChunks; i++)
                {
                    var chunkPath = Path.Combine(tempDir, i.ToString());
                    if (!File.Exists(chunkPath))
                    {
                        // 清理已创建的文件
                        finalStream.Close();
                        try { File.Delete(finalPath); } catch { }
                        return Result.Fail($"分片文件缺失: {i}");
                    }

                    using (var chunkStream = File.OpenRead(chunkPath))
                    {
                        chunkStream.CopyTo(finalStream);
                    }
                }
            }

            // 更新会话状态
            _fsql.Update<ChunkUpload>()
                .Set(x => x.Status, ChunkUploadStatus.Completed)
                .Set(x => x.FilePath, finalRelativePath)
                .Set(x => x.CompletedAt, AppTime.Now)
                .Where(x => x.Id == upload.Id)
                .ExecuteAffrows();

            // 清理临时分片目录
            try { Directory.Delete(tempDir, true); } catch { }

            var host = _httpContextAccessor.HttpContext.RequestHostWithScheme();
            return Result.Success("上传成功", new
            {
                fileName = finalRelativePath,
                absfileName = $"{host}{finalRelativePath}",
                name = upload.FileName,
                size = upload.FileSize
            });
        }

        /// <summary>
        /// 查询上传进度
        /// </summary>
        public virtual Result Progress(string uploadId)
        {
            if (string.IsNullOrWhiteSpace(uploadId))
                return Result.Fail("uploadId 不能为空");

            var upload = _fsql.Select<ChunkUpload>()
                .Where(x => x.UploadId == uploadId)
                .First();

            if (upload == null)
                return Result.Fail("上传会话不存在");

            var uploadedChunks = _fsql.Select<ChunkUploadPart>()
                .Where(x => x.UploadId == uploadId)
                .ToList(x => x.ChunkIndex);

            return Result.Success(new
            {
                uploadId,
                fileName = upload.FileName,
                fileSize = upload.FileSize,
                totalChunks = upload.TotalChunks,
                uploadedChunks,
                status = upload.Status.ToString().ToLower()
            });
        }
    }
}
