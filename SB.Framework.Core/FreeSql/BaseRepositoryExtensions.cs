using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace SB.Framework.Core.FreeSql
{
    public static class BaseRepositoryExtensions
    {
        /// <summary>
        /// 获取工作单元
        /// </summary>
        /// <typeparam name="TEntity">仓储实体对象</typeparam>
        /// <param name="repo">仓储单元对象</param>
        /// <returns>工作单元</returns>
        public static IUnitOfWork GetUnitOfWork<TEntity>(this BaseRepository<TEntity> repo) where TEntity : class
        {
            if (repo.UnitOfWork == null)
            {
                repo.UnitOfWork = repo.Orm.CreateUnitOfWork();
            }

            return repo.UnitOfWork;
        }

        /// <summary>
        /// 根据 ID 获取实体，如果不存在抛出异常
        /// 优化: 统一的单项查询模式，减少重复代码
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="repo">仓储</param>
        /// <param name="id">实体 ID</param>
        /// <param name="errorMsg">未找到时的错误信息</param>
        /// <returns>实体对象</returns>
        /// <exception cref="Exception">实体不存在时抛出</exception>
        public static TEntity GetByIdOrThrow<TEntity>(this BaseRepository<TEntity> repo, int id, string errorMsg = "记录不存在")
            where TEntity : class, IEntity
        {
            var entity = repo.Select.Where(c => c.Id == id).First();
            if (entity == null)
                throw new Exception(errorMsg);
            return entity;
        }

        /// <summary>
        /// 检查 ID 是否存在
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="repo">仓储</param>
        /// <param name="id">实体 ID</param>
        /// <returns>存在返回 true，否则返回 false</returns>
        public static bool ExistsById<TEntity>(this BaseRepository<TEntity> repo, int id)
            where TEntity : class, IEntity
        {
            return repo.Select.Where(c => c.Id == id).Any();
        }

        /// <summary>
        /// 获取分页列表
        /// 优化: 统一的分页查询模式，支持排序和过滤
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="repo">仓储</param>
        /// <param name="predicate">过滤条件</param>
        /// <param name="pageNum">页码（从 1 开始）</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="orderBy">排序表达式（可选）</param>
        /// <param name="isDescending">是否降序</param>
        /// <returns>分页数据 (列表 + 总数)</returns>
        public static (List<TEntity> list, long total) GetPageList<TEntity>(
            this BaseRepository<TEntity> repo,
            Expression<Func<TEntity, bool>> predicate,
            int pageNum,
            int pageSize,
            Expression<Func<TEntity, object>> orderBy = null,
            bool isDescending = true)
            where TEntity : class, IEntity
        {
            var query = repo.Select.Where(predicate);

            // 应用排序
            if (orderBy != null)
            {
                query = isDescending
                    ? query.OrderByDescending(orderBy)
                    : query.OrderBy(orderBy);
            }
            else
            {
                query = query.OrderByDescending(c => c.Id); // 默认按 ID 降序
            }

            // 获取总数和分页数据
            long total = query.Count();
            var list = query.Page(pageNum, pageSize).ToList();

            return (list, total);
        }

        /// <summary>
        /// 根据条件获取单个实体
        /// 优化: 通用的条件查询模式
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="repo">仓储</param>
        /// <param name="predicate">查询条件</param>
        /// <returns>查询结果，不存在返回 null</returns>
        public static TEntity FirstOrDefault<TEntity>(
            this BaseRepository<TEntity> repo,
            Expression<Func<TEntity, bool>> predicate)
            where TEntity : class, IEntity
        {
            return repo.Select.Where(predicate).First();
        }

        /// <summary>
        /// 根据条件统计数量
        /// 优化: 统一的计数模式
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="repo">仓储</param>
        /// <param name="predicate">查询条件</param>
        /// <returns>符合条件的数量</returns>
        public static long Count<TEntity>(
            this BaseRepository<TEntity> repo,
            Expression<Func<TEntity, bool>> predicate)
            where TEntity : class, IEntity
        {
            return repo.Select.Where(predicate).Count();
        }

        /// <summary>
        /// 根据条件检查是否存在任何记录
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="repo">仓储</param>
        /// <param name="predicate">查询条件</param>
        /// <returns>存在返回 true，否则返回 false</returns>
        public static bool Any<TEntity>(
            this BaseRepository<TEntity> repo,
            Expression<Func<TEntity, bool>> predicate)
            where TEntity : class, IEntity
        {
            return repo.Select.Where(predicate).Any();
        }

        /// <summary>
        /// 在事务中执行操作
        /// 优化: 统一的事务处理模式，自动处理提交/回滚
        /// 避免手动的 try-catch-finally 事务管理代码
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <typeparam name="TResult">返回类型</typeparam>
        /// <param name="repo">仓储</param>
        /// <param name="operation">事务内执行的操作</param>
        /// <param name="logger">日志记录器（可选）</param>
        /// <returns>操作的返回值</returns>
        public static TResult ExecuteInTransaction<TEntity, TResult>(
            this BaseRepository<TEntity> repo,
            Func<IUnitOfWork, TResult> operation,
            Microsoft.Extensions.Logging.ILogger logger = null)
            where TEntity : class, IEntity
        {
            IUnitOfWork uow = repo.GetUnitOfWork();
            try
            {
                uow.GetOrBeginTransaction();
                var result = operation(uow);
                uow.Commit();
                logger?.LogInformation("事务执行成功");
                return result;
            }
            catch (Exception ex)
            {
                uow.Rollback();
                logger?.LogError(ex, "事务执行失败，已回滚");
                throw;
            }
        }

        /// <summary>
        /// 在事务中执行操作（无返回值）
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="repo">仓储</param>
        /// <param name="operation">事务内执行的操作</param>
        /// <param name="logger">日志记录器（可选）</param>
        public static void ExecuteInTransaction<TEntity>(
            this BaseRepository<TEntity> repo,
            Action<IUnitOfWork> operation,
            Microsoft.Extensions.Logging.ILogger logger = null)
            where TEntity : class, IEntity 
            {
            IUnitOfWork uow = repo.GetUnitOfWork();
            try
            {
                uow.GetOrBeginTransaction();
                operation(uow);
                uow.Commit();
                logger?.LogInformation("事务执行成功");
            }
            catch (Exception ex)
            {
                uow.Rollback();
                logger?.LogError(ex, "事务执行失败，已回滚");
                throw;
            }
        }

        /// <summary>
        /// 条件查询扩展 — 仅在条件为 true 时应用 Where 条件
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="query">查询对象</param>
        /// <param name="condition">条件</param>
        /// <param name="predicate">查询表达式</param>
        /// <returns>查询对象</returns>
        public static ISelect<TEntity> WhereIf<TEntity>(
            this ISelect<TEntity> query,
            bool condition,
            Expression<Func<TEntity, bool>> predicate)
            where TEntity : class
        {
            return condition ? query.Where(predicate) : query;
        }

        /// <summary>
        /// IEnumerable 条件过滤扩展 — 仅在条件为 true 时应用 Where 过滤
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="source">源集合</param>
        /// <param name="condition">条件</param>
        /// <param name="predicate">过滤表达式</param>
        /// <returns>过滤后的集合</returns>
        public static IEnumerable<TEntity> WhereIf<TEntity>(
            this IEnumerable<TEntity> source,
            bool condition,
            Func<TEntity, bool> predicate)
        {
            return condition ? source.Where(predicate) : source;
        }

        /// <summary>
        /// 批量插入扩展 — 一次数据库往返插入多个实体
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="repo">仓储</param>
        /// <param name="entities">实体集合</param>
        public static void InsertRange<TEntity>(
            this BaseRepository<TEntity> repo,
            IEnumerable<TEntity> entities)
            where TEntity : class
        {
            if (entities == null || !entities.Any())
                return;
            repo.Insert(entities);
        }
    }
}
