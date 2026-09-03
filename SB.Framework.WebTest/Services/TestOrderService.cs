using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Services;
using SB.Framework.WebTest;

namespace SB.Framework.WebTest.Services;

/// <summary>
/// 模拟业务服务:测试订单。
/// 演示框架核心能力映射 ——
///   1. BaseCrudService 下沉基类(分页/软删/排序开箱即用);
///   2. ICacheService 缓存订单统计(带过期);
///   3. 业务状态机校验(真实业务场景:非法流转拒绝);
///   4. 操作人取自当前登录身份(测试台可切换匿名/JWT/API Key 三种身份)。
/// </summary>
[AutoDependencyInjection]
public class TestOrderService : BaseCrudService<TestOrder>
{
    private readonly IFreeSql _db;
    private readonly ICacheService _cache;
    private readonly IHttpContextAccessor _http;

    public TestOrderService(IFreeSql fsql, ILogger<TestOrderService> logger,
        ICacheService cache, IHttpContextAccessor http) : base(fsql, logger)
    {
        _db = fsql;
        _cache = cache;
        _http = http;
    }

    /// <summary>当前操作人标签:API Key 请求显示 key:xxx,JWT 显示用户名,否则 anonymous</summary>
    public string OperatorTag
    {
        get
        {
            var http = _http.HttpContext;
            if (http == null) return "anonymous";
            if (http.Items.TryGetValue(SB.Feature.AccessToken.Services.ApiKeyContext.HttpItemKey,
                    out var ctx) && ctx is SB.Feature.AccessToken.Services.IApiKeyContext key)
                return key.OperatorTag;
            return http.User.Identity?.Name ?? "anonymous";
        }
    }

    /// <summary>下单:生成单号 + 落库 + 失效统计缓存</summary>
    public virtual Result CreateOrder(string productName, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(productName)) return Result.Fail("商品名不能为空");
        if (amount <= 0) return Result.Fail("金额必须大于 0(演示参数校验)");

        var order = new TestOrder
        {
            OrderNo = $"WT{AppTime.Now:yyyyMMddHHmmss}{Random.Shared.Next(100, 999)}",
            ProductName = productName.Trim(),
            Amount = amount,
            Status = TestOrderStatus.Pending,
            CreatedBy = OperatorTag,
            CreatedAt = AppTime.UtcNow,
        };
        var id = (int)_db.Insert(order).ExecuteIdentity();
        _cache.Remove(StatsCacheKey);
        return Result.Success("下单成功", Detail(id).Data);
    }

    /// <summary>支付:Pending → Paid(演示业务状态机:非法流转拒绝)</summary>
    public virtual Result Pay(int id)
    {
        var order = _db.Select<TestOrder>().Where(o => o.Id == id).First();
        if (order == null) return Result.Fail($"订单 {id} 不存在(或已被软删)");
        if (order.Status != TestOrderStatus.Pending)
            return Result.Fail($"状态机校验拒绝:当前 {order.Status},只有 Pending 可支付");
        order.Status = TestOrderStatus.Paid;
        order.PaidAt = AppTime.UtcNow;
        _db.Update<TestOrder>().SetSource(order).ExecuteAffrows();
        _cache.Remove(StatsCacheKey);
        return Result.Success($"支付成功,操作人 {OperatorTag}", order);
    }

    /// <summary>用户取消:仅 Pending 可取消</summary>
    public virtual Result Cancel(int id, string reason)
    {
        var order = _db.Select<TestOrder>().Where(o => o.Id == id).First();
        if (order == null) return Result.Fail($"订单 {id} 不存在(或已被软删)");
        if (order.Status != TestOrderStatus.Pending)
            return Result.Fail($"状态机校验拒绝:当前 {order.Status},只有 Pending 可取消");
        order.Status = TestOrderStatus.Cancelled;
        order.CloseReason = string.IsNullOrWhiteSpace(reason) ? "用户主动取消" : reason;
        _db.Update<TestOrder>().SetSource(order).ExecuteAffrows();
        _cache.Remove(StatsCacheKey);
        return Result.Success("取消成功", order);
    }

    /// <summary>订单列表(BaseCrudService 排序 + 分页助手组合;支持 field/order 排序)</summary>
    public virtual Result List(string field = "", string order = "", int page = 1, int pageSize = 10)
        => PagedResult(ApplySorting(Select, field, order), page, pageSize);

    /// <summary>订单统计(演示缓存:5 秒过期,反复调用第 2 次起命中)</summary>
    public virtual Result Stats()
    {
        var hit = _cache.Get<string>($"{StatsCacheKey}:hit");
        var cached = _cache.Get<object>(StatsCacheKey);
        if (cached != null)
            return Result.Success($"缓存命中(上次写入距今标记:{hit})", cached);

        var stats = _db.Select<TestOrder>()
            .GroupBy(o => o.Status)
            .ToList(g => new { Status = g.Key, Count = g.Count() });
        var data = new
        {
            GeneratedAt = AppTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            ByStatus = stats,
            TotalAmount = _db.Select<TestOrder>().Sum(o => o.Amount),
        };
        _cache.Set(StatsCacheKey, data, TimeSpan.FromSeconds(5));
        _cache.Set($"{StatsCacheKey}:hit", AppTime.Now.ToString("HH:mm:ss.fff"), TimeSpan.FromSeconds(5));
        return Result.Success("缓存未命中,已实时计算并写入(5 秒后过期,可再点一次对比)", data);
    }

    public const string StatsCacheKey = "webtest:order:stats";
}
