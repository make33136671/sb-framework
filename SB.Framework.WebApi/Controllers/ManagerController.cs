using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using SB.Framework.Models;
using SB.Framework.Services;
using System.Collections.Generic;

namespace SB.Framework.WebApi.Controllers
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class ManagerController : Controller
    {
        private readonly ILogger<ManagerController> _logger;
        private readonly AdminService _adminService;
        private readonly AdminRoleService _adminRoleService;
        private readonly AdminRoleMenuService _roleMenuService;

        public ManagerController(ILogger<ManagerController> logger,
            AdminService adminService,
            AdminRoleService adminRoleService,
            AdminRoleMenuService roleMenuService)
        {
            _logger = logger;
            _adminService = adminService;
            _adminRoleService = adminRoleService;
            _roleMenuService = roleMenuService;
        }

        #region 管理员

        /// <summary>
        /// 获取管理员列表
        /// </summary>
        [HttpGet]
        public Result List(string username = "", string name = "", int? roleId = null, string field = "", string order = "", int page = 1, int pageSize = 10)
        {
            return _adminService.GetList(username, name, roleId, field, order, page, pageSize);
        }

        /// <summary>
        /// 添加管理员
        /// </summary>
        [HttpPost]
        public Result Add([FromForm] Admin admin)
        {
            return _adminService.Create(admin);
        }

        /// <summary>
        /// 编辑管理员
        /// </summary>
        [HttpPost]
        public Result Edit([FromForm] Admin admin)
        {
            return _adminService.Edit(admin);
        }

        /// <summary>
        /// 删除管理员
        /// </summary>
        [HttpPost]
        public Result Del([FromForm] int id)
        {
            return _adminService.Del(id);
        }

        /// <summary>
        /// 获取管理员详情
        /// </summary>
        [HttpGet]
        public Result Detail(int id)
        {
            return _adminService.Detail(id);
        }

        /// <summary>
        /// 超级管理员重置他人谷歌验证器
        /// </summary>
        [HttpPost]
        public Result ResetGoogleCode([FromForm] int adminId)
        {
            return _adminService.ResetGoogleCode(adminId);
        }

        #endregion

        #region 角色

        /// <summary>
        /// 获取角色列表
        /// </summary>
        [HttpGet]
        public Result RoleList(string name = "", string roleKey = "", string field = "", string order = "", int page = 1, int pageSize = 10)
        {
            return _adminRoleService.GetList(name, roleKey, field, order, page, pageSize);
        }

        /// <summary>
        /// 查询角色
        /// </summary>
        [HttpGet]
        public Result QueryAdminRole(string key) => _adminRoleService.QueryAdminRole(key);

        /// <summary>
        /// 添加角色
        /// </summary>
        [HttpPost]
        public Result RoleAdd([FromForm] AdminRole adminRole)
        {
            return _adminRoleService.Create(adminRole);
        }

        /// <summary>
        /// 编辑角色
        /// </summary>
        [HttpPost]
        public Result RoleEdit([FromForm] AdminRole adminRole)
        {
            return _adminRoleService.Edit(adminRole);
        }

        /// <summary>
        /// 删除角色
        /// </summary>
        [HttpPost]
        public Result RoleDel([FromForm] int id)
        {
            return _adminRoleService.Del(id);
        }

        /// <summary>
        /// 获取角色详情
        /// </summary>
        [HttpGet]
        public Result RoleDetail(int id)
        {
            return _adminRoleService.Detail(id);
        }

        #endregion

        #region 角色菜单权限

        /// <summary>
        /// 获取角色的菜单ID列表
        /// </summary>
        [HttpGet]
        public Result GetMenuIds(int roleId)
        {
            return _roleMenuService.GetMenuIdsByRoleId(roleId);
        }

        /// <summary>
        /// 获取角色的菜单详情
        /// </summary>
        [HttpGet]
        public Result GetMenusDetail(int roleId)
        {
            return _roleMenuService.GetRoleMenusWithDetail(roleId);
        }

        /// <summary>
        /// 批量设置角色的菜单权限
        /// </summary>
        [HttpPost]
        public Result SetMenus([FromForm] int roleId, [FromForm] string menuIds)
        {
            var menuIdList = new List<int>();
            if (!string.IsNullOrEmpty(menuIds))
            {
                menuIdList = menuIds.Split(',')
                    .Select(id => int.TryParse(id.Trim(), out int menuId) ? menuId : 0)
                    .Where(id => id > 0)
                    .ToList();
            }

            return _roleMenuService.SetMenusForRole(roleId, menuIdList);
        }

        /// <summary>
        /// 为角色添加菜单权限
        /// </summary>
        [HttpPost]
        public Result AddMenu([FromForm] int roleId, [FromForm] int menuId)
        {
            return _roleMenuService.AddMenuToRole(roleId, menuId);
        }

        /// <summary>
        /// 移除角色的菜单权限
        /// </summary>
        [HttpPost]
        public Result RemoveMenu([FromForm] int roleId, [FromForm] int menuId)
        {
            return _roleMenuService.RemoveMenuFromRole(roleId, menuId);
        }

        /// <summary>
        /// 复制角色菜单权限
        /// </summary>
        [HttpPost]
        public Result CopyMenus([FromForm] int fromRoleId, [FromForm] int toRoleId)
        {
            return _roleMenuService.CopyMenuPermissions(fromRoleId, toRoleId);
        }

        /// <summary>
        /// 获取拥有指定菜单的角色列表
        /// </summary>
        [HttpGet]
        public Result GetRolesByMenu(int menuId)
        {
            return _roleMenuService.GetRolesByMenuId(menuId);
        }

        #endregion
    }
}
