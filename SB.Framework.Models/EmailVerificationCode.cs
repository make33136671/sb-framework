using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 邮箱验证码
    /// </summary>
    [Table(Name = "SB_EmailVerificationCode")]
    public class EmailVerificationCode : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "Email")]
        public string Email { get; set; }

        [Column(Name = "Code")]
        public string Code { get; set; }

        [Column(Name = "Type")]
        public VerificationCodeType Type { get; set; }

        [Column(Name = "VerifyCount")]
        public int VerifyCount { get; set; }

        /// <summary>
        /// 是否已使用
        /// </summary>
        [Column(Name = "IsUsed")]
        public bool IsUsed { get; set; }

        /// <summary>
        /// 验证成功时间
        /// </summary>
        [Column(Name = "VerifiedAt", IsNullable = true)]
        public DateTime? VerifiedAt { get; set; }

        /// <summary>
        /// 请求IP地址
        /// </summary>
        [Column(Name = "IpAddress", StringLength = 50, IsNullable = true)]
        public string? IpAddress { get; set; }

        [Column(Name = "ExpireAt", DbType = "timestamp")]
        public DateTime ExpireAt { get; set; }

        [Column(Name = "CreatedAt", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
        public DateTime CreatedAt { get; set; }
    }
}
