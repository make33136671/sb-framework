using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using SB.Framework.Core;
using SB.Framework.Core.Brand;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Models.Dto;
using StackExchange.Redis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace SB.Framework.Services
{
    public class SetupService
    {
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _env;
        private readonly ILogger<SetupService> _logger;
        private readonly object _installLock = new();
        private volatile bool _installing;

        public SetupService(IConfiguration configuration, IHostEnvironment env, ILogger<SetupService> logger)
        {
            _configuration = configuration;
            _env = env;
            _logger = logger;
        }

        public string LockFilePath => Path.Combine(_env.ContentRootPath, "installed.lock");

        public bool IsInstalled() => File.Exists(LockFilePath);

        /// <summary>
        /// 导出系统安装配置（安装完成后仍可导出）。
        /// 值取自运行时生效配置（IConfiguration，含安装后通过运维工具/手改配置文件产生的最新值），
        /// 输出与安装向导「导入配置」的 setup-config.json（version=1.0）格式完全兼容，
        /// 重新安装时可直接导入预填。
        /// 管理员密码为不可逆散列存储无法导出，admin 段仅返回空占位（导入后需重新设置密码）。
        /// </summary>
        public Result ExportInstalledConfig()
        {
            if (!IsInstalled())
                return Result.Fail("系统尚未安装，无需导出");

            var corsOrigins = _configuration.GetSection("CorsConfig:Origins").GetChildren()
                .Select(c => c.Value ?? "").Where(v => v.Length > 0).ToList();

            var redisInstance = _configuration["CacheConfig:Redis:InstanceName"] ?? "";
            bool cacheEnabled = bool.TryParse(_configuration["CacheConfig:Enabled"], out var ce) && ce;
            bool corsAllowAll = bool.TryParse(_configuration["CorsConfig:AllowAll"], out var caa) && caa;

            var config = new
            {
                version = "1.0",
                exportedAt = SB.Framework.Core.Util.AppTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                database = new
                {
                    dbType = _configuration["DbConfig:DbType"] ?? "",
                    server = _configuration["DbConfig:Server"] ?? "",
                    port = _configuration["DbConfig:Port"] ?? "",
                    database = _configuration["DbConfig:Database"] ?? "",
                    user = _configuration["DbConfig:User"] ?? "",
                    password = _configuration["DbConfig:Password"] ?? "",
                },
                cache = new
                {
                    enabled = cacheEnabled,
                    provider = _configuration["CacheConfig:Provider"] ?? "Memory",
                    redis = new
                    {
                        connection = _configuration["CacheConfig:Redis:Connection"] ?? "",
                        instanceName = redisInstance,
                    },
                },
                app = new
                {
                    productCn = _configuration["ProductCn"] ?? "",
                    productEn = _configuration["ProductEn"] ?? "",
                    primaryDomain = _configuration["PrimaryDomain"] ?? "",
                    timeZone = _configuration["TimeZone"] ?? "Asia/Shanghai",
                    cachePrefix = redisInstance,
                    corsAllowAll,
                    corsOrigins,
                    passwordStorageMode = _configuration["PasswordConfig:StorageMode"] ?? "Encrypted",
                    cloudServerUrl = _configuration["CloudConfig:ServerUrl"] ?? "",
                },
                admin = new { username = "", displayName = "", password = "" },
            };
            return Result.Success(config);
        }

        /// <summary>
        /// 推断部署路径和数据库默认值
        /// Docker 模式：检测 DOTNET_RUNNING_IN_CONTAINER，返回容器内固定路径和 PostgreSQL 默认值
        /// 传统模式：取 API 的 ContentRoot 最后一段（如 sb_test_api），去掉 _api 后缀拼装同级路径
        /// </summary>
        public Result GetDeployHints()
        {
            var apiPath = _env.ContentRootPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? "";

            // 多重 Docker 环境检测（任一命中即视为 Docker 模式）
            // 1. SB_IS_DOCKER=1 显式标志（start-api.sh 设置）
            // 2. DOTNET_RUNNING_IN_CONTAINER=true（微软官方镜像内置）
            // 3. /proc/1/cgroup 包含 docker/kubepods（Linux 容器标准特征）
            // 4. ContentRootPath 以 /app/ 开头（sb 框架 Docker 镜像约定）
            // 5. 存在 /.dockerenv 文件
            var isDocker = false;
            if (Environment.GetEnvironmentVariable("SB_IS_DOCKER") == "1")
                isDocker = true;
            else if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
                isDocker = true;
            else if (apiPath.StartsWith("/app/", StringComparison.OrdinalIgnoreCase))
                isDocker = true;
            else if (File.Exists("/.dockerenv"))
                isDocker = true;
            else
            {
                try
                {
                    if (File.Exists("/proc/1/cgroup"))
                    {
                        var cgroup = File.ReadAllText("/proc/1/cgroup");
                        if (cgroup.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
                            cgroup.Contains("kubepods", StringComparison.OrdinalIgnoreCase))
                            isDocker = true;
                    }
                } catch { /* 忽略读取错误 */ }
            }

            if (isDocker)
            {
                var pgPassword = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "";
                var pgDb = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "";

                string dockerAdminPath = Directory.Exists("/app/admin-ui") ? "/app/admin-ui" : "";
                string dockerUserPath = Directory.Exists("/app/user-ui") ? "/app/user-ui" : "";

                // Docker 模式 Redis 配置（容器内本地 Redis，默认无密码）
                var redisHost = "127.0.0.1";
                var redisPort = "6379";
                var redisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? "";
                var redisInstance = Environment.GetEnvironmentVariable("REDIS_INSTANCE_NAME") ?? "";

                return Result.Success(new
                {
                    isDocker = true,
                    apiPath,
                    adminPath = dockerAdminPath,
                    h5Path = dockerUserPath,
                    database = !string.IsNullOrEmpty(pgPassword) ? new
                    {
                        dbType = "PostgreSql",
                        server = "127.0.0.1",
                        port = "5432",
                        database = pgDb,
                        user = "postgres",
                        password = pgPassword
                    } : null,
                    redis = new
                    {
                        host = redisHost,
                        port = redisPort,
                        password = redisPassword,
                        instanceName = redisInstance
                    }
                });
            }

            // 传统部署：从 ContentRootPath 推断同级 *_admin / *_h5
            var folderName = Path.GetFileName(apiPath);
            var parentDir = Path.GetDirectoryName(apiPath) ?? "";
            string adminPath = "", h5Path = "";
            if (!string.IsNullOrEmpty(folderName) && folderName.EndsWith("_api", StringComparison.OrdinalIgnoreCase))
            {
                var prefix = folderName[..^"_api".Length];
                adminPath = Path.Combine(parentDir, prefix + "_admin");
                h5Path = Path.Combine(parentDir, prefix + "_h5");
            }
            return Result.Success(new { isDocker = false, apiPath, adminPath, h5Path });
        }

        /// <summary>
        /// 测试数据库连接
        /// </summary>
        public Result TestDatabaseConnection(DatabaseConfigDto config)
        {
            IFreeSql? fsql = null;
            try
            {
                var dataType = ParseDataType(config.DbType);
                var connStr = BuildConnectionString(config);
                fsql = new FreeSql.FreeSqlBuilder()
                    .UseConnectionString(dataType, connStr)
                    .UseAutoSyncStructure(false)
                    .Build();

                fsql.Ado.ExecuteScalar("SELECT 1");
                return Result.Success("数据库连接成功", new { dbExists = true });
            }
            catch (Exception ex)
            {
                fsql?.Dispose();
                fsql = null;

                // 连接失败，尝试不指定数据库连接服务器，检测是否为"库不存在"
                return TryCreateDatabase(config) ?? Result.Fail($"数据库连接失败: {ex.Message}");
            }
            finally
            {
                fsql?.Dispose();
            }
        }

        /// <summary>
        /// 数据库不存在时：连接服务器检测是否有建库权限
        /// </summary>
        private Result? TryCreateDatabase(DatabaseConfigDto config)
        {
            IFreeSql? serverFsql = null;
            try
            {
                var dataType = ParseDataType(config.DbType);
                var serverConnStr = BuildServerConnectionString(config);
                serverFsql = new FreeSql.FreeSqlBuilder()
                    .UseConnectionString(dataType, serverConnStr)
                    .UseAutoSyncStructure(false)
                    .Build();

                serverFsql.Ado.ExecuteScalar("SELECT 1");

                // 服务器连通，检测建库权限
                var dbName = config.Database;
                bool hasCreatePrivilege;
                if (string.Equals(config.DbType, "PostgreSql", StringComparison.OrdinalIgnoreCase))
                {
                    // PostgreSQL: 检查是否为 superuser 或有 CREATEDB 权限
                    var result = serverFsql.Ado.ExecuteScalar("SELECT rolcreatedb OR rolsuper FROM pg_roles WHERE rolname = current_user");
                    hasCreatePrivilege = result is true || result is bool b && b;
                }
                else
                {
                    // MySQL: 检查 GRANT 中是否包含 CREATE 权限
                    var grants = serverFsql.Ado.ExecuteScalar("SHOW GRANTS FOR CURRENT_USER()")?.ToString() ?? "";
                    hasCreatePrivilege = grants.Contains("ALL PRIVILEGES", StringComparison.OrdinalIgnoreCase)
                        || grants.Contains("CREATE", StringComparison.OrdinalIgnoreCase);
                }

                if (hasCreatePrivilege)
                {
                    return Result.Success($"数据库 {dbName} 不存在，安装时将自动创建", new { dbNotExist = true, canCreate = true });
                }
                else
                {
                    return Result.Fail($"数据库 {dbName} 不存在，当前账号没有创建数据库的权限");
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                serverFsql?.Dispose();
            }
        }

        /// <summary>
        /// 测试 Redis 连接
        /// </summary>
        public Result TestRedisConnection(RedisConfigDto config)
        {
            try
            {
                var redis = ConnectionMultiplexer.Connect(config.Connection);
                var db = redis.GetDatabase();
                db.Ping();
                redis.Close();
                return Result.Success("Redis 连接成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis 连接测试失败");
                return Result.Fail($"Redis 连接失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行完整安装
        /// </summary>
        public Result Install(SetupRequest request)
        {
            if (IsInstalled())
                return Result.Fail("系统已安装，请勿重复安装");

            if (_installing)
                return Result.Fail("安装正在进行中，请勿重复提交");

            lock (_installLock)
            {
                if (_installing)
                    return Result.Fail("安装正在进行中，请勿重复提交");

                _installing = true;
                try
                {
                    return DoInstall(request);
                }
                finally
                {
                    _installing = false;
                }
            }
        }

        private Result DoInstall(SetupRequest request)
        {
            // 1. 验证参数
            if (request.Database == null)
                return Result.Fail("数据库配置不能为空");
            if (string.IsNullOrWhiteSpace(request.AdminUsername))
                return Result.Fail("管理员用户名不能为空");
            if (string.IsNullOrWhiteSpace(request.AdminPassword) || request.AdminPassword.Length < 6)
                return Result.Fail("管理员密码不能为空且至少6位");

            // 仅在启用缓存时验证缓存配置
            if (request.EnableCache)
            {
                if (string.Equals(request.CacheProvider, "Redis", StringComparison.OrdinalIgnoreCase) && request.Redis == null)
                    return Result.Fail("选择 Redis 缓存时必须提供 Redis 配置");
            }

            // 验证产品英文名格式（小写字母+数字，2-20字符，字母开头）
            if (!string.IsNullOrEmpty(request.ProductEn) &&
                !System.Text.RegularExpressions.Regex.IsMatch(request.ProductEn, @"^[a-z][a-z0-9]{1,19}$"))
                return Result.Fail("产品英文名称只允许小写字母和数字，2-20字符，字母开头");

            IFreeSql? fsql = null;
            string? appSettingsBackup = null;
            HashSet<string>? tablesBeforeSync = null;
            try
            {
                // 2. 如果数据库不存在，先创建
                EnsureDatabaseExists(request.Database);

                // 3. 创建临时 FreeSql 实例
                var dataType = ParseDataType(request.Database.DbType);
                var connStr = BuildConnectionString(request.Database);
                fsql = new FreeSql.FreeSqlBuilder()
                    .UseConnectionString(dataType, connStr)
                    .UseAutoSyncStructure(false)
                    .Build();

                // 测试连接
                fsql.Ado.ExecuteScalar("SELECT 1");
                _logger.LogInformation("安装向导: 数据库连接成功");

                // 2.5 抹除数据库（如果选择了重新安装）
                if (request.WipeDatabase)
                {
                    WipeAllTables(fsql, request.Database.Database);
                    _logger.LogInformation("安装向导: 数据库已抹除");
                }

                // 3. 发现所有模块初始化器
                var modules = DiscoverModuleInitializers(
                    _configuration["ProjectAssemblyName"]);
                _logger.LogInformation("安装向导: 发现 {ModuleCount} 个模块初始化器", modules.Count);

                // 4. 建表（收集所有模块的实体类型）
                var entityTypes = modules.SelectMany(m => m.GetEntityTypes()).Distinct().ToArray();
                if (entityTypes.Length > 0)
                {
                    // 快照已有表名，用于安装失败时回滚
                    tablesBeforeSync = GetExistingTableNames(fsql, request.Database.Database, request.Database.DbType);
                    fsql.CodeFirst.SyncStructure(entityTypes);
                    _logger.LogInformation("安装向导: 数据表创建/同步成功 ({Count} 个实体)", entityTypes.Length);
                }

                // 5. 种子数据
                SeedData(fsql, request, modules);
                _logger.LogInformation("安装向导: 种子数据写入成功");

                // 6. 更新 appsettings.json
                var jwtAdminKey = GenerateJwtKey();
                var jwtApiKey = GenerateJwtKey();
                var appSettingsPath = AppGlobal.GetAppSettingsPath(_env.ContentRootPath, _env.EnvironmentName);
                appSettingsBackup = File.ReadAllText(appSettingsPath);
                UpdateAppSettings(request, jwtAdminKey, jwtApiKey);
                _logger.LogInformation("安装向导: 配置文件更新成功");

                // 7. 生成 Logo、Favicon、app-info.json（未上传 Logo 时按 SB 品牌风格自动生成）
                var storageDir = Path.Combine(_env.ContentRootPath, "wwwroot", "storage");
                if (!string.IsNullOrEmpty(request.LogoBase64))
                {
                    FaviconGenerator.GenerateFromBase64(request.LogoBase64, storageDir);
                    _logger.LogInformation("安装向导: Logo 和 Favicon 已生成");
                }
                else
                {
                    BrandIconGenerator.GenerateAll(storageDir,
                        BrandIconGenerator.DefaultSpec(request.ProductEn, request.ProductCn));
                    _logger.LogInformation("安装向导: 未上传 Logo，已按 SB 品牌风格自动生成全套图标（favicon + brand/）");
                }
                FaviconGenerator.GenerateAppInfo(storageDir, request.ProductEn ?? "sb", request.ProductCn ?? "管理系统", request.PrimaryDomain ?? "");
                _logger.LogInformation("安装向导: app-info.json 已生成");

                // 8. 创建部署项目的 storage 软连接
                var symlinkResults = CreateStorageSymlinks(request.DeployProjects);

                // 9. 创建 installed.lock
                var appVersion = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
                var lockContent = System.Text.Json.JsonSerializer.Serialize(new
                {
                    InstalledAt = AppTime.Now,
                    InstalledBy = request.AdminUsername,
                    Version = appVersion,
                    Modules = modules.Select(m => m.ModuleName).ToArray()
                });
                File.WriteAllText(LockFilePath, lockContent);
                _logger.LogInformation("安装向导: 安装标记文件已创建");

                return Result.Success("安装成功！请重启应用以加载新配置。", new
                {
                    symlinkResults,
                    licenseEnabled = false,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "安装过程中发生错误，开始回滚");

                // 回滚 1: 删除安装过程中新建的表
                if (fsql != null && tablesBeforeSync != null)
                {
                    RollbackCreatedTables(fsql, request.Database.Database, request.Database.DbType, tablesBeforeSync);
                }

                // 回滚 2: 还原 appsettings.json
                if (appSettingsBackup != null)
                {
                    try
                    {
                        var path = AppGlobal.GetAppSettingsPath(_env.ContentRootPath, _env.EnvironmentName);
                        File.WriteAllText(path, appSettingsBackup);
                        _logger.LogInformation("安装向导回滚: 配置文件已还原");
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.LogError(rollbackEx, "安装向导回滚: 还原配置文件失败");
                    }
                }

                // 安装失败，不创建 lock 文件
                return Result.Fail($"安装失败: {ex.Message}");
            }
            finally
            {
                fsql?.Dispose();
            }
        }

        private void SeedData(IFreeSql fsql, SetupRequest request, List<IModuleInitializer> modules)
        {
            // 1. 创建超级管理员角色
            var roleId = EnsureSuperAdminRole(fsql);

            // 2. 创建菜单（从所有模块收集）
            EnsureMenus(fsql, modules, roleId);

            // 3. 创建系统设置（从所有模块收集）
            EnsureSystemSettings(fsql, modules);

            // 4. 执行各模块的种子数据
            foreach (var module in modules)
            {
                module.SeedData(fsql);
                _logger.LogInformation("安装向导: 模块 [{ModuleName}] 种子数据已执行", module.ModuleName);
            }

            // 5. 创建管理员
            EnsureAdmin(fsql, request, roleId);
        }

        #region 种子数据辅助方法

        private int EnsureSuperAdminRole(IFreeSql fsql)
        {
            var existingRole = fsql.Select<AdminRole>()
                .Where(r => r.RoleKey == "super")
                .DisableGlobalFilter("IsDeleted")
                .First();

            if (existingRole != null)
            {
                _logger.LogInformation("安装向导: 检测到已有超级管理员角色，跳过角色创建");
                return existingRole.Id;
            }

            var role = new AdminRole
            {
                Name = "超级管理员",
                RoleKey = "super",
                IsDeleted = false
            };
            var roleId = (int)fsql.Insert(role).ExecuteIdentity();
            _logger.LogInformation("安装向导: 超级管理员角色已创建, Id={RoleId}", roleId);
            return roleId;
        }

        private void EnsureMenus(IFreeSql fsql, List<IModuleInitializer> modules, int roleId)
        {
            var existingMenuCount = fsql.Select<Menu>()
                .DisableGlobalFilter("IsDeleted")
                .Count();

            if (existingMenuCount > 0)
            {
                _logger.LogInformation("安装向导: 检测到已有菜单数据，跳过菜单创建");

                // 确保角色菜单关联存在
                var existingRoleMenuCount = fsql.Select<AdminRoleMenu>()
                    .Where(rm => rm.RoleId == roleId)
                    .Count();
                if (existingRoleMenuCount == 0)
                {
                    var allMenus = fsql.Select<Menu>()
                        .DisableGlobalFilter("IsDeleted")
                        .ToList();
                    var existingRoleMenus = allMenus.Select(m => new AdminRoleMenu
                    {
                        RoleId = roleId,
                        MenuId = m.Id
                    }).ToList();
                    fsql.Insert(existingRoleMenus).ExecuteAffrows();
                }
                return;
            }

            // 收集所有模块的菜单定义
            var allMenuDefs = modules.SelectMany(m => m.GetMenus()).ToList();
            var createdMenuIds = new List<int>();
            var pathToId = new Dictionary<string, int>();

            // Phase 1: 创建顶级菜单及其子菜单（ParentPath == null）
            var topLevelMenus = allMenuDefs.Where(m => m.ParentPath == null).ToList();
            foreach (var def in topLevelMenus)
            {
                var menuId = InsertMenu(fsql, def, 0);
                pathToId[def.Path] = menuId;
                createdMenuIds.Add(menuId);

                foreach (var child in def.Children)
                {
                    var childId = InsertMenu(fsql, child, menuId);
                    pathToId[child.Path] = childId;
                    createdMenuIds.Add(childId);
                }
            }

            // Phase 2: 创建需要挂载到已存在父菜单的菜单（ParentPath != null）
            var attachMenus = allMenuDefs.Where(m => m.ParentPath != null).ToList();
            foreach (var def in attachMenus)
            {
                if (!pathToId.TryGetValue(def.ParentPath!, out var parentId))
                {
                    _logger.LogWarning("安装向导: 菜单 [{Title}] 的父路径 [{ParentPath}] 未找到，跳过", def.Title, def.ParentPath);
                    continue;
                }

                var menuId = InsertMenu(fsql, def, parentId);
                pathToId[def.Path] = menuId;
                createdMenuIds.Add(menuId);

                foreach (var child in def.Children)
                {
                    var childId = InsertMenu(fsql, child, menuId);
                    pathToId[child.Path] = childId;
                    createdMenuIds.Add(childId);
                }
            }

            // Phase 3: 所有菜单关联到超级管理员角色
            var roleMenus = createdMenuIds.Select(mid => new AdminRoleMenu
            {
                RoleId = roleId,
                MenuId = mid
            }).ToList();
            fsql.Insert(roleMenus).ExecuteAffrows();
            _logger.LogInformation("安装向导: 菜单和角色菜单关联已创建 ({Count} 个菜单)", createdMenuIds.Count);
        }

        private static int InsertMenu(IFreeSql fsql, MenuDefinition def, int parentId)
        {
            var menu = new Menu
            {
                ParentId = parentId,
                Name = def.Name,
                Path = def.Path,
                Component = def.Component,
                Title = def.Title,
                Icon = def.Icon,
                OrderNo = def.OrderNo,
                Redirect = def.Redirect ?? "",
                HideInMenu = def.HideInMenu,
                IsDeleted = false,
            };
            return (int)fsql.Insert(menu).ExecuteIdentity();
        }

        private void EnsureSystemSettings(IFreeSql fsql, List<IModuleInitializer> modules)
        {
            var allSettings = modules.SelectMany(m => m.GetSystemSettings()).ToList();
            if (allSettings.Count == 0) return;

            // 按分类分组
            var categoryGroups = allSettings.GroupBy(s => s.CategoryName);

            foreach (var group in categoryGroups)
            {
                // 确保分类存在
                var category = fsql.Select<SystemSettingCategory>()
                    .Where(c => c.Name == group.Key)
                    .First();

                if (category == null)
                {
                    var first = group.First();
                    category = new SystemSettingCategory
                    {
                        Name = group.Key,
                        Icon = first.CategoryIcon,
                        Sort = first.CategorySort,
                    };
                    category.Id = (int)fsql.Insert(category).ExecuteIdentity();
                }

                // 确保每个设置项存在
                foreach (var def in group)
                {
                    var exists = fsql.Select<SystemSetting>()
                        .Where(s => s.Key == def.Key)
                        .Any();

                    if (!exists)
                    {
                        fsql.Insert(new SystemSetting
                        {
                            Key = def.Key,
                            Name = def.Name,
                            CategoryId = category.Id,
                            DisplayType = def.DisplayType,
                            DataContent = def.DefaultValue,
                            DataContentConfig = def.DataContentConfig,
                            CantDelete = def.CantDelete,
                            Sort = def.Sort,
                        }).ExecuteAffrows();
                    }
                }
            }

            _logger.LogInformation("安装向导: 系统设置已初始化 ({Count} 项)", allSettings.Count);
        }

        private void EnsureAdmin(IFreeSql fsql, SetupRequest request, int roleId)
        {
            var existingAdmin = fsql.Select<Admin>()
                .Where(a => a.Username == request.AdminUsername)
                .DisableGlobalFilter("IsDeleted")
                .First();

            if (existingAdmin != null)
            {
                _logger.LogInformation("安装向导: 检测到已有管理员账号，跳过创建");
                return;
            }

            var usePlainText = string.Equals(request.PasswordStorageMode, "PlainText", StringComparison.OrdinalIgnoreCase);
            var adminPassword = usePlainText
                ? request.AdminPassword
                : BCrypt.Net.BCrypt.HashPassword(request.AdminPassword);

            if (usePlainText)
                _logger.LogWarning("安装向导: 使用明文密码存储，不建议用于生产环境");
            else
                _logger.LogInformation("安装向导: 使用加密密码存储");

            var admin = new Admin
            {
                ParentId = 0,
                Username = request.AdminUsername,
                Password = adminPassword,
                Name = request.AdminDisplayName,
                Avatar = "/storage/images/logo.png",
                RoleId = roleId,
                Status = AdminStatus.Active,
                CreatedTime = AppTime.Now,
                IsDeleted = false
            };
            fsql.Insert(admin).ExecuteAffrows();
            _logger.LogInformation("安装向导: 管理员账号已创建");
        }

        #endregion

        private void UpdateAppSettings(SetupRequest request, string jwtAdminKey, string jwtApiKey)
        {
            var appSettingsPath = AppGlobal.GetAppSettingsPath(_env.ContentRootPath, _env.EnvironmentName);
            var json = File.ReadAllText(appSettingsPath);
            var jObj = JsonNode.Parse(json)!.AsObject();

            // 更新 DbConfig
            jObj["DbConfig"] = JsonSerializer.SerializeToNode(new
            {
                DbType = request.Database.DbType ?? "MySql",
                Server = request.Database.Server,
                Port = request.Database.Port,
                Database = request.Database.Database,
                User = request.Database.User,
                Password = request.Database.Password,
                AutoSyncStructure = false,
                MonitorSqlCommandForLog = false,
                SlowQueryThresholdMs = 200
            });

            // 更新 CacheConfig
            if (request.EnableCache)
            {
                jObj["CacheConfig"] = JsonSerializer.SerializeToNode(new
                {
                    Enabled = true,
                    Provider = request.CacheProvider ?? "Memory",
                    Redis = new
                    {
                        Connection = request.Redis?.Connection ?? "localhost:6379",
                        InstanceName = request.CachePrefix ?? $"{request.ProductEn ?? "sb"}:"
                    }
                });
            }
            else
            {
                jObj["CacheConfig"] = JsonSerializer.SerializeToNode(new
                {
                    Enabled = false,
                    Provider = "Memory",
                    Redis = new
                    {
                        Connection = "localhost:6379",
                        InstanceName = request.CachePrefix ?? $"{request.ProductEn ?? "sb"}:"
                    }
                });
            }

            // 更新 ProductCn
            jObj["ProductCn"] = request.ProductCn ?? "管理系统";

            // 更新 ProductEn
            jObj["ProductEn"] = request.ProductEn ?? "sb";

            // 更新 PrimaryDomain
            if (!string.IsNullOrWhiteSpace(request.PrimaryDomain))
                jObj["PrimaryDomain"] = request.PrimaryDomain.Trim();

            // 缓存前缀更新到 Redis InstanceName
            var cachePrefix = request.CachePrefix ?? $"{request.ProductEn ?? "sb"}:";
            if (jObj["CacheConfig"] is JsonObject cacheObj && cacheObj["Redis"] is JsonObject redisObj)
            {
                redisObj["InstanceName"] = cachePrefix;
            }

            // 写入 CloudConfig.ServerUrl（云端中心地址；备份等云端功能共用，只配置一次）
            // 缺 scheme 时自动补 https://，避免后续 HttpClient.PostAsync 因 relative URI 抛 BaseAddress 异常
            var cloudUrl = request.CloudServerUrl?.Trim().TrimEnd('/') ?? "";
            if (!string.IsNullOrEmpty(cloudUrl)
                && !cloudUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !cloudUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                cloudUrl = "https://" + cloudUrl;
            }
            jObj["CloudConfig"] = JsonSerializer.SerializeToNode(new { ServerUrl = cloudUrl });

            // 更新时区
            if (!string.IsNullOrEmpty(request.TimeZone))
                jObj["TimeZone"] = request.TimeZone;

            // 更新 CorsConfig
            jObj["CorsConfig"] = JsonSerializer.SerializeToNode(new
            {
                AllowAll = request.CorsAllowAll,
                Origins = request.CorsOrigins ?? Array.Empty<string>()
            });

            // 更新密码存储配置
            jObj["PasswordConfig"] = JsonSerializer.SerializeToNode(new
            {
                StorageMode = request.PasswordStorageMode ?? "Encrypted"
            });

            // 更新 JWT 密钥 + Issuer/Audience（基于产品英文名）
            var nameEn = request.ProductEn ?? "sb";
            var jwtAdminExpires = (jObj["JwtAdmin"] as JsonObject)?["Expires"]?.ToString() ?? "1440";
            jObj["JwtAdmin"] = JsonSerializer.SerializeToNode(new
            {
                Key = jwtAdminKey,
                Issuer = $"{nameEn}AdminAPI",
                Audience = $"{nameEn}Admin",
                Expires = jwtAdminExpires
            });

            var jwtApiExpires = (jObj["JwtApi"] as JsonObject)?["Expires"]?.ToString() ?? "10080";
            jObj["JwtApi"] = JsonSerializer.SerializeToNode(new
            {
                Key = jwtApiKey,
                Issuer = $"{nameEn}API",
                Audience = $"{nameEn}API",
                Expires = jwtApiExpires
            });

            // 写入 DeployConfig
            if (request.DeployProjects != null && request.DeployProjects.Count > 0)
            {
                jObj["DeployConfig"] = JsonSerializer.SerializeToNode(new
                {
                    Projects = request.DeployProjects.Select(p => new
                    {
                        p.Name,
                        p.Key,
                        p.Path
                    }).ToArray()
                });
            }

            File.WriteAllText(appSettingsPath, jObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            _logger.LogInformation("安装向导: 配置已写入 {Path}", Path.GetFileName(appSettingsPath));
        }

        /// <summary>
        /// 为部署项目创建 storage 软连接
        /// </summary>
        private List<object> CreateStorageSymlinks(List<DeployProjectDto> projects)
        {
            var results = new List<object>();
            if (projects == null || projects.Count == 0) return results;

            var storagePath = Path.Combine(_env.ContentRootPath, "wwwroot", "storage");
            // 确保 storage 目录存在
            Directory.CreateDirectory(storagePath);

            foreach (var project in projects)
            {
                if (string.IsNullOrWhiteSpace(project.Path)) continue;

                var targetLink = Path.Combine(project.Path, "storage");
                try
                {
                    // 如果目标已存在（非软连接），先删除
                    if (Directory.Exists(targetLink) && !File.GetAttributes(targetLink).HasFlag(FileAttributes.ReparsePoint))
                    {
                        Directory.Delete(targetLink, true);
                    }
                    else if (File.Exists(targetLink))
                    {
                        File.Delete(targetLink);
                    }

                    // 执行 ln -s（Linux/macOS）
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ln",
                        Arguments = $"-sfn \"{storagePath}\" \"{targetLink}\"",
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = System.Diagnostics.Process.Start(psi)!;
                    var stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    var success = proc.ExitCode == 0;
                    results.Add(new { project = project.Name, link = targetLink, source = storagePath, success, error = success ? "" : stderr });
                    _logger.LogInformation("安装向导: 软连接 {Link} -> {Source} {Status}", targetLink, storagePath, success ? "成功" : "失败: " + stderr);
                }
                catch (Exception ex)
                {
                    results.Add(new { project = project.Name, link = targetLink, source = storagePath, success = false, error = ex.Message });
                    _logger.LogWarning(ex, "安装向导: 创建软连接失败 {Link}", targetLink);
                }
            }

            return results;
        }

        /// <summary>
        /// 安装时确保数据库存在，不存在则创建
        /// </summary>
        private void EnsureDatabaseExists(DatabaseConfigDto config)
        {
            IFreeSql? testFsql = null;
            try
            {
                var dataType = ParseDataType(config.DbType);
                var connStr = BuildConnectionString(config);
                testFsql = new FreeSql.FreeSqlBuilder()
                    .UseConnectionString(dataType, connStr)
                    .UseAutoSyncStructure(false)
                    .Build();
                testFsql.Ado.ExecuteScalar("SELECT 1");
                // 数据库已存在，无需创建
            }
            catch
            {
                testFsql?.Dispose();
                testFsql = null;

                // 尝试连接服务器并创建数据库
                IFreeSql? serverFsql = null;
                try
                {
                    var dataType = ParseDataType(config.DbType);
                    var serverConnStr = BuildServerConnectionString(config);
                    serverFsql = new FreeSql.FreeSqlBuilder()
                        .UseConnectionString(dataType, serverConnStr)
                        .UseAutoSyncStructure(false)
                        .Build();

                    var dbName = config.Database;
                    if (string.Equals(config.DbType, "PostgreSql", StringComparison.OrdinalIgnoreCase))
                        serverFsql.Ado.ExecuteNonQuery($"CREATE DATABASE \"{dbName}\"");
                    else
                        serverFsql.Ado.ExecuteNonQuery($"CREATE DATABASE IF NOT EXISTS `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci");

                    _logger.LogInformation("安装向导: 数据库 {Database} 已自动创建", dbName);
                }
                finally
                {
                    serverFsql?.Dispose();
                }
            }
            finally
            {
                testFsql?.Dispose();
            }
        }

        /// <summary>
        /// 抹除数据库中所有表
        /// </summary>
        private void WipeAllTables(IFreeSql fsql, string database)
        {
            // 查出所有表名
            var tables = fsql.Ado.ExecuteArray(
                "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = " +
                $"'{database.Replace("'", "''")}'");

            if (tables == null || tables.Length == 0) return;

            // 关闭外键检查后逐个 DROP
            fsql.Ado.ExecuteNonQuery("SET FOREIGN_KEY_CHECKS = 0");
            foreach (var row in tables)
            {
                var tableName = row?[0]?.ToString();
                if (!string.IsNullOrEmpty(tableName))
                {
                    fsql.Ado.ExecuteNonQuery($"DROP TABLE IF EXISTS `{tableName}`");
                    _logger.LogInformation("安装向导: 已删除表 {Table}", tableName);
                }
            }
            fsql.Ado.ExecuteNonQuery("SET FOREIGN_KEY_CHECKS = 1");
        }

        private HashSet<string> GetExistingTableNames(IFreeSql fsql, string database, string dbType)
        {
            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (string.Equals(dbType, "PostgreSql", StringComparison.OrdinalIgnoreCase))
                {
                    var rows = fsql.Ado.ExecuteArray("SELECT tablename FROM pg_tables WHERE schemaname = 'public'");
                    if (rows != null)
                        foreach (var row in rows)
                            if (row?[0] != null) tables.Add(row[0].ToString()!);
                }
                else
                {
                    var rows = fsql.Ado.ExecuteArray(
                        $"SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = '{database.Replace("'", "''")}'");
                    if (rows != null)
                        foreach (var row in rows)
                            if (row?[0] != null) tables.Add(row[0].ToString()!);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "安装向导: 获取已有表列表失败，回滚时将跳过表清理");
            }
            return tables;
        }

        private void RollbackCreatedTables(IFreeSql fsql, string database, string dbType, HashSet<string> tablesBeforeSync)
        {
            var currentTables = GetExistingTableNames(fsql, database, dbType);
            var createdTables = currentTables.Except(tablesBeforeSync, StringComparer.OrdinalIgnoreCase).ToList();

            if (createdTables.Count == 0) return;

            _logger.LogInformation("安装向导回滚: 发现 {Count} 个安装期间新建的表，开始清理", createdTables.Count);

            var isPostgreSql = string.Equals(dbType, "PostgreSql", StringComparison.OrdinalIgnoreCase);
            try
            {
                if (!isPostgreSql)
                    fsql.Ado.ExecuteNonQuery("SET FOREIGN_KEY_CHECKS = 0");

                foreach (var table in createdTables)
                {
                    try
                    {
                        if (isPostgreSql)
                            fsql.Ado.ExecuteNonQuery($"DROP TABLE IF EXISTS \"{table}\" CASCADE");
                        else
                            fsql.Ado.ExecuteNonQuery($"DROP TABLE IF EXISTS `{table}`");
                        _logger.LogInformation("安装向导回滚: 已删除表 {Table}", table);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "安装向导回滚: 删除表 {Table} 失败", table);
                    }
                }

                if (!isPostgreSql)
                    fsql.Ado.ExecuteNonQuery("SET FOREIGN_KEY_CHECKS = 1");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "安装向导回滚: 清理表操作失败");
            }
        }

        private static FreeSql.DataType ParseDataType(string dbType)
        {
            return string.Equals(dbType, "PostgreSql", StringComparison.OrdinalIgnoreCase)
                ? FreeSql.DataType.PostgreSQL
                : FreeSql.DataType.MySql;
        }

        private static string BuildConnectionString(DatabaseConfigDto config)
        {
            if (string.Equals(config.DbType, "PostgreSql", StringComparison.OrdinalIgnoreCase))
                return $"Host={config.Server};Port={config.Port};Username={config.User};Password={config.Password};Database={config.Database};";
            return $"server={config.Server}; port={config.Port}; user id={config.User}; password={config.Password}; database={config.Database}; sslMode=PREFERRED;";
        }

        /// <summary>
        /// 构建不含数据库名的连接字符串（仅连接服务器，用于检测/创建数据库）
        /// </summary>
        private static string BuildServerConnectionString(DatabaseConfigDto config)
        {
            if (string.Equals(config.DbType, "PostgreSql", StringComparison.OrdinalIgnoreCase))
                return $"Host={config.Server};Port={config.Port};Username={config.User};Password={config.Password};Database=postgres;";
            return $"server={config.Server}; port={config.Port}; user id={config.User}; password={config.Password}; sslMode=PREFERRED;";
        }

        private static string GenerateJwtKey()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>按黑名单(ModuleKey 或实现类名,大小写不敏感)剔除模块。null/空 → 原样返回。</summary>
        public static List<IModuleInitializer> FilterDisabled(
            List<IModuleInitializer> modules, IReadOnlyCollection<string>? disabledModuleKeys)
        {
            if (disabledModuleKeys == null || disabledModuleKeys.Count == 0) return modules;
            var ban = new HashSet<string>(disabledModuleKeys, StringComparer.OrdinalIgnoreCase);
            return modules.Where(m =>
                !ban.Contains(m.ModuleKey) && !ban.Contains(m.GetType().Name)).ToList();
        }

        /// <summary>
        /// 发现所有 IModuleInitializer（feature 化主路径）。
        /// 遍历 catalog.AllScanAssemblies（框架Services → feature(短名排序) → 项目），
        /// 同名模块（ModuleName 相同）后扫覆盖先扫（项目排最后 ⇒ 项目覆盖 feature/框架）。
        /// </summary>
        public static List<IModuleInitializer> DiscoverModuleInitializers(
            SB.Framework.Features.FeatureCatalog catalog,
            IReadOnlyCollection<string>? disabledModuleKeys = null)
        {
            var modules = new Dictionary<string, IModuleInitializer>();
            foreach (var asm in catalog.AllScanAssemblies)
                ScanModuleInitializers(modules, asm);
            return FilterDisabled(modules.Values.OrderBy(m => m.InitOrder).ToList(), disabledModuleKeys);
        }

        /// <summary>
        /// 扫描框架和项目程序集，发现所有 IModuleInitializer 实现（旧字符串重载，兼容缝）。
        /// 同名模块（ModuleName 相同）项目版本覆盖框架版本，避免重复初始化。
        /// 新代码一律走 DiscoverModuleInitializers(FeatureCatalog)。
        /// </summary>
        public static List<IModuleInitializer> DiscoverModuleInitializers(
            string? projectAssemblyName = null,
            IReadOnlyCollection<string>? disabledModuleKeys = null)
        {
            // 用 ModuleName 做 key，后扫描的覆盖先扫描的
            var modules = new Dictionary<string, IModuleInitializer>();

            // 1. 扫描框架程序集
            ScanModuleInitializers(modules, Assembly.Load("SB.Framework.Services"));

            // 2. 扫描项目程序集（同名模块覆盖框架版本）
            if (!string.IsNullOrEmpty(projectAssemblyName))
            {
                ScanModuleInitializers(modules, Assembly.Load(projectAssemblyName));
            }

            return FilterDisabled(modules.Values.OrderBy(m => m.InitOrder).ToList(), disabledModuleKeys);
        }

        private static void ScanModuleInitializers(Dictionary<string, IModuleInitializer> modules, Assembly assembly)
        {
            foreach (var type in assembly.ExportedTypes)
            {
                if (!typeof(IModuleInitializer).IsAssignableFrom(type) || type.IsInterface || type.IsAbstract)
                    continue;

                // 使用 GetUninitializedObject 避免要求无参构造函数
                // IModuleInitializer 的方法只返回静态元数据，不依赖注入的实例字段
                IModuleInitializer? instance = null;
                try
                {
                    instance = RuntimeHelpers.GetUninitializedObject(type) as IModuleInitializer;
                }
                catch
                {
                    continue;
                }
                if (instance != null)
                {
                    modules[instance.ModuleName] = instance;
                }
            }
        }
    }
}
