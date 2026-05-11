using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace SB.Framework.Services
{
    /// <summary>
    /// 数据库备份云端上传工具
    /// multipart/form-data POST 到 backup_upload_url 配置的服务端
    /// 客户端无鉴权，只携带元数据 (产品名/域名/Logo/客户端时间) 让服务端识别归属
    /// </summary>
    [AutoDependencyInjection]
    public class BackupUploader
    {
        private static readonly HttpClient SharedHttpClient = new()
        {
            // 备份 zip 可能几百 MB，留足够时间
            Timeout = TimeSpan.FromMinutes(30),
        };

        private readonly IConfiguration _config;
        private readonly IHostEnvironment _env;
        private readonly ILogger<BackupUploader> _logger;

        public BackupUploader(IConfiguration config, IHostEnvironment env, ILogger<BackupUploader> logger)
        {
            _config = config;
            _env = env;
            _logger = logger;
        }

        /// <summary>
        /// 上传备份文件 + 元数据到指定 URL
        /// </summary>
        /// <returns>(true, null) 成功；(false, errorMsg) 失败</returns>
        public async Task<(bool Ok, string? Error)> UploadAsync(string url, DatabaseBackup record, CancellationToken ct)
        {
            try
            {
                var filePath = Path.Combine(_env.ContentRootPath, "wwwroot",
                    record.FilePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(filePath))
                    return (false, $"备份文件不存在: {filePath}");

                using var content = new MultipartFormDataContent();

                // 文件本体
                var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                content.Add(fileContent, "file", record.FileName);

                // 元数据：服务端用来识别这份备份归属哪个客户端、哪个产品
                content.Add(new StringContent(_config["ProductEn"] ?? "sb"), "productEn");
                content.Add(new StringContent(_config["ProductCn"] ?? "管理系统"), "productCn");
                content.Add(new StringContent(_config["PrimaryDomain"] ?? ""), "primaryDomain");
                content.Add(new StringContent(record.CreatedTime.ToUniversalTime().ToString("o")), "backupUtcTime");
                content.Add(new StringContent(record.FileSize.ToString()), "fileSize");

                // MD5（服务端可校验文件完整性）
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash(fileBytes);
                    content.Add(new StringContent(Convert.ToHexString(hash)), "md5");
                }

                // Logo base64 (从 wwwroot/storage/images/logo.png 读盘，无则跳过)
                var logoPath = Path.Combine(_env.ContentRootPath, "wwwroot", "storage", "images", "logo.png");
                if (File.Exists(logoPath))
                {
                    var logoBytes = await File.ReadAllBytesAsync(logoPath, ct);
                    content.Add(new StringContent("data:image/png;base64," + Convert.ToBase64String(logoBytes)),
                        "logoBase64");
                }

                using var resp = await SharedHttpClient.PostAsync(url, content, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    return (false, $"HTTP {(int)resp.StatusCode}: {Truncate(body, 500)}");
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BackupUploader: upload to {Url} failed", url);
                return (false, ex.Message);
            }
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...";
    }
}
