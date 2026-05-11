using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using SB.Framework.Core.Middleware;
using SB.Framework.Models;
using SB.Framework.Services.Configuration;
using Serilog;
using System.Diagnostics;

namespace SB.Framework.WebApi.Extensions
{
    public static class FreeSqlSetup
    {
        public static IServiceCollection AddFreeSql(this IServiceCollection services, IConfiguration configuration)
        {
            IConfigurationSection dbConfig = configuration.GetSection("DbConfig");
            string dbType = dbConfig.GetValue("DbType", "MySql");
            string server = dbConfig.GetValue("server", "127.0.0.1");
            string port = dbConfig.GetValue("port", "3306");
            string database = dbConfig.GetValue("database", "sb");
            string user = dbConfig.GetValue("user", "sb");
            string password = dbConfig.GetValue("password", "sb");
            bool autoSyncStructure = dbConfig.GetValue<bool>("AutoSyncStructure", false);
            bool monitorSqlCommandForLog = dbConfig.GetValue<bool>("MonitorSqlCommandForLog", false);
            int slowQueryThresholdMs = dbConfig.GetValue<int>("SlowQueryThresholdMs", 200);

            bool isPostgreSql = string.Equals(dbType, "PostgreSql", StringComparison.OrdinalIgnoreCase);
            var dataType = isPostgreSql ? FreeSql.DataType.PostgreSQL : FreeSql.DataType.MySql;
            var connStr = isPostgreSql
                ? $"Host={server};Port={port};Username={user};Password={password};Database={database};"
                : $"server={server}; port={port}; user id={user}; password={password}; database={database}; sslMode=PREFERRED; ";

            IFreeSql fsql = new FreeSql.FreeSqlBuilder()
                               .UseConnectionString(dataType, connStr)
                               .UseAutoSyncStructure(autoSyncStructure)
                               .Build();

            // 性能监控: 慢查询日志 + 查询计数
            fsql.Aop.CommandBefore += (s, e) =>
            {
                e.States["Stopwatch"] = Stopwatch.StartNew();
            };

            fsql.Aop.CommandAfter += (s, e) =>
            {
                var sw = (Stopwatch)e.States["Stopwatch"];
                sw.Stop();
                var ms = sw.ElapsedMilliseconds;

                // 查询计数 (QueryCountMiddleware 统计每个请求的查询次数)
                QueryCountMiddleware.IncrementQueryCount();

                if (ms > slowQueryThresholdMs)
                {
                    Log.Warning("慢查询 ({Ms}ms, 阈值: {Threshold}ms): {Sql}", ms, slowQueryThresholdMs, e.Command.CommandText);
                }
                else if (monitorSqlCommandForLog)
                {
                    Log.Debug("查询 ({Ms}ms): {Sql}", ms, e.Command.CommandText);
                }
            };

            // 全局软删除过滤：自动为所有 ISoftDelete 实体过滤已删除记录
            fsql.GlobalFilter.Apply<Admin>("IsDeleted", a => a.IsDeleted == false);
            fsql.GlobalFilter.Apply<Menu>("IsDeleted", a => a.IsDeleted == false);
            fsql.GlobalFilter.Apply<AdminRole>("IsDeleted", a => a.IsDeleted == false);
            fsql.GlobalFilter.Apply<Member>("IsDeleted", a => a.IsDeleted == false);
            fsql.GlobalFilter.Apply<LicenseClient>("IsDeleted", a => a.IsDeleted == false);
            Log.Debug("已应用全局软删除过滤，IsDeleted=true 的数据将被自动排除");

            fsql.UseJsonMap();

            // 同步授权相关新表结构（无论 AutoSyncStructure 是否开启）
            try
            {
                fsql.CodeFirst.SyncStructure(
                    typeof(LicenseClient),
                    typeof(LicenseRecord),
                    typeof(LicenseRequest),
                    typeof(LicenseVerifyLog),
                    typeof(ChunkUpload),
                    typeof(ChunkUploadPart)
                );
            }
            catch (Exception ex)
            {
                Log.Warning("授权表同步失败（首次访问时会自动重试）: {Message}", ex.Message);
            }

            services.AddSingleton<IFreeSql>(fsql);

            return services;
        }

        /// <summary>
        /// 应用所有 IFreeSqlConfigurator（基座 + 派生项目）。
        /// 必须在 services.BuildServiceProvider() 之后调用。
        /// </summary>
        public static void ApplyFreeSqlConfigurators(this IServiceProvider sp)
        {
            var fsql = sp.GetRequiredService<IFreeSql>();
            var configurators = sp.GetServices<IFreeSqlConfigurator>()
                .OrderBy(c => c.Order)
                .ToList();

            int applied = 0;
            foreach (var c in configurators)
            {
                try
                {
                    c.Configure(fsql);
                    applied++;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "IFreeSqlConfigurator 失败: {Type}", c.GetType().FullName);
                }
            }
            Log.Information("IFreeSqlConfigurator applied {Count} configurators", applied);
        }
    }
}
