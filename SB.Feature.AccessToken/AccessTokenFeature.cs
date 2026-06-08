using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SB.Feature.AccessToken.Services;
using SB.Framework.Core.Authentication;
using SB.Framework.Features;

namespace SB.Feature.AccessToken
{
    /// <summary>
    /// 统一访问令牌功能清单。
    /// ConfigureServices 完成两件核心包外做不到的事:
    ///   ① 把 IApiKeyAuthenticator → ApiKeyAuthenticator 绑定到 DI(核心缝在请求期解析它);
    ///   ② 全局注册 ScopeFilter(让受限 key 的 scope 闸门对所有项目的控制器生效,含 PM 的 /agent/*)。
    /// 其余 Service 用显式 AddSingleton(IFreeSql 在框架里是单例,故服务皆可单例)。
    /// </summary>
    public sealed class AccessTokenFeature : IFeatureModule
    {
        public string FeatureName => "AccessToken";
        public string DisplayName => "访问令牌";
        public Type OptionsType => typeof(AccessTokenOptions);

        public void ConfigureServices(IServiceCollection services, FeatureContext ctx)
        {
            services.AddMemoryCache(); // 幂等,框架通常已加

            services.AddSingleton<ApiKeyRateLimiter>();
            services.AddSingleton<ApiKeyAuditService>();
            services.AddSingleton<ApiKeyService>();
            services.AddSingleton<ServiceAccountService>();

            // 核心缝实现:JwtSetup / LicenseMiddleware / ApiKeyMiddleware 在请求期解析此接口
            services.AddSingleton<IApiKeyAuthenticator, ApiKeyAuthenticator>();

            // 供控制器构造注入当前请求的 key 上下文(取代原 IAgentContext)。
            // 仅在 API Key 认证成功的请求里可解析;普通请求注入会抛(与原 agent 行为一致,只有 key 端点才注入它)。
            services.AddScoped<IApiKeyContext>(sp =>
            {
                var http = sp.GetRequiredService<IHttpContextAccessor>();
                return http.HttpContext?.Items[ApiKeyContext.HttpItemKey] as IApiKeyContext
                    ?? throw new InvalidOperationException(
                        "IApiKeyContext 只能在 API Key 认证成功的请求里取到 —— 当前请求没有 key 上下文");
            });

            // 全局 scope 闸门(受限 key 默认 deny,FullAccess / 密码登录放行)
            services.AddScoped<ScopeFilter>();
            services.Configure<MvcOptions>(o => o.Filters.AddService<ScopeFilter>());

            services.AddHostedService<ApiKeyAuditCleanupService>();
        }
    }
}
