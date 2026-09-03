using FreeSql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Models;
using SB.Framework.WebTest.Services;

namespace SB.Framework.WebTest.Controllers;

/// <summary>
/// 订单业务场景:把 BaseCrudService / 软删过滤 / 状态机 / 缓存统计 / 操作人识别
/// 映射成可手动点击的真实业务动作。
/// </summary>
[ApiController, Route("[controller]/[action]")]
public class TestOrderController : ControllerBase
{
    private readonly TestOrderService _orders;

    public TestOrderController(TestOrderService orders) => _orders = orders;

    /// <summary>下单</summary>
    [HttpPost, AllowAnonymous]
    public Result Create([FromForm] string productName, [FromForm] decimal amount)
        => _orders.CreateOrder(productName, amount);

    /// <summary>支付(Pending→Paid,非法流转被状态机拒绝)</summary>
    [HttpPost, AllowAnonymous]
    public Result Pay([FromForm] int id) => _orders.Pay(id);

    /// <summary>取消(仅 Pending)</summary>
    [HttpPost, AllowAnonymous]
    public Result Cancel([FromForm] int id, [FromForm] string reason = "") => _orders.Cancel(id, reason);

    /// <summary>列表(BaseCrudService 分页;支持 field/order 排序)</summary>
    [HttpGet, AllowAnonymous]
    public Result List(string field = "", string order = "", int page = 1, int pageSize = 10)
        => _orders.List(field, order, page, pageSize);

    /// <summary>详情(验证 Detail 不存在时的 Fail 语义)</summary>
    [HttpGet, AllowAnonymous]
    public Result Detail(int id) => _orders.Detail(id);

    /// <summary>软删(ISoftDelete → 不物理删除,全局过滤自动隐藏)</summary>
    [HttpPost, AllowAnonymous]
    public Result Del([FromForm] int id) => _orders.Del(id);

    /// <summary>统计(5 秒缓存;连点两次对比命中,验证 ICacheService)</summary>
    [HttpGet, AllowAnonymous]
    public Result Stats() => _orders.Stats();

    /// <summary>直查软删数据(绕过全局过滤器,证明软删数据仍在库里)</summary>
    [HttpGet, AllowAnonymous]
    public Result RawDeleted(int id)
    {
        var svc = HttpContext.RequestServices;
        var fsql = (IFreeSql)svc.GetService(typeof(IFreeSql))!;
        var row = fsql.Select<TestOrder>().Where(o => o.Id == id).First();
        return row == null
            ? Result.Fail($"Id {id} 物理层面也不存在")
            : Result.Success("已绕过全局过滤器直查(可见 IsDeleted=true 仍在库)", row);
    }
}
