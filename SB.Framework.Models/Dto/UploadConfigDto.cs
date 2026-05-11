namespace SB.Framework.Models.Dto
{
    /// <summary>
    /// 上传配置（前端拉取使用，应用生命周期内只读一次缓存）
    /// </summary>
    public class UploadConfigDto
    {
        /// <summary>
        /// 文件大小上限（MB）
        /// </summary>
        public long MaxFileSizeMB { get; set; }

        /// <summary>
        /// 切片阈值（MB）：文件 &gt; 此值走切片上传
        /// </summary>
        public int ChunkThresholdMB { get; set; }

        /// <summary>
        /// 切片并发线程数推荐值
        /// </summary>
        public int MaxConcurrent { get; set; }

        /// <summary>
        /// 允许的扩展名列表（小写、含点）；空列表表示不限制
        /// </summary>
        public List<string> AllowedExtensions { get; set; } = new();

        /// <summary>
        /// URL 前缀（带 scheme 与 host），用于前端拼绝对 URL
        /// </summary>
        public string UrlPrefix { get; set; } = "";
    }
}
