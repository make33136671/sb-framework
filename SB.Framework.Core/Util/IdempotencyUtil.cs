using System.Collections.Concurrent;

namespace SB.Framework.Core.Util;

/// <summary>
/// 幂等去重工具：基于时间窗口的"已处理"标记。
/// 用于消息去重（updateId）、Webhook 重复投递、事件幂等消费。
/// </summary>
/// <remarks>
/// 进程内并发安全（ConcurrentDictionary）。键值带过期时间，访问时惰性清理 + 定期清理，
/// 避免无界增长。跨进程幂等需配合分布式缓存（如 Redis）在业务层实现。
/// </remarks>
public static class IdempotencyUtil
{
    private static readonly ConcurrentDictionary<string, DateTime> _seen = new();
    private static DateTime _lastCleanup = AppTime.UtcNow;
    private static readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 尝试标记某键。若该键在 window 内未被标记过，则标记并返回 true（首次）；否则返回 false（重复）。
    /// </summary>
    /// <param name="key">幂等键（如 "telegram:{accountId}:{updateId}"）</param>
    /// <param name="window">幂等窗口（键在此时间内视为重复）</param>
    /// <returns>true=首次（应处理）；false=重复（应跳过）</returns>
    public static bool TryMark(string key, TimeSpan window)
    {
        if (string.IsNullOrEmpty(key)) return true;

        var now = AppTime.UtcNow;
        LazyCleanup(now);

        var expiry = now + window;
        // AddOrUpdate：不存在→新增返回 true；存在且未过期→false；存在但已过期→更新返回 true
        var firstSeen = true;
        _seen.AddOrUpdate(
            key,
            _ => expiry,
            (_, existingExpiry) =>
            {
                if (existingExpiry > now)
                {
                    firstSeen = false;
                    return existingExpiry;
                }
                return expiry; // 已过期，重新标记
            });
        return firstSeen;
    }

    /// <summary>
    /// 判断某键是否已被标记（在窗口内）。只读，不标记。
    /// </summary>
    public static bool IsMarked(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        return _seen.TryGetValue(key, out var expiry) && expiry > AppTime.UtcNow;
    }

    /// <summary>
    /// 手动移除某键的标记（罕见：需要重新处理已标记项时）。
    /// </summary>
    public static void Forget(string key)
    {
        if (!string.IsNullOrEmpty(key)) _seen.TryRemove(key, out _);
    }

    /// <summary>
    /// 清空全部标记（测试 / 重置用）。
    /// </summary>
    public static void Clear() => _seen.Clear();

    /// <summary>
    /// 惰性清理过期键（每 5 分钟一次，避免每次调用都扫描全表）。
    /// </summary>
    private static void LazyCleanup(DateTime now)
    {
        if (now - _lastCleanup < _cleanupInterval) return;
        _lastCleanup = now;
        foreach (var kv in _seen)
        {
            if (kv.Value <= now) _seen.TryRemove(kv.Key, out _);
        }
    }
}
