using SB.Framework.Models;

namespace SB.Framework.Services
{
    /// <summary>
    /// 验证码服务接口 — 独立于邮件发送
    /// </summary>
    public interface IVerificationCodeService
    {
        Task<Result> SendVerificationCodeAsync(string email, VerificationCodeType type);
        Task<Result> VerifyCodeAsync(string email, string code, VerificationCodeType type);
        Task CleanExpiredCodesAsync();
    }
}
