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
            // net10 起 JsonSerializer.Deserialize 新增 ReadOnlySpan<byte> 重载，
            // RedisValue 同时可隐式转 string / byte[] → 直接传会 CS0121 二义性，显式转 string 消歧。
            return value.HasValue ? JsonSerializer.Deserialize<T>(value.ToString())! : default!;
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

        public int CountByPrefix(string keyPrefix)
        {
            var server = _redisDb.Multiplexer.GetServer(_redisDb.Multiplexer.GetEndPoints().First());
            return server.Keys(pattern: $"{_prefix}{keyPrefix}*").Count();
        }

        public Dictionary<string, object?>? GetStats()
        {
            try
            {
                var mux = _redisDb.Multiplexer;
                var endpoint = mux.GetEndPoints().First();
                var server = mux.GetServer(endpoint);

                var info = server.Info()
                    .SelectMany(g => g)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);

                long.TryParse(info.GetValueOrDefault("keyspace_hits"), out var hits);
                long.TryParse(info.GetValueOrDefault("keyspace_misses"), out var misses);
                var total = hits + misses;
                var hitRate = total == 0 ? 0d : Math.Round(hits * 100d / total, 2);

                var instanceKeys = string.IsNullOrEmpty(_prefix)
                    ? -1
                    : server.Keys(pattern: $"{_prefix}*").Count();

                var slowlog = new List<Dictionary<string, object?>>();
                try
                {
                    var raw = server.Execute("SLOWLOG", "GET", "5");
                    // SLOWLOG GET 返回数组；非数组时强转抛异常由外层 catch 兜底（等价于旧 Type 检查）
                    foreach (var entry in (RedisResult[])raw!)
                    {
                        var arr = (RedisResult[])entry!;
                        var cmd = string.Join(" ",
                            ((RedisResult[])arr[3]!).Select(x => x.ToString()));
                        slowlog.Add(new Dictionary<string, object?>
                        {
                            ["id"] = (long)arr[0],
                            ["timestamp"] = (long)arr[1],
                            ["duration_us"] = (long)arr[2],
                            ["command"] = cmd,
                        });
                    }
                }
                catch { /* slowlog 未启用或权限不足,空 */ }

                long ParseLong(string k) => long.TryParse(info.GetValueOrDefault(k), out var v) ? v : 0;

                return new Dictionary<string, object?>
                {
                    ["redis_version"] = info.GetValueOrDefault("redis_version"),
                    ["endpoint"] = endpoint.ToString(),
                    ["database_id"] = _redisDb.Database,
                    ["instance_name"] = _prefix,
                    ["instance_keys"] = instanceKeys,
                    ["dbsize"] = server.DatabaseSize(),
                    ["used_memory_human"] = info.GetValueOrDefault("used_memory_human"),
                    ["used_memory_peak_human"] = info.GetValueOrDefault("used_memory_peak_human"),
                    ["maxmemory_human"] = info.GetValueOrDefault("maxmemory_human"),
                    ["uptime_in_seconds"] = ParseLong("uptime_in_seconds"),
                    ["keyspace_hits"] = hits,
                    ["keyspace_misses"] = misses,
                    ["hit_rate"] = hitRate,
                    ["instantaneous_ops_per_sec"] = ParseLong("instantaneous_ops_per_sec"),
                    ["connected_clients"] = ParseLong("connected_clients"),
                    ["total_commands_processed"] = ParseLong("total_commands_processed"),
                    ["slowlog"] = slowlog,
                };
            }
            catch (Exception ex)
            {
                // 调用方能看到具体错误,而非吞掉返 null 导致前端只显示"无 stats"无法排查
                return new Dictionary<string, object?>
                {
                    ["instance_name"] = _prefix,
                    ["error"] = ex.GetType().Name + ": " + ex.Message,
                };
            }
        }
    }
}
