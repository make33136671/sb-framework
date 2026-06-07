using System;
using FreeSql;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Models;
using SB.Framework.Models.Dto;
using SB.Framework.Core;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class MenuService : BaseCrudService<Menu>
    {
        IFreeSql _fsql;
        IHttpContextAccessor _httpContextAccessor;
        ICacheService _cacheService;

        private const string CACHE_KEY_ALL_MENUS = "menu:all:list";
        private const string CACHE_KEY_ADMIN_MENU_PREFIX = "menu:user:"; // 管理员菜单缓存前缀
        private const int CACHE_EXPIRATION_MINUTES = 5;

        public MenuService(IFreeSql fsql, ILogger<MenuService> logger, IHttpContextAccessor httpContextAccessor, ICacheService cacheService) : base(fsql, logger)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
            _cacheService = cacheService;
        }

        /// <summary>
        /// 在内存中构建树结构 (优化: 批量加载而非递归查询)
        /// </summary>
        List<VbenMenu> BuildMenuTree(List<VbenMenu> allMenus, int parentId = 0)
        {
            return allMenus
                .Where(m => m.ParentId == parentId)
                .OrderBy(m => m.OrderNo)
                .Select(m =>
                {
                    m.Children = BuildMenuTree(allMenus, m.Id);
                    return m;
                })
                .ToList();
        }

        public virtual Result GetTreeList(string name, string title, string field, string order, int page, int pageSize)
        {
            var cacheKey = CACHE_KEY_ALL_MENUS;
            var allMenus = _cacheService.Get<List<VbenMenu>>(cacheKey);

            if (allMenus == null)
            {
                allMenus = Select
                    .Include(c => c.ParentMenu)
                    .OrderBy(c => c.OrderNo)
                    .ToList()
                    .Select(c => new VbenMenu(c))
                    .ToList();

                _cacheService.Set(cacheKey, allMenus, TimeSpan.FromMinutes(CACHE_EXPIRATION_MINUTES));
                _logger.LogInformation($"菜单列表加载到缓存，有效期 {CACHE_EXPIRATION_MINUTES} 分钟");
            }
            else
            {
                _logger.LogInformation("菜单列表从缓存读取");
            }

            var filteredMenus = allMenus.AsEnumerable();
            filteredMenus = filteredMenus.WhereIf(!string.IsNullOrEmpty(name), c => c.Name == name);
            filteredMenus = filteredMenus.WhereIf(!string.IsNullOrEmpty(title), c => c.Title == title);

            var rootMenus = filteredMenus.Where(c => c.ParentId == 0).ToList();

            if (!string.IsNullOrEmpty(field))
            {
                var ascending = order == "ascend";
                rootMenus = ascending
                    ? rootMenus.OrderBy(c => GetPropertyValue(c, field)).ToList()
                    : rootMenus.OrderByDescending(c => GetPropertyValue(c, field)).ToList();
            }

            long dataCount = rootMenus.Count;
            var data = rootMenus.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var treeData = data.Select(m =>
            {
                m.Children = BuildMenuTree(allMenus, m.Id);
                return m;
            }).ToList();

            return Result.Page(treeData, dataCount);
        }

        private object GetPropertyValue(VbenMenu menu, string propertyName)
        {
            return propertyName.ToLower() switch
            {
                "id" => menu.Id,
                "name" => menu.Name ?? "",
                "title" => menu.Title ?? "",
                "orderno" => menu.OrderNo,
                _ => menu.Id
            };
        }

        /// <summary>
        /// 获取权限菜单列表
        /// </summary>
        public virtual Result GetList(string name = "", string title = "", string field = "", string order = "", int page = 1, int pageSize = 999)
        {
            var query = Select.Include(c => c.ParentMenu);

            query = query.WhereIf(!string.IsNullOrEmpty(name), c => c.Name == name);
            query = query.WhereIf(!string.IsNullOrEmpty(title), c => c.Title == title);

            query = ApplySorting(query, field, order);

            var data = query.Count(out long dataCount).Page(page, pageSize).ToList();

            return Result.Page(data, dataCount);
        }

        /// <summary>
        /// 获取权限菜单列表
        /// </summary>
        public virtual Result QueryMenu(string key = "")
        {
            var query = Select.Include(c => c.ParentMenu);
            var data = query.OrderBy(c => c.Id).Count(out long dataCount).ToList();

            return Result.Page(data, dataCount);
        }

        /// <summary>
        /// 创建权限菜单 (自动清除菜单树缓存)
        /// </summary>
        public virtual Result Create(Menu menu)
        {
            menu = this.Insert(menu);

            // 清除菜单缓存和所有管理员的菜单权限缓存
            _cacheService.Remove(CACHE_KEY_ALL_MENUS);
            _cacheService.RemoveByPrefix(CACHE_KEY_ADMIN_MENU_PREFIX);
            _logger.LogInformation($"菜单缓存已清除 (菜单创建: {menu.Title})");

            return Result.Success("权限菜单添加成功!", menu);
        }

        /// <summary>
        /// 更新权限菜单 (自动清除菜单树缓存)
        /// </summary>
        public virtual Result Edit(Menu menu)
        {
            int count = this.UpdateDiy.UpdateColumns(c => new
            {
                c.ParentId,
                c.Name,
                c.Component,
                c.Path,
                c.Redirect,
                c.Title,
                c.Icon,
                c.OrderNo,
                c.HideInMenu,
                c.IgnoreKeepAlive,
            }).SetSource(menu).ExecuteAffrows();

            if (count <= 0)
            {
                return Result.Fail("权限菜单更新失败!");
            }

            // 清除菜单缓存和所有管理员的菜单权限缓存
            _cacheService.Remove(CACHE_KEY_ALL_MENUS);
            _cacheService.RemoveByPrefix(CACHE_KEY_ADMIN_MENU_PREFIX);
            _logger.LogInformation($"菜单缓存已清除 (菜单更新: {menu.Title})");

            return Result.Success("权限菜单更新成功!", menu);
        }

        /// <summary>
        /// 删除权限菜单（级联删除子菜单） — override 基类，需要清缓存
        /// </summary>
        public override Result Del(int id)
        {
            Menu menu = this.Select.Where(c => c.Id == id).First();
            if (menu == null)
            {
                return Result.Fail("权限菜单不存在,删除失败!");
            }

            // 获取所有子菜单（包括嵌套的子菜单）
            var childMenuIds = GetAllChildMenuIds(id);
            childMenuIds.Add(id); // 包含当前菜单

            // 批量更新 IsDeleted 为 true
            int count = this.UpdateDiy
                .Where(c => childMenuIds.Contains(c.Id))
                .Set(c => c.IsDeleted, true)
                .ExecuteAffrows();

            if (count <= 0)
            {
                return Result.Fail("权限菜单删除失败!");
            }

            // 清除菜单缓存和所有管理员的菜单权限缓存
            _cacheService.Remove(CACHE_KEY_ALL_MENUS);
            _cacheService.RemoveByPrefix(CACHE_KEY_ADMIN_MENU_PREFIX);
            _logger.LogInformation($"菜单缓存已清除 (菜单删除: ID={id}, 共删除 {count} 条记录)");

            return Result.Success($"删除权限菜单成功! (共删除 {count} 条记录)");
        }

        /// <summary>
        /// 递归获取所有子菜单 ID
        /// </summary>
        private List<int> GetAllChildMenuIds(int parentId)
        {
            var childIds = new List<int>();
            var children = this.Select.Where(c => c.ParentId == parentId).ToList();

            foreach (var child in children)
            {
                childIds.Add(child.Id);
                // 递归获取孙菜单
                childIds.AddRange(GetAllChildMenuIds(child.Id));
            }

            return childIds;
        }

        /// <summary>
        /// 查询权限菜单详情 — override 基类，需要 Include ParentMenu
        /// </summary>
        public override Result Detail(int id)
        {
            var query = this.Select.Include(c => c.ParentMenu);
            Menu menu = query.Where(c => c.Id == id).First();

            if (menu == null)
            {
                return Result.Fail("查询的权限菜单不存在,请确认操作是否正确!");
            }

            return Result.Success(menu);
        }

        /// <summary>
        /// 改变权限菜单显示状态
        /// </summary>
        public virtual Result ChangeShow(int id)
        {
            Menu menu = this.Select.Where(c => c.Id == id).First();

            if (menu == null)
            {
                return Result.Fail("查询的权限菜单不存在,请确认操作是否正确!");
            }

            return Result.Success(menu);
        }

        /// <summary>
        /// 修改菜单属性 (自动清除菜单树缓存)
        /// </summary>
        public virtual Result UpdateMenu(int id, string field, string val)
        {
            Menu menu = this.Select.Where(c => c.Id == id).First();

            if (menu == null)
            {
                return Result.Fail("菜单不存在,请确认操作是否正确!");
            }

            switch (field)
            {
                case "name":
                    menu.Name = val;
                    break;

                case "hideInMenu":
                    bool.TryParse(val, out bool hideInMenu);
                    menu.HideInMenu = hideInMenu;
                    break;

                case "ignoreKeepAlive":
                    bool.TryParse(val, out bool ignoreKeepAlive);
                    menu.IgnoreKeepAlive = ignoreKeepAlive;
                    break;

                default:
                    return Result.Fail($"不支持的字段: {field}");
            }

            int count = this.UpdateDiy.SetSource(menu).ExecuteAffrows();

            if (count <= 0)
            {
                return Result.Fail("菜单更新失败!");
            }

            // 清除菜单缓存和所有管理员的菜单权限缓存
            _cacheService.Remove(CACHE_KEY_ALL_MENUS);
            _cacheService.RemoveByPrefix(CACHE_KEY_ADMIN_MENU_PREFIX);
            _logger.LogInformation($"菜单缓存已清除 (菜单属性更新: Id={id}, Field={field})");

            return Result.Success(menu);
        }

        /// <summary>
        /// 通过用户Id 查询默认菜单路由路径
        /// </summary>
        public virtual string GetDefaultMenupathByUserId(int userId)
        {
            try
            {
                Menu menu = Select.Where(c => !c.HideInMenu).OrderBy(c => c.Id).ToOne();

                if (menu == null)
                {
                    _logger.LogWarning("用户 {UserId} 没有默认路由菜单，返回默认值 /profile", userId);
                    return "/profile";
                }

                if (menu.Path == "/" || string.IsNullOrEmpty(menu.Path))
                {
                    _logger.LogWarning("用户 {UserId} 的菜单路径为根路径或空，返回默认值 /profile", userId);
                    return "/profile";
                }

                return menu.Path;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "用户 {UserId} 获取菜单路径异常，返回默认值 /profile", userId);
                return "/profile";
            }
        }
    }
}
