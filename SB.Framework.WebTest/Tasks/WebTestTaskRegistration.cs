using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SB.Framework.Core.BackgroundTask;
using SB.Framework.Services;

namespace SB.Framework.WebTest.Tasks;

/// <summary>
/// 测试任务注册器(衍生项目注册业务任务的标准姿势)。
/// 框架 BackgroundTaskRegistrationService 只注册框架内置任务(DatabaseBackup);
/// 派生项目再挂一个 IHostedService 调 IBackgroundTaskManager.RegisterTask 注入自己的任务。
///
/// 两个框架保证:
///   1. IBackgroundTaskManager 全状态可注入(未安装/冒烟态管理器在、运行器不装配);
///   2. 业务任务实例依赖 IFreeSql —— 只在【已安装】时注册(用 installed.lock 门控,
///      与 SetupMiddleware 同一判据),未安装态跳过,启动后由安装流程/重启自然接管。
/// </summary>
public class WebTestTaskRegistration : IHostedService
{
    private readonly IBackgroundTaskManager _manager;
    private readonly IHostEnvironment _env;
    private readonly IServiceProvider _sp;

    public WebTestTaskRegistration(IBackgroundTaskManager manager, IHostEnvironment env, IServiceProvider sp)
    {
        _manager = manager;
        _env = env;
        _sp = sp;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path.Combine(_env.ContentRootPath, "installed.lock")))
            return Task.CompletedTask;   // 未安装:任务依赖 IFreeSql,注册无意义

        _manager.RegisterTask("OrderTimeout",
            ActivatorUtilities.CreateInstance<OrderTimeoutTask>(_sp));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
