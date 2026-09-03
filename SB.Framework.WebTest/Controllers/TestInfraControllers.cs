using FreeSql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebTest.Controllers;

/// <summary>
/// 缓存场景:直接驱动 ICacheService 全部方法
/// (Provider 由 appsettings CacheConfig 决定:Memory / Redis / 关闭)。
/// </summary>
[ApiController, Route("/test/cache/[action]")]
public class TestCacheController : ControllerBase
{
    private readonly ICacheService _cache;

    public TestCacheController(ICacheService cache) => _cache = cache;

    /// <summary>当前缓存 Provider(Memory/Redis/Null)</summary>
    [HttpGet, AllowAnonymous]
    public Result Provider() => Result.Success(_cache.ProviderName);

    /// <summary>Set + Get(对象往返序列化)</summary>
    [HttpPost, AllowAnonymous]
    public Result Set([FromForm] string key, [FromForm] string value, [FromForm] int expireSeconds = 60)
    {
        _cache.Set(key, new { Value = value, WrittenAt = SB.Framework.Core.Util.AppTime.Now },
            TimeSpan.FromSeconds(expireSeconds));
        return Result.Success($"已写入 {key}(expire {expireSeconds}s)", _cache.Get<object>(key));
    }

    /// <summary>Get(不存在返回默认值)</summary>
    [HttpGet, AllowAnonymous]
    public Result Get(string key)
    {
        var v = _cache.Get<object>(key);
        return v == null ? Result.Fail($"'{key}' 不存在或已过期") : Result.Success(v);
    }

    /// <summary>精确 Remove</summary>
    [HttpPost, AllowAnonymous]
    public Result Remove([FromForm] string key)
    {
        _cache.Remove(key);
        return Result.Success($"已移除 {key},再查应为空:{_cache.Get<object>(key) == null}");
    }

    /// <summary>按前缀批量移除</summary>
    [HttpPost, AllowAnonymous]
    public Result RemoveByPrefix([FromForm] string prefix)
    {
        _cache.RemoveByPrefix(prefix);
        return Result.Success($"已按前缀 '{prefix}' 批量移除");
    }

    /// <summary>前缀计数(Provider 支持时)</summary>
    [HttpGet, AllowAnonymous]
    public Result CountByPrefix(string prefix) => Result.Success(_cache.CountByPrefix(prefix));
}

/// <summary>
/// 后台任务场景:列出任务 / 手动触发 / 查看任务日志。
/// 任务本体 OrderTimeoutTask 由 WebTestTaskRegistration 注册(演示衍生项目接入姿势)。
/// </summary>
[ApiController, Route("/test/task/[action]")]
public class TestTaskController : ControllerBase
{
    private readonly IBackgroundTaskManager _manager;

    public TestTaskController(IBackgroundTaskManager manager) => _manager = manager;

    /// <summary>全部任务(含框架内置 DatabaseBackup 与 WebTest 的 OrderTimeout)</summary>
    [HttpGet, AllowAnonymous]
    public Result Tasks() => Result.Success("ok", _manager.GetAllTasks().Select(t => new
    {
        t.Name,
        t.DisplayName,
        Status = t.Status.ToString(),
        NextRunTime = t.NextRunTime,
        RecentRuns = t.ExecutionRecords.TakeLast(3).Select(r => new
        {
            r.StartTime,
            r.Success,
            r.ErrorMessage,
        }).ToList(),
    }));

    /// <summary>手动触发一次(绕过 Interval,验证任务体 + 熔断路径)</summary>
    [HttpPost, AllowAnonymous]
    public async Task<Result> RunOnce([FromForm] string name)
    {
        var (ok, error) = await _manager.RunOnce(name);
        return ok ? Result.Success($"{name} 执行成功") : Result.Fail($"{name} 执行失败: {error}");
    }

    /// <summary>启动任务(进入框架轮询调度)</summary>
    [HttpPost, AllowAnonymous]
    public async Task<Result> Start([FromForm] string name)
    {
        await _manager.StartTask(name);
        return Result.Success($"{name} 已启动,状态 {_manager.GetTaskStatus(name)}");
    }

    /// <summary>停止任务</summary>
    [HttpPost, AllowAnonymous]
    public async Task<Result> Stop([FromForm] string name)
    {
        await _manager.StopTask(name);
        return Result.Success($"{name} 已停止,状态 {_manager.GetTaskStatus(name)}");
    }

    /// <summary>任务日志(SYS_BackgroundTaskLog,验证 WriteTaskLog 落库)</summary>
    [HttpGet, AllowAnonymous]
    public Result Logs(int pageSize = 20)
    {
        var fsql = (IFreeSql)HttpContext.RequestServices.GetService(typeof(IFreeSql))!;
        var logs = fsql.Select<BackgroundTaskLog>()
            .Where(l => l.TaskName == "OrderTimeout")
            .OrderByDescending(l => l.Id)
            .Take(pageSize)
            .ToList();
        return Result.Page(logs, logs.Count);
    }
}
