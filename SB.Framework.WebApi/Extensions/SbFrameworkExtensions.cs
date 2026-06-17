using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SB.Framework.Core;
using SB.Framework.Core.License;
using SB.Framework.Core.Middleware;
using SB.Framework.Core.Util;
using SB.Framework.Features;
using SB.Framework.WebApi.Middleware;
using Serilog;
using Serilog.Extensions.Logging;
using System.Text.Json.Serialization;

namespace SB.Framework.WebApi.Extensions;

/// <summary>
/// 派生项目启动壳一键装配。Program.cs 调用 AddSbFramework() + UseSbFramework() 完成所有基座基础设施。
/// </summary>
public static class SbFrameworkExtensions
{
    /// <summary>
    /// 冒烟检测模式：部署预检时由 deploy 流程用临时端口拉起本进程,只验证"新包能否正常启动"。
    /// 此模式下绝不触碰生产副作用——跳过 DB 迁移(SeedDatabase)、不启动后台任务系统(含 License 心跳),
    /// 只构建 Web 宿主 + 映射 /health 供探活。由环境变量 SB_SMOKE_TEST=1 触发。
    /// 见 docs/AI_Deploy_Resilience.md。
    /// </summary>
    public static bool IsSmokeTest =>
        string.Equals(Environment.GetEnvironmentVariable("SB_SMOKE_TEST"), "1", StringComparison.Ordinal);

