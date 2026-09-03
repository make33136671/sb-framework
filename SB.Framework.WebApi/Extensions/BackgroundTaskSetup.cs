using SB.Framework.Services;

namespace SB.Framework.WebApi.Extensions;

public static class BackgroundTaskSetup
{
    /// <param name="runTasks">
    /// 是否真正装配后台任务运行器(任务注册 + 启动 HostedService)。
    /// 仅当 已安装 且 非冒烟 时为 true(SbFrameworkExtensions 统一决定):
    ///   false 时只注册 IBackgroundTaskManager 本身(保持 DI 依赖图完整,派生项目
    ///   HostedService/Controller 任何状态都能构造注入),任务不注册、不轮询、零副作用。
    ///   见 docs/AI_Deploy_Resilience.md。
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
