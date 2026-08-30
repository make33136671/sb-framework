using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 分片上传记录
    /// </summary>
    [Table(Name = "SB_ChunkUpload_Part")]
    public class ChunkUploadPart : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 上传标识
        /// </summary>
        [Column(Name = "UploadId", StringLength = 32, IsNullable = false)]
        public string UploadId { get; set; } = null!;

        /// <summary>
        /// 分片序号（从0开始）
        /// </summary>
        [Column(Name = "ChunkIndex", IsNullable = false)]
        public int ChunkIndex { get; set; }

        /// <summary>
        /// 分片实际大小（字节）
        /// </summary>
        [Column(Name = "ChunkSize", IsNullable = false)]
        public int ChunkSize { get; set; }

        /// <summary>
        /// 上传时间
        /// </summary>
        [Column(Name = "UploadedAt", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime UploadedAt { get; set; }
    }
}

