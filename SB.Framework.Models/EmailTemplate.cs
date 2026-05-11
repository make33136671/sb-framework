using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 邮件模板
    /// </summary>
    [Table(Name = "SB_EmailTemplate")]
    public class EmailTemplate : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "Code")]
        public string Code { get; set; }

        [Column(Name = "Name")]
        public string Name { get; set; }

        [Column(Name = "Subject")]
        public string Subject { get; set; }

        [Column(Name = "Body", DbType = "text")]
        public string Body { get; set; }

        [Column(Name = "IsActive")]
        public bool IsActive { get; set; }

        [Column(Name = "CreatedAt", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
        public DateTime CreatedAt { get; set; }

        [Column(Name = "UpdatedAt", DbType = "timestamp")]
        public DateTime UpdatedAt { get; set; }
    }
}
