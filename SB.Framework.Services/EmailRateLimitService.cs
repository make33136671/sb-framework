using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class EmailRateLimitService : IEmailRateLimitService
    {
        private readonly ICacheService _cacheService;
        private readonly ILogger<EmailRateLimitService> _logger;

        private const string SEND_COUNT_PREFIX = "email_send_count:";
        private const string LAST_SEND_PREFIX = "email_last_send:";
        private const string VERIFY_COUNT_PREFIX = "email_verify_count:";

        private const int MIN_INTERVAL_SECONDS = 300;
        private const int MAX_SENDS_PER_DAY = 3;
        private const int MAX_VERIFY_ATTEMPTS = 3;

        public EmailRateLimitService(ICacheService cacheService, ILogger<EmailRateLimitService> logger)
        {
            _cacheService = cacheService;
            _logger = logger;
        }

        public (bool allowed, string? message) CheckSendLimit(string email)
        {
            var normalizedEmail = email.ToLower().Trim();
            var now = AppTime.UtcNow;

            var lastSendKey = LAST_SEND_PREFIX + normalizedEmail;
            var lastSendTime = _cacheService.Get<DateTime?>(lastSendKey);

            if (lastSendTime.HasValue)
            {
                var elapsed = now - lastSendTime.Value;
                if (elapsed.TotalSeconds < MIN_INTERVAL_SECONDS)
                {
                    var remainingSeconds = MIN_INTERVAL_SECONDS - (int)elapsed.TotalSeconds;
                    return (false, $"发送过于频繁，请{remainingSeconds}秒后再试");
                }
            }

            var sendCountKey = SEND_COUNT_PREFIX + normalizedEmail + ":" + now.ToString("yyyyMMdd");
            var sendCount = _cacheService.Get<int>(sendCountKey);

            if (sendCount >= MAX_SENDS_PER_DAY)
                return (false, "今日发送次数已达上限，请明天再试");

            return (true, null);
        }

        public (bool allowed, string? message) CheckVerifyLimit(int codeId)
        {
            var verifyCountKey = VERIFY_COUNT_PREFIX + codeId;
            var verifyCount = _cacheService.Get<int>(verifyCountKey);

            if (verifyCount >= MAX_VERIFY_ATTEMPTS)
                return (false, "验证码错误次数过多，请重新获取");

            return (true, null);
        }

        public void RecordSend(string email)
        {
            var normalizedEmail = email.ToLower().Trim();
            var now = AppTime.UtcNow;

            var lastSendKey = LAST_SEND_PREFIX + normalizedEmail;
            _cacheService.Set(lastSendKey, now, TimeSpan.FromHours(24));

            var sendCountKey = SEND_COUNT_PREFIX + normalizedEmail + ":" + now.ToString("yyyyMMdd");
            var currentCount = _cacheService.Get<int>(sendCountKey);
            _cacheService.Set(sendCountKey, currentCount + 1, TimeSpan.FromHours(24));

            _logger.LogInformation("已记录邮箱 {Email} 的发送操作，今日已发送 {Count} 次", normalizedEmail, currentCount + 1);
        }

        public void RecordVerify(int codeId)
        {
            var verifyCountKey = VERIFY_COUNT_PREFIX + codeId;
            var currentCount = _cacheService.Get<int>(verifyCountKey);
            _cacheService.Set(verifyCountKey, currentCount + 1, TimeSpan.FromMinutes(10));

            _logger.LogInformation("已记录验证码 {CodeId} 的验证尝试，当前次数 {Count}", codeId, currentCount + 1);
        }

        public void ClearSendRecord(string email)
        {
            var normalizedEmail = email.ToLower().Trim();
            var now = AppTime.UtcNow;

            var lastSendKey = LAST_SEND_PREFIX + normalizedEmail;
            var sendCountKey = SEND_COUNT_PREFIX + normalizedEmail + ":" + now.ToString("yyyyMMdd");

            _cacheService.Remove(lastSendKey);
            _cacheService.Remove(sendCountKey);

            _logger.LogInformation("已清除邮箱 {Email} 的发送记录", normalizedEmail);
        }
    }
}
