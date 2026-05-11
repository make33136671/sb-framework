using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class LogViewerController : Controller
    {
        private readonly LogViewerService _service;

        public LogViewerController(LogViewerService service)
        {
            _service = service;
        }

        /// <summary>
        /// 获取日志文件列表
        /// </summary>
        [HttpGet]
        public Result Files() => _service.GetFiles();

        /// <summary>
        /// 读取日志文件内容（archive 可选，指定归档子目录）
        /// </summary>
        [HttpGet]
        public Result Read(string fileName, int tail = 500, string archive = null)
            => _service.ReadFile(fileName, tail, archive);

        /// <summary>
        /// 下载日志文件（archive 可选，指定归档子目录）
        /// </summary>
        [HttpGet]
        public IActionResult Download(string fileName, string archive = null)
        {
            var filePath = _service.GetFilePath(fileName, archive);
            if (filePath == null)
                return NotFound(new { code = -1, message = "文件不存在" });

            return PhysicalFile(filePath, "text/plain", fileName);
        }
    }
}
