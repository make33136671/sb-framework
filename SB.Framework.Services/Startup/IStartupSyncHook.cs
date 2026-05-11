namespace SB.Framework.Services.Startup;

/// <summary>
/// 启动后同步钩子 — 在数据库 schema 同步、Module Seed 完成后、HTTP 监听前执行。
/// 派生项目实现此接口注入启动时一次性同步逻辑（如 MindMap 主导图刷新）。
/// </summary>
public interface IStartupSyncHook
{
    /// <summary>执行顺序，越小越先</summary>
    int Order { get; }

    /// <summary>异步执行钩子</summary>
    Task RunAsync(IServiceProvider sp, CancellationToken ct);
}
