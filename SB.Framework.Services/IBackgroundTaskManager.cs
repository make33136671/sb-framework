using SB.Framework.Core.BackgroundTask;
using TaskStatus = SB.Framework.Core.BackgroundTask.TaskStatus;

namespace SB.Framework.Services;

public class BackgroundTaskInfo
{
    public string Name { get; set; } = string.Empty;
    /// <summary>中文显示名（来自 BaseBackgroundTask.DisplayName）</summary>
    public string DisplayName { get; set; } = string.Empty;
    public TaskStatus Status { get; set; }
    /// <summary>下次执行时间 (UTC)，来自 BaseBackgroundTask.NextRunTime</summary>
    public DateTime? NextRunTime { get; set; }
    public IReadOnlyList<TaskExecutionRecord> ExecutionRecords { get; set; } = [];
}

public interface IBackgroundTaskManager
{
    void RegisterTask(string name, BaseBackgroundTask task);
    Task StartTask(string name);
    Task StopTask(string name);
    Task RestartTask(string name);
    /// <summary>立即触发一次执行（绕过轮询间隔），返回 (ok, error)</summary>
    Task<(bool Ok, string? Error)> RunOnce(string name);
    TaskStatus GetTaskStatus(string name);
    List<BackgroundTaskInfo> GetAllTasks();
    Task ReloadParameters(string name);
}
