using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class GoogleSmtpEmailProvider : IEmailProvider
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<GoogleSmtpEmailProvider> _logger;

        public GoogleSmtpEmailProvider(IFreeSql fsql, ILogger<GoogleSmtpEmailProvider> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        public async Task<Result> SendEmailAsync(string to, string subject, string body, bool isHtml = true)
        {
            try
            {
                var config = await GetActiveConfigAsync();
                if (config == null)
                    return Result.Fail("Google SMTP 未配置");

                var message = new MailMessage
                {
                    From = new MailAddress(config.FromEmail, config.FromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = isHtml,
                };
                message.To.Add(to);

                using var client = new SmtpClient(config.SmtpServer, config.SmtpPort)
                {
                    EnableSsl = config.EnableSsl,
                    Credentials = new NetworkCredential(config.Username, config.Password),
                    Timeout = 30000
                };

                await client.SendMailAsync(message);
                _logger.LogInformation("Google SMTP 邮件已发送至 {To}", to);
                return Result.Success("邮件发送成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google SMTP 发送邮件到 {To} 失败", to);
                return Result.Fail($"邮件发送失败: {ex.Message}");
            }
        }

        public async Task<bool> IsConfiguredAsync()
        {
            var config = await GetActiveConfigAsync();
            return config != null && !string.IsNullOrEmpty(config.SmtpServer)
                && !string.IsNullOrEmpty(config.Username);
        }

        private async Task<EmailConfig?> GetActiveConfigAsync()
        {
            return await _fsql.Select<EmailConfig>()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Id)
                .FirstAsync();
        }
    }
}
