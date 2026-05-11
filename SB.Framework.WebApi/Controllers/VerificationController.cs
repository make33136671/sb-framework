using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Core.License;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers
{
    /// <summary>
    /// 客户端本地授权验证
    /// </summary>
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class VerificationController : Controller
    {
        private readonly LicenseClientService _clientService;

        public VerificationController(LicenseClientService clientService)
        {
            _clientService = clientService;
        }

        /// <summary>
        /// 获取当前授权状态 + 客户端标识信息
        /// </summary>
        [HttpGet, AllowAnonymous]
        public Result Status()
        {
            return _clientService.GetStatus();
        }

        /// <summary>
        /// 向授权服务器发送授权申请
        /// </summary>
        [HttpPost]
        public async Task<Result> RequestLicense(string? domain)
        {
            return await _clientService.RequestLicenseAsync(domain);
        }

        /// <summary>
        /// 向授权服务器查询是否有新授权。
        /// 允许前端传 serverUrl 临时覆盖配置里的服务器地址，actualDomain 透传给云端用于多域名识别。
        /// 前端走 query string，所以参数用 [FromQuery] 明确绑定。
        /// </summary>
        [HttpPost]
        public async Task<Result> CheckUpdate([FromQuery] string? serverUrl, [FromQuery] string? actualDomain)
        {
            return await _clientService.CheckUpdateAsync(serverUrl, actualDomain);
        }

        /// <summary>
        /// 从授权服务器下载并应用新授权（前端走 query string）
        /// </summary>
        [HttpPost]
        public async Task<Result> ApplyLicense([FromQuery] int recordId, [FromQuery] string? serverUrl)
        {
            return await _clientService.ApplyLicenseAsync(recordId, serverUrl);
        }

        /// <summary>
        /// 离线应用管理员生成好的授权 Key（不走网络查询，直接验证+写入）
        /// 前端走 query string，所以参数用 [FromQuery] 明确绑定。
        /// </summary>
        [HttpPost]
        public Result ApplyKey([FromQuery] string? licenseKey)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
                return Result.Fail("授权 Key 不能为空");
            return _clientService.ApplyKey(licenseKey);
        }
    }
}
