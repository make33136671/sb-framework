using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 邮件发送日志
    /// </summary>
    [Table(Name = "SB_EmailLog")]
    public class EmailLog : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "TemplateId")]
        public int TemplateId { get; set; }

        [Column(Name = "ToEmail")]
        public string ToEmail { get; set; } = null!;

        [Column(Name = "Subject")]
        public string Subject { get; set; } = null!;

        [Column(Name = "Body", DbType = "text")]
        public string Body { get; set; } = null!;

        [Column(Name = "Status", MapType = typeof(string))]
        public EmailStatus Status { get; set; }

        [Column(Name = "ErrorMessage", IsNullable = true)]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 使用的邮件提供商
        /// </summary>
        [Column(Name = "Provider", StringLength = 50, IsNullable = true)]
        public string? Provider { get; set; }

        [Column(Name = "SentAt", DbType = "timestamp", IsNullable = true)]
        public DateTime? SentAt { get; set; }

        [Column(Name = "CreatedAt", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
        public DateTime CreatedAt { get; set; }
    }
}

