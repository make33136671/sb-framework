using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using SB.Framework.Core;
using SB.Framework.Core.Authentication;
using SB.Framework.Core.Authentication.Jwt;
using SB.Framework.Core.Expand;
using SB.Framework.Models;
using System.Reflection;
using System.Security.Claims;
using System.Text;

namespace SB.Framework.WebApi.Extensions
{
    public static class JwtSetup
    {
        public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            Assembly jwtToken = Assembly.Load("SB.Framework.Core");
            var jwtTokenAssembly = jwtToken.ExportedTypes.Where(t => t.Name == "JwtToken").FirstOrDefault();
            services.AddSingleton(jwtTokenAssembly);

            services
                .AddAuthentication("JwtAdmin")
                .AddJwtBearer("JwtAdmin", options =>
                {
                    IConfigurationSection jwtConfig = configuration.GetSection("JwtAdmin");
                    jwtConfig.Bind(options);

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwtConfig["Issuer"],
                        ValidAudience = jwtConfig["Audience"],
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfig["Key"]))
                    };

                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            return HandleApiKey(context);
                        }
                    };
                });

            services
                .AddAuthentication("JwtApi")
                .AddJwtBearer("JwtApi", options =>
                {
                    IConfigurationSection jwtConfig = configuration.GetSection("JwtApi");
                    jwtConfig.Bind(options);

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwtConfig["Issuer"],
                        ValidAudience = jwtConfig["Audience"],
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfig["Key"]))
                    };

                    options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents()
                    {
                        OnTokenValidated = context =>
                        {
                            var cache = context.HttpContext.RequestServices.GetService<IMemoryCache>();
                            JwtToken? jwtToken = context.HttpContext.RequestServices.GetService<JwtToken>();

                            string appToken = context.Request.Headers["Authorization"].ToString();
                            appToken = appToken.Replace("Bearer ", "");

                            System.Security.Claims.ClaimsPrincipal claims = jwtToken.DecryptToken("JwtApi", appToken);
                            string userIdStr = claims.FindFirstValue("Id") ?? "";
                            string token = cache.Get<string>(userIdStr);

                            if (token != null && token != appToken)
                            {
                                context.Fail("请重新登录App");
                            }
                            else if (token == null)
                            {
                                cache.Set<string>(userIdStr, appToken);
                            }

                            IFreeSql? freeSql = context.HttpContext.RequestServices.GetService<IFreeSql>();
                            if (freeSql != null && !string.IsNullOrEmpty(userIdStr) && int.TryParse(userIdStr, out int userId))
                            {
                                Member member = freeSql.Select<Member>().Where(c => c.Id == userId).First();
                                if (member != null && member.Status != UserStatus.Active)
                                {
                                    context.Fail("请重新登录App");
                                }
                            }

                            return Task.CompletedTask;
                        },
                        OnMessageReceived = context => HandleApiKey(context),
                        OnForbidden = context => Task.CompletedTask,
                        OnChallenge = context => Task.CompletedTask,
                        OnAuthenticationFailed = context => Task.CompletedTask
                    };
                });

            return services;
        }

        /// <summary>
        /// API Key 认证 — 在 JWT 验证前拦截。请求带 Authorization: Bearer sk_… 时,
        /// 交给功能包提供的 IApiKeyAuthenticator 校验(含失败节流 / 审计 / 归属账号状态校验)。
        /// 取代原静态 X-Master-Key:不再做配置串比对。功能包未加载(DI 取不到)时本方法无副作用,交回 JWT 校验。
        /// </summary>
        private static Task HandleApiKey(MessageReceivedContext context)
        {
            var authenticator = context.HttpContext.RequestServices
                .GetService(typeof(IApiKeyAuthenticator)) as IApiKeyAuthenticator;
            if (authenticator == null) return Task.CompletedTask;

            var result = authenticator.Resolve(context.HttpContext, context.Scheme.Name);
            if (result.Outcome == ApiKeyOutcome.Authenticated)
            {
                context.Principal = result.Principal;
                context.Success();
            }
            else if (result.Outcome == ApiKeyOutcome.Invalid)
            {
                // 带了 key 但无效 → 直接判失败,避免再走 JWT 解析报无关错误
                context.Fail(result.FailReason ?? "无效的 API Key");
            }
            return Task.CompletedTask;
        }
    }
}
