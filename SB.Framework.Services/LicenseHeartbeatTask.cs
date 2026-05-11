using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SB.Framework.Core;
using SB.Framework.Core.BackgroundTask;
using SB.Framework.Core.License;
using SB.Framework.Core.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SB.Framework.Services
{
    /// <summary>
    /// 客户端心跳后台任务 — 定期向授权服务器发送心跳
    /// </summary>
    public class LicenseHeartbeatTask : BaseBackgroundTask, IHeartbeatGracePeriodChecker
    {
        private readonly ILicenseService _licenseService;
        private readonly ILogger<LicenseHeartbeatTask> _logger;
        private readonly IHostEnvironment _env;
        private readonly int _intervalMinutes;
        private readonly int _graceHours;
        private readonly string _serverUrl;
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly string _heartbeatFilePath = Path.Combine(AppContext.BaseDirectory, "license_heartbeat.dat");
        private static readonly byte[] _hmacKey = SHA256.HashData(Encoding.UTF8.GetBytes(HardwareIdService.GetHardwareId()));

        private DateTime? _lastSuccessfulHeartbeat;
        private int _consecutiveFailures;

        /// <summary>
        /// 最后一次成功心跳的时间
        /// </summary>
        public DateTime? LastSuccessfulHeartbeat => _lastSuccessfulHeartbeat;

        /// <summary>
        /// 连续失败次数
        /// </summary>
        public int ConsecutiveFailures => _consecutiveFailures;

        protected override TimeSpan Interval => TimeSpan.FromMinutes(_intervalMinutes);

        public LicenseHeartbeatTask(
            ILicenseService licenseService,
            ILogger<LicenseHeartbeatTask> logger,
            IHostEnvironment env)
            : base(logger)
        {
            _licenseService = licenseService;
            _logger = logger;
            _env = env;

            // 心跳参数硬编码，不从配置读取，防止用户禁用
            _intervalMinutes = 5;
            _graceHours = 48;
            _serverUrl = AppGlobal.AppSettings?.GetCloudServerUrl() ?? "";

            // 从持久化文件恢复上次心跳时间，防止重启续命
            _lastSuccessfulHeartbeat = ReadHeartbeatTime() ?? AppTime.UtcNow;
        }

        protected override async Task ExecuteTaskAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_serverUrl))
            {
                LogWarning("License server URL not configured, skipping heartbeat.");
                return;
            }

            // 统一用 ClientIdentityPayload, 和 Check/Verify/Request 发送点保持字段一致
            var identity = ClientIdentityPayload.Collect(_env.ContentRootPath);
            var extra = new Dictionary<string, object?> { ["licenseKeyHash"] = "" };

            try
            {
                var url = _serverUrl + "/api/verify/Heartbeat";
                var content = new StringContent(identity.ToJson(extra), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(body);
                    var data = doc.RootElement.GetProperty("data");
                    var status = data.GetProperty("status").GetString();

                    if (status == "revoked")
                    {
                        LogWarning("License has been revoked by server. Refreshing license status.");
                        _licenseService.Refresh();
                        _consecutiveFailures = 0;
                        return;
                    }

                    if (status == "active")
                    {
                        _lastSuccessfulHeartbeat = AppTime.UtcNow;
                        _consecutiveFailures = 0;
                        WriteHeartbeatTime(_lastSuccessfulHeartbeat.Value);
                        LogDebug("Heartbeat successful.");
                        return;
                    }

                    // unknown or other status
                    LogWarning($"Heartbeat returned status: {status}");
                    _consecutiveFailures++;
                }
                else
                {
                    LogWarning($"Heartbeat request failed with status code: {response.StatusCode}");
                    _consecutiveFailures++;
                }
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                LogWarning($"Heartbeat request failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查是否超过宽限期
        /// </summary>
        public bool IsGracePeriodExceeded()
        {
            if (_lastSuccessfulHeartbeat == null)
                return true;

            return (AppTime.UtcNow - _lastSuccessfulHeartbeat.Value).TotalHours > _graceHours;
        }

        /// <summary>
        /// 从文件读取上次心跳时间（HMAC 校验防篡改）
        /// </summary>
        private static DateTime? ReadHeartbeatTime()
        {
            try
            {
                if (!File.Exists(_heartbeatFilePath)) return null;

                var content = File.ReadAllText(_heartbeatFilePath).Trim();
                var parts = content.Split('.');
                if (parts.Length != 2) return null;

                var ticks = parts[0];
                var storedHmac = parts[1];

                using var hmac = new HMACSHA256(_hmacKey);
                var expectedHmac = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(ticks))).ToLowerInvariant();
                if (storedHmac != expectedHmac) return null; // 文件被篡改

                return new DateTime(long.Parse(ticks), DateTimeKind.Utc);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 持久化心跳时间到文件（HMAC 签名保护）
        /// </summary>
        private static void WriteHeartbeatTime(DateTime utcTime)
        {
            try
            {
                var ticks = utcTime.Ticks.ToString();
                using var hmac = new HMACSHA256(_hmacKey);
                var mac = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(ticks))).ToLowerInvariant();
                File.WriteAllText(_heartbeatFilePath, $"{ticks}.{mac}");
            }
            catch
            {
                // 写入失败不影响运行
            }
        }

    }
}
