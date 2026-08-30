using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Authentication.Jwt;
using SB.Framework.Core.Expand;
using SB.Framework.Models;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class AppService
    {
        IFreeSql _fsql;
        ILogger<AppService> _logger;
        IHttpContextAccessor _httpContextAccessor;
        MemberService _memberService;
        CommonService _commonService;
        SystemSettingService _systemSettingService;
        JwtToken _jwtToken;
        public AppService(IFreeSql fsql,
            ILogger<AppService> logger,
            IHttpContextAccessor httpContextAccessor,
            MemberService memberService,
            CommonService commonService,
            SystemSettingService systemSettingService,
            JwtToken jwtToken)
        {
            _fsql = fsql;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _memberService = memberService;
            _commonService = commonService;
            _systemSettingService = systemSettingService;
            _jwtToken = jwtToken;
        }

        #region 已登录操作

        private int UserId => _httpContextAccessor.HttpContext.User.Id();

        private Member _Member => _memberService.GetMemberById(UserId);

        private object GetMemberInfo(Member? _member = null,
            string accessToken = "")
        {
            Member? member = null;
            if (_Member != null)
            {
                member = _Member;
            }
            else if (_member != null)
            {
                member = _member;
            }
            else
            {
                _logger.LogError("获取用户信息异常");
                throw new Exception("获取用户信息异常");
            }

            return new
            {
                member.Id,
                member.Avatar,
                accessToken
            };
        }

        /// <summary>
        /// 获取已登陆用户信息
        /// </summary>
        public virtual Result GetUserInfo() => Result.Success(GetMemberInfo());


        #endregion


        #region 免登录
        /// <summary>
        /// 获取系统信息
        /// </summary>
        public virtual Result GetSystemInfo()
        {
            int? userId = null;

            try
            {
                if (UserId != 0)
                {
                    userId = UserId;
                }
            }
            catch (Exception) { userId = null; }

            string whitePaperUrl = _systemSettingService.GetWhitePaper();
            string serviceUrl = _systemSettingService.GetServiceUrl();
            decimal ethExchangeUsdcRate = _systemSettingService.GetEthExchangeUsdcRate();

            return Result.Success("Success", new
            {
                whitePaperUrl,
                serviceUrl,
                ethExchangeUsdcRate
            });
        }

        #endregion

    }
}