    public static WebApplicationBuilder AddSbFramework(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        AppGlobal.AppSettings = configuration.Get<AppSettings>();
        var smoke = IsSmokeTest;

        ArchiveOldLogs();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();
        builder.Host.UseSerilog();
        builder.Logging.ClearProviders();
        builder.Host.UseSystemd();

        builder.Services.AddSetupServices();

        // ★[F-1] 发现 + 黑名单过滤 —— 必须最早，后续所有扫描（[F-2/3/4] + SeedDatabase）都依赖它产出的 catalog
        var featureOptions = new FeatureScannerOptions
        {
            Disabled = configuration.GetSection("Features:Disabled").Get<string[]>() ?? Array.Empty<string>(),
            ProjectAssemblyName = configuration.GetValue<string>("ProjectAssemblyName"),
            // 单文件发布时派生项目显式传 SingleFilePublishAssemblies（§3.4）
        };
        FeatureRuntime.Catalog = FeatureScanner.Scan(featureOptions);
        var catalog = FeatureRuntime.Catalog;
        // 实例化清单 + 应用 EnabledByDefault + 拓扑排序（被禁的程序集已不在 catalog，这里只排已启用的）
        var modules = FeatureModuleActivator.ActivateAndSort(catalog, configuration);

        bool isInstalled = File.Exists(Path.Combine(
            builder.Environment.ContentRootPath, "installed.lock"));

#if !DISABLE_LICENSE
        builder.Services.AddLicenseService();      // 主权功能，独立于 feature 体系，原样不动
#else
        builder.Services.AddSingleton<ILicenseService>(new NullLicenseService());
#endif

        var featureCtx = new FeatureContext
        {
            Configuration = configuration,
            Environment = builder.Environment,
            IsInstalled = isInstalled,
            LoadedFeatures = modules,
            Logger = new SerilogLoggerFactory(Log.Logger).CreateLogger("Features"),
        };

        if (isInstalled)
        {
            builder.Services.AddFreeSql(configuration);
            builder.Services.AddJwtAuthentication(configuration);

            // ▲[F-2] 业务服务注册：遍历 catalog.AllScanAssemblies，[AutoDependencyInjection] 自动扫
            //   （框架Services → 各feature(短名排序) → 项目程序集，后注册覆盖先注册）
            builder.Services.AddBusinessServices(catalog);

            builder.Services.AddCacheService();
            builder.Services.AddEmailServices();
            // 通用电子表格读取(xlsx/xls/csv)——框架自注册接口绑定,业务层注入 ISpreadsheetReader 即用
            builder.Services.AddScoped<SB.Framework.Services.ISpreadsheetReader, SB.Framework.Services.NpoiSpreadsheetReader>();
            // 随机名称生成(中文人名 / 生活服务备注名)——业务层注入 IRandomNameGenerator 即用
            builder.Services.AddSingleton<SB.Framework.Services.IRandomNameGenerator, SB.Framework.Services.RandomNameGenerator>();
            builder.Services.AddQdrantService(configuration);
            // 冒烟模式不启动后台任务运行器(定时任务 + License 心跳都挂在它下面),避免临时探活实例
            // 对生产发心跳/跑定时任务;但仍注册 IBackgroundTaskManager 保持 DI 依赖图完整。
            builder.Services.AddBackgroundTasks(runTasks: !smoke);
            builder.Services.AddLoginRateLimiter();
            builder.Services.AddDatabaseHealthCheck(configuration);

            // ★[F-3] feature 自定义 DI 安装 + 强类型 Options 自动绑定（按拓扑序）
            foreach (var m in modules)
            {
                if (m.OptionsType is not null)
                    BindFeatureOptions(builder.Services, m.OptionsType,
                        configuration.GetSection($"Features:{m.FeatureName}"));
                m.ConfigureServices(builder.Services, featureCtx);   // 只做接口绑定等特例
            }
        }
        else
        {
            builder.Services.AddAuthentication();
            builder.Services.AddMemoryCache();
        }

        builder.Services.AddCorsPolicy(configuration);
        builder.Services.AddSwaggerDoc();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddRazorPages();

        builder.Services.Configure<KestrelServerOptions>(o =>
            o.Limits.MaxRequestBodySize = 500L * 1024 * 1024);
        builder.Services.Configure<FormOptions>(o =>
            o.MultipartBodyLengthLimit = 500L * 1024 * 1024);

        var mvc = builder.Services.AddControllersWithViews(options =>
        {
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            options.Conventions.Add(new AdminRoutePrefixConvention());
            var excludeControllers = configuration.GetSection("ExcludeControllers").Get<string[]>();
            if (excludeControllers?.Length > 0)
                options.Conventions.Add(new ControllerExclusionConvention(excludeControllers));
        }).AddJsonOptions(x =>
        {
            x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            x.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        // ★[F-4] Controller 跨程序集发现：框架硬编码，不交插件。必须在 AddControllersWithViews 返回的
        //   mvcBuilder 上链式调用（AddApplicationPart 是 IMvcBuilder 扩展）。这是整个插件化最易塌、
        //   塌了就是"feature Controller 全 404 且无报错"的一环。
        foreach (var asm in catalog.FeatureAssemblies)
            mvc.AddApplicationPart(asm);

        AppTime.Initialize(configuration["TimeZone"]);
        return builder;
    }

    public static WebApplication UseSbFramework(this WebApplication app)
    {
        bool isInstalled = File.Exists(Path.Combine(
            app.Environment.ContentRootPath, "installed.lock"));
        var smoke = IsSmokeTest;

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            // 不挂 UseHttpsRedirection — saibo 所有派生项目部署模式都是 nginx 反代终结 TLS 后转 HTTP 给 .NET。
            // 反代场景下 .NET 看到的请求永远是 HTTP,UseHttpsRedirection 找不到 https port 会每次都 warning,
            // 而且功能上由 nginx 在 80→443 层已经做掉 redirect,.NET 再做一遍是冗余。
        }

        app.UseCors();
        if (isInstalled) app.UseRateLimiter();
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (ctx, elapsed, ex) =>
            {
                if (ex != null || ctx.Response.StatusCode >= 500) return Serilog.Events.LogEventLevel.Error;
                if (ctx.Response.StatusCode >= 400) return Serilog.Events.LogEventLevel.Warning;
                if (elapsed > 500) return Serilog.Events.LogEventLevel.Warning;
                return Serilog.Events.LogEventLevel.Debug;
            };
        });

#if !DISABLE_LICENSE
        if (isInstalled && !app.Environment.IsDevelopment()) app.UseLicense();
#endif

