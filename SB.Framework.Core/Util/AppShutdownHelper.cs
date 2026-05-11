using Microsoft.Extensions.Hosting;

namespace SB.Framework.Core.Util;

/// <summary>
/// 应用优雅停止工具，延迟后调用 StopApplication，让当前响应先返回客户端
/// systemd Restart=always 会自动重新拉起进程
/// </summary>
public static class AppShutdownHelper
{
    public static void GracefulStop(IHostApplicationLifetime lifetime, int delayMs = 1500)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            lifetime.StopApplication();
        });
    }
}
