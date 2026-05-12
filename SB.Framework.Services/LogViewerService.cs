using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;
using SB.Framework.Models;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class LogViewerService : IModuleInitializer
    {
        #region IModuleInitializer

        public string ModuleName => "运行日志模块";
        public int InitOrder => 21;

        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition
            {
                ParentPath = "/system/devtools",
                Name = "LogViewer", Path = "/system/devtools/log-viewer", Component = "/log-viewer/List",
                Title = "运行日志", Icon = "ion:document-text-outline", OrderNo = 5,
            }
        };

        #endregion

        // 日志文件后缀:历史上是 .txt,改造为 NDJSON 后是 .ndjson;两者都纳入扫描以兼容归档
        private static readonly string[] LogFilePatterns = { "*.ndjson", "*.txt" };
        private static readonly HashSet<string> AllowedDiagLevels =
            new(StringComparer.OrdinalIgnoreCase) { "info", "warning", "error" };

        private readonly string _logsPath;
        private readonly ILogger<LogViewerService> _logger;

        public LogViewerService(IHostEnvironment env, ILogger<LogViewerService> logger)
        {
            _logsPath = Path.Combine(env.ContentRootPath, "logs");
            _logger = logger;
        }

        /// <summary>
        /// 获取日志文件列表（含当前日志和归档子目录）
        /// </summary>
        public Result GetFiles()
        {
            if (!Directory.Exists(_logsPath))
                return Result.Success(new { current = Array.Empty<object>(), archives = Array.Empty<object>() });

            // 当前日志(顶层 *.ndjson 优先,兼容期间也列 *.txt)
            var current = LogFilePatterns
                .SelectMany(p => Directory.GetFiles(_logsPath, p, SearchOption.TopDirectoryOnly))
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => new
                {
                    name = f.Name,
                    size = f.Length,
                    lastModified = f.LastWriteTime,
                })
                .ToList();

            // 归档子目录（每次启动都会创建一个 yyyyMMdd_HHmmss 命名的目录）
            var archives = Directory.GetDirectories(_logsPath)
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.Name)
                .Select(d => new
                {
                    folder = d.Name,
                    lastModified = d.LastWriteTime,
                    files = LogFilePatterns
                        .SelectMany(p => Directory.GetFiles(d.FullName, p))
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.LastWriteTime)
                        .Select(f => new
                        {
                            name = f.Name,
                            size = f.Length,
                            lastModified = f.LastWriteTime,
                        })
                        .ToList(),
                })
                .ToList();

            return Result.Success(new { current, archives });
        }

        /// <summary>
        /// AI 诊断查询:按日期 + 级别 + tail + 关键字过滤,合并多文件 NDJSON 返回。
        /// 日志文件按约定命名 {level}-{yyyyMMdd}.ndjson(level ∈ info / warning / error)。
        /// </summary>
        /// <param name="date">日期,yyyyMMdd 格式,逗号分隔多个;空 = 今天</param>
        /// <param name="level">级别 info / warning / error / all,逗号分隔多个;空 = "warning,error"</param>
        /// <param name="tail">每个文件取尾部 N 行,默认 1000,0 表示不截断</param>
        /// <param name="contains">关键字过滤(子串匹配,大小写不敏感)</param>
        public DiagQueryResult QueryDiag(string date = null, string level = null, int tail = 1000, string contains = null)
        {
            var dates = ParseDates(date);
            var levels = ParseLevels(level);

            var files = new List<string>();
            var collected = new List<string>();
            var totalLines = 0;

            if (Directory.Exists(_logsPath))
            {
                foreach (var d in dates)
                {
                    foreach (var lv in levels)
                    {
                        var fileName = $"{lv}-{d:yyyyMMdd}.ndjson";
                        var path = Path.Combine(_logsPath, fileName);
                        if (!File.Exists(path)) continue;

                        files.Add(fileName);
                        var lines = File.ReadAllLines(path);
                        totalLines += lines.Length;

                        var taken = (tail > 0 && tail < lines.Length)
                            ? lines.Skip(lines.Length - tail).ToArray()
                            : lines;

                        collected.AddRange(taken);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(contains))
            {
                collected = collected
                    .Where(l => l.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            return new DiagQueryResult
            {
                Dates = dates.Select(d => d.ToString("yyyyMMdd")).ToList(),
                Levels = levels,
                Tail = tail,
                Contains = contains,
                Files = files,
                TotalLines = totalLines,
                ReturnedLines = collected.Count,
                Ndjson = string.Join("\n", collected),
            };
        }

        private static List<DateTime> ParseDates(string date)
        {
            if (string.IsNullOrWhiteSpace(date))
                return new List<DateTime> { AppTime.Today };

            var result = new List<DateTime>();
            foreach (var part in date.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (DateTime.TryParseExact(part, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    result.Add(d);
            }
            return result.Count > 0 ? result : new List<DateTime> { AppTime.Today };
        }

        private static List<string> ParseLevels(string level)
        {
            if (string.IsNullOrWhiteSpace(level))
                return new List<string> { "warning", "error" };

            if (level.Equals("all", StringComparison.OrdinalIgnoreCase))
                return new List<string> { "info", "warning", "error" };

            var result = level
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => AllowedDiagLevels.Contains(l))
                .Select(l => l.ToLowerInvariant())
                .Distinct()
                .ToList();

            return result.Count > 0 ? result : new List<string> { "warning", "error" };
        }

        /// <summary>
        /// 读取日志文件内容（支持尾部读取，可指定归档子目录）
        /// </summary>
        public Result ReadFile(string fileName, int tail = 500, string archive = null)
        {
            var filePath = ResolveFilePath(fileName, archive);
            if (filePath == null)
                return Result.Fail("文件不存在或路径非法");

            var allLines = System.IO.File.ReadAllLines(filePath);
            var totalLines = allLines.Length;

            // 取最后 tail 行
            var lines = tail > 0 && tail < totalLines
                ? allLines.Skip(totalLines - tail).ToArray()
                : allLines;

            return Result.Success(new
            {
                fileName,
                archive,
                totalLines,
                returnedLines = lines.Length,
                content = string.Join("\n", lines),
            });
        }

        /// <summary>
        /// 获取日志文件完整路径（用于下载，可指定归档子目录）
        /// </summary>
        public string GetFilePath(string fileName, string archive = null)
        {
            return ResolveFilePath(fileName, archive);
        }

        /// <summary>
        /// 解析文件路径，统一做路径穿越校验
        /// </summary>
        private string ResolveFilePath(string fileName, string archive)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;
            if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
                return null;

            string filePath;
            if (string.IsNullOrWhiteSpace(archive))
            {
                filePath = Path.Combine(_logsPath, fileName);
            }
            else
            {
                if (archive.Contains("..") || archive.Contains('/') || archive.Contains('\\'))
                    return null;

                var archiveDir = Path.Combine(_logsPath, archive);
                if (!Directory.Exists(archiveDir))
                    return null;

                filePath = Path.Combine(archiveDir, fileName);
            }

            return System.IO.File.Exists(filePath) ? filePath : null;
        }
    }

    /// <summary>
    /// LogViewerService.QueryDiag 返回结构。Ndjson 字段是合并后的多行 NDJSON 文本(CLEF 格式),
    /// 其余字段用于响应头摘要或上层校验。
    /// </summary>
    public class DiagQueryResult
    {
        public List<string> Dates { get; set; } = new();
        public List<string> Levels { get; set; } = new();
        public int Tail { get; set; }
        public string Contains { get; set; }
        public List<string> Files { get; set; } = new();
        public int TotalLines { get; set; }
        public int ReturnedLines { get; set; }
        public string Ndjson { get; set; } = "";
    }
}
