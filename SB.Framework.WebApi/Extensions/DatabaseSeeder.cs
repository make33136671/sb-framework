using FreeSql;
using Microsoft.Extensions.Configuration;
using SB.Framework.Models;
using SB.Framework.Services;
using SB.Framework.Services.Startup;
using Serilog;

namespace SB.Framework.WebApi.Extensions;

/// <summary>
/// 数据库启动同步器 — 纯调度，不包含任何模块数据
/// 各模块数据通过 IModuleInitializer 声明（菜单、系统设置、种子数据）
/// </summary>
public static class DatabaseSeeder
{
    public static void SeedDatabase(this WebApplication app)
    {
        var fsql = app.Services.GetRequiredService<IFreeSql>();
        // feature 化后：模块发现统一走 FeatureRuntime.Catalog（框架Services → 各feature → 项目程序集），
        // 不再依赖单个 ProjectAssemblyName。catalog 在 AddSbFramework 的 [F-1] 已构建。
        var catalog = SB.Framework.Features.FeatureRuntime.Catalog;

        var config = app.Services.GetRequiredService<IConfiguration>();
        var disabledModules = config.GetSection("Modules:Disabled").Get<string[]>() ?? Array.Empty<string>();

        try
        {
            // 从各模块 IModuleInitializer 同步表结构和声明式数据
            SyncModuleEntityTypes(fsql, catalog, disabledModules);
            SyncModuleMenus(fsql, catalog, disabledModules);
            SyncModuleSystemSettings(fsql, catalog, disabledModules);
            SyncModuleSeedData(fsql, catalog, disabledModules);

            // 应用 IFreeSqlConfigurator（基座 + 派生）
            app.Services.ApplyFreeSqlConfigurators();

            // 执行所有 IStartupSyncHook
            StartupSyncRunner.RunAllAsync(app.Services).GetAwaiter().GetResult();

            Log.Debug("数据库启动同步完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "数据库启动同步失败");
        }
    }

    /// <summary>
    /// 从各模块 IModuleInitializer.GetEntityTypes() 收集实体类型，自动同步表结构
    /// </summary>
    private static void SyncModuleEntityTypes(IFreeSql fsql, SB.Framework.Features.FeatureCatalog catalog, string[] disabledModules)
    {
        var modules = SetupService.DiscoverModuleInitializers(catalog, disabledModules);
        var entityTypes = modules
            .SelectMany(m => m.GetEntityTypes())
            .Distinct()
            .ToArray();

        if (entityTypes.Length == 0) return;

        try
        {
            fsql.CodeFirst.SyncStructure(entityTypes);
            Log.Information("模块表结构同步完成，共 {Count} 个实体", entityTypes.Length);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "模块表结构同步失败");
        }
    }

