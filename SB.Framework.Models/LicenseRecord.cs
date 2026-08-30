using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 授权记录（管理员审批/手动生成的授权文件）
    /// </summary>
    [Table(Name = "SB_License_Record")]
    public class LicenseRecord : IEntity
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
        /// 验证模式（0=仅ClientKey, 1=仅HardwareId, 2=双验证）
        /// </summary>
        [Column(Name = "VerifyMode")]
        public LicenseVerifyMode VerifyMode { get; set; } = LicenseVerifyMode.Both;

        /// <summary>
        /// 绑定域名列表 (JSON 数组, 如 ["*.example.com", "api.test.com"]).
        /// 为 null/空 表示不验证域名.
        /// </summary>
        [Column(Name = "Domains", DbType = "text", IsNullable = true)]
        public string? Domains { get; set; }

        /// <summary>
        /// 签发时绑定的产品英文名. 为 null/空 表示不验证产品.
        /// </summary>
        [Column(Name = "ProductEn", StringLength = 100, IsNullable = true)]
        public string? ProductEn { get; set; }

        /// <summary>
        /// 绑定 ClientKey
        /// </summary>
        [Column(Name = "ClientKey", StringLength = 100, IsNullable = true)]
        public string? ClientKey { get; set; }

        /// <summary>
        /// 绑定 HardwareId
        /// </summary>
        [Column(Name = "HardwareId", StringLength = 200, IsNullable = true)]
        public string? HardwareId { get; set; }

        /// <summary>
        /// 签发时间
        /// </summary>
        [Column(Name = "IssuedAt", DbType = "timestamp")]
        public DateTime IssuedAt { get; set; }

        /// <summary>
        /// 到期时间
        /// </summary>
        [Column(Name = "ExpiresAt", DbType = "timestamp")]
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// 到期后仍可访问的路由前缀 JSON（如 ["/admin/"]），为空表示全部拦截
        /// </summary>
        [Column(Name = "AllowedRoutes", StringLength = 2000, IsNullable = true)]
        public string? AllowedRoutes { get; set; }

        /// <summary>
        /// 到期后是否仍允许访问管理后台 (/admin/* 非白名单路径)
        /// </summary>
        [Column(Name = "AllowAdminAfterExpiry")]
        public bool AllowAdminAfterExpiry { get; set; } = false;

        /// <summary>
        /// 到期后是否仍允许访问客户端 API (/api/* 非白名单路径)
        /// </summary>
        [Column(Name = "AllowClientApiAfterExpiry")]
        public bool AllowClientApiAfterExpiry { get; set; } = false;

        /// <summary>
        /// 签名后的授权文件内容
        /// </summary>
        [Column(Name = "LicenseData", DbType = "text")]
        public string LicenseData { get; set; } = null!;

        /// <summary>
        /// 客户端是否已下载
        /// </summary>
        [Column(Name = "IsDownloaded")]
        public bool IsDownloaded { get; set; } = false;

        /// <summary>
        /// 状态（0=有效, 1=已吊销）
        /// </summary>
        [Column(Name = "Status")]
        public int Status { get; set; } = 0;

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
        public DateTime CreatedTime { get; set; }
    }

    /// <summary>
    /// 授权验证模式
    /// </summary>
    public enum LicenseVerifyMode
    {
        /// <summary>
        /// 仅验证 ClientKey
        /// </summary>
        ClientKeyOnly = 0,

        /// <summary>
        /// 仅验证 HardwareId
        /// </summary>
        HardwareIdOnly = 1,

        /// <summary>
        /// 双验证（ClientKey + HardwareId）
        /// </summary>
        Both = 2
    }
}

