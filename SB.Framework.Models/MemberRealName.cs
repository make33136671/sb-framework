using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 实名认证
    /// </summary>
    [Table(Name = "SB_MemberRealName")]
    public class MemberRealName : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "MemberId")]
        public int MemberId { get; set; }

        [Column(Name = "RealName")]
        public string RealName { get; set; } = null!;

        [Column(Name = "IdNumber")]
        public string IdNumber { get; set; } = null!;

        [Column(Name = "IdFrontImage")]
        public string IdFrontImage { get; set; } = null!;

        [Column(Name = "IdBackImage")]
        public string IdBackImage { get; set; } = null!;

        [Column(Name = "Status", MapType = typeof(string))]
        public RealNameStatus Status { get; set; } = RealNameStatus.Reviewing;

        [Column(Name = "RejectReason", IsNullable = true)]
        public string? RejectReason { get; set; }

        [Column(Name = "SubmitTime", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
        public DateTime SubmitTime { get; set; }

        [Column(Name = "ReviewTime", IsNullable = true)]
        public DateTime? ReviewTime { get; set; }

        [Column(Name = "ReviewedBy", IsNullable = true)]
        public int? ReviewedBy { get; set; }
    }
}

