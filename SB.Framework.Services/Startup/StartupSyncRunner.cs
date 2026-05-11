using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace SB.Framework.Services.Startup;

/// <summary>
/// 串行执行所有 IStartupSyncHook 实现。
/// 由 DatabaseSeeder 在 Module Seed 完成后调用。
/// </summary>
public static class StartupSyncRunner
{
    public static async Task RunAllAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        using var scope = sp.CreateScope();
        var hooks = scope.ServiceProvider
            .GetServices<IStartupSyncHook>()
            .OrderBy(h => h.Order)
            .ToList();

        if (hooks.Count == 0)
        {
            Log.Debug("IStartupSyncHook: 无可执行钩子");
            return;
        }

        int ok = 0, fail = 0;
        foreach (var hook in hooks)
        {
            try
            {
                await hook.RunAsync(scope.ServiceProvider, ct);
                ok++;
            }
            catch (Exception ex)
            {
                fail++;
                Log.Error(ex, "IStartupSyncHook 执行失败: {Type}", hook.GetType().FullName);
            }
        }
        Log.Information("IStartupSyncHook ran {Total} hooks (ok={Ok}, fail={Fail})", hooks.Count, ok, fail);
    }
}
