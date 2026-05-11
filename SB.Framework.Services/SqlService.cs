using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using System.Data;
using System.Text;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class SqlService : IModuleInitializer
    {
        #region IModuleInitializer

        public string ModuleName => "SQL执行模块";
        public int InitOrder => 20;

        // 注：SQL 执行作为 Tab 整合进 OpsTools 菜单（/system/devtools/ops），
        // 不再单独注册菜单条目。旧路径 /system/devtools/sql 由前端兼容跳转。

        #endregion

        private readonly IFreeSql _fsql;
        private readonly ILogger<SqlService> _logger;

        public SqlService(IFreeSql fsql, ILogger<SqlService> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        /// <summary>
        /// 执行 SQL 语句
        /// </summary>
        public Result Execute(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return Result.Fail("SQL 不能为空");

            var trimmed = sql.TrimStart();
            try
            {
                if (IsSelectQuery(trimmed))
                {
                    var dt = _fsql.Ado.ExecuteDataTable(sql);
                    var columns = new List<string>();
                    foreach (DataColumn col in dt.Columns)
                        columns.Add(col.ColumnName);

                    var rows = new List<Dictionary<string, object>>();
                    foreach (DataRow row in dt.Rows)
                    {
                        var dict = new Dictionary<string, object>();
                        foreach (DataColumn col in dt.Columns)
                        {
                            var val = row[col];
                            dict[col.ColumnName] = val == DBNull.Value ? null : val;
                        }
                        rows.Add(dict);
                    }

                    _logger.LogInformation("SQL 执行: 查询返回 {Count} 行", rows.Count);
                    return Result.Success(new
                    {
                        type = "query",
                        columns,
                        rows,
                        rowCount = rows.Count
                    });
                }
                else
                {
                    var affected = _fsql.Ado.ExecuteNonQuery(sql);
                    _logger.LogInformation("SQL 执行: 影响 {Count} 行", affected);
                    return Result.Success(new
                    {
                        type = "execute",
                        affectedRows = affected
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SQL 执行失败");
                return Result.Fail($"SQL 执行失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行查询并返回 CSV 字节
        /// </summary>
        public (byte[] data, string fileName) ExportCsv(string sql)
        {
            var dt = _fsql.Ado.ExecuteDataTable(sql);
            var sb = new StringBuilder();

            // BOM for Excel
            // Header
            for (int i = 0; i < dt.Columns.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(CsvEscape(dt.Columns[i].ColumnName));
            }
            sb.AppendLine();

            // Rows
            foreach (DataRow row in dt.Rows)
            {
                for (int i = 0; i < dt.Columns.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var val = row[i];
                    sb.Append(CsvEscape(val == DBNull.Value ? "" : val?.ToString() ?? ""));
                }
                sb.AppendLine();
            }

            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var csv = Encoding.UTF8.GetBytes(sb.ToString());
            var result = new byte[bom.Length + csv.Length];
            bom.CopyTo(result, 0);
            csv.CopyTo(result, bom.Length);

            var fileName = $"query_{AppTime.Now:yyyyMMdd_HHmmss}.csv";
            return (result, fileName);
        }

        private static string CsvEscape(string value)
        {
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static bool IsSelectQuery(string sql)
        {
            var upper = sql.TrimStart().ToUpperInvariant();
            return upper.StartsWith("SELECT") || upper.StartsWith("SHOW") ||
                   upper.StartsWith("DESC") || upper.StartsWith("EXPLAIN");
        }
    }
}
