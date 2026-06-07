using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;
using SB.Framework.Core;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class SystemSettingCategoryService : BaseCrudService<SystemSettingCategory>
    {
        IFreeSql _fsql;
        IHttpContextAccessor _httpContextAccessor;
        public SystemSettingCategoryService(IFreeSql fsql, ILogger<SystemSettingCategoryService> logger, IHttpContextAccessor httpContextAccessor) : base(fsql, logger)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// 获取设置分类列表
        /// </summary>
        public virtual Result GetList(string key = "", string name = "", string field = "", string order = "", int page = 1, int pageSize = 999)
        {
            var query = Select;
            query = query.WhereIf(!string.IsNullOrEmpty(name), c => c.Name == name);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        /// <summary>
        /// 获取设置分类列表
        /// </summary>
        public virtual Result QuerySystemSettingCategory(string key = "")
        {
            var query = Select;
            var data = query.OrderBy(c => c.Id).Count(out long dataCount).ToList();
            return Result.Page(data, dataCount);
        }

        /// <summary>
        /// 创建设置分类
        /// </summary>
        public virtual Result Create(SystemSettingCategory systemSettingCategory)
        {
            systemSettingCategory = this.Insert(systemSettingCategory);

            return Result.Success("设置分类添加成功!", systemSettingCategory);
        }

        /// <summary>
        /// 更新设置分类
        /// </summary>
        public virtual Result Edit(SystemSettingCategory systemSettingCategory)
        {
            int count = this.UpdateDiy.UpdateColumns(c => new
            {
                c.Name,
                c.Icon,
                c.Sort,
            }).SetSource(systemSettingCategory).ExecuteAffrows();

            if (count <= 0)
            {
                return Result.Fail("设置分类更新失败!");
            }

            return Result.Success("设置分类更新成功!", systemSettingCategory);
        }
    }
}
