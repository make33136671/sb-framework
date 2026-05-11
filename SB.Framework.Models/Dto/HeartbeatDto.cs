namespace SB.Framework.Models.Dto
{
    /// <summary>
    /// 心跳请求
    /// </summary>
    public class HeartbeatRequest
    {
        /// <summary>
        /// 客户端密钥
        /// </summary>
        public string? ClientKey { get; set; }

        /// <summary>
        /// 硬件 ID
        /// </summary>
        public string? HardwareId { get; set; }

        /// <summary>
        /// 域名
        /// </summary>
        public string? Domain { get; set; }

        /// <summary>
        /// 授权 Key 的哈希值
        /// </summary>
        public string? LicenseKeyHash { get; set; }

        // --- 以下客户端身份扩展字段, 和 Check/Verify 接口对齐 ---
        // 首次只跑 Heartbeat 的客户端 (没有用户主动 Check/Verify 过) 也能注册出完整的 LicenseClient 记录,
        // 避免产品列表出现只有 CK/HW 其他字段全空的"幽灵记录".

        /// <summary>产品英文名 (客户端 ProductEn)</summary>
        public string? ProductEn { get; set; }

        /// <summary>产品中文名 (客户端 ProductCn)</summary>
        public string? ProductCn { get; set; }

        /// <summary>机器名</summary>
        public string? MachineName { get; set; }

        /// <summary>操作系统描述</summary>
        public string? OsDescription { get; set; }

        /// <summary>CPU 核心数</summary>
        public int? ProcessorCount { get; set; }

        /// <summary>MAC 地址 (逗号分隔)</summary>
        public string? MacAddresses { get; set; }

        /// <summary>客户端服务器 IP</summary>
        public string? ServerIP { get; set; }
    }
}
