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
    }
}
