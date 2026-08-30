using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 授权验证日志（客户端启动时的验证上报）
    /// </summary>
    [Table(Name = "SB_License_VerifyLog")]
    public class LicenseVerifyLog : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 关联网站客户
        /// </summary>
        [Column(Name = "ClientId")]
        public int ClientId { get; set; }
        [Navigate(nameof(ClientId))]
        public LicenseClient Client { get; set; } = null!;

        /// <summary>
        /// 验证时声明域名 (客户端 AppSettings.PrimaryDomain)
        /// </summary>
        [Column(Name = "Domain", StringLength = 500)]
        public string Domain { get; set; } = null!;

        /// <summary>
        /// 客户端浏览器实际访问域名 (window.location.host).
        /// 和 Domain 不一致时提示管理员"客户声明 vs 实际"有差异, 用于决定是否签发多域名授权.
        /// </summary>
        [Column(Name = "ActualDomain", StringLength = 500, IsNullable = true)]
        public string? ActualDomain { get; set; }

        /// <summary>
        /// 验证时服务器 IP
        /// </summary>
        [Column(Name = "ServerIP", StringLength = 100, IsNullable = true)]
        public string? ServerIP { get; set; }

        /// <summary>
        /// 验证时 ClientKey
        /// </summary>
        [Column(Name = "ClientKey", StringLength = 100, IsNullable = true)]
        public string? ClientKey { get; set; }

        /// <summary>
        /// 验证时 HardwareId
        /// </summary>
        [Column(Name = "HardwareId", StringLength = 200, IsNullable = true)]
        public string? HardwareId { get; set; }

        /// <summary>
        /// 本地授权是否有效
        /// </summary>
        [Column(Name = "LicenseValid")]
        public bool LicenseValid { get; set; }

        /// <summary>
        /// 产品英文标识
        /// </summary>
        [Column(Name = "ProductEn", StringLength = 100, IsNullable = true)]
        public string? ProductEn { get; set; }

        /// <summary>
        /// 产品中文名称
        /// </summary>
        [Column(Name = "ProductCn", StringLength = 200, IsNullable = true)]
        public string? ProductCn { get; set; }

        /// <summary>
        /// 授权到期时间
        /// </summary>
        [Column(Name = "ExpiresAt", IsNullable = true)]
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// 验证消息
        /// </summary>
        [Column(Name = "Message", StringLength = 500, IsNullable = true)]
        public string? Message { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
        public DateTime CreatedTime { get; set; }
    }
}

