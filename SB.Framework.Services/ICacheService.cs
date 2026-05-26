namespace SB.Framework.Services
{
    /// <summary>
    /// 缓存服务抽象接口
    /// </summary>
    public interface ICacheService
    {
        /// <summary>
        /// 缓存提供商名称
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// 获取缓存值
        /// </summary>
        T Get<T>(string key);

        /// <summary>
        /// 设置缓存
        /// </summary>
        void Set<T>(string key, T value, TimeSpan? expiration = null);

        /// <summary>
        /// 删除缓存
        /// </summary>
        void Remove(string key);

        /// <summary>
        /// 删除匹配前缀的所有缓存（用于批量清除相关缓存）
        /// </summary>
        void RemoveByPrefix(string keyPrefix);

        /// <summary>
        /// 清空所有缓存
        /// </summary>
        void ClearAll();

        /// <summary>
        /// 获取缓存运行时统计信息。返回字段视实现而定:
        /// Redis 包含 redis_version/endpoint/dbsize/used_memory_human/keyspace_hits/keyspace_misses/hit_rate/
        /// instantaneous_ops_per_sec/connected_clients/uptime_in_seconds/instance_name/instance_keys/slowlog 等。
        /// Memory 只返回基础字段(entry_count)。Null 返回 null。
        /// 默认实现返 null,实现可选 override。
        /// </summary>
        Dictionary<string, object?>? GetStats() => null;

        /// <summary>
        /// 数指定业务前缀下的 key 数量(已自动叠加 instanceName,调用方传业务前缀即可)。
        /// 默认实现返 -1 表示不支持。
        /// </summary>
        int CountByPrefix(string keyPrefix) => -1;
    }
}
