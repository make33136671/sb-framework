using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SB.Framework.Core.Middleware
{
    /// <summary>
    /// 请求级别的数据库查询计数中间件
    /// 用于检测 N+1 查询等性能问题
    /// </summary>
    public class QueryCountMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<QueryCountMiddleware> _logger;

        /// <summary>
        /// 当前请求的查询计数 (AsyncLocal 保证线程安全)
        /// </summary>
        private static readonly AsyncLocal<int> _queryCount = new();

        /// <summary>
        /// 单次请求的查询数量告警阈值
        /// </summary>
        private const int QUERY_COUNT_WARNING_THRESHOLD = 10;

        public QueryCountMiddleware(RequestDelegate next, ILogger<QueryCountMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            _queryCount.Value = 0;

            await _next(context);

            var count = _queryCount.Value;
            var path = context.Request.Path;

            if (count > QUERY_COUNT_WARNING_THRESHOLD)
            {
                _logger.LogWarning("请求 {Method} {Path} 执行了 {QueryCount} 次数据库查询 (阈值: {Threshold})，可能存在 N+1 问题",
                    context.Request.Method, path, count, QUERY_COUNT_WARNING_THRESHOLD);
            }
            else if (count > 0)
            {
                _logger.LogDebug("请求 {Method} {Path} 执行了 {QueryCount} 次数据库查询",
                    context.Request.Method, path, count);
            }
        }

        /// <summary>
        /// 增加当前请求的查询计数 (由 FreeSql Aop.CommandAfter 调用)
        /// </summary>
        public static void IncrementQueryCount()
        {
            _queryCount.Value++;
        }
    }
}
