using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
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

            // 当前日志（顶层 *.txt）
            var current = Directory.GetFiles(_logsPath, "*.txt", SearchOption.TopDirectoryOnly)
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
                    files = Directory.GetFiles(d.FullName, "*.txt")
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
}
