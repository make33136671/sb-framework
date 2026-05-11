using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 邮件SMTP配置
    /// </summary>
    [Table(Name = "SB_EmailConfig")]
    public class EmailConfig : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "SmtpServer")]
        public string SmtpServer { get; set; }

        [Column(Name = "SmtpPort")]
        public int SmtpPort { get; set; }

        [Column(Name = "Username")]
        public string Username { get; set; }

        [Column(Name = "Password")]
        public string Password { get; set; }

        [Column(Name = "FromEmail")]
        public string FromEmail { get; set; }

        [Column(Name = "FromName")]
        public string FromName { get; set; }

        [Column(Name = "EnableSsl")]
        public bool EnableSsl { get; set; }

        [Column(Name = "IsActive")]
        public bool IsActive { get; set; }

        [Column(Name = "CreatedAt", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
        public DateTime CreatedAt { get; set; }

        [Column(Name = "UpdatedAt", DbType = "timestamp")]
        public DateTime UpdatedAt { get; set; }
    }
}
