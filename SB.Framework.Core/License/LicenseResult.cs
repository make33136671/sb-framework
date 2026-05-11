using System.Collections.Generic;
using SB.Framework.Core.Util;

namespace SB.Framework.Core.License
{
    public class LicenseResult
    {
        public bool IsValid { get; init; }
        public bool IsExpired { get; init; }
        public string Message { get; init; } = "";
        public DateTime? ExpiresAt { get; init; }
        public DateTime? IssuedAt { get; init; }
        public string? ProductEn { get; init; }
        public string? ProductCn { get; init; }
        public string? Domain { get; init; }
        public string? ClientKey { get; init; }
        public string? HardwareId { get; init; }
        public int VerifyMode { get; init; }

        /// <summary>
        /// 到期后仍可访问的路由前缀列表
        /// </summary>
        public List<string> AllowedRoutes { get; init; } = new();

        /// <summary>
        /// 到期后是否仍允许访问管理后台 (/admin/* 非白名单路径)
        /// </summary>
        public bool AllowAdminAfterExpiry { get; init; }

        /// <summary>
        /// 到期后是否仍允许访问客户端 API (/api/* 非白名单路径)
        /// </summary>
        public bool AllowClientApiAfterExpiry { get; init; }

        /// <summary>
        /// 允许的域名列表 (供 LicenseMiddleware 每请求 Host 匹配使用). 空表示不验证域名.
        /// 注意: 旧 Key 的单值 Payload.Domain 不填充进来 (保守策略, 避免升级日老部署突然挂).
        /// </summary>
        public List<string> Domains { get; init; } = new();

        /// <summary>
        /// 剩余有效天数（过期时为 0）
        /// </summary>
        public int? RemainingDays => ExpiresAt.HasValue
            ? Math.Max(0, (int)Math.Ceiling((ExpiresAt.Value - AppTime.UtcNow).TotalDays))
            : null;

        public static LicenseResult Fail(string message) => new() { IsValid = false, Message = message };

        public static LicenseResult Ok(string message, DateTime expiresAt, DateTime? issuedAt, LicensePayload payload)
            => new()
            {
                IsValid = true,
                Message = message,
                ExpiresAt = expiresAt,
                IssuedAt = issuedAt,
                ProductEn = payload.ProductEn,
                ProductCn = payload.ProductCn,
                Domain = payload.Domain,
                ClientKey = payload.ClientKey,
                HardwareId = payload.HardwareId,
                VerifyMode = payload.VerifyMode,
                AllowedRoutes = payload.AllowedRoutes ?? new(),
                Domains = payload.Domains ?? new(),   // 旧 Key 的 payload.Domain 不 fallback 进来 (保守策略)
                AllowAdminAfterExpiry = payload.AllowAdminAfterExpiry,
                AllowClientApiAfterExpiry = payload.AllowClientApiAfterExpiry
            };

        public static LicenseResult Expired(DateTime expiresAt, DateTime? issuedAt, LicensePayload payload)
            => new()
            {
                IsValid = false,
                IsExpired = true,
                Message = $"授权已过期，到期时间: {expiresAt:yyyy-MM-dd HH:mm:ss} UTC",
                ExpiresAt = expiresAt,
                IssuedAt = issuedAt,
                ProductEn = payload.ProductEn,
                ProductCn = payload.ProductCn,
                Domain = payload.Domain,
                ClientKey = payload.ClientKey,
                HardwareId = payload.HardwareId,
                VerifyMode = payload.VerifyMode,
                AllowedRoutes = payload.AllowedRoutes ?? new(),
                Domains = payload.Domains ?? new(),
                AllowAdminAfterExpiry = payload.AllowAdminAfterExpiry,
                AllowClientApiAfterExpiry = payload.AllowClientApiAfterExpiry
            };

        // 向后兼容的旧签名
        public static LicenseResult Expired(DateTime expiresAt, DateTime? issuedAt, string? product)
            => new()
            {
                IsValid = false,
                IsExpired = true,
                Message = $"授权已过期，到期时间: {expiresAt:yyyy-MM-dd HH:mm:ss} UTC",
                ExpiresAt = expiresAt,
                IssuedAt = issuedAt,
                ProductEn = product
            };
    }

    /// <summary>
    /// 授权 Payload（签名内容）
    /// </summary>
    public class LicensePayload
    {
        /// <summary>
        /// 产品英文名. JSON 保留 "Product" 兼容已签发的旧 Key.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("Product")]
        public string? ProductEn { get; set; }

        /// <summary>
        /// 产品中文名称
        /// </summary>
        public string? ProductCn { get; set; }

        /// <summary>
        /// 旧字段, 保留供老 Key 兼容解析. 新 Key 改用 Domains.
        /// </summary>
        public string? Domain { get; set; }

        /// <summary>
        /// 绑定域名列表 (支持通配符 *.example.com). 为 null/空 表示不验证域名.
        /// 新 Key 用此字段; 老 Key 只有 Domain (单值).
        /// </summary>
        public List<string>? Domains { get; set; }

        public string? ClientKey { get; set; }
        public string? HardwareId { get; set; }

        /// <summary>
        /// 已废弃. 保留仅为兼容老 Key 解析, 新验证逻辑不再读取.
        /// </summary>
        public int VerifyMode { get; set; } = 2;

        public string? IssuedAt { get; set; }
        public string? ExpiresAt { get; set; }

        /// <summary>
        /// 到期后仍可访问的路由前缀
        /// </summary>
        public List<string>? AllowedRoutes { get; set; }

        /// <summary>
        /// 到期后是否仍允许访问管理后台 (/admin/* 非白名单路径)
        /// </summary>
        public bool AllowAdminAfterExpiry { get; set; }

        /// <summary>
        /// 到期后是否仍允许访问客户端 API (/api/* 非白名单路径)
        /// </summary>
        public bool AllowClientApiAfterExpiry { get; set; }
    }
}
