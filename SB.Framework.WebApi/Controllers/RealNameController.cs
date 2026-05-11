using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SB.Framework.Models;
using SB.Framework.Services;
using SB.Framework.Core.Expand;

namespace SB.Framework.WebApi.Controllers
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class RealNameController : Controller
    {
        private readonly RealNameService _service;

        public RealNameController(RealNameService service)
        {
            _service = service;
        }

        [HttpGet]
        public Result List(RealNameStatus? status = null, string? keyword = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            return _service.GetList(status, keyword, field, order, page, pageSize);
        }

        [HttpGet]
        public Result Detail(int id) => _service.Detail(id);

        [HttpPost]
        public Result Approve([FromForm] int id)
        {
            int adminId = HttpContext.User.Id();
            return _service.Approve(id, adminId);
        }

        [HttpPost]
        public Result Reject([FromForm] int id, [FromForm] string? reason = null)
        {
            int adminId = HttpContext.User.Id();
            return _service.Reject(id, adminId, reason);
        }
    }
}
