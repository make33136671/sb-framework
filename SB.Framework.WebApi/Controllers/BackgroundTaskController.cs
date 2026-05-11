using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class BackgroundTaskController : Controller
    {
        private readonly IBackgroundTaskManager _taskManager;
        private readonly IFreeSql _fsql;

        public BackgroundTaskController(IBackgroundTaskManager taskManager, IFreeSql fsql)
        {
            _taskManager = taskManager;
            _fsql = fsql;
        }

        [HttpGet]
        public Result List()
        {
            var tasks = _taskManager.GetAllTasks();
            return Result.Success(tasks);
        }

        [HttpPost]
        public async Task<Result> Start([FromForm] string name)
        {
            try
            {
                await _taskManager.StartTask(name);
                return Result.Success($"任务 '{name}' 已启动");
            }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpPost]
        public async Task<Result> Stop([FromForm] string name)
        {
            try
            {
                await _taskManager.StopTask(name);
                return Result.Success($"任务 '{name}' 已停止");
            }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpPost]
        public async Task<Result> Restart([FromForm] string name)
        {
            try
            {
                await _taskManager.RestartTask(name);
                return Result.Success($"任务 '{name}' 已重启");
            }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        /// <summary>
        /// 立即触发一次执行（绕过轮询间隔）
        /// </summary>
        [HttpPost]
        public async Task<Result> RunOnce([FromForm] string name)
        {
            try
            {
                var (ok, error) = await _taskManager.RunOnce(name);
                return ok
                    ? Result.Success($"任务 '{name}' 手动执行成功")
                    : Result.Fail($"任务 '{name}' 执行失败: {error}");
            }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }

        [HttpGet]
        public Result GetLogs(string name, int limit = 100)
        {
            var logs = _fsql.Select<BackgroundTaskLog>()
                .Where(l => l.TaskName == name)
                .OrderByDescending(l => l.CreatedTime)
                .Take(limit)
                .ToList();
            return Result.Success(logs);
        }

        [HttpGet]
        public Result LogList(LogSource? source = null, string? taskName = null,
            TaskLogLevel? logLevel = null, int page = 1, int pageSize = 20)
        {
            var query = _fsql.Select<BackgroundTaskLog>();
            query = query.WhereIf(source.HasValue, l => l.Source == source!.Value);
            query = query.WhereIf(!string.IsNullOrEmpty(taskName), l => l.TaskName == taskName);
            query = query.WhereIf(logLevel.HasValue, l => l.LogLevel == logLevel!.Value);
            query = query.OrderByDescending(l => l.CreatedTime);

            var logs = query.Count(out long dataCount).Page(page, pageSize).ToList();
            return Result.Page(logs, dataCount);
        }

        [HttpPost]
        public async Task<Result> ReloadParameters([FromForm] string name)
        {
            try
            {
                await _taskManager.ReloadParameters(name);
                return Result.Success($"任务 '{name}' 参数已重载");
            }
            catch (Exception ex) { return Result.Fail(ex.Message); }
        }
    }
}
