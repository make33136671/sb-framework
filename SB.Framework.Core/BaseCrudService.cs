using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Models;
using System.Linq.Expressions;

namespace SB.Framework.Core
{
    /// <summary>
    /// 通用 CRUD 基类 — 提供 Detail、Del、排序和分页的默认实现
    /// </summary>
    public class BaseCrudService<T> : BaseRepository<T> where T : class, IEntity
    {
        protected readonly ILogger _logger;

        public BaseCrudService(IFreeSql fsql, ILogger logger) : base(fsql)
        {
            _logger = logger;
        }

        /// <summary>
        /// 通用详情查询 — 子类可 override 添加 Include()
        /// </summary>
        public virtual Result Detail(int id)
        {
            T entity = Select.Where(c => c.Id == id).First();

            if (entity == null)
            {
                return Result.Fail("数据不存在,请确认操作是否正确!");
            }

            return Result.Success(entity);
        }

        /// <summary>
        /// 通用删除 — 自动检测 ISoftDelete 走软删除，否则硬删除
        /// </summary>
        public virtual Result Del(int id)
        {
            T entity = Select.Where(c => c.Id == id).First();

            if (entity == null)
            {
                return Result.Fail("数据不存在,删除失败!");
            }

            if (entity is ISoftDelete softDelete)
            {
                softDelete.IsDeleted = true;
                int count = Update(entity);

                if (count <= 0)
                {
                    return Result.Fail("删除失败!");
                }
            }
            else
            {
                int count = Delete(c => c.Id == id);
                if (count <= 0)
                {
                    return Result.Fail("删除失败!");
                }
            }

            return Result.Success("删除成功!");
        }

        /// <summary>
        /// 排序助手 — 空字段默认 Id DESC
        /// </summary>
        protected virtual ISelect<T> ApplySorting(ISelect<T> query, string field, string order)
        {
            if (string.IsNullOrEmpty(field))
            {
                return query.OrderByDescending(c => c.Id);
            }

            return query.OrderByPropertyName(field, order == "ascend");
        }

        /// <summary>
        /// 分页助手 — 全量返回
        /// </summary>
        protected virtual Result PagedResult(ISelect<T> query, int page, int pageSize)
        {
            var data = query.Count(out long dataCount).Page(page, pageSize).ToList();
            return Result.Page(data, dataCount);
        }

        /// <summary>
        /// 分页助手 — 带投影
        /// </summary>
        protected virtual Result PagedResult<TDto>(ISelect<T> query, int page, int pageSize,
            Expression<Func<T, TDto>> selector)
        {
            var data = query.Count(out long dataCount).Page(page, pageSize).ToList(selector);
            return Result.Page(data, dataCount);
        }
    }
}
