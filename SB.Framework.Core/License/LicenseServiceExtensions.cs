using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace SB.Framework.Core.License
{
    public static class LicenseServiceExtensions
    {
        /// <summary>
        /// 注册授权验证服务（公钥为空时注册 NullLicenseService）
        /// </summary>
        public static IServiceCollection AddLicense(this IServiceCollection services, string? publicKeyXml)
        {
            if (string.IsNullOrEmpty(publicKeyXml))
            {
                var message = "授权系统已启用但未检测到公钥，请使用包含公钥的正式构建版本，或使用 -p:DisableLicense=true 构建以关闭授权。";
                Console.Error.WriteLine($"[FATAL] {message}");
                services.AddSingleton<ILicenseService>(new MissingKeyLicenseService(message));
                return services;
            }

            var config = AppGlobal.AppSettings?.LicenseConfig ?? new LicenseConfig();
            var service = new LicenseService(publicKeyXml, config.Key, AppGlobal.AppSettings?.ProductEn);
            services.AddSingleton<ILicenseService>(service);

            TimeRegressionGuard.Initialize(publicKeyXml);
            return services;
        }

        /// <summary>
        /// 注册心跳宽限期检查器（由 Services 层调用，注入具体实现）
        /// </summary>
        public static IServiceCollection AddHeartbeatChecker(this IServiceCollection services, IHeartbeatGracePeriodChecker checker)
        {
            services.AddSingleton(checker);
            return services;
        }

        /// <summary>
        /// 启用授权验证中间件（仅当 ILicenseService 已启用时生效）
        /// </summary>
        public static IApplicationBuilder UseLicense(this IApplicationBuilder app)
        {
            var service = app.ApplicationServices.GetService<ILicenseService>();
            if (service == null || !service.IsEnabled) return app;
            return app.UseMiddleware<LicenseMiddleware>();
        }
    }
}
