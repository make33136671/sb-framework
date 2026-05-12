using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SB.Framework.Core;

namespace SB.Framework.Services.BackupTools;

/// <summary>
/// 按 DbConfig.DbType 选择具体的 IBackupTool 实现。
/// DatabaseBackupService 通过 ServiceProvider 拿到当前 DB 对应的 tool,无需 framework setup 显式注册多实现 + 路由。
/// </summary>
public static class BackupToolFactory
{
    /// <summary>从 AppSettings 当前 DbType 创建工具</summary>
    public static IBackupTool Create(IServiceProvider sp)
    {
        var dbType = AppGlobal.AppSettings?.DbConfig?.DbType ?? "MySql";
        return Create(dbType, sp);
    }

    public static IBackupTool Create(string dbType, IServiceProvider sp)
    {
        if (string.Equals(dbType, "PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            return new PostgreSqlBackupTool(sp.GetService<ILogger<PostgreSqlBackupTool>>());
        }
        return new MySqlBackupTool(sp.GetService<ILogger<MySqlBackupTool>>());
    }
}
