using Microsoft.Extensions.Caching.Memory;

namespace SB.Framework.Services
{
    /// <summary>
    /// 基于 MemoryCache 的缓存实现
    /// </summary>
    public class CacheMemoryCacheService : ICacheService
    {
        private readonly IMemoryCache _cache;
        private readonly object _lockObject = new object();
        // 维护一个前缀和键的映射，用于支持 RemoveByPrefix
        private readonly Dictionary<string, HashSet<string>> _prefixKeys = new Dictionary<string, HashSet<string>>();
        // 记录所有缓存键，用于 ClearAll
        private readonly HashSet<string> _allKeys = new HashSet<string>();

        public string ProviderName => "Memory";

        public CacheMemoryCacheService(IMemoryCache memoryCache)
        {
            _cache = memoryCache;
        }

        public T Get<T>(string key)
        {
            return _cache.TryGetValue(key, out T value) ? value : default;
        }

        public void Set<T>(string key, T value, TimeSpan? expiration = null)
        {
            var options = new MemoryCacheEntryOptions();
            if (expiration.HasValue)
                options.SetAbsoluteExpiration(expiration.Value);

            _cache.Set(key, value, options);

            // 记录键
            lock (_lockObject)
            {
                _allKeys.Add(key);
            }

            // 记录前缀映射关系
            lock (_lockObject)
            {
                foreach (var prefix in _prefixKeys.Keys.ToList())
                {
                    if (key.StartsWith(prefix))
                    {
                        _prefixKeys[prefix].Add(key);
                    }
                }
            }
        }

        public void Remove(string key)
        {
            _cache.Remove(key);
            lock (_lockObject) { _allKeys.Remove(key); }
        }

        public void RemoveByPrefix(string keyPrefix)
        {
            lock (_lockObject)
            {
                if (!_prefixKeys.ContainsKey(keyPrefix))
                {
                    _prefixKeys[keyPrefix] = new HashSet<string>();
                }

                var keysToRemove = _prefixKeys[keyPrefix].ToList();
                foreach (var key in keysToRemove)
                {
                    _cache.Remove(key);
                    _prefixKeys[keyPrefix].Remove(key);
                    _allKeys.Remove(key);
                }
            }
        }

        public void ClearAll()
        {
            lock (_lockObject)
            {
                foreach (var key in _allKeys.ToList())
                {
                    _cache.Remove(key);
                }
                _allKeys.Clear();
                _prefixKeys.Clear();
            }
        }

        public int CountByPrefix(string keyPrefix)
        {
            lock (_lockObject)
            {
                return _allKeys.Count(k => k.StartsWith(keyPrefix));
            }
        }

        public Dictionary<string, object?>? GetStats()
        {
            lock (_lockObject)
            {
                return new Dictionary<string, object?>
                {
                    ["instance_name"] = "",
                    ["instance_keys"] = _allKeys.Count,
                    ["dbsize"] = _allKeys.Count,
                };
            }
        }
    }
}
