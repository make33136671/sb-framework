using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SB.Framework.Core;
using SB.Framework.Core.License;
using SB.Framework.Core.Util;

namespace SB.Framework.Services
{
    /// <summary>
    /// 节流的异步吊销查询客户端.
    /// DI 生命周期: Scoped (在 LicenseSetup.cs 手动注册为 ILicenseVerifyClient).
    /// 内部状态 (_nextAllowedAt / _lock / _http) 用 static 字段确保进程级共享.
    /// - 每请求触发, 成功后 1 小时节流; 失败 60 秒短冷却
    /// - 3 秒超时, 任何异常静默吞掉, 不影响当前 HTTP 请求
    /// - 响应 revoked=true 时清空 appsettings 里的 Key 并 Refresh 内存状态
    /// - 无 CloudConfig.ServerUrl 配置时直接跳过, 不消耗节流窗口
    /// </summary>
    public class LicenseVerifyClient : ILicenseVerifyClient
    {
        // 进程级共享 HttpClient, 避免 scoped 实例每请求 new HttpClient 耗 socket.
        // 和 LicenseClientService 同样的做法.
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

        // 进程级共享节流窗口
        private static DateTime _nextAllowedAt = DateTime.MinValue;
        private static readonly object _lock = new();

        private readonly ILicenseService _licenseService;
        private readonly LicenseClientService _licenseClientService;
        private readonly IHostEnvironment _env;
        private readonly ILogger<LicenseVerifyClient> _logger;

        public LicenseVerifyClient(
            ILicenseService licenseService,
            LicenseClientService licenseClientService,
            IHostEnvironment env,
            ILogger<LicenseVerifyClient> logger)
        {
            _licenseService = licenseService;
            _licenseClientService = licenseClientService;
            _env = env;
            _logger = logger;
        }

        public Task TryVerifyAsync(HttpContext ctx)
        {
            var serverUrl = AppGlobal.AppSettings?.GetCloudServerUrl();
            if (string.IsNullOrWhiteSpace(serverUrl)) return Task.CompletedTask;

            lock (_lock)
            {
                if (AppTime.UtcNow < _nextAllowedAt) return Task.CompletedTask;
                _nextAllowedAt = AppTime.UtcNow.AddHours(1);  // 先占位: 成功则保持, 失败下面短冷却覆盖
            }

            // 从 HttpContext 先同步取值, fire-and-forget 里不再持有 ctx
            var actualDomain = ctx.Request.Host.Value ?? "";

            _ = Task.Run(() => DoVerifyAsync(serverUrl, actualDomain));
            return Task.CompletedTask;
        }

        private async Task DoVerifyAsync(string serverUrl, string actualDomain)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

                // 统一用 ClientIdentityPayload, actualDomain/currentKey/licenseValid 通过 extra 追加
                var identity = ClientIdentityPayload.Collect(_env.ContentRootPath);
                var currentKey = AppGlobal.AppSettings?.LicenseConfig?.Key ?? "";
                var extra = new Dictionary<string, string?>
                {
                    ["actualDomain"] = actualDomain,
                    ["currentKey"] = currentKey,
                    ["licenseValid"] = "true",
                };

                var url = $"{serverUrl.TrimEnd('/')}/api/verify/Verify?" + identity.ToQueryString(extra);
                var resp = await _http.PostAsync(url, null, cts.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    RetryAfter(TimeSpan.FromMinutes(1));
                    return;
                }

                var body = await resp.Content.ReadAsStringAsync(cts.Token);
                var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Object
                    && data.TryGetProperty("revoked", out var r)
                    && r.ValueKind == JsonValueKind.True)
                {
                    _logger.LogWarning("授权服务器通知当前 Key 已被吊销, 清除本地 Key");
                    _licenseClientService.UpdateLicenseKey("");
                    _licenseService.Refresh("");
                }
                // 成功保持 1h 节流不重置
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Verify 调用失败 (静默降级, 60 秒后重试)");
                RetryAfter(TimeSpan.FromMinutes(1));
            }
        }

        private static void RetryAfter(TimeSpan cooldown)
        {
            lock (_lock) { _nextAllowedAt = AppTime.UtcNow + cooldown; }
        }
    }
}
