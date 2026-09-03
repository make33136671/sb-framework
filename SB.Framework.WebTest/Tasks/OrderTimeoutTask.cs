using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.BackgroundTask;
using SB.Framework.Core.Util;

namespace SB.Framework.WebTest.Tasks;

/// <summary>
/// 模拟业务后台任务:订单超时巡检。
/// 演示框架后台任务能力映射 —— 继承 BaseBackgroundTask(熔断/执行记录/任务日志全框架托管),
/// Interval 轮询 + ShouldRunNowAsync 节流 + WriteTaskLog 写 SYS_BackgroundTaskLog,
/// 且被 WebTestTaskRegistration 注册进 IBackgroundTaskManager 后可从测试台手动 RunOnce。
/// 业务行为:把创建超过 TimeoutMinutes 的 Pending 订单批量置为 Timeout(幂等,可反复触发)。
/// </summary>
public class OrderTimeoutTask : BaseBackgroundTask
{
    private readonly IFreeSql _fsql;

    public OrderTimeoutTask(ILogger<OrderTimeoutTask> logger, IFreeSql fsql) : base(logger, fsql)
        => _fsql = fsql;

    public override string DisplayName => "订单超时巡检(WebTest 模拟)";

    /// <summary>轮询周期:测试项目故意设短,便于观察自动调度</summary>
    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

    /// <summary>超时阈值(分钟):测试用 30 秒级别体感更直观,取整分钟按 1 分钟</summary>
    public const int TimeoutMinutes = 1;

    protected override Task ExecuteTaskAsync(CancellationToken cancellationToken)
    {
        var deadline = AppTime.UtcNow.AddMinutes(-TimeoutMinutes);
        var expired = _fsql.Select<TestOrder>()
            .Where(o => o.Status == TestOrderStatus.Pending && o.CreatedAt < deadline)
            .ToList();

        if (expired.Count == 0)
        {
            WriteTaskLog(TaskLogLevel.Info, $"巡检完成:无超时订单(阈值 {TimeoutMinutes} 分钟)");
            return Task.CompletedTask;
        }

        foreach (var o in expired)
        {
            o.Status = TestOrderStatus.Timeout;
            o.CloseReason = $"超时 {TimeoutMinutes} 分钟未支付,系统自动关单";
        }
        var affected = _fsql.Update<TestOrder>().SetSource(expired).ExecuteAffrows();
        WriteTaskLog(TaskLogLevel.Warning,
            $"巡检完成:关单 {affected} 笔 [{string.Join(",", expired.Select(o => o.OrderNo))}]");
        return Task.CompletedTask;
    }
}
