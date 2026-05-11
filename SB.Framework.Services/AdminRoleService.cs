using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;
using SB.Framework.Core.FreeSql;
using System;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class AdminRoleService : BaseCrudService<AdminRole>
    {
        IFreeSql _fsql;
        IHttpContextAccessor _httpContextAccessor;
        public AdminRoleService(IFreeSql fsql, ILogger<AdminRoleService> logger, IHttpContextAccessor httpContextAccessor) : base(fsql, logger)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
        }

        public virtual Result GetList(string name = "", string roleKey = "", string field = "", string order = "", int page = 1, int pageSize = 999)
        {
            // 构建基础查询
            var query = Select;
            query = query.WhereIf(!string.IsNullOrEmpty(name), c => c.Name == name);
            query = query.WhereIf(!string.IsNullOrEmpty(roleKey), c => c.RoleKey == roleKey);

            query = ApplySorting(query, field, order);

            // 获取分页数据总数
            long dataCount = query.Count();
            var roles = query.Page(page, pageSize).ToList();

            // 优化: 使用 JOIN 替代循环查询，避免 N+1 问题
            if (!roles.Any())
            {
                return Result.Page(new List<object>(), dataCount);
            }

            var roleIds = roles.Select(r => r.Id).ToList();

            // 单次查询获取所有菜单信息
            var roleMenuData = _fsql.Select<AdminRole>()
                .Where(r => roleIds.Contains(r.Id))
                .IncludeMany(r => r.RoleMenu)
                .ToList()
                .GroupBy(x => x.Id)
                .Select(g => new
                {
                    RoleId = g.Key,
                    MenuCount = g.FirstOrDefault()?.RoleMenu?.Count ?? 0,
                    MenuIds = g.FirstOrDefault()?.RoleMenu?.Select(rm => rm.MenuId).Distinct().ToList() ?? new List<int>()
                })
                .ToDictionary(x => x.RoleId);

            // 获取所有菜单的名称映射（仅顶级菜单）
            var allMenus = _fsql.Select<Menu>()
                .Where(m => m.ParentId == 0)  // 只获取顶级菜单
                .ToList();

            var menuNameDict = allMenus.ToDictionary(m => m.Id, m => m.Title);

            // 构建响应数据
            var data = roles.Select(role => {
                var menuInfo = roleMenuData.TryGetValue(role.Id, out var info)
                    ? info
                    : new { RoleId = role.Id, MenuCount = 0, MenuIds = new List<int>() };

                // 将菜单 ID 映射为菜单名称
                var topMenuNames = menuInfo.MenuIds
                    .Where(id => menuNameDict.ContainsKey(id))
                    .Select(id => menuNameDict[id])
                    .ToList();

                var topMenusStr = string.Join(", ", topMenuNames);

                return new
                {
                    role.Id,
                    role.Name,
                    role.RoleKey,
                    MenuCount = menuInfo.MenuCount,
                    TopMenus = topMenusStr
                };
            }).ToList();

            return Result.Page(data, dataCount);
        }

        public virtual Result QueryAdminRole(string key = "")
        {
            var query = Select;
            var data = query.OrderBy(c => c.Id).Count(out long dataCount).ToList();
            return Result.Page(data, dataCount);
        }

        public virtual Result Create(AdminRole role)
        {
            try
            {
                var result = this.ExecuteInTransaction(uow =>
                {
                    if (this.Any(c => c.Name == role.Name))
                        return Result.Fail("名称已存在!");

                    if (this.Any(c => c.RoleKey == role.RoleKey))
                        return Result.Fail("角色Key已存在!");

                    role = this.Insert(role);
                    return Result.Success("管理员角色添加成功!", role);
                }, _logger);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"添加角色失败，数据操作异常，异常信息:{ex.Message}");
                return Result.Fail("添加角色失败，数据操作异常!");
            }
        }

        public virtual Result Edit(AdminRole adminRole)
        {
            int count = this.UpdateDiy.UpdateColumns(c => new
            {
                c.Name,
                c.RoleKey,
            }).SetSource(adminRole).ExecuteAffrows();

            if (count <= 0)
            {
                return Result.Fail("管理员角色更新失败!");
            }

            return Result.Success("管理员角色更新成功!", adminRole);
        }

        /// <summary>
        /// 删除管理员角色 — override 基类，需要事务 + 业务规则
        /// </summary>
        public override Result Del(int id)
        {
            if (id == 1)
            {
                return Result.Fail("超级管理员角色不允许删除!");
            }

            try
            {
                var result = this.ExecuteInTransaction(uow =>
                {
                    AdminRole role = this.Select.Where(c => c.Id == id).ForUpdate().First();

                    if (role == null)
                        return Result.Fail("管理员角色不存在,删除失败!");

                    role.IsDeleted = true;
                    int count = this.UpdateDiy.UpdateColumns(c => new { c.IsDeleted })
                        .SetSource(role).ExecuteAffrows();

                    if (count <= 0)
                        throw new Exception("角色伪删除失败!");

                    return Result.Success("删除管理员角色成功!");
                }, _logger);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"删除管理员角色失败: {ex.Message}");
                return Result.Fail("删除管理员角色失败，数据操作异常!");
            }
        }

        public virtual Result ChangeShow(int id)
        {
            AdminRole adminRole = this.Select.Where(c => c.Id == id).First();

            if (adminRole == null)
            {
                return Result.Fail("查询的管理员角色不存在,请确认操作是否正确!");
            }

            return Result.Success(adminRole);
        }
    }
}
