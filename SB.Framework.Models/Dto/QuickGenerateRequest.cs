namespace SB.Framework.Models.Dto
{
    public class QuickGenerateRequest
    {
        /// <summary>
        /// 产品英文名
        /// </summary>
        public string? ProductEn { get; set; }

        /// <summary>
        /// 产品中文名
        /// </summary>
        public string? ProductCn { get; set; }

        /// <summary>
        /// 客户端密钥
        /// </summary>
        public string? ClientKey { get; set; }

        /// <summary>
        /// 硬件ID
        /// </summary>
        public string? HardwareId { get; set; }

        /// <summary>
        /// 域名
        /// </summary>
        public string? Domain { get; set; }

        /// <summary>
        /// 机器名
        /// </summary>
        public string? MachineName { get; set; }

        /// <summary>
        /// 有效天数（与 ExpiresAt 二选一）
        /// </summary>
        public int? Days { get; set; }

        /// <summary>
        /// 到期时间（与 Days 二选一）
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// 验证模式（0=仅CK, 1=仅HW, 2=双验证）
        /// </summary>
        public int VerifyMode { get; set; } = 2;
    }
}
