using SB.Framework.Services;

namespace SB.Framework.WebApi.Extensions;

public static class BackgroundTaskSetup
{
    public static IServiceCollection AddBackgroundTasks(this IServiceCollection services)
    {
        // 注册后台任务管理器
        services.AddSingleton<IBackgroundTaskManager, BackgroundTaskManager>();

        // 注册任务初始化器（基座空壳，衍生项目重写此方法注册业务任务）
        services.AddHostedService<BackgroundTaskRegistrationService>();

        // 注册HostedService来启动所有后台任务
        services.AddHostedService<BackgroundTaskHostedService>();

        return services;
    }
}