    /// <summary>
    /// 从各模块 IModuleInitializer.GetMenus() 收集菜单声明，自动同步到数据库
    /// </summary>
    public static (int created, int updated) SyncModuleMenus(IFreeSql fsql, SB.Framework.Features.FeatureCatalog catalog, string[]? disabledModules = null)
    {
        disabledModules ??= Array.Empty<string>();
        var modules = SetupService.DiscoverModuleInitializers(catalog, disabledModules);
        var allMenuDefs = modules.SelectMany(m => m.GetMenus()).ToList();
        if (allMenuDefs.Count == 0) return (0, 0);

        var existingMenus = fsql.Select<Menu>().DisableGlobalFilter().ToList();

        // 自愈:Path 唯一性 — 历史遗留可能在 SB_Menu 出现同 Path 多条记录(早期 seed 逻辑缺失 dedup
        // 校验,或并发启动导致),会让下面 ToDictionary 直接抛 ArgumentException 整个 seed 回滚。
        // 这里发现重复就物理删多余,保留 id 最小的一条 + 关联 AdminRoleMenu。
        var duplicatePathGroups = existingMenus
            .GroupBy(m => m.Path)
            .Where(g => g.Count() > 1)
            .ToList();
        if (duplicatePathGroups.Count > 0)
        {
            foreach (var grp in duplicatePathGroups)
            {
                var keeper = grp.OrderBy(m => m.Id).First();
                var removeIds = grp.Where(m => m.Id != keeper.Id).Select(m => m.Id).ToList();
                fsql.Delete<AdminRoleMenu>().Where(rm => removeIds.Contains(rm.MenuId)).ExecuteAffrows();
                fsql.Delete<Menu>().Where(m => removeIds.Contains(m.Id)).ExecuteAffrows();
                Log.Warning(
                    "菜单同步自愈: Path [{Path}] 在 SB_Menu 出现 {Count} 条重复, 保留 id={KeptId}, 删除 id={RemovedIds}",
                    grp.Key, grp.Count(), keeper.Id, string.Join(",", removeIds));
            }
            existingMenus = fsql.Select<Menu>().DisableGlobalFilter().ToList();
        }

        var pathToMenu = existingMenus.ToDictionary(m => m.Path, m => m);
        var pathToId = existingMenus.ToDictionary(m => m.Path, m => m.Id);

        var superRoleId = fsql.Select<AdminRole>().Where(r => r.RoleKey == "super").First()?.Id ?? 1;
        var existingRoleMenuIds = new HashSet<int>(
            fsql.Select<AdminRoleMenu>().Where(rm => rm.RoleId == superRoleId).ToList(rm => rm.MenuId));

        int created = 0, updated = 0;

        // Phase 1: 顶级菜单（ParentPath == null）
        foreach (var def in allMenuDefs.Where(m => m.ParentPath == null))
            EnsureMenuRecursive(fsql, def, 0, pathToMenu, pathToId, superRoleId, existingRoleMenuIds, ref created, ref updated);

        // Phase 2: 挂载到已有父菜单的菜单（ParentPath != null）
        foreach (var def in allMenuDefs.Where(m => m.ParentPath != null))
        {
            if (!pathToId.TryGetValue(def.ParentPath!, out var parentId))
            {
                Log.Warning("菜单同步: [{Title}] 的父路径 [{ParentPath}] 未找到，跳过", def.Title, def.ParentPath);
                continue;
            }
            EnsureMenuRecursive(fsql, def, parentId, pathToMenu, pathToId, superRoleId, existingRoleMenuIds, ref created, ref updated);
        }

        if (created > 0 || updated > 0)
            Log.Information("菜单同步完成，新增 {Created} 个，更新 {Updated} 个", created, updated);

        return (created, updated);
    }

    private static int EnsureMenu(IFreeSql fsql, MenuDefinition def, int parentId,
        Dictionary<string, Menu> pathToMenu, Dictionary<string, int> pathToId,
        int superRoleId, HashSet<int> existingRoleMenuIds, ref int created, ref int updated)
    {
        if (pathToMenu.TryGetValue(def.Path, out var existing))
        {
            var redirect = def.Redirect ?? "";
            if (existing.Name != def.Name || existing.Component != def.Component ||
                existing.Title != def.Title || existing.Icon != def.Icon ||
                existing.OrderNo != def.OrderNo || existing.Redirect != redirect ||
                existing.HideInMenu != def.HideInMenu || existing.ParentId != parentId)
            {
                fsql.Update<Menu>()
                    .Where(m => m.Id == existing.Id)
                    .Set(m => m.ParentId, parentId)
                    .Set(m => m.Name, def.Name)
                    .Set(m => m.Component, def.Component)
                    .Set(m => m.Title, def.Title)
                    .Set(m => m.Icon, def.Icon)
                    .Set(m => m.OrderNo, def.OrderNo)
                    .Set(m => m.Redirect, redirect)
                    .Set(m => m.HideInMenu, def.HideInMenu)
                    .ExecuteAffrows();
                updated++;
                Log.Information("菜单同步: 更新 [{Title}] (Path={Path})", def.Title, def.Path);
            }

            if (!existingRoleMenuIds.Contains(existing.Id))
            {
                fsql.Insert(new AdminRoleMenu { RoleId = superRoleId, MenuId = existing.Id }).ExecuteAffrows();
                existingRoleMenuIds.Add(existing.Id);
            }
            return existing.Id;
        }

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
        var menuId = (int)fsql.Insert(menu).ExecuteIdentity();

        pathToMenu[def.Path] = menu;
        pathToId[def.Path] = menuId;
        created++;

        fsql.Insert(new AdminRoleMenu { RoleId = superRoleId, MenuId = menuId }).ExecuteAffrows();
        existingRoleMenuIds.Add(menuId);

        Log.Information("菜单同步: 新增 [{Title}] (Path={Path})", def.Title, def.Path);
        return menuId;
    }

