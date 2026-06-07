namespace SB.Feature.AccessToken
{
    /// <summary>
    /// 访问令牌功能强类型配置。框架自动绑定到 appsettings 的 "Features:AccessToken" 节。
    /// </summary>
    public sealed class AccessTokenOptions
    {
        /// <summary>
        /// 是否允许签发 / 使用 FullAccess(god)key。默认 false —— 生产建议保持关闭(对应原 MasterKey
        /// "生产建议关闭"的基调)。关闭时:已存在的 FullAccess key 不再被认证通过;管理端也不允许新签发。
        /// </summary>
        public bool EnableFullAccess { get; set; } = false;

        /// <summary>新签发 key 的明文前缀。校验按 hash,与前缀无关;识别"是否 api key" 按 sk_ 统一判定。</summary>
        public string KeyPrefix { get; set; } = "sk_";

        /// <summary>FullAccess key 的最大有效期(天)。签发 god key 必须设过期且不得超过此上限。0=不强制(不建议)。</summary>
        public int FullAccessMaxTtlDays { get; set; } = 90;

        /// <summary>单 IP 在窗口内允许的最大"无效 key"尝试次数,超过即临时拉黑(防爆破)。</summary>
        public int FailThreshold { get; set; } = 10;

        /// <summary>失败计数窗口(秒)。</summary>
        public int FailWindowSeconds { get; set; } = 300;

        /// <summary>触发拉黑后的封禁时长(秒)。</summary>
        public int FailBlockSeconds { get; set; } = 900;

        /// <summary>审计日志保留天数(超期清理)。</summary>
        public int AuditRetentionDays { get; set; } = 180;
    }
}
