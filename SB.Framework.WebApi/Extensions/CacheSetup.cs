using SB.Framework.Core;
using SB.Framework.Services;
using StackExchange.Redis;

namespace SB.Framework.WebApi.Extensions
{
    public static class CacheSetup
    {
        /// <summary>
        /// 注册缓存服务
        /// - 若 CacheConfig.Enabled = false，使用 CacheNullService（No-Op 实现）
        /// - 若 CacheConfig.Enabled = true，根据 Provider 选择 Redis 或 Memory 实现
        /// </summary>
        public static IServiceCollection AddCacheService(this IServiceCollection services)
        {
            var cacheConfig = AppGlobal.AppSettings.CacheConfig;

            // 如果禁用缓存，使用 Null Object 模式的缓存服务
            if (!cacheConfig.Enabled)
            {
                services.AddSingleton<ICacheService, CacheNullService>();
                return services;
            }

            var provider = cacheConfig.Provider?.Trim();

            if (string.Equals(provider, "Redis", StringComparison.OrdinalIgnoreCase))
            {
                var redisConfig = cacheConfig.Redis;
                var instanceName = redisConfig.InstanceName ?? "";
                services.AddSingleton<IConnectionMultiplexer>(_ =>
                {
                    // AllowAdmin=true 是 server.Info() / server.Keys(pattern) / SLOWLOG GET 等
                    // 运维诊断命令的必要开关。StackExchange.Redis 默认拒绝这些命令,
                    // 导致 CacheRedisService.GetStats() 抛 RedisCommandException → stats 字段返 null。
                    var opts = ConfigurationOptions.Parse(redisConfig.Connection);
                    opts.AllowAdmin = true;
                    return ConnectionMultiplexer.Connect(opts);
                });
                services.AddSingleton<ICacheService>(_ =>
                    new CacheRedisService(_.GetRequiredService<IConnectionMultiplexer>(), instanceName));
            }
            else
            {
                // 默认使用 MemoryCache
                services.AddMemoryCache();
                services.AddSingleton<ICacheService, CacheMemoryCacheService>();
            }

            return services;
        }
    }
}
