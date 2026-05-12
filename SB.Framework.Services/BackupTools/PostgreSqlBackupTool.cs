using System.Diagnostics;
using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core;

namespace SB.Framework.Services.BackupTools;

/// <summary>
/// PostgreSQL 备份/恢复实现。
/// 工具:pg_dump(导出)/ psql(导入)。密码走 PGPASSWORD 环境变量。
/// </summary>
public class PostgreSqlBackupTool : IBackupTool
{
    private readonly ILogger<PostgreSqlBackupTool>? _logger;

    public PostgreSqlBackupTool(ILogger<PostgreSqlBackupTool>? logger = null) => _logger = logger;

    public string DumpToolName => "pg_dump";
    public string RestoreToolName => "psql";

    public async Task<(bool Available, string Version)> CheckDumpToolAsync(CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = DumpToolName,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi)!;
            var version = (await proc.StandardOutput.ReadToEndAsync(ct)).Trim();
            await proc.WaitForExitAsync(ct);
            return (proc.ExitCode == 0, version);
        }
        catch
        {
            return (false, "");
        }
    }

    public async Task<bool> CheckRestoreToolAsync(CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = RestoreToolName,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi)!;
            await proc.WaitForExitAsync(ct);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public DatabaseMetaInfo GetMetaInfo(IFreeSql fsql, DbConfig db)
    {
        string serverVersion = "";
        string charSet = "";
        string collation = "";
        long tableCount = 0;
        long totalSize = 0;
        long totalRows = 0;

        try
        {
            // 注:FreeSql + Npgsql 用 @param 参数占位符
            serverVersion = (fsql.Ado.ExecuteScalar("SHOW server_version")?.ToString() ?? "").Trim();

            // 字符集 + collation:从 pg_database 拿
            using var dt = fsql.Ado.ExecuteDataTable(
                "SELECT pg_encoding_to_char(encoding) AS charset, datcollate AS collation " +
                "FROM pg_database WHERE datname = @dbName",
                new { dbName = db.Database });
            if (dt.Rows.Count > 0)
            {
                charSet = dt.Rows[0]["charset"]?.ToString() ?? "";
                collation = dt.Rows[0]["collation"]?.ToString() ?? "";
            }

            // 表数 + 总大小 + 行数估算(用 pg_class.reltuples;真实行数昂贵)
            using var stat = fsql.Ado.ExecuteDataTable(
                "SELECT COUNT(*) AS table_count, " +
                "COALESCE(SUM(pg_total_relation_size(c.oid)), 0) AS total_size, " +
                "COALESCE(SUM(c.reltuples)::bigint, 0) AS total_rows " +
                "FROM pg_class c " +
                "JOIN pg_namespace n ON n.oid = c.relnamespace " +
                "WHERE c.relkind = 'r' AND n.nspname NOT IN ('pg_catalog', 'information_schema')");
            if (stat.Rows.Count > 0)
            {
                tableCount = Convert.ToInt64(stat.Rows[0]["table_count"]);
                totalSize = Convert.ToInt64(stat.Rows[0]["total_size"]);
                totalRows = Convert.ToInt64(stat.Rows[0]["total_rows"]);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "PostgreSQL 元信息查询失败");
        }

        return new DatabaseMetaInfo(serverVersion, charSet, collation, tableCount, totalSize, totalRows);
    }

    public async Task<(bool Ok, string Stderr)> DumpAsync(DbConfig db, string outputSqlPath, CancellationToken ct = default)
    {
        // --no-owner / --no-privileges 让导出文件不带 user/grant 信息,便于不同环境恢复
        // --format=plain 输出 sql 文本(跟 mysqldump 一致,zip 后体积差不多)
        // --clean --if-exists 让生成的 sql 先 DROP TABLE IF EXISTS 再 CREATE — 否则 restore
        // 到已存在 db 时第一条 CREATE TABLE 撞已存在表,psql 立即退出导致 broken pipe
        var args = $"--host={db.Server} --port={db.Port} --username={db.User} " +
                   $"--format=plain --no-owner --no-privileges --clean --if-exists " +
                   $"--file=\"{outputSqlPath}\" {db.Database}";

        var psi = new ProcessStartInfo
        {
            FileName = DumpToolName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["PGPASSWORD"] = db.Password;

        using var proc = Process.Start(psi)!;
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        var stderr = await stderrTask;
        return (proc.ExitCode == 0, stderr);
    }

    public async Task<(bool Ok, string Stderr)> RestoreAsync(DbConfig db, string inputSqlPath, CancellationToken ct = default)
    {
        // --single-transaction 让整个 sql 在事务里跑,失败回滚(避免半路 fail 留下不一致 schema)
        // --quiet 减少 NOTICE 输出
        // -v ON_ERROR_STOP=1 让任意 SQL 错误立即终止
        var args = $"--host={db.Server} --port={db.Port} --username={db.User} " +
                   $"--dbname={db.Database} --quiet --single-transaction " +
                   $"-v ON_ERROR_STOP=1";

        var psi = new ProcessStartInfo
        {
            FileName = RestoreToolName,
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["PGPASSWORD"] = db.Password;

        using var proc = Process.Start(psi)!;
        try
        {
            using var fs = File.OpenRead(inputSqlPath);
            await fs.CopyToAsync(proc.StandardInput.BaseStream, ct);
        }
        catch (IOException)
        {
            // ON_ERROR_STOP=1 时 psql 撞到第一条失败 SQL 会立即退出 → stdin pipe broken。
            // 不当作 fatal,正常 wait + 读 stderr 拿真实错误(SQL 错误内容)。
        }
        try { proc.StandardInput.Close(); } catch { /* already closed */ }
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        var stderr = await stderrTask;
        return (proc.ExitCode == 0, stderr);
    }
}
