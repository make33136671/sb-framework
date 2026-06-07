using Microsoft.Extensions.Caching.Memory;
using SB.Feature.AccessToken.Models;

namespace SB.Feature.AccessToken.Services
{
    /// <summary>
    /// 速率与防爆破限制器(无状态,计数全落 IMemoryCache,可任意生命周期)。
    ///   - AllowKey:单 key 每分钟请求数上限(继承自 agent token RateLimitPerMinute);
    ///   - IsIpBlocked / RecordIpFailure:对"无效 key 尝试"按来源 IP 计数 + 拉黑,防在线爆破。
    /// </summary>
    public class ApiKeyRateLimiter
    {
        private readonly IMemoryCache _cache;

        public ApiKeyRateLimiter(IMemoryCache cache)
        {
            _cache = cache;
        }

        /// <summary>true=允许, false=超限。</summary>
        public bool AllowKey(ApiKey key)
        {
            if (key.RateLimitPerMinute == null || key.RateLimitPerMinute.Value <= 0)
                return true;

            var cacheKey = $"apikey_rate_{key.Id}";
            var limit = key.RateLimitPerMinute.Value;
            var count = _cache.GetOrCreate(cacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                return 0;
            });

            if (count >= limit) return false;
            _cache.Set(cacheKey, count + 1, TimeSpan.FromMinutes(1));
            return true;
        }

        /// <summary>该 IP 是否处于因爆破被临时拉黑状态。</summary>
        public bool IsIpBlocked(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;
            return _cache.TryGetValue($"apikey_block_{ip}", out _);
        }

        /// <summary>记录一次该 IP 的无效尝试;窗口内累计超过阈值即拉黑 blockSeconds 秒。</summary>
        public void RecordIpFailure(string ip, int threshold, int windowSeconds, int blockSeconds)
        {
            if (string.IsNullOrEmpty(ip)) return;

            var failKey = $"apikey_fail_{ip}";
            var count = _cache.GetOrCreate(failKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(windowSeconds);
                return 0;
            }) + 1;
            _cache.Set(failKey, count, TimeSpan.FromSeconds(windowSeconds));

            if (count >= threshold)
                _cache.Set($"apikey_block_{ip}", true, TimeSpan.FromSeconds(blockSeconds));
        }
    }
}
