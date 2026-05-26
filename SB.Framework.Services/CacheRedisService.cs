using StackExchange.Redis;
using System.Text.Json;

namespace SB.Framework.Services
{
    /// <summary>
    /// 基于 Redis 的缓存实现
    /// </summary>
    public class CacheRedisService : ICacheService
    {
        private readonly IDatabase _redisDb;
        private readonly string _prefix;

        public string ProviderName => "Redis";

        public CacheRedisService(IConnectionMultiplexer connectionMultiplexer, string instanceName = "")
        {
            _redisDb = connectionMultiplexer.GetDatabase();
            _prefix = instanceName;
        }

        private string P(string key) => string.IsNullOrEmpty(_prefix) ? key : $"{_prefix}{key}";

        public T Get<T>(string key)
        {
            var value = _redisDb.StringGet(P(key));
            return value.HasValue ? JsonSerializer.Deserialize<T>(value) : default;
        }

        public void Set<T>(string key, T value, TimeSpan? expiration = null)
        {
            var json = JsonSerializer.Serialize(value);
            _redisDb.StringSet(P(key), json, expiration);
        }

        public void Remove(string key) => _redisDb.KeyDelete(P(key));

        public void RemoveByPrefix(string keyPrefix)
        {
            var server = _redisDb.Multiplexer.GetServer(_redisDb.Multiplexer.GetEndPoints().First());
            var keys = server.Keys(pattern: $"{_prefix}{keyPrefix}*").ToArray();
            if (keys.Length > 0)
            {
                _redisDb.KeyDelete(keys);
            }
        }

        public void ClearAll()
        {
            var server = _redisDb.Multiplexer.GetServer(_redisDb.Multiplexer.GetEndPoints().First());
            // 只清当前系统前缀的 key，避免清掉同一 Redis 实例上其他系统的缓存
            var pattern = string.IsNullOrEmpty(_prefix) ? "*" : $"{_prefix}*";
            var keys = server.Keys(pattern: pattern).ToArray();
            if (keys.Length > 0)
            {
                _redisDb.KeyDelete(keys);
            }
        }
    }
}
