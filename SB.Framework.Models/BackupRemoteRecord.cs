#if ENABLE_BACKUP_RECEIVE
using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 云端备份接收记录（服务端角色独有，ENABLE_BACKUP_RECEIVE 编译宏开启时建表）
    /// 客户端通过 POST /api/BackupReceive/Upload 上传时，服务端落盘 + 写一条本表记录
    /// 不开放删除接口（防黑客通过应用层删云端备份）
    /// </summary>
    [Table(Name = "SB_BackupRemoteRecord")]
    public class BackupRemoteRecord : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>客户端产品英文名（来自客户端 ProductEn 配置）</summary>
        [Column(Name = "ProductEn", StringLength = 50)]
        public string ProductEn { get; set; } = "";

        /// <summary>客户端产品中文名</summary>
        [Column(Name = "ProductCn", StringLength = 100)]
        public string ProductCn { get; set; } = "";

        /// <summary>客户端配置的主域名（如有）</summary>
        [Column(Name = "PrimaryDomain", StringLength = 200, IsNullable = true)]
        public string? PrimaryDomain { get; set; }

        /// <summary>客户端 Logo 在服务端的本地相对路径，如 /storage/remote-logos/{productEn}.png</summary>
        [Column(Name = "LogoPath", StringLength = 500, IsNullable = true)]
        public string? LogoPath { get; set; }

        /// <summary>上传请求的来源 IP（透过 X-Forwarded-For 兼容反代）</summary>
        [Column(Name = "ClientIp", StringLength = 64)]
        public string ClientIp { get; set; } = "";

        /// <summary>客户端原始备份文件名</summary>
        [Column(Name = "FileName", StringLength = 200)]
        public string FileName { get; set; } = "";

        /// <summary>服务端落盘相对路径，如 /storage/remote-backups/{productEn}/{ip}/{filename}</summary>
        [Column(Name = "FilePath", StringLength = 500)]
        public string FilePath { get; set; } = "";

        /// <summary>文件大小（字节）</summary>
        [Column(Name = "FileSize")]
        public long FileSize { get; set; }

        /// <summary>客户端附带的 MD5 哈希</summary>
        [Column(Name = "Md5", StringLength = 64, IsNullable = true)]
        public string? Md5 { get; set; }

        /// <summary>客户端备份完成时间 (UTC)</summary>
        [Column(Name = "BackupTime")]
        public DateTime BackupTime { get; set; }

        /// <summary>服务端接收时间 (UTC)</summary>
        [Column(Name = "ReceivedTime", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
        public DateTime ReceivedTime { get; set; }
    }
}
#endif
