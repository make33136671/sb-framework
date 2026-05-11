using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Expand;
using SB.Framework.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SB.Framework.Services
{
    /// <summary>
    /// 权限服务
    /// </summary>
    [AutoDependencyInjection]
    public class PermissionService
    {
        IFreeSql _fsql;
        ILogger<PermissionService> _logger;
        IHttpContextAccessor _httpContextAccessor;

        public PermissionService(
            IFreeSql fsql,
            ILogger<PermissionService> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _fsql = fsql;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        #region 获取当前用户信息

        /// <summary>
        /// 获取当前登录用户ID
        /// </summary>
        public virtual int GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext?.User.Id() ?? 0;
        }

        /// <summary>
        /// 获取当前登录用户信息
        /// </summary>
        public virtual Admin GetCurrentUser()
        {
            int userId = GetCurrentUserId();
            if (userId <= 0) return null;

            return _fsql.Select<Admin>()
                .Where(c => c.Id == userId)
                .First();
        }

        #endregion

        #region 权限检查

        /// <summary>
        /// 检查当前用户是否有指定操作权限
        /// 权限完全由 Role 和 RoleMenu 控制
        /// </summary>
        public virtual bool HasPermission(string action)
        {
            var user = GetCurrentUser();
            if (user == null) return false;

            // 权限由 Role 和 RoleMenu 控制
            return true;
        }

        #endregion

        #region 数据范围控制

        /// <summary>
        /// 数据范围类型
        /// </summary>
        public enum DataScopeType
        {
            AllData = 0,
            SubordinateData = 1,
            OwnDataOnly = 2
        }

        /// <summary>
        /// 获取当前用户的数据范围
        /// 权限由 Role 和 RoleMenu 控制，此方法可根据实际需求自定义
        /// </summary>
        public virtual DataScopeType GetDataScope()
        {
            var user = GetCurrentUser();
            if (user == null) return DataScopeType.OwnDataOnly;

            // 权限由 Role 和 RoleMenu 控制
            return DataScopeType.AllData;
        }

        /// <summary>
        /// 应用数据范围过滤到管理员查询
        /// 权限由 Role 和 RoleMenu 控制
        /// </summary>
        public virtual ISelect<Admin> ApplyDataScopeFilter(ISelect<Admin> query)
        {
            var user = GetCurrentUser();
            if (user == null) return query.Where(c => false); // 未登录，返回空

            // 权限由 Role 和 RoleMenu 控制，不进行额外过滤
            return query;
        }

        #endregion

        #region 权限验证

        /// <summary>
        /// 检查用户是否可以管理指定的管理员
        /// 权限由 Role 和 RoleMenu 控制
        /// </summary>
        public virtual bool CanManageUser(int targetUserId)
        {
            var currentUser = GetCurrentUser();
            if (currentUser == null) return false;

            // 权限由 Role 和 RoleMenu 控制
            return true;
        }

        /// <summary>
        /// 检查用户是否可以删除客户
        /// 权限由 Role 和 RoleMenu 控制
        /// </summary>
        public virtual bool CanDeleteChatUser()
        {
            var user = GetCurrentUser();
            if (user == null) return false;

            // 权限由 Role 和 RoleMenu 控制
            return true;
        }

        #endregion
    }
}
