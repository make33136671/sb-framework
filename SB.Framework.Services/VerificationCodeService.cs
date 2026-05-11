using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;
using SB.Framework.Models;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class VerificationCodeService : IVerificationCodeService
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<VerificationCodeService> _logger;
        private readonly IEmailRateLimitService _rateLimitService;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;

        private const int CODE_EXPIRY_MINUTES = 5;
        private const int CODE_LENGTH = 6;

        public VerificationCodeService(
            IFreeSql fsql,
            ILogger<VerificationCodeService> logger,
            IEmailRateLimitService rateLimitService,
            IEmailService emailService,
            IConfiguration configuration)
        {
            _fsql = fsql;
            _logger = logger;
            _rateLimitService = rateLimitService;
            _emailService = emailService;
            _configuration = configuration;
        }

        public async Task<Result> SendVerificationCodeAsync(string email, VerificationCodeType type)
        {
            if (!IsValidEmail(email))
                return Result.Fail("邮箱格式不正确");

            var (allowed, message) = _rateLimitService.CheckSendLimit(email);
            if (!allowed)
                return Result.Fail(message ?? "发送过于频繁");

            try
            {
                var code = GenerateVerificationCode();

                var verificationCode = new EmailVerificationCode
                {
                    Email = email.ToLower().Trim(),
                    Code = code,
                    Type = type,
                    VerifyCount = 0,
                    IsUsed = false,
                    ExpireAt = AppTime.UtcNow.AddMinutes(CODE_EXPIRY_MINUTES),
                    CreatedAt = AppTime.UtcNow
                };

                await _fsql.Insert(verificationCode).ExecuteAffrowsAsync();

                // 获取品牌名
                var appName = _configuration["ProductCn"] ?? "System";

                // 尝试使用模板发送
                var templateCodeStr = type == VerificationCodeType.Register ? "RegisterVerify" : "ResetPasswordVerify";
                var template = await _fsql.Select<EmailTemplate>()
                    .Where(t => t.Code == templateCodeStr && t.IsActive)
                    .FirstAsync();

                string subject;
                string body;

                if (template != null)
                {
                    var parameters = new Dictionary<string, string>
                    {
                        { "code", code },
                        { "email", email },
                        { "expiryMinutes", CODE_EXPIRY_MINUTES.ToString() },
                        { "appName", appName }
                    };
                    subject = RenderTemplate(template.Subject, parameters);
                    body = RenderTemplate(template.Body, parameters);
                }
                else
                {
                    var typeName = type == VerificationCodeType.Register ? "注册" : "重置密码";
                    subject = $"{appName} - {typeName}验证码";
                    body = $@"
                        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                            <h2 style='color: #333;'>{appName} {typeName}验证码</h2>
                            <p style='color: #666; font-size: 16px;'>您的验证码是：</p>
                            <div style='background: #f5f5f5; padding: 20px; text-align: center; margin: 20px 0;'>
                                <span style='font-size: 32px; font-weight: bold; color: #333; letter-spacing: 8px;'>{code}</span>
                            </div>
                            <p style='color: #999; font-size: 14px;'>验证码 {CODE_EXPIRY_MINUTES} 分钟内有效，请勿泄露给他人。</p>
                            <p style='color: #999; font-size: 12px;'>如非本人操作，请忽略此邮件。</p>
                        </div>";
                }

                var result = await _emailService.SendEmailAsync(email, subject, body, true);

                if (result.Code == 0)
                {
                    _rateLimitService.RecordSend(email);
                    _logger.LogInformation("验证码已发送至 {Email}，类型: {Type}", email, type);
                }
                else
                {
                    await _fsql.Delete<EmailVerificationCode>()
                        .Where(v => v.Id == verificationCode.Id)
                        .ExecuteAffrowsAsync();
                }

                return result.Code == 0
                    ? Result.Success("验证码已发送")
                    : Result.Fail($"验证码发送失败: {result.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送验证码到 {Email} 失败", email);
                return Result.Fail($"发送验证码失败: {ex.Message}");
            }
        }

        public async Task<Result> VerifyCodeAsync(string email, string code, VerificationCodeType type)
        {
            try
            {
                var normalizedEmail = email.ToLower().Trim();

                var verificationCode = await _fsql.Select<EmailVerificationCode>()
                    .Where(v => v.Email == normalizedEmail
                        && v.Code == code
                        && v.Type == type
                        && !v.IsUsed
                        && v.ExpireAt > AppTime.UtcNow)
                    .OrderByDescending(v => v.CreatedAt)
                    .FirstAsync();

                if (verificationCode == null)
                    return Result.Fail("验证码无效或已过期");

                var (allowed, message) = _rateLimitService.CheckVerifyLimit(verificationCode.Id);
                if (!allowed)
                    return Result.Fail(message ?? "验证次数过多");

                // 验证成功: 标记为已使用（保留审计记录）
                verificationCode.IsUsed = true;
                verificationCode.VerifiedAt = AppTime.UtcNow;
                await _fsql.Update<EmailVerificationCode>()
                    .Set(v => v.IsUsed, true)
                    .Set(v => v.VerifiedAt, AppTime.UtcNow)
                    .Where(v => v.Id == verificationCode.Id)
                    .ExecuteAffrowsAsync();

                _logger.LogInformation("邮箱 {Email} 验证码验证成功", email);
                return Result.Success("验证成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证 {Email} 验证码失败", email);
                return Result.Fail($"验证失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 清理过期验证码
        /// </summary>
        public async Task CleanExpiredCodesAsync()
        {
            var count = await _fsql.Delete<EmailVerificationCode>()
                .Where(v => v.ExpireAt < AppTime.UtcNow.AddDays(-7))
                .ExecuteAffrowsAsync();

            if (count > 0)
                _logger.LogInformation("已清理 {Count} 条过期验证码", count);
        }

        private string GenerateVerificationCode()
        {
            return Random.Shared.Next(0, 1000000).ToString("D6");
        }

        private bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;
            return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
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
    }
}
