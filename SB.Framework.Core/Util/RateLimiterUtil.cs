using System.Collections.Concurrent;

namespace SB.Framework.Core.Util;

/// <summary>
/// 统一限流入口：按 key 的固定窗口限流（纯进程内，无外部依赖）。
/// 用于业务配额、接口频率控制、调用限额等。基于 ConcurrentDictionary 的滑动窗口计数。
/// </summary>
/// <remarks>
/// 进程内并发安全。跨进程限流需用分布式方案（如 Redis）。需要异步令牌桶语义时
/// 可改用 System.Threading.RateLimiting，本工具提供轻量同步判定。
/// </remarks>
public static class RateLimiterUtil
{
    private sealed class WindowCounter
    {
        public long WindowStartTicks;
        public int Count;
    }

    private static readonly ConcurrentDictionary<string, WindowCounter> _counters = new();

    /// <summary>
    /// 尝试获取一次许可。窗口内未超 limit 则计数 +1 返回 true，否则返回 false（被限流）。
    /// </summary>
    /// <param name="key">限流键（如 "telegram:send:{accountId}"）</param>
    /// <param name="limit">窗口内最大允许次数</param>
    /// <param name="window">时间窗口</param>
    /// <returns>true=放行；false=被限流</returns>
    public static bool TryAcquire(string key, int limit, TimeSpan window)
    {
        if (string.IsNullOrEmpty(key) || limit <= 0 || window <= TimeSpan.Zero) return true;

        var now = AppTime.UtcNow;
        var windowTicks = window.Ticks;
        var counter = _counters.GetOrAdd(key, _ => new WindowCounter { WindowStartTicks = now.Ticks, Count = 0 });

        lock (counter)
        {
            // 窗口已过 → 重置
            if (now.Ticks - counter.WindowStartTicks >= windowTicks)
            {
                counter.WindowStartTicks = now.Ticks;
                counter.Count = 0;
            }

            if (counter.Count >= limit) return false;
            counter.Count++;
            return true;
        }
    }

    /// <summary>
    /// 异步获取一次许可：被限流时按估算等待时间异步等待后重试，直到成功或超时。
    /// 适用于"宁可等也要执行"的场景（如出站发送节流）。
    /// </summary>
    public static async Task<bool> WaitAcquireAsync(string key, int limit, TimeSpan window, CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (TryAcquire(key, limit, window)) return true;

            // 估算窗口剩余时间，等待一小段后重试
            await Task.Delay(Math.Max(50, (int)(window.TotalMilliseconds / Math.Max(1, limit))), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 重置某 key 的计数（如账号状态变更）。
    /// </summary>
    public static void Reset(string key)
    {
        if (!string.IsNullOrEmpty(key)) _counters.TryRemove(key, out _);
    }

    /// <summary>
    /// 清空全部（测试 / 重置用）。
    /// </summary>
    public static void Clear() => _counters.Clear();
}
