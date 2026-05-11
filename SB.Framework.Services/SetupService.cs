using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using SB.Framework.Core;
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
        /// 推断 admin / h5 部署路径
        /// 规则：取 API 的 ContentRoot 最后一段（如 sb_test_api），
        /// 去掉 _api 后缀，与同级 *_admin / *_h5 拼装
        /// 推断不出（如开发环境 SB.API）则返回空字符串，让用户手填
        /// </summary>
        public Result GetDeployHints()
        {
            var apiPath = _env.ContentRootPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? "";
            var folderName = Path.GetFileName(apiPath);
            var parentDir = Path.GetDirectoryName(apiPath) ?? "";
            string adminPath = "", h5Path = "";
            if (!string.IsNullOrEmpty(folderName) && folderName.EndsWith("_api", StringComparison.OrdinalIgnoreCase))
            {
                var prefix = folderName[..^"_api".Length];
                adminPath = Path.Combine(parentDir, prefix + "_admin");
                h5Path = Path.Combine(parentDir, prefix + "_h5");
            }
            return Result.Success(new { apiPath, adminPath, h5Path });
        }

        /// <summary>
        /// 测试数据库连接
        /// </summary>
        public Result TestDatabaseConnection(DatabaseConfigDto config)
        {
            IFreeSql fsql = null;
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
            IFreeSql serverFsql = null;
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

            IFreeSql fsql = null;
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
                    fsql.CodeFirst.SyncStructure(entityTypes);
                    _logger.LogInformation("安装向导: 数据表创建/同步成功 ({Count} 个实体)", entityTypes.Length);
                }

                // 5. 种子数据
                SeedData(fsql, request, modules);
                _logger.LogInformation("安装向导: 种子数据写入成功");

                // 6. 更新 appsettings.json
                var jwtAdminKey = GenerateJwtKey();
                var jwtApiKey = GenerateJwtKey();
                // EnableMasterKey=false 时写空字符串，X-Master-Key 通道关闭
                var masterKey = request.EnableMasterKey ? Guid.NewGuid().ToString("N") : string.Empty;
                UpdateAppSettings(request, jwtAdminKey, jwtApiKey, masterKey);
                _logger.LogInformation("安装向导: 配置文件更新成功");

                // 7. 生成 Logo、Favicon、app-info.json
                var storageDir = Path.Combine(_env.ContentRootPath, "wwwroot", "storage");
                if (!string.IsNullOrEmpty(request.LogoBase64))
                {
                    FaviconGenerator.GenerateFromBase64(request.LogoBase64, storageDir);
                    _logger.LogInformation("安装向导: Logo 和 Favicon 已生成");
                }
                FaviconGenerator.GenerateAppInfo(storageDir, request.ProductEn ?? "sb", request.ProductCn ?? "管理系统", request.PrimaryDomain ?? "");
                _logger.LogInformation("安装向导: app-info.json 已生成");

                // 8. 创建部署项目的 storage 软连接
                var symlinkResults = CreateStorageSymlinks(request.DeployProjects);

                // 9. 创建 installed.lock
                var lockContent = System.Text.Json.JsonSerializer.Serialize(new
                {
                    InstalledAt = AppTime.Now,
                    InstalledBy = request.AdminUsername,
                    Version = "1.0.0",
                    Modules = modules.Select(m => m.ModuleName).ToArray()
                });
                File.WriteAllText(LockFilePath, lockContent);
                _logger.LogInformation("安装向导: 安装标记文件已创建");

                var licenseEnabled =
#if !DISABLE_LICENSE
                    true;
#else
                    false;
#endif

                // EnableMasterKey=false 时不返回 masterKey 给前端，前端据此不显示 MasterKey 区块
                return Result.Success("安装成功！请重启应用以加载新配置。", new
                {
                    masterKey = request.EnableMasterKey ? masterKey : null,
                    enableMasterKey = request.EnableMasterKey,
                    symlinkResults,
                    licenseEnabled,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "安装过程中发生错误");
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
                Avatar = "/storage/images/Logo.png",
                RoleId = roleId,
                Status = AdminStatus.Active,
                CreatedTime = AppTime.Now,
                IsDeleted = false
            };
            fsql.Insert(admin).ExecuteAffrows();
            _logger.LogInformation("安装向导: 管理员账号已创建");
        }

        #endregion

        private void UpdateAppSettings(SetupRequest request, string jwtAdminKey, string jwtApiKey, string masterKey)
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

            // 清空旧授权 Key，避免残留（授权服务器地址已统一到 CloudConfig.ServerUrl）
            jObj["LicenseConfig"] = JsonSerializer.SerializeToNode(new { Key = "" });

            // 写入 CloudConfig.ServerUrl（云端中心地址；授权 / 备份 等所有云端功能共用，只配置一次）
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

            // 写入 MasterKey
            jObj["MasterKey"] = masterKey;

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
            IFreeSql testFsql = null;
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
                IFreeSql serverFsql = null;
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

        /// <summary>
        /// 扫描框架和项目程序集，发现所有 IModuleInitializer 实现。
        /// 同名模块（ModuleName 相同）项目版本覆盖框架版本，避免重复初始化。
        /// </summary>
        public static List<IModuleInitializer> DiscoverModuleInitializers(string? projectAssemblyName = null)
        {
            // 用 ModuleName 做 key，后扫描的覆盖先扫描的
            var modules = new Dictionary<string, IModuleInitializer>();

            // 1. 扫描框架程序集
            ScanModuleInitializers(modules, "SB.Framework.Services");

            // 2. 扫描项目程序集（同名模块覆盖框架版本）
            if (!string.IsNullOrEmpty(projectAssemblyName))
            {
                ScanModuleInitializers(modules, projectAssemblyName);
            }

            return modules.Values.OrderBy(m => m.InitOrder).ToList();
        }

        private static void ScanModuleInitializers(Dictionary<string, IModuleInitializer> modules, string assemblyName)
        {
            var assembly = Assembly.Load(assemblyName);

            foreach (var type in assembly.ExportedTypes)
            {
                if (!typeof(IModuleInitializer).IsAssignableFrom(type) || type.IsInterface || type.IsAbstract)
                    continue;

                var instance = RuntimeHelpers.GetUninitializedObject(type) as IModuleInitializer;
                if (instance != null)
                {
                    modules[instance.ModuleName] = instance;
                }
            }
        }
    }
}
