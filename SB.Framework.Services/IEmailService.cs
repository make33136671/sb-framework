using SB.Framework.Models;

namespace SB.Framework.Services
{
    /// <summary>
    /// 邮件服务接口 — 只负责发送邮件
    /// </summary>
    public interface IEmailService
    {
        Task<Result> SendEmailAsync(string to, string subject, string body, bool isHtml = true);
        Task<Result> SendTemplateEmailAsync(string to, string templateCode, Dictionary<string, string> parameters);
    }
}
