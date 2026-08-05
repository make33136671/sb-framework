using SB.Framework.Core;
using SB.Framework.Core.License;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Extensions
{
    public static class LicenseSetup
    {
        /// <summary>
        /// 授权公钥文件名（位于产物根目录）
        /// </summary>
        public const string PublicKeyFileName = "license_public.key";

        /// <summary>
        /// 注册授权验证服务。
        ///
        /// 构建参数 <c>-p:DisableLicense=true</c> 会定义 <c>DISABLE_LICENSE</c> 常量，
        /// 此时直接注册 <see cref="NullLicenseService"/>（永久授权，不做验证）。
        ///
        /// 默认构建下优先使用<see cref="SB.Framework.BuildInfo.LicensePublicKey"/>
        /// （构建时把 license_public.key 内嵌进程序集，无法被替换文件绕过）；
        /// 内嵌为空（旧产物/缺密钥）才回退到产物目录的 <c>license_public.key</c> 物理文件。
        /// </summary>
        public static IServiceCollection AddLicenseService(this IServiceCollection services)
        {
            // ILicenseVerifyClient 是 LicenseMiddleware InvokeAsync 的 DI 参数，
            // 自动 DI 扫描机制（[AutoDependencyInjection]）只注册类型本身、不绑接口，
            // 这里必须手动绑定，否则生产开启 LicenseMiddleware 时会 500
            // (Unable to resolve service for type 'ILicenseVerifyClient')。
            services.AddScoped<ILicenseVerifyClient, LicenseVerifyClient>();

#if DISABLE_LICENSE
            services.AddSingleton<ILicenseService>(new NullLicenseService());
            return services;
#else
            // 优先内嵌公钥（编译进 DLL）：顺手党替换 license_public.key 文件后，
            // 自己签名仍会被内嵌公钥拒绝 —— 堵死"替换授权证书 + 自签"绕过路径。
            var publicKeyXml = !string.IsNullOrEmpty(SB.Framework.BuildInfo.LicensePublicKey)
                ? SB.Framework.BuildInfo.LicensePublicKey
                : LoadPublicKeyFromFile();
            return services.AddLicense(publicKeyXml);
#endif
        }

        /// <summary>
        /// 尝试从产物根目录读取 <c>license_public.key</c>。
        /// 文件不存在、为空或读取失败时返回 null。
        /// </summary>
        private static string? LoadPublicKeyFromFile()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, PublicKeyFileName);
                if (!File.Exists(path)) return null;
                var content = File.ReadAllText(path).Trim();
                return string.IsNullOrEmpty(content) ? null : content;
            }
            catch
            {
                return null;
            }
        }
    }
}
