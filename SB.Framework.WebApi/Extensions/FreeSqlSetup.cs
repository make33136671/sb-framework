using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using SB.Framework.Core.Middleware;
using SB.Framework.Models;
using SB.Framework.Services.Configuration;
using Serilog;
using System.Diagnostics;
using System.Data;
using System.Reflection;

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

            // PostgreSQL boolean 兼容修复
            // FreeSql 全局过滤器(如 IsDeleted==false)和 InsertValueSql="0" 生成 integer 类型的参数/字面量,
            // PostgreSQL 的 boolean 列要求 true/false, 此 AOP 拦截 SQL 并自动修正
            if (isPostgreSql)
            {
                // 延迟发现所有 bool 类型列名: 扫描已加载程序集中带 [Table] 的类型,
                // 收集所有 bool/bool? 属性的 [Column(Name)] 值。新增模型自动覆盖,无需维护列名列表。
                var boolColumnNames = new Lazy<HashSet<string>>(() =>
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type[] types;
                        try { types = asm.GetTypes(); }
                        catch { continue; }
                        foreach (var type in types)
                        {
                            // 检测 FreeSql [Table] 特性
                            var tableAttr = type.GetCustomAttributes(false)
                                .FirstOrDefault(a => a.GetType().Name == "TableAttribute");
                            if (tableAttr == null) continue;

                            foreach (var prop in type.GetProperties())
                            {
                                var pt = prop.PropertyType;
                                var isBool = pt == typeof(bool) ||
                                    (Nullable.GetUnderlyingType(pt) == typeof(bool));
                                if (!isBool) continue;

                                var colAttr = prop.GetCustomAttributes(false)
                                    .FirstOrDefault(a => a.GetType().Name == "ColumnAttribute");
                                if (colAttr == null) continue;

                                var name = colAttr.GetType().GetProperty("Name")?.GetValue(colAttr) as string;
                                if (!string.IsNullOrEmpty(name))
                                    set.Add(name);
                            }
                        }
                    }
                    Log.Debug("PostgreSQL boolean 兼容修复: 发现 {Count} 个 boolean 列: {Cols}",
                        set.Count, string.Join(", ", set));
                    return set;
                });

                fsql.Aop.CommandBefore += (s, e) =>
                {
                    var cmd = e.Command;
                    var text = cmd.CommandText;
                    if (string.IsNullOrEmpty(text)) return;

                    var cols = boolColumnNames.Value;
                    if (cols.Count == 0) return;

                    bool textModified = false;

                    // 修复 1: SQL 文本中的字面量 0/1 → false/true
                    // 覆盖 WHERE "BoolCol" = 0, SET "BoolCol" = 1, 以及 INSERT VALUES 中的字面量
                    foreach (var col in cols)
                    {
                        if (!text.Contains($"\"{col}\"")) continue;

                        var replacements = new[]
                        {
                            ($"\"{col}\" = 0", $"\"{col}\" = false"),
                            ($"\"{col}\"=0", $"\"{col}\"=false"),
                            ($"\"{col}\" = 1", $"\"{col}\" = true"),
                            ($"\"{col}\"=1", $"\"{col}\"=true"),
                            ($"\"{col}\" = '0'", $"\"{col}\" = false"),
                            ($"\"{col}\" = '1'", $"\"{col}\" = true"),
                            ($"\"{col}\" = 'f'", $"\"{col}\" = false"),
                            ($"\"{col}\" = 't'", $"\"{col}\" = true"),
                        };
                        foreach (var (old, rep) in replacements)
                        {
                            if (text.Contains(old))
                            {
                                text = text.Replace(old, rep);
                                textModified = true;
                            }
                        }
                    }

                    // 修复 2: 参数类型 int → boolean
                    // FreeSql 全局过滤器生成 "BoolCol" = @p 参数, 值为 int 0/1, 改为 DbType.Boolean
                    foreach (System.Data.Common.DbParameter p in cmd.Parameters)
                    {
                        var name = p.ParameterName;
                        if (string.IsNullOrEmpty(name)) continue;

                        foreach (var col in cols)
                        {
                            if (text.Contains($"\"{col}\" = {name}") ||
                                text.Contains($"\"{col}\"={name}"))
                            {
                                if (p.Value is int intVal)
                                {
                                    p.DbType = DbType.Boolean;
                                    p.Value = intVal != 0;
                                }
                                else if (p.Value is long longVal)
                                {
                                    p.DbType = DbType.Boolean;
                                    p.Value = longVal != 0;
                                }
                                else if (p.Value is bool)
                                {
                                    p.DbType = DbType.Boolean;
                                }
                                break;
                            }
                        }
                    }

                    // 修复 3: MySQL LAST_INSERT_ID() → PostgreSQL lastval()
                    // FreeSql 的 BaseRepository.Insert() 在 INSERT 后调用 SELECT LAST_INSERT_ID() 获取自增ID,
                    // 这是 MySQL 函数, PostgreSQL 中不存在。lastval() 返回当前 session 最近一个序列的值。
                    if (text.Contains("LAST_INSERT_ID()", StringComparison.OrdinalIgnoreCase))
                    {
                        text = text.Replace("LAST_INSERT_ID()", "lastval()", StringComparison.OrdinalIgnoreCase);
                        textModified = true;
                    }

                    // 修复 4: DateTime Kind=UTC → Kind=Unspecified
                    // Npgsql 不允许将 UTC DateTime 写入 timestamp (without time zone) 列,
                    // 将所有 UTC DateTime 参数转为 Kind=Unspecified（保持时间值不变）
                    foreach (System.Data.Common.DbParameter p in cmd.Parameters)
                    {
                        if (p.Value is DateTime dt && dt.Kind == DateTimeKind.Utc)
                        {
                            p.Value = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
                        }
                    }

                    if (textModified) cmd.CommandText = text;
                };
            }

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
