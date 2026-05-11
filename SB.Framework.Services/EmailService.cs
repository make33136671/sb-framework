using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;
using SB.Framework.Models;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class EmailService : IEmailService, IModuleInitializer
    {
        #region IModuleInitializer

        public string ModuleName => "邮件管理";
        public int InitOrder => 30;

        public Type[] GetEntityTypes() => new[]
        {
            typeof(EmailConfig), typeof(EmailTemplate),
            typeof(EmailVerificationCode), typeof(EmailLog),
        };

        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition
            {
                Name = "Email", Path = "/email", Component = "LAYOUT",
                Title = "邮件管理", Icon = "ion:mail-outline", OrderNo = 80,
                Children = new()
                {
                    new() { Name = "EmailConfig", Path = "/email/config", Component = "/email/config/index", Title = "邮件配置", Icon = "ion:settings-outline", OrderNo = 1 },
                    new() { Name = "EmailTemplate", Path = "/email/template", Component = "/email/template/index", Title = "邮件模板", Icon = "ion:document-text-outline", OrderNo = 2 },
                    new() { Name = "EmailLog", Path = "/email/log", Component = "/email/log/index", Title = "发送日志", Icon = "ion:list-outline", OrderNo = 3 },
                }
            },
        };

        #endregion

        private readonly IFreeSql _fsql;
        private readonly ILogger<EmailService> _logger;
        private readonly IEmailProviderFactory _providerFactory;

        public EmailService(
            IFreeSql fsql,
            ILogger<EmailService> logger,
            IEmailProviderFactory providerFactory)
        {
            _fsql = fsql;
            _logger = logger;
            _providerFactory = providerFactory;
        }

        public async Task<Result> SendEmailAsync(string to, string subject, string body, bool isHtml = true)
        {
            var provider = _providerFactory.GetCurrentProvider();
            var result = await provider.SendEmailAsync(to, subject, body, isHtml);

            var providerType = _providerFactory.GetCurrentProviderType();
            await LogEmailAsync(0, to, subject, body,
                result.Code == 0 ? EmailStatus.Sent : EmailStatus.Failed,
                result.Code == 0 ? null : result.Message,
                providerType.ToString());

            return result;
        }

        public async Task<Result> SendTemplateEmailAsync(string to, string templateCode, Dictionary<string, string> parameters)
        {
            var template = await _fsql.Select<EmailTemplate>()
                .Where(t => t.Code == templateCode && t.IsActive)
                .FirstAsync();

            if (template == null)
                return Result.Fail($"模板 {templateCode} 不存在或未激活");

            var subject = RenderTemplate(template.Subject, parameters);
            var body = RenderTemplate(template.Body, parameters);

            return await SendEmailAsync(to, subject, body, true);
        }

        private string RenderTemplate(string template, Dictionary<string, string> parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return template;

            var result = template;
            foreach (var param in parameters)
            {
                result = result.Replace($"{{{{{param.Key}}}}}", param.Value);
            }
            return result;
        }

        private async Task LogEmailAsync(int templateId, string to, string subject, string body,
            EmailStatus status, string? errorMessage, string? provider = null)
        {
            try
            {
                var log = new EmailLog
                {
                    TemplateId = templateId,
                    ToEmail = to,
                    Subject = subject,
                    Body = body,
                    Status = status,
                    ErrorMessage = errorMessage,
                    Provider = provider,
                    SentAt = status == EmailStatus.Sent ? AppTime.UtcNow : null,
                    CreatedAt = AppTime.UtcNow
                };

                await _fsql.Insert(log).ExecuteAffrowsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录邮件日志失败");
            }
        }
    }
}
