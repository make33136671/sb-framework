#if ENABLE_LICENSE_MANAGE
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Models;
using SB.Framework.Models.Dto;
using SB.Framework.Services;
using System.Collections.Generic;

namespace SB.Framework.WebApi.Controllers
{
    /// <summary>
    /// 权益管理（服务端：管理客户、审批申请、生成授权证书）
    /// </summary>
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class EntitlementController : Controller
    {
        private readonly LicenseManageService _service;

        public EntitlementController(LicenseManageService service)
        {
            _service = service;
        }

        #region 密钥管理

        /// <summary>
        /// 获取公钥（供客户端配置使用）
        /// </summary>
        [HttpGet]
        public Result GetPublicKey()
        {
            var publicKey = _service.GetPublicKeyXml();
            return Result.Success(new { publicKeyXml = publicKey, hasKeyPair = _service.HasKeyPair() });
        }

        /// <summary>
        /// 重新生成密钥对（谨慎操作，旧授权将失效）
        /// </summary>
        [HttpPost]
        public Result RegenerateKeyPair()
        {
            var (publicKey, _) = _service.GenerateKeyPair();
            return Result.Success("密钥对已重新生成，旧授权将失效", new { publicKeyXml = publicKey });
        }

        #endregion

        #region 网站客户

        [HttpGet]
        public Result ClientList(int page = 1, int pageSize = 20, string? keyword = null)
        {
            return _service.ClientList(page, pageSize, keyword);
        }

        [HttpGet]
        public Result ClientDetail(int id)
        {
            return _service.ClientDetail(id);
        }

        [HttpPost]
        public Result ClientEdit(int id, string? remark, int? status)
        {
            return _service.ClientEdit(id, remark, status);
        }

        #endregion

        #region 授权申请

        [HttpGet]
        public Result RequestList(int page = 1, int pageSize = 20, int? status = null)
        {
            return _service.RequestList(page, pageSize, status);
        }

        [HttpPost]
        public Result ApproveRequest(int requestId, DateTime expiresAt, LicenseVerifyMode verifyMode = LicenseVerifyMode.Both, [FromBody] List<string>? allowedRoutes = null)
        {
            return _service.ApproveRequest(requestId, expiresAt, verifyMode, allowedRoutes);
        }

        [HttpPost]
        public Result RejectRequest(int requestId, string? reason)
        {
            return _service.RejectRequest(requestId, reason);
        }

        #endregion

        #region 授权记录

        [HttpGet]
        public Result RecordList(int page = 1, int pageSize = 20, int? clientId = null)
        {
            return _service.RecordList(page, pageSize, clientId);
        }

        /// <summary>
        /// 签发授权 (统一入口). 各验证字段为 null/空 时 Payload 对应字段留空, 客户端跳过该项验证.
        /// </summary>
        [HttpPost]
        public Result IssueLicense([FromBody] IssueLicenseRequest req)
        {
            return _service.GenerateLicense(req);
        }

        /// <summary>
        /// 快速签发：粘贴客户端身份信息直接生成授权 (向后兼容, 内部转发到 IssueLicense).
        /// </summary>
        [HttpPost]
        public Result QuickGenerate([FromBody] QuickGenerateRequest request)
        {
            return _service.QuickGenerate(request);
        }

        /// <summary>
        /// 软删除客户. 历史记录保留, 列表自动过滤, 不可再签发.
        /// </summary>
        [HttpPost]
        public Result DeleteClient(int id)
        {
            return _service.DeleteClient(id);
        }

        [HttpPost]
        public Result RevokeLicense(int recordId)
        {
            return _service.RevokeLicense(recordId);
        }

        #endregion

        #region 验证日志

        [HttpGet]
        public Result VerifyLogList(int page = 1, int pageSize = 20, int? clientId = null)
        {
            return _service.VerifyLogList(page, pageSize, clientId);
        }

        #endregion
    }
}
#endif
