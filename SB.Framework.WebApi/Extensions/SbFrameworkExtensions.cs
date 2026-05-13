using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using SB.Framework.Core;
using SB.Framework.Core.License;
using SB.Framework.Core.Middleware;
using SB.Framework.Core.Util;
using SB.Framework.WebApi.Middleware;
using Serilog;
using System.Text.Json.Serialization;

namespace SB.Framework.WebApi.Extensions;

/// <summary>
/// 派生项目启动壳一键装配。Program.cs 调用 AddSbFramework() + UseSbFramework() 完成所有基座基础设施。
/// </summary>
public static class SbFrameworkExtensions
{
    public static WebApplicationBuilder AddSbFramework(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        AppGlobal.AppSettings = configuration.Get<AppSettings>();

        ArchiveOldLogs();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();
        builder.Host.UseSerilog();
        builder.Logging.ClearProviders();
        builder.Host.UseSystemd();

        builder.Services.AddSetupServices();

        bool isInstalled = File.Exists(Path.Combine(
            builder.Environment.ContentRootPath, "installed.lock"));

#if !DISABLE_LICENSE
        builder.Services.AddLicenseService();
#else
        builder.Services.AddSingleton<ILicenseService>(new NullLicenseService());
#endif

        if (isInstalled)
        {
            builder.Services.AddFreeSql(configuration);
            builder.Services.AddJwtAuthentication(configuration);
            var projectAssembly = configuration.GetValue<string>("ProjectAssemblyName");
            builder.Services.AddBusinessServices(projectAssembly);
            builder.Services.AddCacheService();
            builder.Services.AddEmailServices();
            builder.Services.AddQdrantService(configuration);
            builder.Services.AddBackgroundTasks();
            builder.Services.AddLoginRateLimiter();
            builder.Services.AddDatabaseHealthCheck(configuration);
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

        builder.Services.AddControllersWithViews(options =>
        {
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            options.Conventions.Add(new AdminRoutePrefixConvention());
            var excludeControllers = configuration.GetSection("ExcludeControllers").Get<string[]>();
            if (excludeControllers?.Length > 0)
                options.Conventions.Add(new ControllerExclusionConvention(excludeControllers));
        }).AddJsonOptions(x => x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

        AppTime.Initialize(configuration["TimeZone"]);
        return builder;
    }

    public static WebApplication UseSbFramework(this WebApplication app)
    {
        bool isInstalled = File.Exists(Path.Combine(
            app.Environment.ContentRootPath, "installed.lock"));

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
        app.UseAuthorization();
        app.UseSwaggerDoc();

        if (isInstalled) app.MapHealthChecks("/health");
        app.MapControllers();
        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=DownloadApp}/{id?}");
        app.MapRazorPages();

        if (isInstalled) app.SeedDatabase();

        return app;
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
