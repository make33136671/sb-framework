using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class CommonService
    {
        IFreeSql _fsql;
        ILogger<CommonService> _logger;
        IHttpContextAccessor _httpContextAccessor;
        public CommonService(IFreeSql fsql, ILogger<CommonService> logger, IHttpContextAccessor httpContextAccessor)
        {
            _fsql = fsql;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }
    }
}
