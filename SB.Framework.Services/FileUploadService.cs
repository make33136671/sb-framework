using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using SB.Framework.Core;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Expand;
using SB.Framework.Models;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class FileUploadService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IHostEnvironment _env;

        public FileUploadService(IHttpContextAccessor httpContextAccessor, IHostEnvironment env)
        {
            _httpContextAccessor = httpContextAccessor;
            _env = env;
        }

        public virtual Result Upload(IFormFile file) => Upload(file, null);

        public virtual Result Upload(IFormFile file, string subPath)
        {
            if (file == null || file.Length == 0)
                return Result.Fail("请选择要上传的文件");

            var config = AppGlobal.AppSettings.UploadConfig;

            var maxBytes = config.MaxFileSizeMB * 1024 * 1024;
            if (file.Length > maxBytes)
                return Result.Fail($"文件大小不能超过 {config.MaxFileSizeMB}MB");

            var extension = Path.GetExtension(file.FileName);
            if (config.AllowedExtensions != null && config.AllowedExtensions.Count > 0)
            {
                if (!config.AllowedExtensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase)))
                    return Result.Fail($"不允许上传 {extension} 类型的文件");
            }

            // 物理根 = {ContentRoot}/{UploadPath}[/{subPath}](脱离 wwwroot,放到 ContentRoot 下,
            // 默认 UploadPath="/storage/uploads/" 时物理落 {ContentRoot}/storage/uploads/...)
            var relativePath = config.UploadPath;
            var baseFolder = relativePath.TrimStart('/').TrimEnd('/').Replace('/', Path.DirectorySeparatorChar);
            var savePath = Path.Combine(_env.ContentRootPath, baseFolder);
            var normalizedSub = string.IsNullOrEmpty(subPath) ? null : subPath.Trim('/').Replace('/', Path.DirectorySeparatorChar);
            if (!string.IsNullOrEmpty(normalizedSub))
                savePath = Path.Combine(savePath, normalizedSub);
            if (!Directory.Exists(savePath))
                Directory.CreateDirectory(savePath);

            var fileName = Guid.NewGuid().ToString("N") + extension;
            var filePath = Path.Combine(savePath, fileName);

            using (var stream = File.Create(filePath))
            {
                file.CopyTo(stream);
            }

            var storageKey = string.IsNullOrEmpty(normalizedSub)
                ? fileName
                : $"{subPath.Trim('/')}/{fileName}";

            var ctx = _httpContextAccessor.HttpContext;
            var host = ctx != null ? ctx.RequestHostWithScheme() : string.Empty;

            return Result.Success("上传成功", new
            {
                fileName = $"{relativePath}{storageKey}",
                absfileName = $"{host}{relativePath}{storageKey}",
                name = file.FileName,
                size = file.Length,
                storageKey
            });
        }
    }
}
