namespace SB.Framework.Models
{
    /// <summary>
    /// 会员状态
    /// </summary>
    public enum UserStatus
    {
        Active = 0,
        Banned = 1,
    }

    /// <summary>
    /// 实名认证状态
    /// </summary>
    public enum RealNameStatus
    {
        Unverified = 0,
        Reviewing = 1,
        Verified = 2,
        Rejected = 3
    }

    /// <summary>
    /// 邮件发送状态
    /// </summary>
    public enum EmailStatus
    {
        Pending = 0,
        Sent = 1,
        Failed = 2
    }

    /// <summary>
    /// 验证码类型
    /// </summary>
    public enum VerificationCodeType
    {
        Register = 0,
        ResetPassword = 1
    }
}
