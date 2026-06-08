using FreeSql.DataAnnotations;
using SB.Framework.Models;
using System.ComponentModel.DataAnnotations;

namespace SB.Feature.AccessToken.Models
{
    /// <summary>
    /// API Key —— 挂在某个账号(SB_Admin)上的"凭据"。用 key 认证 = 以该账号身份访问(账号即 token)。
    /// 明文只在签发时回显一次,库里只存 sha256/HMAC hash。吸收原 PM AgentToken 的全部能力。
    /// </summary>
    [Table(Name = "SB_ApiKey")]
    [Index("idx_apikey_token_hash", nameof(TokenHash), true)]
    [Index("idx_apikey_account", nameof(AccountId), false)]
    [Index("idx_apikey_name", nameof(Name), false)]
    [Index("idx_apikey_updated", nameof(UpdatedTime), false)]
    public class ApiKey : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>归属账号 SB_Admin.Id。用 key 认证即以此账号身份发 principal。</summary>
        [Column(Name = "AccountId", IsNullable = false)]
        public int AccountId { get; set; }

        [Required, StringLength(100)]
        [Column(Name = "Name")]
        public string Name { get; set; } = "";

        /// <summary>sha256/HMAC(明文 key) 的 hex 字符串(64 字符)。明文不入库。</summary>
        [Required, StringLength(64)]
        [Column(Name = "TokenHash")]
        public string TokenHash { get; set; } = "";

        /// <summary>明文前 12 位(含前缀),仅 list 显示识别用。</summary>
        [Required, StringLength(16)]
        [Column(Name = "TokenPrefix")]
        public string TokenPrefix { get; set; } = "";

        /// <summary>
        /// 逗号分隔 scope 列表,RBAC 风格(read:* / write:task / *);null = 该 key 在所有 [Scope] 端点放行
        /// (向后兼容原 agent token null=全权语义,但仍只限"已声明 [Scope] 的端点")。
        /// 仅对 FullAccess=false 的受限 key 有意义。
        /// </summary>
        [Column(Name = "Scopes", StringLength = 500)]
        public string? Scopes { get; set; }

        /// <summary>
        /// FullAccess(god):忽略 scope、穿透所有端点、并旁路 License(取代原 MasterKey)。
        /// 仅 super 角色可签发,且需功能开关 EnableFullAccess=true 才生效。
        /// </summary>
        [Column(Name = "FullAccess", IsNullable = false, InsertValueSql = "false")]
        public bool FullAccess { get; set; }

        /// <summary>逗号分隔的允许来源 IP/CIDR;非空时仅这些来源可用此 key(god key 建议设置)。</summary>
        [Column(Name = "IpAllowlist", StringLength = 500)]
        public string? IpAllowlist { get; set; }

        [Column(Name = "ExpiresAt")]
        public DateTime? ExpiresAt { get; set; }

        [Column(Name = "LastUsedAt")]
        public DateTime? LastUsedAt { get; set; }

        /// <summary>每分钟最大请求数(0/null = 不限)。</summary>
        [Column(Name = "RateLimitPerMinute")]
        public int? RateLimitPerMinute { get; set; }

        [Column(Name = "Notes", StringLength = 2000)]
        public string? Notes { get; set; }

        /// <summary>创建者标识(admin username 或 "cli")。</summary>
        [StringLength(50)]
        [Column(Name = "CreatedBy")]
        public string CreatedBy { get; set; } = "";

        [StringLength(50)]
        [Column(Name = "ModifiedBy")]
        public string? ModifiedBy { get; set; }

        [StringLength(500)]
        [Column(Name = "RevokeReason")]
        public string? RevokeReason { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "CURRENT_TIMESTAMP")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp")]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}
