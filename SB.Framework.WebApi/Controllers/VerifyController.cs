#if ENABLE_LICENSE_MANAGE
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Models.Dto;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers
{
    /// <summary>
    /// 授权验证公开 API（供客户端调用，无需认证）
    /// </summary>
    [ApiController, AllowAnonymous, Route("/api/verify/[action]")]
    public class VerifyController : Controller
    {
        private readonly LicenseManageService _service;

        public VerifyController(LicenseManageService service)
        {
            _service = service;
        }

        /// <summary>
        /// 心跳 - 客户端定期上报状态
        /// </summary>
        [HttpPost]
        public Result Heartbeat([FromBody] HeartbeatRequest request)
        {
            return _service.ProcessHeartbeat(request);
        }

        /// <summary>
        /// 客户端申请授权
        /// </summary>
        [HttpPost]
        // 统一参数顺序 (全系统对齐):
        //   domain, actualDomain, productEn, productCn,
        //   clientKey, hardwareId,
        //   machineName, osDescription, processorCount, macAddresses,
        //   serverIP,
        //   [接口独有]: currentKey / licenseValid / message
        // 命名约定:
        //   productEn = 产品英文名 (客户端 AppSettings.ProductEn)
        //   productCn = 产品中文名 (客户端 AppSettings.ProductCn)

        public Result Request(string domain, string? productEn, string? productCn, string? clientKey, string? hardwareId,
            string? machineName, string? osDescription, int? processorCount, string? macAddresses, string? serverIP)
        {
            return _service.CreateRequest(domain, productEn, productCn, clientKey, hardwareId, machineName, osDescription, processorCount, macAddresses, serverIP);
        }

        /// <summary>
        /// 客户端验证上报 + 吊销查询.
        /// 响应 { revoked: bool } 让客户端知道是否需要清本地 Key.
        /// product/productCn 用于更新 LicenseClient 记录, 确保管理员签发时看到客户真实产品名.
        /// </summary>
        [HttpPost]
        public Result Verify(string? domain, string? actualDomain, string? productEn, string? productCn,
            string? clientKey, string? hardwareId, string? currentKey,
            string? serverIP, bool licenseValid = false, string? message = null)
        {
            return _service.Verify(domain, actualDomain, productEn, productCn, clientKey, hardwareId, currentKey, serverIP, licenseValid, message);
        }

        /// <summary>
        /// 客户端查询是否有可下载的新授权
        /// domain: 客户端 appsettings.PrimaryDomain (系统配置里声明的主域名)
        /// actualDomain: 客户端浏览器实际访问的 host (window.location.host)
        /// </summary>
        [HttpGet]
        public Result Check(string? domain, string? actualDomain, string? productEn, string? productCn,
            string? clientKey, string? hardwareId,
            string? machineName, string? osDescription, int? processorCount, string? macAddresses, string? currentKey)
        {
            return _service.CheckLicense(domain, actualDomain, productEn, productCn, clientKey, hardwareId, machineName, osDescription, processorCount, macAddresses, currentKey);
        }

        /// <summary>
        /// 客户端下载授权文件
        /// </summary>
        [HttpGet]
        public Result Download(int recordId)
        {
            return _service.DownloadLicense(recordId);
        }
    }
}
#endif
