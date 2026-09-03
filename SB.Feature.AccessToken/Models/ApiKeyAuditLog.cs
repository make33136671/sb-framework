using FreeSql.DataAnnotations;
using SB.Framework.Models;

namespace SB.Feature.AccessToken.Models
{
    /// <summary>
    /// API Key 审计日志。记录签发 / 吊销 / 更新 / 鉴权失败 / 限流等关键事件。
    /// 成功的每次"Used"不逐条写(量太大),仅 LastUsedAt 节流更新;失败/拒绝/生命周期事件必记。
    /// </summary>
    [Table(Name = "SB_ApiKey_Audit")]
    [Index("idx_apikey_audit_key", nameof(KeyId), false)]
    [Index("idx_apikey_audit_time", nameof(CreatedTime), false)]
    [Index("idx_apikey_audit_action", nameof(Action), false)]
    public class ApiKeyAuditLog : IEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>命中的 key id;鉴权失败(未命中任何 key)时为 0。</summary>
        [Column(Name = "KeyId")]
        public int KeyId { get; set; }

        [Column(Name = "KeyName", StringLength = 100)]
        public string KeyName { get; set; } = "";

        [Column(Name = "TokenPrefix", StringLength = 16)]
        public string TokenPrefix { get; set; } = "";

        /// <summary>Created/Revoked/Updated/AuthFailed/Denied/RateLimited/Throttled/Expired</summary>
        [Column(Name = "Action", StringLength = 20)]
        public string Action { get; set; } = "";

        /// <summary>Success/Denied</summary>
        [Column(Name = "Result", StringLength = 10)]
        public string Result { get; set; } = "";

        [Column(Name = "IpAddress", StringLength = 45)]
        public string? IpAddress { get; set; }

        [Column(Name = "RequestPath", StringLength = 500)]
        public string? RequestPath { get; set; }

        [Column(Name = "DenyReason", StringLength = 200)]
        public string? DenyReason { get; set; }

        [Column(Name = "Operator", StringLength = 50)]
        public string? Operator { get; set; }

        [Column(Name = "CreatedTime")]
        public DateTime CreatedTime { get; set; }
    }
}
