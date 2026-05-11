using FreeSql.DataAnnotations;

namespace SB.Framework.Models
{
    /// <summary>
    /// 部署记录
    /// </summary>
    [Table(Name = "SB_DeployRecord")]
    public class DeployRecord : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 部署目标：api 或 DeployConfig 中的 key
        /// </summary>
        [Column(Name = "Target", StringLength = 50)]
        public string Target { get; set; } = "";

        /// <summary>
        /// 显示名称，如 "API程序" "Admin管理后台"
        /// </summary>
        [Column(Name = "TargetName", StringLength = 100)]
        public string TargetName { get; set; } = "";

        /// <summary>
        /// 备份文件路径（相对于 deploy_backups 目录）
        /// </summary>
        [Column(Name = "BackupPath", StringLength = 500)]
        public string BackupPath { get; set; } = "";

        /// <summary>
        /// 上传的文件名
        /// </summary>
        [Column(Name = "FileName", StringLength = 200)]
        public string FileName { get; set; } = "";

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        [Column(Name = "FileSize")]
        public long FileSize { get; set; }

        /// <summary>
        /// 操作人
        /// </summary>
        [Column(Name = "Operator", StringLength = 100)]
        public string Operator { get; set; } = "";

        /// <summary>
        /// 备注
        /// </summary>
        [Column(Name = "Remark", StringLength = 500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }
    }
}
