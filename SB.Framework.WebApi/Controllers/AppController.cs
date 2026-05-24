using System.IO.Compression;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Core;
using SB.Framework.Core.Expand;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class AppController : Controller
{
    private static readonly DateTime _startTime = AppTime.Now;

    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<AppController> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly ICacheService _cacheService;
    private readonly DeployRecordService _deployRecordService;
    private readonly UploadRecordService _uploadRecordService;

    public AppController(
        IHostApplicationLifetime appLifetime,
        ILogger<AppController> logger,
        IWebHostEnvironment env,
        ICacheService cacheService,
        DeployRecordService deployRecordService,
        UploadRecordService uploadRecordService)
    {
        _appLifetime = appLifetime;
        _logger = logger;
        _env = env;
        _cacheService = cacheService;
        _deployRecordService = deployRecordService;
        _uploadRecordService = uploadRecordService;
    }

    /// <summary>
    /// 获取应用信息：版本、启动时间、运行时长等
    /// </summary>
    [HttpGet]
    public Result Info()
    {
        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? assembly?.GetName().Version?.ToString()
            ?? "unknown";

        // buildTime 优先用入口 dll 的文件 mtime(派生项目每次 publish/部署都更新),
        // fallback 到 BuildInfo.Timestamp(framework 包自身 build 时间) 兜底。
        // 解决"派生项目部署后版本号/时间不更新"的痛点 — BuildInfo.Timestamp 在 framework NuGet
        // 包里硬编码,派生项目 build 时不会重新生成,所以单看它永远是 framework 发包时间。
        string buildTime;
        try
        {
            var loc = assembly?.Location;
            buildTime = !string.IsNullOrEmpty(loc) && System.IO.File.Exists(loc)
                ? System.IO.File.GetLastWriteTime(loc).ToString("yyyy-MM-dd HH:mm:ss")
                : BuildInfo.Timestamp;
        }
        catch
        {
            buildTime = BuildInfo.Timestamp;
        }

        var proc = System.Diagnostics.Process.GetCurrentProcess();
        var workingSetMb = Math.Round(proc.WorkingSet64 / 1024.0 / 1024.0, 1);
        var privateMemoryMb = Math.Round(proc.PrivateMemorySize64 / 1024.0 / 1024.0, 1);
        var gcTotalMb = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 1);

        // 当前 ASP.NET Core 监听地址（appsettings.json "Urls" / 环境变量 / 命令行决定）
        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
            ?? Environment.GetEnvironmentVariable("URLS")
            ?? "";
        if (string.IsNullOrEmpty(urls))
        {
            try
            {
                var addressFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
                if (addressFeature != null)
                    urls = string.Join(",", addressFeature.Addresses);
            }
            catch { }
        }

        return Result.Success(new
        {
            version,
            environment = _env.EnvironmentName,
            startTime = _startTime.ToString("yyyy-MM-dd HH:mm:ss"),
            uptime = (AppTime.Now - _startTime).ToString(@"d\.hh\:mm\:ss"),
            machineName = Environment.MachineName,
            dotnetVersion = Environment.Version.ToString(),
            buildTime,
            timeZone = AppTime.Zone.Id,
            serverTime = AppTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            installPath = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
            // 进程资源
            processId = proc.Id,
            threadCount = proc.Threads.Count,
            workingSetMb,
            privateMemoryMb,
            gcTotalMb,
            processorCount = Environment.ProcessorCount,
            osVersion = Environment.OSVersion.VersionString,
            // 当前 listen 地址
            listeningUrls = urls,
        });
    }

    /// <summary>
    /// 重启应用，systemd 自动拉起新进程
    /// </summary>
    [HttpPost]
    public Result Restart()
    {
        var username = HttpContext.User.Username();
        _logger.LogWarning("Application restart triggered by {Username}", username);

        AppShutdownHelper.GracefulStop(_appLifetime);
        return Result.Success("程序即将重启");
    }

    /// <summary>
    /// 上传 API 程序包（zip），解压覆盖后自动重启。
    /// 内部走 Upload → Deploy 两步，上传文件持久保存；部署失败时文件仍在，可凭 UploadRecord.Id 重试。
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<Result> UploadApi(IFormFile file, [FromForm] bool keepConfig = true)
    {
        if (file == null || file.Length == 0)
            return Result.Fail("请选择文件");
        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return Result.Fail("仅支持 zip 文件");

        var username = HttpContext.User.Username() ?? "unknown";
        using var stream = file.OpenReadStream();
        var record = await _uploadRecordService.SaveUploadFromStream("api", "API程序", stream, file.FileName, file.Length, username);
        return await DeployByRecord(record, keepConfig: keepConfig, clearCache: false);
    }

    /// <summary>
    /// 原子替换 API zip 内容到 appDir。
    /// 直接 ZipFile.ExtractToDirectory(overwriteFiles=true) 走 File.Open(FileMode.Create) in-place 重写,
    /// 跟 .NET runtime mmap 加载有 race — 进程持有 dll 的 mmap 时,新字节进 page cache 但不能完整刷到 inode,
    /// systemd 拉起的新进程读到新旧字节混合 → assembly load 失败、版本不匹配。framework 跨版本(几十个 dll
    /// 同时换)100% 撞 race。
    ///
    /// 修法:解压到 appDir 同盘的 staging 子目录(零 mmap 交互),然后逐文件 File.Move(overwrite:true) 替换到
    /// appDir。File.Move 在 Linux 是 rename(2) atomic,**替换的是 inode**:
    /// - 运行中进程的 mmap 仍指向旧 inode(看到旧内容,无中断)
    /// - filename 现在指向新 inode
    /// - 进程退出 → mmap 释放 → 旧 inode 被 GC
    /// - systemd 拉起的新进程 open(filename) → 看到新 inode → mmap 新内容
    /// 零 race,跨多少个 dll 都安全。
    /// </summary>
    private static void ExtractApiAtomic(string zipPath, string appDir, bool keepConfig = true)
    {
        var staging = Path.Combine(appDir, $".upload-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, staging);

            foreach (var src in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(staging, src);
                // keepConfig=true 时跳过根目录的 appsettings.json / appsettings.*.json,
                // 防止误打包的配置文件覆盖线上配置(默认开启,生产部署兜底)。
                if (keepConfig && IsProtectedConfigFile(rel))
                    continue;
                var dest = Path.Combine(appDir, rel);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);
                System.IO.File.Move(src, dest, overwrite: true);
            }
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 判断是否为受保护的配置文件(只针对部署根目录的 appsettings.json / appsettings.*.json)。
    /// 子目录里同名文件不受保护 — 它们一般是 plugin 自带不会跟运行时配置冲突。
    /// </summary>
    private static bool IsProtectedConfigFile(string relPath)
    {
        // 只保护根目录的配置文件,子目录的不算
        var dirName = Path.GetDirectoryName(relPath);
        if (!string.IsNullOrEmpty(dirName)) return false;
        var fileName = Path.GetFileName(relPath);
        if (string.Equals(fileName, "appsettings.json", StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// 通过分片上传的文件路径部署 API 程序（filePath 由 ChunkUpload/Merge 返回）。
    /// 内部创建 UploadRecord 后走统一 DeployByRecord 逻辑。
    /// </summary>
    [HttpPost]
    public async Task<Result> DeployApiByPath(
        [FromForm] string filePath,
        [FromForm] string fileName,
        [FromForm] long fileSize,
        [FromForm] bool clearCache = false,
        [FromForm] bool keepConfig = true)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Result.Fail("文件路径不能为空");

        var fullPath = ResolveUploadedFile(filePath);
        if (!System.IO.File.Exists(fullPath))
            return Result.Fail("文件不存在");

        var username = HttpContext.User.Username() ?? "unknown";
        var displayName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(fullPath) : fileName;
        var actualSize = fileSize > 0 ? fileSize : new FileInfo(fullPath).Length;

        var record = await _uploadRecordService.SaveUpload("api", "API程序", fullPath, displayName, actualSize, username);
        return await DeployByRecord(record, keepConfig: keepConfig, clearCache: clearCache);
    }

    // ==================== 缓存管理 ====================

    /// <summary>
    /// 缓存类别定义
    /// </summary>
    private static readonly (string Prefix, string Name, string Description)[] CacheCategories =
    {
        ("SystemSetting:", "系统设置", "系统设置项缓存"),
        ("menu:", "菜单", "菜单列表和用户菜单缓存"),
        ("EmailRateLimit:", "邮件限流", "邮件发送频率限制计数器"),
    };

    /// <summary>
    /// 获取缓存信息：提供商、各类别状态
    /// </summary>
    [HttpGet]
    public Result CacheInfo()
    {
        var categories = CacheCategories.Select(c => new
        {
            prefix = c.Prefix,
            name = c.Name,
            description = c.Description
        });

        return Result.Success(new
        {
            provider = _cacheService.ProviderName,
            categories
        });
    }

    /// <summary>
    /// 清空所有缓存
    /// </summary>
    [HttpPost]
    public Result ClearAllCache()
    {
        var username = HttpContext.User.Username();
        _cacheService.ClearAll();
        _logger.LogWarning("All cache cleared by {Username}", username);
        return Result.Success("所有缓存已清空");
    }

    /// <summary>
    /// 按类别前缀清除缓存
    /// </summary>
    [HttpPost]
    public Result ClearCacheByPrefix([FromForm] string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return Result.Fail("请指定缓存前缀");

        var category = CacheCategories.FirstOrDefault(c => c.Prefix == prefix);
        var username = HttpContext.User.Username();

        _cacheService.RemoveByPrefix(prefix);
        _logger.LogWarning("Cache with prefix '{Prefix}' cleared by {Username}", prefix, username);

        var name = category.Name ?? prefix;
        return Result.Success($"{name}缓存已清空");
    }

    // ==================== 部署管理 ====================

    /// <summary>
    /// 部署目标的 zip 必备标志文件（防呆：传错文件直接拒绝）
    /// </summary>
    private static string GetRequiredMarker(string target) =>
        string.Equals(target, "api", StringComparison.OrdinalIgnoreCase)
            ? $"{typeof(AppController).Assembly.GetName().Name}.dll"
            : "index.html";

    /// <summary>
    /// 校验 zip：返回 (valid, error, 入口前 10 项)
    /// 标志文件需存在于 zip 任意层级
    /// </summary>
    private static (bool Valid, string? Error, List<string> Entries) InspectZip(string zipPath, string target)
    {
        var marker = GetRequiredMarker(target);
        var entries = new List<string>();

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var hasMarker = false;
            foreach (var e in archive.Entries)
            {
                if (entries.Count < 10 && !string.IsNullOrEmpty(e.Name))
                    entries.Add(e.FullName);

                if (!hasMarker &&
                    string.Equals(e.Name, marker, StringComparison.OrdinalIgnoreCase))
                {
                    hasMarker = true;
                }
            }

            if (!hasMarker)
                return (false, $"zip 内未找到 {marker}，疑似传错文件", entries);

            return (true, null, entries);
        }
        catch (Exception ex)
        {
            return (false, $"zip 文件读取失败: {ex.Message}", entries);
        }
    }

    /// <summary>
    /// 把 admin/wwwroot 相对路径解析为本地全路径
    /// </summary>
    private string ResolveUploadedFile(string filePath)
        => Path.Combine(Directory.GetCurrentDirectory(), $"wwwroot{filePath}");

    /// <summary>
    /// 部署预览：校验 zip 后返回目标信息 + 文件信息 + zip 入口
    /// 前端用它在弹窗里展示「即将部署到 xx，路径 xx，文件含 xx」
    /// </summary>
    [HttpGet]
    public Result PreviewDeploy(string target, string filePath, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(target))
            return Result.Fail("请指定部署目标");
        if (string.IsNullOrWhiteSpace(filePath))
            return Result.Fail("文件路径不能为空");

        var fullPath = ResolveUploadedFile(filePath);
        if (!System.IO.File.Exists(fullPath))
            return Result.Fail("文件不存在");

        string targetName;
        string targetPath;
        if (string.Equals(target, "api", StringComparison.OrdinalIgnoreCase))
        {
            targetName = "API 程序";
            targetPath = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        }
        else
        {
            var project = AppGlobal.AppSettings?.DeployConfig?.Projects?
                .FirstOrDefault(p => string.Equals(p.Key, target, StringComparison.OrdinalIgnoreCase));
            if (project == null)
                return Result.Fail($"未找到项目配置: {target}");
            targetName = project.Name;
            targetPath = project.Path;
        }

        var fi = new FileInfo(fullPath);
        var (valid, error, entries) = InspectZip(fullPath, target);

        return Result.Success(new
        {
            valid,
            error,
            target,
            targetName,
            targetPath,
            requiredMarker = GetRequiredMarker(target),
            // 优先用前端传过来的原始文件名，回退到合并后的哈希名（非空校验）
            fileName = string.IsNullOrWhiteSpace(fileName) ? fi.Name : fileName,
            fileSize = fi.Length,
            entries,
        });
    }

    /// <summary>
    /// 用户在确认弹窗点了取消：清掉合并完的 temp zip
    /// </summary>
    [HttpPost]
    public Result CancelUpload([FromForm] string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Result.Fail("文件路径不能为空");

        var fullPath = ResolveUploadedFile(filePath);
        try
        {
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete cancelled upload {Path}", fullPath);
        }
        return Result.Success("已取消");
    }

    /// <summary>
    /// 获取可部署的前端项目列表
    /// </summary>
    [HttpGet]
    public Result DeployTargets()
    {
        var projects = AppGlobal.AppSettings?.DeployConfig?.Projects ?? new List<DeployProject>();
        return Result.Success(projects);
    }

    /// <summary>
    /// 上传前端项目包（zip），解压到对应目录。
    /// 内部走 Upload → Deploy 两步，上传文件持久保存；部署失败时文件仍在，可凭 UploadRecord.Id 重试。
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<Result> UploadProject([FromForm] string key, IFormFile file)
    {
        if (string.IsNullOrEmpty(key))
            return Result.Fail("请指定项目标识");
        if (file == null || file.Length == 0)
            return Result.Fail("请选择文件");
        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return Result.Fail("仅支持 zip 文件");

        var project = AppGlobal.AppSettings?.DeployConfig?.Projects?
            .FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (project == null)
            return Result.Fail($"未找到项目配置: {key}");

        var username = HttpContext.User.Username() ?? "unknown";
        using var stream = file.OpenReadStream();
        var record = await _uploadRecordService.SaveUploadFromStream(key, project.Name, stream, file.FileName, file.Length, username);
        return await DeployByRecord(record);
    }

    /// <summary>
    /// 通过分片上传的文件路径部署前端项目（filePath 由 ChunkUpload/Merge 返回）。
    /// 内部创建 UploadRecord 后走统一 DeployByRecord 逻辑。
    /// </summary>
    [HttpPost]
    public async Task<Result> DeployProjectByPath([FromForm] string key, [FromForm] string filePath, [FromForm] string fileName, [FromForm] long fileSize)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Result.Fail("请指定项目标识");
        if (string.IsNullOrWhiteSpace(filePath))
            return Result.Fail("文件路径不能为空");

        var fullPath = ResolveUploadedFile(filePath);
        if (!System.IO.File.Exists(fullPath))
            return Result.Fail("文件不存在");

        var project = AppGlobal.AppSettings?.DeployConfig?.Projects?
            .FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (project == null)
            return Result.Fail($"未找到项目配置: {key}");

        var username = HttpContext.User.Username() ?? "unknown";
        var displayName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(fullPath) : fileName;
        var actualSize = fileSize > 0 ? fileSize : new FileInfo(fullPath).Length;

        var record = await _uploadRecordService.SaveUpload(key, project.Name, fullPath, displayName, actualSize, username);
        return await DeployByRecord(record);
    }

    // ==================== 统一上传 / 部署接口 ====================

    /// <summary>
    /// 统一上传接口：将 zip 持久化到 upload_staging，创建 UploadRecord（Status=Pending）并返回。
    /// 不触发部署。后续调用 Deploy 接口传入 uploadId 完成部署；若部署失败，文件仍在，可重试。
    /// target = "api" 时部署 API 二进制；其他值对应 DeployConfig.Projects 的 key（如 admin、h5）。
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<Result> Upload([FromForm] string target, IFormFile file)
    {
        if (string.IsNullOrEmpty(target))
            return Result.Fail("请指定 target（api 或项目 key）");
        if (file == null || file.Length == 0)
            return Result.Fail("请选择文件");
        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return Result.Fail("仅支持 zip 文件");

        string targetName;
        if (string.Equals(target, "api", StringComparison.OrdinalIgnoreCase))
        {
            targetName = "API程序";
        }
        else
        {
            var project = AppGlobal.AppSettings?.DeployConfig?.Projects?
                .FirstOrDefault(p => string.Equals(p.Key, target, StringComparison.OrdinalIgnoreCase));
            if (project == null)
                return Result.Fail($"未找到项目配置: {target}");
            targetName = project.Name;
        }

        var username = HttpContext.User.Username() ?? "unknown";
        using var stream = file.OpenReadStream();
        var record = await _uploadRecordService.SaveUploadFromStream(target, targetName, stream, file.FileName, file.Length, username);

        return Result.Success(new
        {
            uploadId = record.Id,
            target = record.Target,
            targetName = record.TargetName,
            fileName = record.FileName,
            fileSize = record.FileSize,
            status = record.Status,
        });
    }

    /// <summary>
    /// 统一部署接口：根据 uploadId 找到已上传文件并部署。
    /// target=api 时重启进程；其他 target 解压到前端目录，不重启。
    /// keepConfig / clearCache 仅对 api 部署有效。
    /// </summary>
    [HttpPost]
    public async Task<Result> Deploy(
        [FromForm] int uploadId,
        [FromForm] bool keepConfig = true,
        [FromForm] bool clearCache = false)
    {
        var record = await _uploadRecordService.Select.Where(r => r.Id == uploadId).FirstAsync();
        if (record == null)
            return Result.Fail("上传记录不存在");

        var fullPath = _uploadRecordService.GetFullPath(record);
        if (!System.IO.File.Exists(fullPath))
            return Result.Fail("上传文件已不存在，请重新上传");

        return await DeployByRecord(record, keepConfig: keepConfig, clearCache: clearCache);
    }

    /// <summary>
    /// 上传记录列表：可按 target / status 过滤，方便查找可重试的失败记录。
    /// status 可选值：Pending / Deployed / Failed
    /// </summary>
    [HttpGet]
    public async Task<Result> UploadRecords(string? target = null, string? status = null, int page = 1, int pageSize = 20)
    {
        var data = await _uploadRecordService.GetRecords(target, status, page, pageSize);
        return Result.Success(data);
    }

    /// <summary>
    /// 核心部署逻辑：根据 UploadRecord 决定走 api 部署还是前端项目部署，并更新记录状态。
    /// </summary>
    private async Task<Result> DeployByRecord(
        UploadRecord record,
        bool keepConfig = true,
        bool clearCache = false)
    {
        var fullPath = _uploadRecordService.GetFullPath(record);
        if (!System.IO.File.Exists(fullPath))
        {
            await _uploadRecordService.MarkFailed(record.Id, "上传文件已不存在");
            return Result.Fail("上传文件已不存在，请重新上传");
        }

        // 兜底校验
        var (valid, error, _) = InspectZip(fullPath, record.Target);
        if (!valid)
        {
            await _uploadRecordService.MarkFailed(record.Id, error ?? "zip 校验失败");
            return Result.Fail(error ?? "zip 文件校验失败");
        }

        var username = record.UploadedBy;
        var isApi = string.Equals(record.Target, "api", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isApi)
            {
                var appDir = AppContext.BaseDirectory;

                await _deployRecordService.SaveRecord("api", "API程序", fullPath, record.FileName, record.FileSize, username);

                try { System.IO.File.Delete(Path.Combine(appDir, "license_time.dat")); } catch { }

                ExtractApiAtomic(fullPath, appDir, keepConfig);

                if (clearCache)
                {
                    try { _cacheService.ClearAll(); }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to clear cache during deploy"); }
                }

                await _uploadRecordService.MarkDeployed(record.Id);
                _logger.LogWarning("API package deployed (uploadId={Id}) by {Username}, restarting...", record.Id, username);

                AppShutdownHelper.GracefulStop(_appLifetime);
                return Result.Success(clearCache ? "程序包已更新+缓存已清空，即将重启" : "程序包已更新，即将重启");
            }
            else
            {
                var project = AppGlobal.AppSettings?.DeployConfig?.Projects?
                    .FirstOrDefault(p => string.Equals(p.Key, record.Target, StringComparison.OrdinalIgnoreCase));
                if (project == null)
                {
                    await _uploadRecordService.MarkFailed(record.Id, $"未找到项目配置: {record.Target}");
                    return Result.Fail($"未找到项目配置: {record.Target}");
                }
                if (!Directory.Exists(project.Path))
                {
                    await _uploadRecordService.MarkFailed(record.Id, $"目标目录不存在: {project.Path}");
                    return Result.Fail($"目标目录不存在: {project.Path}");
                }

                await _deployRecordService.SaveRecord(record.Target, project.Name, fullPath, record.FileName, record.FileSize, username);

                var dirInfo = new DirectoryInfo(project.Path);
                foreach (var f in dirInfo.GetFiles())
                {
                    if (f.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    try { f.Delete(); } catch (UnauthorizedAccessException) { }
                }
                foreach (var d in dirInfo.GetDirectories())
                {
                    if (d.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    try { d.Delete(true); } catch (UnauthorizedAccessException) { }
                }

                ZipFile.ExtractToDirectory(fullPath, project.Path, overwriteFiles: true);

                await _uploadRecordService.MarkDeployed(record.Id);
                _logger.LogWarning("Project '{ProjectName}' deployed (uploadId={Id}) by {Username}", project.Name, record.Id, username);

                return Result.Success($"项目 {project.Name} 已更新");
            }
        }
        catch (Exception ex)
        {
            await _uploadRecordService.MarkFailed(record.Id, ex.Message);
            _logger.LogError(ex, "Deploy failed for uploadId={Id} target={Target}", record.Id, record.Target);
            return Result.Fail($"部署失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取部署记录列表
    /// </summary>
    [HttpGet]
    public async Task<Result> DeployRecords(string? target, int page = 1, int pageSize = 20)
    {
        var data = await _deployRecordService.GetRecords(target, page, pageSize);
        return Result.Success(data);
    }

    /// <summary>
    /// 回滚到指定部署记录
    /// </summary>
    [HttpPost]
    public async Task<Result> Rollback([FromForm] int id)
    {
        var record = await _deployRecordService.Select.Where(r => r.Id == id).FirstAsync();
        if (record == null)
            return Result.Fail("部署记录不存在");

        var backupPath = _deployRecordService.GetBackupFullPath(record);
        if (backupPath == null)
            return Result.Fail("备份文件不存在，无法回滚");

        var username = HttpContext.User.Username();

        if (record.Target == "api")
        {
            // 回滚 API — 同样走 atomic rename,避免 mmap race
            var appDir = AppContext.BaseDirectory;
            ExtractApiAtomic(backupPath, appDir);

            // 记录回滚操作
            await _deployRecordService.SaveRecord("api", "API程序(回滚)", backupPath, record.FileName, record.FileSize, username ?? "unknown");

            _logger.LogWarning("API rolled back to record #{RecordId} by {Username}", id, username);
            AppShutdownHelper.GracefulStop(_appLifetime);
            return Result.Success("API 已回滚，即将重启");
        }
        else
        {
            // 回滚前端项目
            var project = AppGlobal.AppSettings?.DeployConfig?.Projects?
                .FirstOrDefault(p => string.Equals(p.Key, record.Target, StringComparison.OrdinalIgnoreCase));

            if (project == null)
                return Result.Fail($"未找到项目配置: {record.Target}");

            if (!Directory.Exists(project.Path))
                return Result.Fail($"目标目录不存在: {project.Path}");

            // 清空目标目录（保留软连接）
            var dirInfo = new DirectoryInfo(project.Path);
            foreach (var f in dirInfo.GetFiles())
            {
                if (f.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                try { f.Delete(); }
                catch (UnauthorizedAccessException) { /* 跳过受保护文件，如宝塔 .user.ini 带 chattr +i */ }
            }
            foreach (var d in dirInfo.GetDirectories())
            {
                if (d.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                try { d.Delete(true); }
                catch (UnauthorizedAccessException) { /* 跳过受保护目录 */ }
            }

            ZipFile.ExtractToDirectory(backupPath, project.Path, overwriteFiles: true);

            // 记录回滚操作
            await _deployRecordService.SaveRecord(record.Target, $"{project.Name}(回滚)", backupPath, record.FileName, record.FileSize, username ?? "unknown");

            _logger.LogWarning("Project '{ProjectName}' rolled back to record #{RecordId} by {Username}", project.Name, id, username);
            return Result.Success($"项目 {project.Name} 已回滚");
        }
    }
}
