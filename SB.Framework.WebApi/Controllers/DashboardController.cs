using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class DashboardController : Controller
    {
        private readonly DashboardService _service;

        public DashboardController(DashboardService service)
        {
            _service = service;
        }

        [HttpGet]
        public Result Overview() => _service.GetOverview();

        [HttpGet]
        public Result MemberTrend(int days = 30) => _service.GetMemberTrend(days);

        [HttpGet]
        public Result MemberDistribution() => _service.GetMemberDistribution();

        [HttpGet]
        public Result RecentLogins(int limit = 10) => _service.GetRecentLogins(limit);

        [HttpGet]
        public Result RecentMembers(int limit = 10) => _service.GetRecentMembers(limit);

        [HttpGet]
        public Result PendingRealNames(int limit = 10) => _service.GetPendingRealNames(limit);

        [HttpGet]
        public Result LoginTrend(int days = 7) => _service.GetLoginTrend(days);
    }
}
