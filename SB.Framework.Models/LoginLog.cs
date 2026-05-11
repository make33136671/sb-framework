using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 登录日志
    /// </summary>
    [Table(Name = "SB_LoginLog")]
    public class LoginLog : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// Member.Id 或 Admin.Id
        /// </summary>
        [Column(Name = "UserId")]
        public int UserId { get; set; }

        /// <summary>
        /// 0=H5会员, 1=Admin管理员
        /// </summary>
        [Column(Name = "UserType")]
        public int UserType { get; set; }

        [Column(Name = "Username", StringLength = 100)]
        public string Username { get; set; }

        [Column(Name = "IpAddress", StringLength = 50, IsNullable = true)]
        public string? IpAddress { get; set; }

        [Column(Name = "UserAgent", StringLength = 500, IsNullable = true)]
        public string? UserAgent { get; set; }

        /// <summary>
        /// 0=成功, 1=失败
        /// </summary>
        [Column(Name = "Status")]
        public int Status { get; set; }

        [Column(Name = "CreateTime", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
        public DateTime CreateTime { get; set; }
    }
}