    private static void EnsureMenuRecursive(IFreeSql fsql, MenuDefinition def, int parentId,
        Dictionary<string, Menu> pathToMenu, Dictionary<string, int> pathToId,
        int superRoleId, HashSet<int> existingRoleMenuIds, ref int created, ref int updated)
    {
        var menuId = EnsureMenu(fsql, def, parentId, pathToMenu, pathToId, superRoleId, existingRoleMenuIds, ref created, ref updated);
        foreach (var child in def.Children)
            EnsureMenuRecursive(fsql, child, menuId, pathToMenu, pathToId, superRoleId, existingRoleMenuIds, ref created, ref updated);
    }

    /// <summary>
    /// 从各模块 IModuleInitializer.GetSystemSettings() 收集设置声明，自动同步缺失项到数据库
    /// </summary>
    public static int SyncModuleSystemSettings(IFreeSql fsql, SB.Framework.Features.FeatureCatalog catalog, string[]? disabledModules = null)
    {
        disabledModules ??= Array.Empty<string>();
        var modules = SetupService.DiscoverModuleInitializers(catalog, disabledModules);
        var allDefs = modules.SelectMany(m => m.GetSystemSettings()).ToList();
        if (allDefs.Count == 0) return 0;

        var existingKeys = new HashSet<string>(
            fsql.Select<SystemSetting>().ToList(s => s.Key));

        int created = 0;

        foreach (var group in allDefs.GroupBy(s => s.CategoryName))
        {
            var category = fsql.Select<SystemSettingCategory>()
                .Where(c => c.Name == group.Key).First();

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
                Log.Information("系统设置同步: 新增分类 [{Name}]", group.Key);
            }

            foreach (var def in group)
            {
                if (existingKeys.Contains(def.Key))
                {
                    // 同步 DataContentConfig（如 Select 下拉选项），代码更新后数据库自动跟进
                    if (!string.IsNullOrEmpty(def.DataContentConfig))
                    {
                        fsql.Update<SystemSetting>()
                            .Set(s => s.DataContentConfig, def.DataContentConfig)
                            .Where(s => s.Key == def.Key && s.DataContentConfig != def.DataContentConfig)
                            .ExecuteAffrows();
                    }
                    // 同步 DisplayType（如 Input→Switch），否则存量行会一直停留在旧的显示类型，
                    // 导致 bool 项明明定义成 Switch 却仍渲染成文本框（数据漂移）。
                    fsql.Update<SystemSetting>()
                        .Set(s => s.DisplayType, def.DisplayType)
                        .Where(s => s.Key == def.Key && s.DisplayType != def.DisplayType)
                        .ExecuteAffrows();
                    continue;
                }

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

                existingKeys.Add(def.Key);
                created++;
                Log.Information("系统设置同步: 新增 [{Name}] (Key={Key})", def.Name, def.Key);
            }
        }

        if (created > 0)
            Log.Information("系统设置同步完成，新增 {Count} 项", created);
        return created;
    }

    /// <summary>
    /// 执行各模块的 SeedData（幂等，每次启动都安全运行）
    /// </summary>
    private static void SyncModuleSeedData(IFreeSql fsql, SB.Framework.Features.FeatureCatalog catalog, string[] disabledModules)
    {
        var modules = SetupService.DiscoverModuleInitializers(catalog, disabledModules);
        foreach (var module in modules)
        {
            try
            {
                module.SeedData(fsql);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "模块 [{ModuleName}] SeedData 执行失败", module.ModuleName);
            }
        }
    }
}
