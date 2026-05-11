using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SB.Framework.Services.BackgroundTasks;

namespace SB.Framework.Services;

/// <summary>
/// 后台任务注册服务
///
/// 基座框架自带任务在此注册（DatabaseBackup 等）；
/// 衍生项目如需追加业务任务，可继承此类或在自己的 IHostedService 中通过 IBackgroundTaskManager.RegisterTask 注入
/// </summary>
public class BackgroundTaskRegistrationService : IHostedService
{
    private readonly IBackgroundTaskManager _taskManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IFreeSql _fsql;
    private readonly ILogger<BackgroundTaskRegistrationService> _logger;

    public BackgroundTaskRegistrationService(
        IBackgroundTaskManager taskManager,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        IFreeSql fsql,
        ILogger<BackgroundTaskRegistrationService> logger)
    {
        _taskManager = taskManager;
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
        _fsql = fsql;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 数据库自动备份 + 云端上传（默认 12 小时一次，按系统设置 backup_interval_hours 调整，0 关闭）
        // 仅注册任务；统一由 BackgroundTaskHostedService 启动，避免重复触发
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var dbBackupTask = new DatabaseBackupTask(
            _loggerFactory.CreateLogger<DatabaseBackupTask>(),
            _fsql,
            scopeFactory);
        _taskManager.RegisterTask("DatabaseBackup", dbBackupTask);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
