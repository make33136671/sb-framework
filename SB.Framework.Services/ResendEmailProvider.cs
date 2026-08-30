using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class ResendEmailProvider : IEmailProvider
    {
        private readonly ILogger<ResendEmailProvider> _logger;
        private readonly IFreeSql _fsql;

        private const string RESEND_API_BASE_URL = "https://api.resend.com";

        public ResendEmailProvider(ILogger<ResendEmailProvider> logger, IFreeSql fsql)
        {
            _logger = logger;
            _fsql = fsql;
        }

        public async Task<Result> SendEmailAsync(string to, string subject, string body, bool isHtml = true)
        {
            try
            {
                var config = await GetConfigAsync();
                if (config == null || string.IsNullOrEmpty(config.ApiKey))
                    return Result.Fail("Resend API 未配置");

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
                client.Timeout = TimeSpan.FromSeconds(30);

                var payload = new
                {
                    from = $"{config.FromName} <{config.FromEmail}>",
                    to = new[] { to },
                    subject = subject,
                    html = isHtml ? body : null,
                    text = !isHtml ? body : null
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync($"{RESEND_API_BASE_URL}/emails", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Resend 邮件已发送至 {To}", to);
                    return Result.Success("邮件发送成功");
                }
                else
                {
                    _logger.LogError("Resend API 调用失败: {Status} - {Content}", response.StatusCode, responseContent);
                    return Result.Fail($"邮件发送失败: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Resend 发送邮件到 {To} 失败", to);
                return Result.Fail($"邮件发送失败: {ex.Message}");
            }
        }

        public async Task<bool> IsConfiguredAsync()
        {
            var config = await GetConfigAsync();
            return config != null && !string.IsNullOrEmpty(config.ApiKey)
                && !string.IsNullOrEmpty(config.FromEmail);
        }

        private async Task<ResendConfig?> GetConfigAsync()
        {
            var apiKeySetting = await _fsql.Select<SystemSetting>()
                .Where(s => s.Key == "Email_ResendApiKey").FirstAsync();
            var fromEmailSetting = await _fsql.Select<SystemSetting>()
                .Where(s => s.Key == "Email_ResendFromEmail").FirstAsync();
            var fromNameSetting = await _fsql.Select<SystemSetting>()
                .Where(s => s.Key == "Email_ResendFromName").FirstAsync();

            var apiKey = apiKeySetting?.DataContent;
            var fromEmail = fromEmailSetting?.DataContent;
            var fromName = fromNameSetting?.DataContent;

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(fromEmail))
                return null;

            return new ResendConfig
            {
                ApiKey = apiKey,
                FromEmail = fromEmail,
                FromName = fromName ?? "System"
            };
        }

        private class ResendConfig
        {
            public string ApiKey { get; set; } = null!;
            public string FromEmail { get; set; } = null!;
            public string FromName { get; set; } = null!;
        }
    }
}

