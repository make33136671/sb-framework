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
                        policy.AllowAnyOrigin()
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    }
                });
            });

            return services;
        }
    }
}
