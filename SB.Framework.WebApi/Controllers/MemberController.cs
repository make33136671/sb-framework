using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class MemberController : Controller
    {
        private readonly MemberService _service;

        public MemberController(MemberService service)
        {
            _service = service;
        }

        [HttpGet]
        public Result List(int? id = null, string? keyword = null, UserStatus? status = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            return _service.GetList(id, keyword, status, field, order, page, pageSize);
        }

        [HttpGet]
        public Result Detail(int id) => _service.Detail(id);

        [HttpPost]
        public Result Add([FromForm] Member member) => _service.Create(member);

        [HttpPost]
        public Result Edit([FromForm] Member member) => _service.Edit(member);

        [HttpPost]
        public Result Del([FromForm] int id) => _service.Del(id);

        [HttpPost]
        public Result ResetPassword([FromForm] int id, [FromForm] string newPassword)
            => _service.ResetPassword(id, newPassword);

        [HttpGet]
        public Result Search(string? keyword = null, int limit = 20)
            => _service.Search(keyword, limit);

        [HttpGet]
        public Result ReferralTree(int memberId) => _service.GetReferralTree(memberId);

        [HttpGet]
        public Result MemberTree(int memberId) => _service.GetMemberTree(memberId);

        [HttpGet]
        public Result Children(int? parentId) => _service.GetChildren(parentId);
    }
}
