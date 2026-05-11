using Microsoft.OpenApi.Models;
using SB.Framework.Core;
using System.Reflection;

namespace SB.Framework.WebApi.Extensions
{
    public static class SwaggerSetup
    {
        /// <summary>
        /// 有条件注册 Swagger 文档生成服务
        /// </summary>
        public static IServiceCollection AddSwaggerDoc(this IServiceCollection services)
        {
            if (!AppGlobal.AppSettings.UseSwagger)
                return services;

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = AppGlobal.AppSettings.ProductCn ?? "SB API",
                    Version = "v1"
                });

                // 加载 XML 注释文件
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                {
                    c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
                }

                // JWT Bearer 认证
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer"
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                        },
                        Array.Empty<string>()
                    }
                });
            });

            return services;
        }

        /// <summary>
        /// 有条件启用 Swagger UI 中间件
        /// </summary>
        public static WebApplication UseSwaggerDoc(this WebApplication app)
        {
            if (!AppGlobal.AppSettings.UseSwagger)
                return app;

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", $"{AppGlobal.AppSettings.ProductCn ?? "SB API"} v1");
            });

            return app;
        }
    }
}
