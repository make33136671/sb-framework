using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Util;
using SB.Framework.Models;

namespace SB.Framework.Core.BackgroundTask;

public abstract class BaseBackgroundTask : BackgroundService
{
    private const int MaxConsecutiveFailures = 3;

    private readonly ILogger _logger;
    private readonly IFreeSql? _fsql;
    private readonly List<TaskExecutionRecord> _executionRecords = new();
    private int _consecutiveFailures;
    private int _executionCount;
    private DateTime _lastHeartbeatTime = DateTime.MinValue;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(10);

    public TaskStatus Status { get; private set; } = TaskStatus.Stopped;
    public IReadOnlyList<TaskExecutionRecord> ExecutionRecords => _executionRecords;

    /// <summary>
    /// 中文显示名（管理后台任务列表展示用）
    /// 子类 override 提供：如 "数据库备份"；不 override 时默认用类名 (TaskShortName)
    /// </summary>
    public virtual string DisplayName => TaskShortName;

    /// <summary>
    /// 下次执行时间（UTC）。
    /// 默认估算：状态非 Running 返回 null；有历史则 lastEndTime + Interval；否则 now（首轮立即跑）。
    /// 子类可 override 给出更精确的"业务下次时间"（如 DatabaseBackupTask 基于 backup_interval_hours 算）。
    /// </summary>
    public virtual DateTime? NextRunTime
    {
        get
        {
            if (Status != TaskStatus.Running) return null;
            if (_executionRecords.Count > 0)
                return _executionRecords[^1].EndTime.Add(Interval);
            return AppTime.UtcNow;
        }
    }

    protected BaseBackgroundTask(ILogger logger, IFreeSql? fsql = null)
    {
        _logger = logger;
        _fsql = fsql;
    }

    protected abstract Task ExecuteTaskAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 轮询时是否到了该执行的时机（默认 true 每轮都跑）；
    /// 子类可 override 加"距上次执行时间 / 是否启用 / cron 命中" 等条件判断
    /// **手动 RunOnceAsync 不会调用此方法**，可绕过节流强制执行
    /// </summary>
    protected virtual Task<bool> ShouldRunNowAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <summary>
    /// 立即触发一次任务（绕过 Interval 轮询），调用方拿到 Result 知道这次跑成功还是出错
    /// 注意：与轮询同时跑互不干扰（业务自身保证幂等/锁）
    /// </summary>
    public async Task<(bool Ok, string? Error)> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var record = new TaskExecutionRecord { StartTime = AppTime.UtcNow };
        try
        {
            await ExecuteTaskAsync(cancellationToken);
            record.Success = true;
            WriteTaskLog(TaskLogLevel.Info, "手动触发执行成功");
            return (true, null);
        }
        catch (Exception ex)
        {
            record.Success = false;
            record.ErrorMessage = ex.Message;
            LogError($"Manual run failed: {ex.Message}");
            WriteTaskLog(TaskLogLevel.Error, $"手动触发执行失败: {ex.Message}", ex.ToString());
            return (false, ex.Message);
        }
        finally
        {
            record.EndTime = AppTime.UtcNow;
            record.Duration = record.EndTime - record.StartTime;
            _executionRecords.Add(record);
        }
    }

    protected abstract TimeSpan Interval { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 关键:防御性 yield,让 BackgroundService.StartAsync 立即拿到 yield 点。
        // BackgroundService.StartAsync 源码会同步执行 ExecuteAsync 到第一个真异步 await 才返回。
        // 如果 ExecuteTaskAsync 内部有 sync 阻塞(像 DatabaseBackupService 老版本 Process.WaitForExit
        // 卡 60 秒等 MySQL TCP timeout),整个 BackgroundTaskHostedService.StartAsync 链会同步阻塞,
        // Kestrel 监听被推迟,应用启动延迟。这一行确保任何 task 都先 yield 给 hosted service 链,
        // 真正实现 fire-and-forget。
        await Task.Yield();

        await OnStartAsync(stoppingToken);
        Status = TaskStatus.Running;
        LogInfo($"{GetType().Name} started.");
        WriteTaskLog(TaskLogLevel.Info, "任务启动");

        while (!stoppingToken.IsCancellationRequested)
        {
            // 节流 hook：到不到时机由子类决定（手动 RunOnceAsync 绕过此判断）
            bool shouldRun;
            try { shouldRun = await ShouldRunNowAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { shouldRun = true; /* hook 异常不阻塞，按到时机处理交给 ExecuteTaskAsync */ }

            if (!shouldRun)
            {
                try { await Task.Delay(Interval, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                continue;
            }

            var record = new TaskExecutionRecord { StartTime = AppTime.UtcNow };

            try
            {
                await ExecuteTaskAsync(stoppingToken);

                record.Success = true;
                _consecutiveFailures = 0;
                _executionCount++;

                var now = AppTime.UtcNow;
                if (now - _lastHeartbeatTime >= HeartbeatInterval)
                {
                    _lastHeartbeatTime = now;
                    WriteTaskLog(TaskLogLevel.Info, $"运行中: 已执行 {_executionCount} 次");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                record.Success = false;
                record.ErrorMessage = ex.Message;
                _consecutiveFailures++;

                LogError($"Execution failed ({_consecutiveFailures}/{MaxConsecutiveFailures}): {ex.Message}");
                WriteTaskLog(TaskLogLevel.Error,
                    $"执行失败 ({_consecutiveFailures}/{MaxConsecutiveFailures}): {ex.Message}",
                    ex.ToString());

                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    Status = TaskStatus.Failed;
                    LogError($"Circuit breaker triggered after {MaxConsecutiveFailures} consecutive failures. Task stopped.");
                    WriteTaskLog(TaskLogLevel.Error,
                        $"熔断触发: 连续 {MaxConsecutiveFailures} 次失败，任务已停止");
                    break;
                }
            }
            finally
            {
                record.EndTime = AppTime.UtcNow;
                record.Duration = record.EndTime - record.StartTime;
                _executionRecords.Add(record);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        if (Status != TaskStatus.Failed)
            Status = TaskStatus.Stopped;

        await OnStopAsync(stoppingToken);
        LogInfo($"{GetType().Name} stopped.");
        WriteTaskLog(TaskLogLevel.Info, "任务停止");
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        LogInfo($"{GetType().Name} restarting...");
        WriteTaskLog(TaskLogLevel.Info, "任务重启");
        _consecutiveFailures = 0;
        Status = TaskStatus.Running;
        await OnRestartAsync(cancellationToken);
    }

    protected virtual Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task OnStopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task OnRestartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected string TaskShortName => GetType().Name.EndsWith("Task")
        ? GetType().Name[..^4]
        : GetType().Name;

    protected void LogError(string message) => _logger.LogError("[{Task}] {Message}", GetType().Name, message);
    protected void LogWarning(string message) => _logger.LogWarning("[{Task}] {Message}", GetType().Name, message);
    protected void LogInfo(string message) => _logger.LogInformation("[{Task}] {Message}", GetType().Name, message);
    protected void LogDebug(string message) => _logger.LogDebug("[{Task}] {Message}", GetType().Name, message);

    protected void WriteTaskLog(TaskLogLevel level, string message, string? exception = null)
    {
        if (_fsql == null) return;
        try
        {
            _fsql.Insert(new BackgroundTaskLog
            {
                TaskName = TaskShortName,
                LogLevel = (Models.TaskLogLevel)(int)level,
                Message = message,
                Exception = exception,
            }).ExecuteAffrows();
        }
        catch { /* 日志写入失败不影响任务运行 */ }
    }
}
