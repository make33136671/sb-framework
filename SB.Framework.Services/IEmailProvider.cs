using SB.Framework.Models;

namespace SB.Framework.Services
{
    public interface IEmailProvider
    {
        Task<Result> SendEmailAsync(string to, string subject, string body, bool isHtml = true);
        Task<bool> IsConfiguredAsync();
    }
}
