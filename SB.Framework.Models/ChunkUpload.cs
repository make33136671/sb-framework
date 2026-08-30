using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 分片上传会话
    /// </summary>
    [Table(Name = "SB_ChunkUpload")]
    public class ChunkUpload : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 上传标识（GUID）
        /// </summary>
        [Column(Name = "UploadId", StringLength = 32, IsNullable = false)]
        public string UploadId { get; set; } = null!;

        /// <summary>
        /// 原始文件名
        /// </summary>
        [Column(Name = "FileName", StringLength = 500, IsNullable = false)]
        public string FileName { get; set; } = null!;

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        [Column(Name = "FileSize", IsNullable = false)]
        public long FileSize { get; set; }

        /// <summary>
        /// 文件MD5
        /// </summary>
        [Column(Name = "FileMd5", StringLength = 32, IsNullable = false)]
        public string FileMd5 { get; set; } = null!;

        /// <summary>
        /// 每片大小（字节）
        /// </summary>
        [Column(Name = "ChunkSize", IsNullable = false)]
        public int ChunkSize { get; set; }

        /// <summary>
        /// 总分片数
        /// </summary>
        [Column(Name = "TotalChunks", IsNullable = false)]
        public int TotalChunks { get; set; }

        /// <summary>
        /// 状态：0-上传中 1-已完成 2-已过期
        /// </summary>
        [Column(Name = "Status", IsNullable = false)]
        public ChunkUploadStatus Status { get; set; } = ChunkUploadStatus.Uploading;

        /// <summary>
        /// 合并后的文件相对路径
        /// </summary>
        [Column(Name = "FilePath", StringLength = 500)]
        public string FilePath { get; set; } = null!;

        /// <summary>
        /// 创建时间
        /// </summary>
        [Column(Name = "CreatedAt", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 完成时间
        /// </summary>
        [Column(Name = "CompletedAt", DbType = "timestamp", IsNullable = true)]
        public DateTime? CompletedAt { get; set; }
    }

    /// <summary>
    /// 分片上传状态
    /// </summary>
    public enum ChunkUploadStatus
    {
        Uploading = 0,
        Completed = 1,
        Expired = 2
    }
}

