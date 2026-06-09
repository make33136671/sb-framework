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

        /// <summary>
        /// AI 诊断查询:按日期 + 级别 + tail + 关键字过滤,返回合并的 NDJSON 文本流。
        /// 查询摘要(日期/级别/文件清单/总行数等)放在响应头 X-Diag-* 字段,避免污染 body。
        /// 设计上配合访问令牌(FullAccess sk_ key)调用,Claude Code 端会自然语言触发本接口。
        /// </summary>
        /// <param name="date">日期 yyyyMMdd,逗号分隔多个;空 = 今天</param>
        /// <param name="level">级别 info/warning/error/all,逗号分隔;空 = "warning,error"</param>
        /// <param name="tail">每个文件取尾部 N 行,默认 1000,0 = 不截断</param>
        /// <param name="contains">关键字过滤(子串匹配,大小写不敏感)</param>
        [HttpGet]
        public IActionResult Diag(string date = null, string level = null, int tail = 1000, string contains = null)
        {
            var result = _service.QueryDiag(date, level, tail, contains);

            Response.Headers["X-Diag-Dates"] = string.Join(",", result.Dates);
            Response.Headers["X-Diag-Levels"] = string.Join(",", result.Levels);
            Response.Headers["X-Diag-Tail"] = tail.ToString();
            Response.Headers["X-Diag-Files"] = string.Join(",", result.Files);
            Response.Headers["X-Diag-FilesScanned"] = result.Files.Count.ToString();
            Response.Headers["X-Diag-TotalLines"] = result.TotalLines.ToString();
            Response.Headers["X-Diag-ReturnedLines"] = result.ReturnedLines.ToString();
            if (!string.IsNullOrEmpty(result.Contains))
                Response.Headers["X-Diag-Contains"] = result.Contains;

            return Content(result.Ndjson, "application/x-ndjson");
        }
    }
}
