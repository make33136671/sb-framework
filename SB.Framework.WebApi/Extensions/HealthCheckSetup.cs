namespace SB.Framework.WebApi.Extensions
{
    public static class HealthCheckSetup
    {
        /// <summary>
        /// 注册健康检查（根据 DbType 选择 MySQL 或 PostgreSQL）
        /// </summary>
        public static IServiceCollection AddDatabaseHealthCheck(this IServiceCollection services, IConfiguration configuration)
        {
            IConfigurationSection dbConfig = configuration.GetSection("DbConfig");
            string dbType = dbConfig["DbType"] ?? "MySql";

            if (string.Equals(dbType, "PostgreSql", StringComparison.OrdinalIgnoreCase))
            {
                string connStr = $"Host={dbConfig["Server"]};Port={dbConfig["Port"]};Username={dbConfig["User"]};Password={dbConfig["Password"]};Database={dbConfig["Database"]};";
                services.AddHealthChecks()
                    .AddNpgSql(connStr, name: "postgresql");
            }
            else
            {
                string connStr = $"server={dbConfig["Server"]}; port={dbConfig["Port"]}; user id={dbConfig["User"]}; password={dbConfig["Password"]}; database={dbConfig["Database"]}; sslMode=PREFERRED;";
                services.AddHealthChecks()
                    .AddMySql(connStr, name: "mysql");
            }

            return services;
        }
    }
}
