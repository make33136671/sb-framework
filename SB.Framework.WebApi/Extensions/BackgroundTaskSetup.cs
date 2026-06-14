using SB.Framework.Services;

namespace SB.Framework.WebApi.Extensions;

public static class BackgroundTaskSetup
{
    /// <param name="runTasks">
    /// 是否真正启动后台任务的 HostedService。部署冒烟预检(SB_SMOKE_TEST=1)传 false:
    /// 保留 IBackgroundTaskManager 的 DI 注册(否则注入它的控制器在冒烟实例里解析失败 → 假性拦截部署),
    /// 但不启动任务运行器,避免临时探活实例跑定时任务 / 发 License 心跳。见 docs/AI_Deploy_Resilience.md。
    /// </param>
    public static IServiceCollection AddBackgroundTasks(this IServiceCollection services, bool runTasks = true)
    {
        // 注册后台任务管理器（DI 依赖,冒烟模式也需要,保持依赖图完整）
        services.AddSingleton<IBackgroundTaskManager, BackgroundTaskManager>();

        if (runTasks)
        {
            // 注册任务初始化器（基座空壳，衍生项目重写此方法注册业务任务）
            services.AddHostedService<BackgroundTaskRegistrationService>();

            // 注册HostedService来启动所有后台任务
            services.AddHostedService<BackgroundTaskHostedService>();
        }

        return services;
    }
}
