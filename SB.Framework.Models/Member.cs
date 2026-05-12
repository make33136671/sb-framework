using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 会员 (H5前端用户)
    /// </summary>
    [Table(Name = "SB_Member")]
    public class Member : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        [Column(Name = "Username")]
        public string Username { get; set; }

        /// <summary>
        /// 密码
        /// </summary>
        [Column(Name = "Password")]
        public string Password { get; set; }

        /// <summary>
        /// 头像
        /// </summary>
        [Column(Name = "Avatar")]
        public string Avatar { get; set; }

        /// <summary>
        /// 名称
        /// </summary>
        [Column(Name = "Name")]
        public string Name { get; set; }

        /// <summary>
        /// 账户状态
        /// </summary>
        [Column(Name = "Status", MapType = typeof(string))]
        public UserStatus Status { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 最后在线时间
        /// </summary>
        [Column(Name = "LastOnlineTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime LastOnlineTime { get; set; }

        /// <summary>
        /// 支付密码
        /// </summary>
        [Column(Name = "PaymentPassword", IsNullable = true)]
        public string? PaymentPassword { get; set; }

        /// <summary>
        /// 信用分
        /// </summary>
        [Column(Name = "CreditScore")]
        public int CreditScore { get; set; } = 100;

        /// <summary>
        /// 实名认证状态
        /// </summary>
        [Column(Name = "RealNameStatus")]
        public RealNameStatus RealNameStatus { get; set; } = RealNameStatus.Unverified;

        /// <summary>
        /// 邀请码
        /// </summary>
        [Column(Name = "InvitationCode", IsNullable = true)]
        public string? InvitationCode { get; set; }

        /// <summary>
        /// 邀请人ID
        /// </summary>
        [Column(Name = "ParentId", IsNullable = true)]
        public int? ParentId { get; set; }

        /// <summary>
        /// 祖先路径 (格式: /1/5/ 表示上级链)
        /// </summary>
        [Column(Name = "AncestryPath", IsNullable = true, StringLength = 500)]
        public string? AncestryPath { get; set; }

        /// <summary>
        /// 软删除标记
        /// </summary>
        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}
