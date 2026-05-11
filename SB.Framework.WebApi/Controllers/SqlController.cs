using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class SqlController : Controller
    {
        private readonly SqlService _service;

        public SqlController(SqlService service)
        {
            _service = service;
        }

        /// <summary>
        /// 执行 SQL
        /// </summary>
        [HttpPost]
        public Result Execute([FromForm] string sql)
        {
            return _service.Execute(sql);
        }

        /// <summary>
        /// 导出查询结果为 CSV
        /// </summary>
        [HttpPost]
        public IActionResult ExportCsv([FromForm] string sql)
        {
            try
            {
                var (data, fileName) = _service.ExportCsv(sql);
                return File(data, "text/csv; charset=utf-8", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { code = -1, message = $"导出失败: {ex.Message}" });
            }
        }
    }
}
