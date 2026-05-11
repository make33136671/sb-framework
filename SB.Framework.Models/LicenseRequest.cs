using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 授权申请（客户端发起的授权请求）
    /// </summary>
    [Table(Name = "SB_License_Request")]
    public class LicenseRequest : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 关联网站客户
        /// </summary>
        [Column(Name = "ClientId")]
        public int ClientId { get; set; }
        [Navigate(nameof(ClientId))]
        public LicenseClient Client { get; set; }

        /// <summary>
        /// 申请时的域名
        /// </summary>
        [Column(Name = "Domain", StringLength = 500)]
        public string Domain { get; set; }

        /// <summary>
        /// 申请时的服务器 IP
        /// </summary>
        [Column(Name = "ServerIP", StringLength = 100, IsNullable = true)]
        public string? ServerIP { get; set; }

        /// <summary>
        /// 申请时的 ClientKey
        /// </summary>
        [Column(Name = "ClientKey", StringLength = 100, IsNullable = true)]
        public string? ClientKey { get; set; }

        /// <summary>
        /// 申请时的 HardwareId
        /// </summary>
        [Column(Name = "HardwareId", StringLength = 200, IsNullable = true)]
        public string? HardwareId { get; set; }

        /// <summary>
        /// 状态（0=待审批, 1=已批准, 2=已拒绝）
        /// </summary>
        [Column(Name = "Status")]
        public LicenseRequestStatus Status { get; set; } = LicenseRequestStatus.Pending;

        /// <summary>
        /// 拒绝原因
        /// </summary>
        [Column(Name = "RejectReason", StringLength = 500, IsNullable = true)]
        public string? RejectReason { get; set; }

        /// <summary>
        /// 批准后关联的授权记录 Id
        /// </summary>
        [Column(Name = "RecordId", IsNullable = true)]
        public int? RecordId { get; set; }
        [Navigate(nameof(RecordId))]
        public LicenseRecord Record { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 处理时间
        /// </summary>
        [Column(Name = "ProcessedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? ProcessedTime { get; set; }
    }

    /// <summary>
    /// 授权申请状态
    /// </summary>
    public enum LicenseRequestStatus
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2
    }
}
