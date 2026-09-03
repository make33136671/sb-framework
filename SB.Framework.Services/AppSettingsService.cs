using FreeSql;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SB.Framework.Services
{
    /// <summary>
    /// appsettings.json 管理服务。
    /// 支持读取 / 写入 / 上传整文件 / Diff / 历史版本（.bak）/ 回滚。
    /// 保存写文件后由 Controller 决定是否触发 AppShutdownHelper.GracefulStop（systemd 拉起加载新配置）。
    /// </summary>
    [AutoDependencyInjection]
    public class AppSettingsService : IModuleInitializer
    {
        // === IModuleInitializer 实现（菜单注册）===
        public string ModuleName => "配置文件管理";
        public int InitOrder => 1000;
        public Type[] GetEntityTypes() => Array.Empty<Type>();
        public List<SystemSettingDefinition> GetSystemSettings() => new();
        // 配置文件功能已统一从「运维工具」页的 Tab 进入，不再注册独立菜单。
        // 注意:种子是 upsert-only,删此声明不会自动清掉已入库的旧菜单,
        // 需到「菜单管理」手动删除 /system/devtools/app-settings 这一项。
        public List<MenuDefinition> GetMenus() => new();
        public void SeedData(IFreeSql fsql) { }
        // === IModuleInitializer 实现结束 ===


        private readonly IHostEnvironment _env;
        private readonly ILogger<AppSettingsService> _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        // 保留最近 N 个 .bak（避免无限堆积）
        private const int KEEP_HISTORY = 30;

        public AppSettingsService(IHostEnvironment env, ILogger<AppSettingsService> logger)
        {
            _env = env;
            _logger = logger;
        }

        /// <summary>主配置文件绝对路径</summary>
        public string AppSettingsPath => Path.Combine(_env.ContentRootPath, "appsettings.json");

        /// <summary>历史版本目录</summary>
        public string HistoryDir => Path.Combine(_env.ContentRootPath, "appsettings-history");

        /// <summary>读当前 appsettings.json 原文 + 解析后的 JsonNode。</summary>
        public Result Get()
        {
            if (!File.Exists(AppSettingsPath))
                return Result.Fail($"配置文件不存在: {AppSettingsPath}");

            try
            {
                var text = File.ReadAllText(AppSettingsPath);
                JsonNode? parsed = null;
                try { parsed = JsonNode.Parse(text); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "appsettings.json 当前内容解析失败");
                }

                var info = new FileInfo(AppSettingsPath);
                return Result.Success(new
                {
                    path = AppSettingsPath,
                    rawText = text,
                    parsed,
                    size = info.Length,
                    lastModified = info.LastWriteTime,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取 appsettings.json 失败");
                return Result.Fail($"读取失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存新的 appsettings.json 内容。
        /// 流程：① 校验 JSON 格式 ② 当前文件存 .bak ③ 写入新内容 ④ 清理超量 .bak。
        /// </summary>
        public Result Save(string newJsonText, string? operatorName = null)
        {
            if (string.IsNullOrWhiteSpace(newJsonText))
                return Result.Fail("配置内容不能为空");

            // 1. 校验 JSON 格式
            JsonNode? parsed;
            try { parsed = JsonNode.Parse(newJsonText); }
            catch (JsonException ex)
            {
                return Result.Fail($"JSON 格式错误: {ex.Message}");
            }
            if (parsed == null)
                return Result.Fail("JSON 解析后为空");

            try
            {
                // 2. 备份当前文件到 .bak
                BackupCurrent(operatorName);

                // 3. 美化输出（重新格式化为 2 空格缩进），写入新内容
                var beautified = parsed.ToJsonString(_jsonOptions);
                File.WriteAllText(AppSettingsPath, beautified, Encoding.UTF8);

                _logger.LogWarning("appsettings.json 已保存（操作人 {Op}, 大小 {Size} bytes）",
                    operatorName ?? "unknown", beautified.Length);

                // 4. 清理超量历史
                TrimHistory();

                return Result.Success(new
                {
                    path = AppSettingsPath,
                    size = beautified.Length,
                    savedAt = SB.Framework.Core.Util.AppTime.Now,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存 appsettings.json 失败");
                return Result.Fail($"保存失败: {ex.Message}");
            }
        }

        /// <summary>Diff 预览：返回当前文件原文 + 新内容（前端做 diff 展示）。</summary>
        public Result Diff(string newJsonText)
        {
            if (!File.Exists(AppSettingsPath))
                return Result.Fail("配置文件不存在");

            string oldText;
            try { oldText = File.ReadAllText(AppSettingsPath); }
            catch (Exception ex) { return Result.Fail($"读取失败: {ex.Message}"); }

            // 校验新内容格式（前端做 diff 需要保证两边都是合法 JSON）
            try
            {
                var node = JsonNode.Parse(newJsonText);
                if (node == null) return Result.Fail("新配置 JSON 解析后为空");
            }
            catch (JsonException ex)
            {
                return Result.Fail($"新配置 JSON 格式错误: {ex.Message}");
            }

            return Result.Success(new
            {
                oldText,
                newText = newJsonText,
            });
        }

        /// <summary>历史 .bak 列表（时间倒序）</summary>
        public Result History()
        {
            if (!Directory.Exists(HistoryDir))
                return Result.Success(Array.Empty<object>());

            var files = Directory.GetFiles(HistoryDir, "appsettings.*.bak.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => new
                {
                    fileName = f.Name,
                    size = f.Length,
                    backupAt = f.LastWriteTime,
                })
                .ToList();

            return Result.Success(files);
        }

        /// <summary>查看指定 .bak 内容（rollback 前预览）</summary>
        public Result HistoryContent(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || fileName.Contains(".."))
                return Result.Fail("非法文件名");

            var path = Path.Combine(HistoryDir, fileName);
            if (!File.Exists(path))
                return Result.Fail("历史文件不存在");

            return Result.Success(new { fileName, rawText = File.ReadAllText(path) });
        }

        /// <summary>
        /// 回滚到指定 .bak。
        /// 当前 appsettings.json 先存为新 .bak，然后覆盖回 .bak 内容。
        /// </summary>
        public Result Rollback(string fileName, string? operatorName = null)
        {
            if (string.IsNullOrEmpty(fileName) || fileName.Contains(".."))
                return Result.Fail("非法文件名");

            var bakPath = Path.Combine(HistoryDir, fileName);
            if (!File.Exists(bakPath))
                return Result.Fail("历史文件不存在");

            string bakText;
            try { bakText = File.ReadAllText(bakPath); }
            catch (Exception ex) { return Result.Fail($"读取 .bak 失败: {ex.Message}"); }

            try { JsonNode.Parse(bakText); }
            catch (JsonException ex) { return Result.Fail($".bak JSON 格式错误（不能回滚）: {ex.Message}"); }

            return Save(bakText, operatorName == null ? $"rollback:{fileName}" : $"{operatorName}（回滚 {fileName}）");
        }

        /// <summary>当前文件存 .bak（保存 / 回滚前都调用）</summary>
        private void BackupCurrent(string? operatorName)
        {
            if (!File.Exists(AppSettingsPath)) return;

            Directory.CreateDirectory(HistoryDir);
            var stamp = SB.Framework.Core.Util.AppTime.Now.ToString("yyyyMMdd_HHmmss");
            var dst = Path.Combine(HistoryDir, $"appsettings.{stamp}.bak.json");
            try
            {
                File.Copy(AppSettingsPath, dst, overwrite: false);
                _logger.LogInformation("appsettings.json 已备份到 {Path}（操作人 {Op}）", dst, operatorName ?? "unknown");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "备份当前 appsettings.json 失败（不影响保存继续）");
            }
        }

        /// <summary>保留最近 KEEP_HISTORY 个 .bak，删除更老的。</summary>
        private void TrimHistory()
        {
            if (!Directory.Exists(HistoryDir)) return;

            var files = Directory.GetFiles(HistoryDir, "appsettings.*.bak.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .Skip(KEEP_HISTORY)
                .ToList();

            foreach (var f in files)
            {
                try { f.Delete(); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "清理旧 .bak 失败: {Path}", f.FullName);
                }
            }
        }
    }
}
