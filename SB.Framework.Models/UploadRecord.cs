using FreeSql.DataAnnotations;

namespace SB.Framework.Models
{
    /// <summary>
    /// 上传记录：文件上传后持久保存，deploy 失败时可凭 Id 重试，不需要重新上传
    /// </summary>
    [Table(Name = "SB_UploadRecord")]
    public class UploadRecord : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 部署目标：api 或 DeployConfig 中的 key（如 admin、h5）
        /// </summary>
        [Column(Name = "Target", StringLength = 50)]
        public string Target { get; set; } = "";

        /// <summary>
        /// 目标显示名称，如 "API程序" "Admin管理后台"
        /// </summary>
        [Column(Name = "TargetName", StringLength = 100)]
        public string TargetName { get; set; } = "";

        /// <summary>
        /// 原始文件名
        /// </summary>
        [Column(Name = "FileName", StringLength = 200)]
        public string FileName { get; set; } = "";

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        [Column(Name = "FileSize")]
        public long FileSize { get; set; }

        /// <summary>
        /// 持久化存储路径（相对于 upload_staging 目录）
        /// </summary>
        [Column(Name = "StoragePath", StringLength = 500)]
        public string StoragePath { get; set; } = "";

        /// <summary>
        /// 状态：Pending / Deployed / Failed
        /// </summary>
        [Column(Name = "Status", StringLength = 20)]
        public string Status { get; set; } = UploadRecordStatus.Pending;

        /// <summary>
        /// 上传人
        /// </summary>
        [Column(Name = "UploadedBy", StringLength = 100)]
        public string UploadedBy { get; set; } = "";

        /// <summary>
        /// 上传时间
        /// </summary>
        [Column(Name = "UploadTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime UploadTime { get; set; }

        /// <summary>
        /// 部署时间（Deployed/Failed 时写入）
        /// </summary>
        [Column(Name = "DeployTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? DeployTime { get; set; }

        /// <summary>
        /// 部署失败原因（Status = Failed 时写入）
        /// </summary>
        [Column(Name = "DeployError", StringLength = 1000, IsNullable = true)]
        public string? DeployError { get; set; }
    }

    public static class UploadRecordStatus
    {
        /// <summary>上传完成，等待部署</summary>
        public const string Pending = "Pending";
        /// <summary>已成功部署</summary>
        public const string Deployed = "Deployed";
        /// <summary>部署失败，文件仍在，可重试</summary>
        public const string Failed = "Failed";
    }
}
