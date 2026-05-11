using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 网站客户（授权客户端）
    /// </summary>
    [Table(Name = "SB_License_Client")]
    public class LicenseClient : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 产品英文标识
        /// </summary>
        [Column(Name = "ProductEn", StringLength = 200, IsNullable = true)]
        public string? ProductEn { get; set; }

        /// <summary>
        /// 产品中文名称
        /// </summary>
        [Column(Name = "ProductCn", StringLength = 200, IsNullable = true)]
        public string? ProductCn { get; set; }

        /// <summary>
        /// 声明域名 (客户端 AppSettings.PrimaryDomain)
        /// </summary>
        [Column(Name = "Domain", StringLength = 500)]
        public string Domain { get; set; }

        /// <summary>
        /// 最近一次 Verify/Check 上报的 actualDomain (浏览器实际访问 host).
        /// 和 Domain 不一致时 ClientList 会标色提示管理员.
        /// </summary>
        [Column(Name = "LastActualDomain", StringLength = 500, IsNullable = true)]
        public string? LastActualDomain { get; set; }

        /// <summary>
        /// 服务器 IP
        /// </summary>
        [Column(Name = "ServerIP", StringLength = 100, IsNullable = true)]
        public string? ServerIP { get; set; }

        /// <summary>
        /// 客户端密钥（UUID）
        /// </summary>
        [Column(Name = "ClientKey", StringLength = 100, IsNullable = true)]
        public string? ClientKey { get; set; }

        /// <summary>
        /// 硬件 ID 哈希
        /// </summary>
        [Column(Name = "HardwareId", StringLength = 200, IsNullable = true)]
        public string? HardwareId { get; set; }

        /// <summary>
        /// 机器名
        /// </summary>
        [Column(Name = "MachineName", StringLength = 200, IsNullable = true)]
        public string? MachineName { get; set; }

        /// <summary>
        /// 操作系统
        /// </summary>
        [Column(Name = "OsDescription", StringLength = 500, IsNullable = true)]
        public string? OsDescription { get; set; }

        /// <summary>
        /// CPU 核心数
        /// </summary>
        [Column(Name = "ProcessorCount", IsNullable = true)]
        public int? ProcessorCount { get; set; }

        /// <summary>
        /// MAC 地址（逗号分隔）
        /// </summary>
        [Column(Name = "MacAddresses", StringLength = 200, IsNullable = true)]
        public string? MacAddresses { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        [Column(Name = "Remark", StringLength = 500, IsNullable = true)]
        public string? Remark { get; set; }

        /// <summary>
        /// 状态（0=正常, 1=禁用）
        /// </summary>
        [Column(Name = "Status")]
        public int Status { get; set; } = 0;

        /// <summary>
        /// 软删除标记. true = 已删除 (列表/签发自动过滤).
        /// 和 Status(0=正常/1=禁用) 语义不同: Status 控制"是否可用", IsDeleted 控制"是否展示".
        /// </summary>
        [Column(Name = "IsDeleted")]
        public bool IsDeleted { get; set; } = false;

        /// <summary>
        /// 最后心跳时间
        /// </summary>
        [Column(Name = "LastHeartbeat", DbType = "timestamp", IsNullable = true)]
        public DateTime? LastHeartbeat { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime UpdatedTime { get; set; }
    }
}
