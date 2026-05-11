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

        public string ProviderName => "Redis";

        public CacheRedisService(IConnectionMultiplexer connectionMultiplexer)
        {
            _redisDb = connectionMultiplexer.GetDatabase();
        }

        public T Get<T>(string key)
        {
            var value = _redisDb.StringGet(key);
            return value.HasValue ? JsonSerializer.Deserialize<T>(value) : default;
        }

        public void Set<T>(string key, T value, TimeSpan? expiration = null)
        {
            var json = JsonSerializer.Serialize(value);
            _redisDb.StringSet(key, json, expiration);
        }

        public void Remove(string key) => _redisDb.KeyDelete(key);

        public void RemoveByPrefix(string keyPrefix)
        {
            var server = _redisDb.Multiplexer.GetServer(_redisDb.Multiplexer.GetEndPoints().First());
            var keys = server.Keys(pattern: $"{keyPrefix}*").ToArray();
            if (keys.Length > 0)
            {
                _redisDb.KeyDelete(keys);
            }
        }

        public void ClearAll()
        {
            var server = _redisDb.Multiplexer.GetServer(_redisDb.Multiplexer.GetEndPoints().First());
            var keys = server.Keys(pattern: "*").ToArray();
            if (keys.Length > 0)
            {
                _redisDb.KeyDelete(keys);
            }
        }
    }
}
