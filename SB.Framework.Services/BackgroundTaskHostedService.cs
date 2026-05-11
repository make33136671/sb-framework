using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SB.Framework.Services;

public class BackgroundTaskHostedService : IHostedService
{
    private readonly IBackgroundTaskManager _taskManager;
    private readonly ILogger<BackgroundTaskHostedService> _logger;

    public BackgroundTaskHostedService(
        IBackgroundTaskManager taskManager,
        ILogger<BackgroundTaskHostedService> logger)
    {
        _taskManager = taskManager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("BackgroundTaskHostedService starting, launching all registered tasks...");

        var tasks = _taskManager.GetAllTasks();
        foreach (var taskInfo in tasks)
        {
            try
            {
                await _taskManager.StartTask(taskInfo.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start background task '{TaskName}'.", taskInfo.Name);
            }
        }

        _logger.LogDebug("BackgroundTaskHostedService started, {Count} tasks launched.", tasks.Count);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BackgroundTaskHostedService stopping, shutting down all tasks...");

        var tasks = _taskManager.GetAllTasks();
        foreach (var taskInfo in tasks)
        {
            try
            {
                await _taskManager.StopTask(taskInfo.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop background task '{TaskName}'.", taskInfo.Name);
            }
        }

        _logger.LogInformation("BackgroundTaskHostedService stopped.");
    }
}
