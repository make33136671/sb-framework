using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace SB.Framework.Core.License
{
    /// <summary>
    /// 异步吊销查询接口 — 由 SB.Framework.Services.LicenseVerifyClient 实现,
    /// 放在 Core 层是为了让 LicenseMiddleware 能注入调用而不反向依赖 Services.
    /// </summary>
    public interface ILicenseVerifyClient
    {
        /// <summary>
        /// 异步向授权服务器查询当前 Key 是否被吊销 (fire-and-forget, 不阻塞当前请求).
        /// 内部节流: 成功 1 小时, 失败 60 秒; 无服务器地址则直接返回.
        /// 服务器返回 revoked=true 时自动清空本地 Key + Refresh 内存状态.
        /// </summary>
        Task TryVerifyAsync(HttpContext context);
    }
}
