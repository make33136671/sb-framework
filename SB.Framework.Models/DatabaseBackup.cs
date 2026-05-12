using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 数据库备份记录
    /// </summary>
    [Table(Name = "SB_DatabaseBackup")]
    public class DatabaseBackup : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 数据库名称
        /// </summary>
        [Column(Name = "DatabaseName", StringLength = 100)]
        public string DatabaseName { get; set; }

        /// <summary>
        /// 备份文件名
        /// </summary>
        [Column(Name = "FileName", StringLength = 200)]
        public string FileName { get; set; }

        /// <summary>
        /// 文件相对路径 (如 /storage/db/xxx.zip)
        /// </summary>
        [Column(Name = "FilePath", StringLength = 500)]
        public string FilePath { get; set; }

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        [Column(Name = "FileSize")]
        public long FileSize { get; set; }

        /// <summary>
        /// 备份耗时（毫秒）
        /// </summary>
        [Column(Name = "Duration")]
        public int Duration { get; set; }

        /// <summary>
        /// 操作人（管理员用户名 或 MasterKey）
        /// </summary>
        [Column(Name = "CreatedBy", StringLength = 100)]
        public string CreatedBy { get; set; }

        /// <summary>
        /// 备份时间
        /// </summary>
        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        [Column(Name = "Remark", StringLength = 500, IsNullable = true)]
        public string? Remark { get; set; }

        /// <summary>
        /// 备份文件 MD5（用于文件完整性校验、防篡改、客户端去重）
        /// </summary>
        [Column(Name = "Md5", StringLength = 64, IsNullable = true)]
        public string? Md5 { get; set; }

        /// <summary>
        /// 云端上传状态：null=未上传 / "success" / "failed"
        /// </summary>
        [Column(Name = "RemoteUploadStatus", StringLength = 16, IsNullable = true)]
        public string? RemoteUploadStatus { get; set; }

        /// <summary>
        /// 云端上传成功时间 (UTC)
        /// </summary>
        [Column(Name = "RemoteUploadTime", IsNullable = true)]
        public DateTime? RemoteUploadTime { get; set; }

        /// <summary>
        /// 云端上传失败原因
        /// </summary>
        [Column(Name = "RemoteUploadError", StringLength = -2, IsNullable = true)]
        public string? RemoteUploadError { get; set; }

        /// <summary>
        /// 实际上传到的远程 URL (审计 + 多服务端切换时区分)
        /// </summary>
        [Column(Name = "RemoteUploadUrl", StringLength = 500, IsNullable = true)]
        public string? RemoteUploadUrl { get; set; }
    }
}
