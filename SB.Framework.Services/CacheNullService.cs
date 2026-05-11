namespace SB.Framework.Services
{
    /// <summary>
    /// Null Object 缓存实现 - 当系统不启用缓存时使用
    /// 所有方法都是 No-Op，不执行任何实际操作
    /// </summary>
    public class CacheNullService : ICacheService
    {
        public string ProviderName => "Disabled";

        public T Get<T>(string key)
        {
            // 不启用缓存，总是返回默认值（null）
            return default;
        }

        public void Set<T>(string key, T value, TimeSpan? expiration = null)
        {
            // 不启用缓存，什么都不做
        }

        public void Remove(string key)
        {
            // 不启用缓存，什么都不做
        }

        public void RemoveByPrefix(string keyPrefix)
        {
            // 不启用缓存，什么都不做
        }

        public void ClearAll()
        {
            // 不启用缓存，什么都不做
        }
    }
}
