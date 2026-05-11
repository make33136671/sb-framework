using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SB.Framework.Core;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.License;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace SB.Framework.Services
{
    /// <summary>
    /// 客户端侧授权服务（向授权服务器申请、查询、下载授权）
    /// </summary>
    [AutoDependencyInjection]
    public class LicenseClientService
    {
        private readonly ILicenseService _licenseService;
        private readonly ILogger<LicenseClientService> _logger;
        private readonly IHostEnvironment _env;
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

        public LicenseClientService(ILicenseService licenseService, ILogger<LicenseClientService> logger, IHostEnvironment env)
        {
            _licenseService = licenseService;
            _logger = logger;
            _env = env;
        }

        /// <summary>
        /// 获取本机客户端标识信息
        /// </summary>
        public virtual object GetClientIdentity()
        {
            return new
            {
                clientKey = ClientKeyService.GetClientKey(_env.ContentRootPath),
                hardwareId = HardwareIdService.GetHardwareId(),
                hardwareInfo = HardwareIdService.GetHardwareInfo()
            };
        }

        /// <summary>
        /// 获取当前授权状态
        /// </summary>
        public virtual Result GetStatus()
        {
            var status = _licenseService.CurrentStatus;
            var identity = GetClientIdentity();

            return Result.Success(new
            {
                isEnabled = _licenseService.IsEnabled,
                isValid = status.IsValid,
                isExpired = status.IsExpired,
                message = status.Message,
                expiresAt = status.ExpiresAt,
                issuedAt = status.IssuedAt,
                remainingDays = status.RemainingDays,
                productEn = status.ProductEn,
                productCn = status.ProductCn,
                domain = status.Domain,
                verifyMode = status.VerifyMode,
                allowedRoutes = status.AllowedRoutes,
                // 到期后行为：签发授权时勾选的策略，前端 Status.vue 据此显示"管理端/客户端 是否可用"
                allowAdminAfterExpiry = status.AllowAdminAfterExpiry,
                allowClientApiAfterExpiry = status.AllowClientApiAfterExpiry,
                // 服务器操作系统时区 ID（如 Asia/Shanghai、Asia/Phnom_Penh、UTC）
                serverTimeZone = TimeZoneInfo.Local.Id,
                // 程序时区 ID（appsettings.json 的 TimeZone 配置，由 AppTime 初始化）
                appTimeZone = AppTime.Zone.Id,
                // 云端中心地址（客户端向此地址申请/下载授权、上传备份等，前端输入框预填）
                cloudServerUrl = AppGlobal.AppSettings?.GetCloudServerUrl() ?? "",
                identity
            });
        }

        /// <summary>
        /// 向授权服务器申请授权
        /// </summary>
        public virtual async Task<Result> RequestLicenseAsync(string? domain)
        {
            var serverUrl = AppGlobal.AppSettings?.GetCloudServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
                return Result.Fail("未配置授权服务器地址");

            var clientKey = ClientKeyService.GetClientKey(_env.ContentRootPath);
            var hardwareId = HardwareIdService.GetHardwareId();

            var payload = new
            {
                domain = domain ?? "",
                clientKey,
                hardwareId,
                serverIP = GetLocalIP()
            };

            try
            {
                var url = serverUrl.TrimEnd('/') + "/api/verify/Request";
                var content = new StringContent(
                    $"domain={Uri.EscapeDataString(payload.domain)}&clientKey={Uri.EscapeDataString(payload.clientKey)}&hardwareId={Uri.EscapeDataString(payload.hardwareId)}&serverIP={Uri.EscapeDataString(payload.serverIP ?? "")}",
                    Encoding.UTF8, "application/x-www-form-urlencoded");

                var response = await _httpClient.PostAsync(url, content);
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("授权申请已发送至 {Url}", url);
                    return Result.Success("授权申请已提交");
                }

                _logger.LogWarning("授权申请失败: {StatusCode} {Body}", response.StatusCode, body);
                return Result.Fail($"授权申请失败: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "授权申请异常");
                return Result.Fail($"无法连接授权服务器: {ex.Message}");
            }
        }

        /// <summary>
        /// 向授权服务器查询是否有新授权。
        /// 允许运行时传入 serverUrl 覆盖 appsettings 配置，方便客户在 UI 上填入临时地址。
        /// actualDomain 是浏览器侧的 window.location.host，透传给云端用于多域名识别（配置声明域名 vs 实际运行域名）。
        /// </summary>
        public virtual async Task<Result> CheckUpdateAsync(string? serverUrl = null, string? actualDomain = null)
        {
            var effectiveUrl = string.IsNullOrWhiteSpace(serverUrl)
                ? AppGlobal.AppSettings?.GetCloudServerUrl()
                : serverUrl.Trim();
            if (string.IsNullOrEmpty(effectiveUrl))
                return Result.Fail("未配置授权服务器地址");

            var clientKey = ClientKeyService.GetClientKey(_env.ContentRootPath);
            var hardwareId = HardwareIdService.GetHardwareId();

            try
            {
                var url = effectiveUrl.TrimEnd('/') + $"/api/verify/Check?clientKey={Uri.EscapeDataString(clientKey)}&hardwareId={Uri.EscapeDataString(hardwareId)}";
                if (!string.IsNullOrWhiteSpace(actualDomain))
                    url += $"&actualDomain={Uri.EscapeDataString(actualDomain)}";
                var response = await _httpClient.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var json = JsonDocument.Parse(body);
                    return Result.Success(json.RootElement.GetProperty("data").Clone());
                }

                return Result.Fail($"查询失败: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询授权更新异常");
                return Result.Fail($"无法连接授权服务器: {ex.Message}");
            }
        }

        /// <summary>
        /// 离线应用管理员生成好的授权 Key（不走网络）
        /// 验证签名有效 → 写入 appsettings.json → Refresh 内存状态
        /// </summary>
        public virtual Result ApplyKey(string licenseKey)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
                return Result.Fail("授权 Key 不能为空");

            var validateResult = _licenseService.Validate(licenseKey);
            if (!validateResult.IsValid && !validateResult.IsExpired)
                return Result.Fail($"授权 Key 验证失败: {validateResult.Message}");

            try
            {
                UpdateLicenseKey(licenseKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写入 appsettings.json 失败");
                return Result.Fail($"保存授权失败: {ex.Message}");
            }

            _licenseService.Refresh(licenseKey);

            _logger.LogInformation("离线应用授权成功，到期时间: {ExpiresAt}", validateResult.ExpiresAt);
            return Result.Success("授权已应用，即将刷新");
        }

        /// <summary>
        /// 从授权服务器下载并应用新授权。
        /// 允许运行时传入 serverUrl 覆盖 appsettings 配置，对齐 CheckUpdateAsync — 客户在 UI 上填的临时地址也能用于 Apply。
        /// </summary>
        public virtual async Task<Result> ApplyLicenseAsync(int recordId, string? serverUrl = null)
        {
            var effectiveUrl = string.IsNullOrWhiteSpace(serverUrl)
                ? AppGlobal.AppSettings?.GetCloudServerUrl()
                : serverUrl.Trim();
            if (string.IsNullOrEmpty(effectiveUrl))
                return Result.Fail("未配置授权服务器地址");

            try
            {
                var url = effectiveUrl.TrimEnd('/') + $"/api/verify/Download?recordId={recordId}";
                var response = await _httpClient.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return Result.Fail($"下载失败: {response.StatusCode}");

                var json = JsonDocument.Parse(body);
                var data = json.RootElement.GetProperty("data");
                var licenseData = data.GetProperty("licenseData").GetString();

                if (string.IsNullOrEmpty(licenseData))
                    return Result.Fail("授权文件内容为空");

                // 先验证授权文件是否有效
                var validateResult = _licenseService.Validate(licenseData);
                if (!validateResult.IsValid && !validateResult.IsExpired)
                    return Result.Fail($"授权文件验证失败: {validateResult.Message}");

                // 更新 appsettings.json
                UpdateLicenseKey(licenseData);

                // 刷新内存中的授权状态
                _licenseService.Refresh(licenseData);

                _logger.LogInformation("授权已更新，到期时间: {ExpiresAt}", validateResult.ExpiresAt);
                return Result.Success("授权更新成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "下载授权异常");
                return Result.Fail($"下载授权失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 启动时向授权服务器发送验证上报（fire-and-forget）
        /// </summary>
        public virtual async Task SendVerificationAsync(string? domain)
        {
            var serverUrl = AppGlobal.AppSettings?.GetCloudServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;

            var status = _licenseService.CurrentStatus;
            var clientKey = ClientKeyService.GetClientKey(_env.ContentRootPath);
            var hardwareId = HardwareIdService.GetHardwareId();

            try
            {
                var url = serverUrl.TrimEnd('/') + "/api/verify/Verify";
                var content = new StringContent(
                    $"domain={Uri.EscapeDataString(domain ?? "")}&clientKey={Uri.EscapeDataString(clientKey)}&hardwareId={Uri.EscapeDataString(hardwareId)}&serverIP={Uri.EscapeDataString(GetLocalIP() ?? "")}&licenseValid={status.IsValid}&message={Uri.EscapeDataString(status.Message ?? "")}",
                    Encoding.UTF8, "application/x-www-form-urlencoded");

                await _httpClient.PostAsync(url, content);
                _logger.LogInformation("授权验证上报已发送");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("授权验证上报失败: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// 更新 appsettings.json 中的 LicenseConfig.Key
        /// </summary>
        internal void UpdateLicenseKey(string newKey)
        {
            var appSettingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
            var json = File.ReadAllText(appSettingsPath);
            var node = JsonNode.Parse(json);

            if (node?["LicenseConfig"] is JsonObject licenseObj)
            {
                licenseObj["Key"] = newKey;
            }
            else
            {
                if (node is JsonObject root)
                {
                    root["LicenseConfig"] = new JsonObject { ["Key"] = newKey };
                }
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(appSettingsPath, node?.ToJsonString(options) ?? "{}");
        }

        private static string? GetLocalIP()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip.ToString();
                }
            }
            catch { }
            return null;
        }
    }
}
