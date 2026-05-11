using System;
using System.Collections.Generic;

namespace SB.Framework.Models.Dto
{
    /// <summary>
    /// 签发授权请求. 各验证字段为 null/空 表示"不验证该项".
    /// </summary>
    public class IssueLicenseRequest
    {
        public int ClientId { get; set; }

        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// 空 = 不验证域名. 条目支持:
        /// - "example.com" (仅 1 个点) → 自动扩展为 ["example.com", "*.example.com"]
        /// - "api.example.com" (2+ 个点) → 精确匹配
        /// - "*.x.com" → 任意子域匹配
        /// </summary>
        public List<string>? Domains { get; set; }

        /// <summary>空 = 不验证产品. 仅限 ^[a-z][a-z0-9]{1,19}$ (小写字母+数字, 字母开头, 2-20 字符).</summary>
        public string? ProductEn { get; set; }

        /// <summary>空 = 不验证 ClientKey.</summary>
        public string? ClientKey { get; set; }

        /// <summary>空 = 不验证 HardwareId.</summary>
        public string? HardwareId { get; set; }

        /// <summary>到期后仍可访问的路由前缀列表 (如 ["/admin/License/"]).</summary>
        public List<string>? AllowedRoutes { get; set; }

        /// <summary>到期后是否仍允许访问管理后台 (/admin/* 非白名单路径).</summary>
        public bool AllowAdminAfterExpiry { get; set; }

        /// <summary>到期后是否仍允许访问客户端 API (/api/* 非白名单路径).</summary>
        public bool AllowClientApiAfterExpiry { get; set; }
    }
}
