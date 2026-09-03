using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SB.Framework.Core.BackgroundTask;
using SB.Framework.Services;

namespace SB.Framework.WebTest.Tasks;

/// <summary>
/// 测试任务注册器(衍生项目注册业务任务的标准姿势)。
/// 框架 BackgroundTaskRegistrationService 只注册框架内置任务(DatabaseBackup);
/// 派生项目再挂一个 IHostedService 调 IBackgroundTaskManager.RegisterTask 注入自己的任务。
/// 注意:未安装态(无 installed.lock)框架不注册 IBackgroundTaskManager,此处可空跳过。
/// </summary>
public class WebTestTaskRegistration : IHostedService
{
    private readonly IServiceProvider _sp;

    public WebTestTaskRegistration(IServiceProvider sp) => _sp = sp;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var manager = _sp.GetService(typeof(IBackgroundTaskManager)) as IBackgroundTaskManager;
        if (manager == null)
            return Task.CompletedTask;   // 未安装态:任务系统未装配,静默跳过

        manager.RegisterTask("OrderTimeout",
            ActivatorUtilities.CreateInstance<OrderTimeoutTask>(_sp));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
