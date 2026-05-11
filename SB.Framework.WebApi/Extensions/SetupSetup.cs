using SB.Framework.Services;

namespace SB.Framework.WebApi.Extensions
{
    public static class SetupSetup
    {
        /// <summary>
        /// 注册安装向导服务
        /// </summary>
        public static IServiceCollection AddSetupServices(this IServiceCollection services)
        {
            services.AddSingleton<SetupService>();
            return services;
        }
    }
}
