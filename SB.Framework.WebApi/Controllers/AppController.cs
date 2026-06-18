using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Core;
using SB.Framework.Core.Authentication;
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
    private readonly DeployJobService _deployJobService;

    public AppController(
        IHostApplicationLifetime appLifetime,
        ILogger<AppController> logger,
        IWebHostEnvironment env,
        ICacheService cacheService,
        DeployJobService deployJobService)
    {
        _appLifetime = appLifetime;
        _logger = logger;
        _env = env;
        _cacheService = cacheService;
        _deployJobService = deployJobService;
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
    /// [已废弃] 上传 API 程序包（zip），解压覆盖后自动重启。
    /// 请改用 Upload(target="api") + Deploy(uploadId) 两步流程。
    /// </summary>
    [Obsolete("请改用 Upload(target=\"api\") + Deploy(uploadId) 两步流程")]
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
        var job = await _deployJobService.CreateFromStream("api", "API程序", stream, file.FileName, file.Length, username);
        return await DeployByJob(job, keepConfig: keepConfig, clearCache: false);
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
    private static void ExtractApiAtomic(string zipPath, string appDir, bool keepConfig = true, bool keepLock = true)
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
                // keepLock=true 时跳过根目录的 installed.lock,
                // 防止部署包里的安装标记覆盖线上已安装状态、导致部署后被打回安装向导(默认开启)。
                if (keepLock && IsProtectedLockFile(rel))
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
    /// installed.lock 由独立的 IsProtectedLockFile + keepLock 开关控制,不在此处。
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
    /// 部署预检冒烟:把待部署 zip 解压到临时 staging,拷入线上运行配置,用临时端口以 SB_SMOKE_TEST=1
    /// 模式拉起新包,轮询 /health。返回 (是否健康, 详情)。全程不碰 /app 现有文件。
    /// 冒烟模式下新进程跳过 DB 迁移/后台任务/心跳(见 SbFrameworkExtensions.IsSmokeTest),只读探活。
    /// </summary>
    private async Task<(bool ok, string message)> SmokeTestApiPackageAsync(string zipPath, string appDir)
    {
        var staging = Path.Combine(appDir, $".smoke-{Guid.NewGuid():N}");
        Process? proc = null;
        try
        {
            // 1. 解压新包到 staging
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(zipPath, staging);

            // 2. 拷入线上运行所需文件(配置 / 安装锁 / 客户端密钥),让冒烟实例能像生产一样启动
            foreach (var f in Directory.EnumerateFiles(appDir, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(f);
                var isCfg = name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                if (isCfg || name.Equals("installed.lock", StringComparison.OrdinalIgnoreCase) || name.Equals("client.key", StringComparison.OrdinalIgnoreCase))
                {
                    try { System.IO.File.Copy(f, Path.Combine(staging, name), overwrite: true); } catch { }
                }
            }

            // 3. 定位入口 dll(与当前进程同名),挑一个空闲端口
            var entryDll = Path.GetFileName(Assembly.GetEntryAssembly()?.Location ?? "");
            if (string.IsNullOrEmpty(entryDll) || !System.IO.File.Exists(Path.Combine(staging, entryDll)))
                return (false, $"新包内未找到入口 dll: {entryDll}");
            int port = GetFreeTcpPort();

            // 4. 以冒烟模式拉起
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = staging,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add(Path.Combine(staging, entryDll));
            psi.ArgumentList.Add("--urls");
            psi.ArgumentList.Add($"http://127.0.0.1:{port}");
            psi.Environment["SB_SMOKE_TEST"] = "1";
            psi.Environment["ASPNETCORE_ENVIRONMENT"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            var stderr = new System.Text.StringBuilder();
            proc = new Process { StartInfo = psi };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
            proc.Start();
            proc.BeginErrorReadLine();
            proc.BeginOutputReadLine();

            // 5. 轮询 /health,最多 ~45s
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline)
            {
                if (proc.HasExited)
                {
                    var tail = stderr.ToString();
                    return (false, $"新包启动即退出(code={proc.ExitCode}): {Trunc(tail, 300)}");
                }
                try
                {
                    var resp = await http.GetAsync($"http://127.0.0.1:{port}/health");
                    if (resp.IsSuccessStatusCode) return (true, $"健康检查通过(port={port})");
                }
                catch { /* 还没起好,继续等 */ }
                await Task.Delay(1500);
            }
            return (false, $"45s 内 /health 未通过(端口 {port});stderr: {Trunc(stderr.ToString(), 300)}");
        }
        catch (Exception ex)
        {
            return (false, $"冒烟异常: {ex.Message}");
        }
        finally
        {
            try { if (proc is { HasExited: false }) proc.Kill(entireProcessTree: true); } catch { }
            try { proc?.Dispose(); } catch { }
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    private static int GetFreeTcpPort()
    {
        var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static string Trunc(string s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");

    /// <summary>
    /// 判断是否为受保护的安装标记文件(只针对部署根目录的 installed.lock)。
    /// 该文件标记应用已完成安装向导,被部署包覆盖会导致线上被打回重新安装。
    /// 由独立开关 keepLock 控制,与 appsettings 的 keepConfig 分离。
    /// </summary>
    private static bool IsProtectedLockFile(string relPath)
    {
        // 只保护根目录的 installed.lock,子目录的不算
        var dirName = Path.GetDirectoryName(relPath);
        if (!string.IsNullOrEmpty(dirName)) return false;
        var fileName = Path.GetFileName(relPath);
        return string.Equals(fileName, "installed.lock", StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>
    /// [已废弃] 通过分片上传的文件路径部署 API 程序。
    /// 请改用 Upload(target="api") + Deploy(uploadId) 两步流程。
    /// </summary>
    [Obsolete("请改用 Upload(target=\"api\") + Deploy(uploadId) 两步流程")]
    [HttpPost]
    public async Task<Result> DeployApiByPath(
        [FromForm] string filePath,
        [FromForm] string fileName,
        [FromForm] long fileSize,
        [FromForm] bool clearCache = false,
        [FromForm] bool keepConfig = true,
        [FromForm] bool keepLock = true)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Result.Fail("文件路径不能为空");

        var fullPath = ResolveUploadedFile(filePath);
        if (!System.IO.File.Exists(fullPath))
            return Result.Fail("文件不存在");

        var username = HttpContext.User.Username() ?? "unknown";
        var displayName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(fullPath) : fileName;
        var actualSize = fileSize > 0 ? fileSize : new FileInfo(fullPath).Length;

        var job = await _deployJobService.CreateFromFile("api", "API程序", fullPath, displayName, actualSize, username);
        return await DeployByJob(job, keepConfig: keepConfig, clearCache: clearCache, keepLock: keepLock);
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
        var stats = _cacheService.GetStats();

        var categories = CacheCategories.Select(c => new
        {
            prefix = c.Prefix,
            name = c.Name,
            description = c.Description,
            count = _cacheService.CountByPrefix(c.Prefix),
        }).ToList();

        // 未分类 = 当前实例 key 数 - 各分类 count 之和
        int uncategorized = -1;
        if (stats != null && stats.TryGetValue("instance_keys", out var ikObj) && ikObj is int ik && ik >= 0)
        {
            var categorized = categories.Sum(c => Math.Max(0, c.count));
            uncategorized = Math.Max(0, ik - categorized);
        }

        return Result.Success(new
        {
            provider = _cacheService.ProviderName,
            stats,
            categories,
            uncategorized,
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
    /// [已废弃] 上传前端项目包（zip），解压到对应目录。
    /// 请改用 Upload(target=key) + Deploy(uploadId) 两步流程。
    /// </summary>
    [Obsolete("请改用 Upload(target=key) + Deploy(uploadId) 两步流程")]
    [HttpPost]
    [RequestSizeLimit(200 * 1024 * 1024)]
    [Scope("deploy")]
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
        var job = await _deployJobService.CreateFromStream(key, project.Name, stream, file.FileName, file.Length, username);
        return await DeployByJob(job);
    }

    /// <summary>
    /// [已废弃] 通过分片上传的文件路径部署前端项目。
    /// 请改用 Upload(target=key) + Deploy(uploadId) 两步流程。
    /// </summary>
    [Obsolete("请改用 Upload(target=key) + Deploy(uploadId) 两步流程")]
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

        var job = await _deployJobService.CreateFromFile(key, project.Name, fullPath, displayName, actualSize, username);
        return await DeployByJob(job);
    }

    // ==================== 统一上传 / 部署接口 ====================

    /// <summary>
    /// 统一上传接口:将 zip 持久化到 deploy_archive,创建 DeployJob(Status=Uploaded)并返回。
    /// 不触发部署。后续调用 Deploy 接口传入 jobId 完成部署;若部署失败,文件仍在,可重试。
    /// target = "api" 时部署 API 二进制;其他值对应 DeployConfig.Projects 的 key(admin / h5)。
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<Result> Upload([FromForm] string target, IFormFile file)
    {
        if (string.IsNullOrEmpty(target))
            return Result.Fail("请指定 target(api 或项目 key)");
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
        var job = await _deployJobService.CreateFromStream(target, targetName, stream, file.FileName, file.Length, username);

        return Result.Success(new
        {
            jobId = job.Id,
            uploadId = job.Id,  // 向后兼容旧前端字段名
            target = job.Target,
            targetName = job.TargetName,
            fileName = job.FileName,
            fileSize = job.FileSize,
            status = job.Status,
        });
    }

    /// <summary>
    /// 统一部署接口:根据 jobId(旧字段名 uploadId 也接受)找到已上传文件并部署。
    /// target=api 时重启进程;其他 target 解压到前端目录,不重启。
    /// keepConfig / clearCache 仅对 api 部署有效。
    /// </summary>
    [HttpPost]
    public async Task<Result> Deploy(
        [FromForm] int uploadId,
        [FromForm] bool keepConfig = true,
        [FromForm] bool clearCache = false,
        [FromForm] bool keepLock = true)
    {
        var jobId = uploadId;  // 字段名向后兼容
        var job = await _deployJobService.Select.Where(r => r.Id == jobId).FirstAsync();
        if (job == null)
            return Result.Fail("部署作业不存在");

        var fullPath = _deployJobService.GetFullPath(job);
        if (!System.IO.File.Exists(fullPath))
            return Result.Fail("上传文件已不存在,请重新上传");

        return await DeployByJob(job, keepConfig: keepConfig, clearCache: clearCache, keepLock: keepLock);
    }

    /// <summary>
    /// 部署作业列表(向后兼容旧 endpoint 名 UploadRecords)。可按 target / status 过滤。
    /// 旧前端 status=Pending 自动映射到新 Uploaded;新值 Uploaded/Deploying/Deployed/Failed/RolledBack 也接受。
    /// </summary>
    [HttpGet]
    public async Task<Result> UploadRecords(string? target = null, string? status = null, int page = 1, int pageSize = 20)
    {
        // 旧前端字段兼容:Pending → Uploaded
        if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            status = DeployJobStatus.Uploaded;
        var data = await _deployJobService.GetJobs(target, status, page, pageSize);
        return Result.Success(data);
    }

    /// <summary>部署作业列表(新名,等同 UploadRecords)</summary>
    [HttpGet]
    public async Task<Result> DeployJobs(string? target = null, string? status = null, int page = 1, int pageSize = 20)
    {
        var data = await _deployJobService.GetJobs(target, status, page, pageSize);
        return Result.Success(data);
    }

    /// <summary>
    /// 核心部署逻辑:根据 DeployJob 决定走 api 部署还是前端项目部署,流转 Uploaded/Deploying/Deployed/Failed 状态。
    /// 文件已在 deploy_archive,部署成功的旧 job(同 target)自动转 RolledBack 并被新 job 的 PreviousJobId 指向。
    /// </summary>
    private async Task<Result> DeployByJob(
        DeployJob job,
        bool keepConfig = true,
        bool clearCache = false,
        bool keepLock = true)
    {
        var fullPath = _deployJobService.GetFullPath(job);
        if (!System.IO.File.Exists(fullPath))
        {
            await _deployJobService.MarkFailed(job.Id, "上传文件已不存在");
            return Result.Fail("上传文件已不存在,请重新上传");
        }

        var (valid, error, _) = InspectZip(fullPath, job.Target);
        if (!valid)
        {
            await _deployJobService.MarkFailed(job.Id, error ?? "zip 校验失败");
            return Result.Fail(error ?? "zip 文件校验失败");
        }

        var username = job.UploadedBy;
        var isApi = string.Equals(job.Target, "api", StringComparison.OrdinalIgnoreCase);

        await _deployJobService.MarkDeploying(job.Id, username);

        try
        {
            if (isApi)
            {
                var appDir = AppContext.BaseDirectory;

                // ① 预检门控:覆盖 /app 之前,先把新包在临时端口以冒烟模式跑一遍 /health。
                //    不通过 → 直接失败,/app 从未被动过,线上 API 不掉。可用 SB_DEPLOY_SMOKE=0 关闭。
                if (!string.Equals(Environment.GetEnvironmentVariable("SB_DEPLOY_SMOKE"), "0", StringComparison.Ordinal))
                {
                    var (smokeOk, smokeMsg) = await SmokeTestApiPackageAsync(fullPath, appDir);
                    if (!smokeOk)
                    {
                        await _deployJobService.MarkFailed(job.Id, $"预检未通过(已拦截,线上未改动): {smokeMsg}");
                        _logger.LogError("Deploy pre-flight smoke test FAILED (jobId={Id}): {Msg}", job.Id, smokeMsg);
                        return Result.Fail($"预检未通过,已拦截部署(线上保持原样): {smokeMsg}");
                    }
                    _logger.LogWarning("Deploy pre-flight smoke test PASSED (jobId={Id})", job.Id);
                }

                try { System.IO.File.Delete(Path.Combine(appDir, "license_time.dat")); } catch { }

                ExtractApiAtomic(fullPath, appDir, keepConfig, keepLock);

                if (clearCache)
                {
                    try { _cacheService.ClearAll(); }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to clear cache during deploy"); }
                }

                await _deployJobService.MarkDeployed(job.Id, job.Target);
                _logger.LogWarning("API package deployed (jobId={Id}) by {Username}, restarting...", job.Id, username);

                // ② 给独立看门狗(pm-deploy-guard)落"刚部署"标记:重启后若 /health 久不恢复,
                //    看门狗会自动从 deploy_archive 回滚上一份 Deployed。见 docs/AI_Deploy_Resilience.md。
                try
                {
                    System.IO.File.WriteAllText(Path.Combine(appDir, ".deploying"),
                        $"{DateTime.UtcNow:o}|job={job.Id}|target={job.Target}");
                }
                catch (Exception ex) { _logger.LogWarning(ex, "写 .deploying 标记失败(看门狗回滚不受影响,仅少一个信号)"); }

                AppShutdownHelper.GracefulStop(_appLifetime);
                return Result.Success(clearCache ? "程序包已更新+缓存已清空,即将重启" : "程序包已更新,即将重启");
            }
            else
            {
                var project = AppGlobal.AppSettings?.DeployConfig?.Projects?
                    .FirstOrDefault(p => string.Equals(p.Key, job.Target, StringComparison.OrdinalIgnoreCase));
                if (project == null)
                {
                    await _deployJobService.MarkFailed(job.Id, $"未找到项目配置: {job.Target}");
                    return Result.Fail($"未找到项目配置: {job.Target}");
                }
                if (!Directory.Exists(project.Path))
                {
                    await _deployJobService.MarkFailed(job.Id, $"目标目录不存在: {project.Path}");
                    return Result.Fail($"目标目录不存在: {project.Path}");
                }

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

                await _deployJobService.MarkDeployed(job.Id, job.Target);
                _logger.LogWarning("Project '{ProjectName}' deployed (jobId={Id}) by {Username}", project.Name, job.Id, username);

                return Result.Success($"项目 {project.Name} 已更新");
            }
        }
        catch (Exception ex)
        {
            await _deployJobService.MarkFailed(job.Id, ex.Message);
            _logger.LogError(ex, "Deploy failed for jobId={Id} target={Target}", job.Id, job.Target);
            return Result.Fail($"部署失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 部署记录列表(向后兼容旧 endpoint 名):查 DeployJob filter Status=Deployed/RolledBack(已完成生命周期的部署历史)。
    /// </summary>
    [HttpGet]
    public async Task<Result> DeployRecords(string? target, int page = 1, int pageSize = 20)
    {
        // 旧 DeployRecord 只记部署成功,新模型可同时查 Deployed + RolledBack 看历史
        var data = await _deployJobService.GetJobs(target, status: null, page, pageSize);
        return Result.Success(data);
    }

    /// <summary>
    /// 回滚到指定 DeployJob(被回滚的目标必须是 Deployed 或 RolledBack)。
    /// 创建一个新 DeployJob(RollbackOfJobId 指向原 job,共享旧 StoragePath),立即触发部署。
    /// 部署成功后:新 job → Deployed;当前活动的 Deployed job → RolledBack。
    /// </summary>
    [HttpPost]
    public async Task<Result> Rollback([FromForm] int id)
    {
        var target = await _deployJobService.Select.Where(r => r.Id == id).FirstAsync();
        if (target == null)
            return Result.Fail("部署作业不存在");

        var fullPath = _deployJobService.GetFullPath(target);
        if (!System.IO.File.Exists(fullPath))
            return Result.Fail("归档文件不存在,无法回滚");

        var username = HttpContext.User.Username() ?? "unknown";
        var rollbackJob = await _deployJobService.CreateRollbackJob(id, username);
        _logger.LogWarning("Rollback initiated to job #{TargetId} (new jobId={NewId}) by {Username}", id, rollbackJob.Id, username);

        return await DeployByJob(rollbackJob);
    }
}
