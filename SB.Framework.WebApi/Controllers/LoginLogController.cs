using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SB.Framework.Models;

namespace SB.Framework.WebApi.Controllers
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class LoginLogController : Controller
    {
        private readonly IFreeSql _fsql;

        public LoginLogController(IFreeSql fsql)
        {
            _fsql = fsql;
        }

        [HttpGet]
        public Result List(int? userType = null, string? username = null, int? status = null,
            DateTime? startDate = null, DateTime? endDate = null,
            int page = 1, int pageSize = 20)
        {
            var query = _fsql.Select<LoginLog>();

            query = query.WhereIf(userType.HasValue, l => l.UserType == userType!.Value);
            query = query.WhereIf(!string.IsNullOrEmpty(username), l => l.Username.Contains(username!));
            query = query.WhereIf(status.HasValue, l => l.Status == status!.Value);
            query = query.WhereIf(startDate.HasValue, l => l.CreateTime >= startDate!.Value);
            query = query.WhereIf(endDate.HasValue, l => l.CreateTime <= endDate!.Value);

            query = query.OrderByDescending(l => l.CreateTime);

            var logs = query.Count(out long dataCount).Page(page, pageSize).ToList();
            return Result.Page(logs, dataCount);
        }
    }
}
