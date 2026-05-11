namespace SB.Framework.Services
{
    public interface IEmailRateLimitService
    {
        (bool allowed, string? message) CheckSendLimit(string email);
        (bool allowed, string? message) CheckVerifyLimit(int codeId);
        void RecordSend(string email);
        void RecordVerify(int codeId);
        void ClearSendRecord(string email);
    }
}
