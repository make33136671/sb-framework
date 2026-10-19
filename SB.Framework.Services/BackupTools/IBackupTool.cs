using FreeSql;
using SB.Framework.Core;

namespace SB.Framework.Services.BackupTools;

/// <summary>
/// 数据库备份工具抽象。当前实现:MySQL (mysqldump/mysql) + PostgreSQL (pg_dump/psql)。
/// DatabaseBackupService 通过 IBackupTool 屏蔽 DB 命令行差异。
/// 按 DbConfig.DbType 选择实现,见 <see cref="BackupToolFactory"/>。
/// </summary>
public interface IBackupTool
{
    /// <summary>
    /// 备份文件方言标识:"mysql" / "postgresql"。
    /// 导入「外来备份文件」时用来判断脚本与当前数据库是否兼容 —— mysqldump 与 pg_dump 的 SQL 互不兼容,
    /// 跨库直接导入会产生大量 SQL 错误(甚至半截恢复),必须在导入前拦截。
    /// </summary>
    string Dialect { get; }

    /// <summary>导出工具名(显示用,如 "mysqldump" / "pg_dump")</summary>
    string DumpToolName { get; }

    /// <summary>恢复工具名(显示用,如 "mysql" / "psql")</summary>
    string RestoreToolName { get; }

    /// <summary>检查 dump 工具可用 + 返回版本字符串</summary>
    Task<(bool Available, string Version)> CheckDumpToolAsync(CancellationToken ct = default);

    /// <summary>检查 restore 工具可用</summary>
    Task<bool> CheckRestoreToolAsync(CancellationToken ct = default);

    /// <summary>查询数据库元信息(版本/字符集/表数/大小/行数等);异常时各字段返回默认值,不抛出</summary>
    DatabaseMetaInfo GetMetaInfo(IFreeSql fsql, DbConfig db);

    /// <summary>导出整库到 sql 文件;返回 (是否成功, stderr 输出)</summary>
    Task<(bool Ok, string Stderr)> DumpAsync(DbConfig db, string outputSqlPath, CancellationToken ct = default);

    /// <summary>从 sql 文件恢复整库;返回 (是否成功, stderr 输出)</summary>
    Task<(bool Ok, string Stderr)> RestoreAsync(DbConfig db, string inputSqlPath, CancellationToken ct = default);
}

/// <summary>数据库元信息(供 DatabaseBackupService.CheckBackupTool 返回前端展示)</summary>
public record DatabaseMetaInfo(
    string ServerVersion,
    string CharSet,
    string Collation,
    long TableCount,
    long TotalSize,
    long TotalRows
)
{
    public static DatabaseMetaInfo Empty => new("", "", "", 0, 0, 0);
}
