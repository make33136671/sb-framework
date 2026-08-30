namespace SB.Framework.WebApi.Extensions
{
    public static class CorsSetup
    {
        /// <summary>
        /// 注册 CORS 策略，根据 CorsConfig 配置决定是否限制域名
        /// </summary>
        public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration)
        {
            var allowAll = configuration.GetValue<bool>("CorsConfig:AllowAll");
            var origins = configuration.GetSection("CorsConfig:Origins").Get<string[]>();

            services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    if (!allowAll && origins is { Length: > 0 })
                    {
                        policy.WithOrigins(origins)
                              .AllowAnyMethod()
                              .AllowAnyHeader()
                              .AllowCredentials();
                    }
                    else
                    {
                        // 不用 AllowAnyOrigin()：SignalR 等客户端默认 credentials=include，
                        // 通配符 * 与凭据请求不兼容；回显任意 Origin + AllowCredentials 覆盖全放行场景。
                        policy.SetIsOriginAllowed(_ => true)
                              .AllowAnyMethod()
                              .AllowAnyHeader()
                              .AllowCredentials();
                    }
                });
            });

            return services;
        }
    }
}