        app.UseMiddleware<GlobalExceptionMiddleware>();
        app.UseMiddleware<QueryCountMiddleware>();
        app.UseStaticFiles();
        app.UseMiddleware<SetupMiddleware>();
        app.UseRouting();
        // API Key 全局身份中间件：有效 sk_ key → 设 context.User 为归属账号的 JwtAdmin 身份，
        // 使 /agent/* 和 /admin/* 接口用同一 Bearer Token 统一访问。
        // 功能包未加载时内部无副作用，故无条件挂载。
        if (isInstalled)
            app.UseMiddleware<ApiKeyMiddleware>();

        app.UseAuthorization();
        app.UseSwaggerDoc();

        // ★[F-5] feature 自定义中间件：固定锚点 = UseAuthorization 之后、MapControllers 之前
        //   （所有插件中间件在 License 闸门内侧；插件不得插队到 License 校验之前）
        var modules = FeatureModuleActivator.GetSorted();   // 复用 AddSbFramework 阶段排好的实例
        if (modules.Count > 0)
        {
            var featureCtx = FeatureModuleActivator.BuildAppContext(
                app.Configuration, app.Environment, isInstalled, modules,
                new SerilogLoggerFactory(Log.Logger).CreateLogger("Features"));
            foreach (var m in modules)
                m.Configure(app, featureCtx);
        }

        if (isInstalled) app.MapHealthChecks("/health");
        app.MapControllers();        // feature Controller 已通过 [F-4] AddApplicationPart 进入路由表
        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=DownloadApp}/{id?}");
        app.MapRazorPages();

        // 冒烟模式跳过 DB 迁移:预检只验证"新包能启动",绝不在临时实例里改生产 schema。
        // 真实迁移仍在 flip 后的正式启动时跑。见 docs/AI_Deploy_Resilience.md 的"数据库迁移红线"。
        if (isInstalled && !smoke) app.SeedDatabase();

        if (smoke)
            Log.Logger.Warning("[SmokeTest] 冒烟模式启动:已跳过 DB 迁移与后台任务,仅供 /health 探活。");

        // ★[F-7] 启动日志：打印已加载 / 被禁用 feature 清单（含原因）
        FeatureRuntime.Catalog?.LogStartupSummary();

        return app;
    }

    /// <summary>
    /// [F-3 辅助] 把 Features:{Name} 配置节绑定到 feature 声明的强类型 Options。
    /// 标准库的 OptionsConfigurationServiceCollectionExtensions.Configure 只有泛型版，
    /// OptionsType 是运行时 Type，故反射调用 Configure&lt;TOptions&gt;(IServiceCollection, IConfiguration)。
    /// </summary>
    private static void BindFeatureOptions(IServiceCollection services, Type optionsType, IConfiguration section)
    {
        var method = typeof(Microsoft.Extensions.DependencyInjection.OptionsConfigurationServiceCollectionExtensions)
            .GetMethods()
            .First(m => m.Name == "Configure"
                        && m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 2
                        && m.GetParameters()[1].ParameterType == typeof(IConfiguration));
        method.MakeGenericMethod(optionsType).Invoke(null, new object[] { services, section });
    }

    /// <summary>
    /// 启动前归档旧日志（与 Serilog 相对路径写入位置一致，用 CurrentDirectory）
    /// </summary>
    private static void ArchiveOldLogs()
    {
        var logsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        if (!Directory.Exists(logsDir)) return;

        var logFiles = Directory.GetFiles(logsDir, "*.*", SearchOption.TopDirectoryOnly);
        if (logFiles.Length == 0) return;

        var archiveDir = Path.Combine(logsDir, AppTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(archiveDir);

        foreach (var file in logFiles)
        {
            var fileName = Path.GetFileName(file);
            File.Move(file, Path.Combine(archiveDir, fileName));
        }
    }
}
