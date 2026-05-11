using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.BackgroundTask;
using TaskStatus = SB.Framework.Core.BackgroundTask.TaskStatus;

namespace SB.Framework.Services;

public class BackgroundTaskManager : IBackgroundTaskManager, SB.Framework.Models.IModuleInitializer
{
    #region IModuleInitializer
    public string ModuleName => "后台任务";
    public int InitOrder => 60;
    public Type[] GetEntityTypes() => new[] { typeof(SB.Framework.Models.BackgroundTaskLog) };
    #endregion

    private readonly ConcurrentDictionary<string, BaseBackgroundTask> _tasks = new();
    private readonly ILogger<BackgroundTaskManager> _logger;

    public BackgroundTaskManager(ILogger<BackgroundTaskManager> logger)
    {
        _logger = logger;
    }

    public void RegisterTask(string name, BaseBackgroundTask task)
    {
        if (!_tasks.TryAdd(name, task))
            throw new InvalidOperationException($"Task '{name}' is already registered.");
        _logger.LogInformation("Background task '{TaskName}' registered.", name);
    }

    public async Task StartTask(string name)
    {
        var task = GetTask(name);
        if (task.Status == TaskStatus.Running)
        {
            _logger.LogWarning("Background task '{TaskName}' is already running.", name);
            return;
        }
        await task.StartAsync(CancellationToken.None);
        _logger.LogInformation("Background task '{TaskName}' started.", name);
    }

    public async Task StopTask(string name)
    {
        var task = GetTask(name);
        if (task.Status == TaskStatus.Stopped)
        {
            _logger.LogWarning("Background task '{TaskName}' is already stopped.", name);
            return;
        }
        await task.StopAsync(CancellationToken.None);
        _logger.LogInformation("Background task '{TaskName}' stopped.", name);
    }

    public async Task RestartTask(string name)
    {
        var task = GetTask(name);
        if (task.Status == TaskStatus.Running)
            await task.StopAsync(CancellationToken.None);
        await task.StartAsync(CancellationToken.None);
        _logger.LogInformation("Background task '{TaskName}' restarted.", name);
    }

    public TaskStatus GetTaskStatus(string name)
    {
        return GetTask(name).Status;
    }

    public Task<(bool Ok, string? Error)> RunOnce(string name)
    {
        var task = GetTask(name);
        _logger.LogInformation("Background task '{TaskName}' manually triggered (RunOnce).", name);
        return task.RunOnceAsync();
    }

    public List<BackgroundTaskInfo> GetAllTasks()
    {
        return _tasks.Select(kvp => new BackgroundTaskInfo
        {
            Name = kvp.Key,
            DisplayName = kvp.Value.DisplayName,
            Status = kvp.Value.Status,
            NextRunTime = kvp.Value.NextRunTime,
            ExecutionRecords = kvp.Value.ExecutionRecords
        }).ToList();
    }

    public async Task ReloadParameters(string name)
    {
        var task = GetTask(name);
        await task.RestartAsync();
        _logger.LogInformation("Background task '{TaskName}' parameters reloaded.", name);
    }

    private BaseBackgroundTask GetTask(string name)
    {
        if (!_tasks.TryGetValue(name, out var task))
            throw new KeyNotFoundException($"Task '{name}' is not registered.");
        return task;
    }
}
