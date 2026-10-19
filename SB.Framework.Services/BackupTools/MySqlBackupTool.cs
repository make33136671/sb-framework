using System.Diagnostics;
using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core;

namespace SB.Framework.Services.BackupTools;

/// <summary>
/// MySQL 备份/恢复实现。
/// 工具:mysqldump(导出)/ mysql(导入)。密码走 MYSQL_PWD 环境变量(避免命令行暴露)。
/// </summary>
public class MySqlBackupTool : IBackupTool
{
    private readonly ILogger<MySqlBackupTool>? _logger;

    public MySqlBackupTool(ILogger<MySqlBackupTool>? logger = null) => _logger = logger;

    public string Dialect => "mysql";
    public string DumpToolName => "mysqldump";
    public string RestoreToolName => "mysql";

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
            serverVersion = (fsql.Ado.ExecuteScalar("SELECT VERSION()")?.ToString() ?? "").Trim();

            using var dt = fsql.Ado.ExecuteDataTable(
                "SELECT DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME " +
                "FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = ?dbName",
                new { dbName = db.Database });
            if (dt.Rows.Count > 0)
            {
                charSet = dt.Rows[0][0]?.ToString() ?? "";
                collation = dt.Rows[0][1]?.ToString() ?? "";
            }

            using var stat = fsql.Ado.ExecuteDataTable(
                "SELECT COUNT(*) AS table_count, " +
                "IFNULL(SUM(data_length + index_length), 0) AS total_size, " +
                "IFNULL(SUM(table_rows), 0) AS total_rows " +
                "FROM information_schema.TABLES WHERE TABLE_SCHEMA = ?dbName",
                new { dbName = db.Database });
            if (stat.Rows.Count > 0)
            {
                tableCount = Convert.ToInt64(stat.Rows[0]["table_count"]);
                totalSize = Convert.ToInt64(stat.Rows[0]["total_size"]);
                totalRows = Convert.ToInt64(stat.Rows[0]["total_rows"]);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "MySQL 元信息查询失败");
        }

        return new DatabaseMetaInfo(serverVersion, charSet, collation, tableCount, totalSize, totalRows);
    }

    public async Task<(bool Ok, string Stderr)> DumpAsync(DbConfig db, string outputSqlPath, CancellationToken ct = default)
    {
        var args = $"--host={db.Server} --port={db.Port} --user={db.User} " +
                   $"--single-transaction --routines --triggers --set-gtid-purged=OFF " +
                   $"--result-file=\"{outputSqlPath}\" {db.Database}";

        var psi = new ProcessStartInfo
        {
            FileName = DumpToolName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["MYSQL_PWD"] = db.Password;

        using var proc = Process.Start(psi)!;
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        var stderr = await stderrTask;
        return (proc.ExitCode == 0, stderr);
    }

    public async Task<(bool Ok, string Stderr)> RestoreAsync(DbConfig db, string inputSqlPath, CancellationToken ct = default)
    {
        var args = $"--host={db.Server} --port={db.Port} --user={db.User} {db.Database}";
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
        psi.Environment["MYSQL_PWD"] = db.Password;

        using var proc = Process.Start(psi)!;
        try
        {
            using var fs = File.OpenRead(inputSqlPath);
            await fs.CopyToAsync(proc.StandardInput.BaseStream, ct);
        }
        catch (IOException)
        {
            // mysql 客户端中途退出(权限/语法错)会让 stdin pipe 断。继续读 stderr 拿真错。
        }
        try { proc.StandardInput.Close(); } catch { /* already closed */ }
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        var stderr = await stderrTask;
        return (proc.ExitCode == 0, stderr);
    }
}
