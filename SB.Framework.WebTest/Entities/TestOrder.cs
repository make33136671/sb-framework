using FreeSql.DataAnnotations;
using SB.Framework.Models;

namespace SB.Framework.WebTest;

/// <summary>测试订单状态(模拟真实电商订单生命周期)</summary>
public enum TestOrderStatus
{
    /// <summary>待支付</summary>
    Pending = 0,
    /// <summary>已支付</summary>
    Paid = 1,
    /// <summary>已取消(用户主动)</summary>
    Cancelled = 2,
    /// <summary>超时关单(后台任务自动)</summary>
    Timeout = 3,
}

/// <summary>
/// 模拟业务实体:测试订单。
/// 演示框架全链条能力 —— IEntity 主键 / FreeSql 特性建表 / ISoftDelete 软删全局过滤 /
/// AppTime 时区时间 / decimal 金额 / 枚举存储。
/// </summary>
[Table(Name = "TEST_Order")]
public class TestOrder : IEntity, ISoftDelete
{
    [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>业务单号(如 WT20260904001)</summary>
    [Column(Name = "OrderNo", StringLength = 32)]
    public string OrderNo { get; set; } = null!;

    /// <summary>商品名</summary>
    [Column(Name = "ProductName", StringLength = 100)]
    public string ProductName { get; set; } = null!;

    /// <summary>金额</summary>
    [Column(Name = "Amount", Precision = 12, Scale = 2)]
    public decimal Amount { get; set; }

    /// <summary>状态</summary>
    [Column(Name = "Status")]
    public TestOrderStatus Status { get; set; } = TestOrderStatus.Pending;

    /// <summary>下单人(登录用户名 / API Key 归属账号)</summary>
    [Column(Name = "CreatedBy", StringLength = 64)]
    public string CreatedBy { get; set; } = "anonymous";

    /// <summary>下单时间(UTC 存库)</summary>
    [Column(Name = "CreatedAt", InsertValueSql = "now()", CanUpdate = false)]
    public DateTime CreatedAt { get; set; }

    /// <summary>支付时间(UTC)</summary>
    [Column(Name = "PaidAt", IsNullable = true)]
    public DateTime? PaidAt { get; set; }

    /// <summary>关闭原因(取消/超时)</summary>
    [Column(Name = "CloseReason", StringLength = 200, IsNullable = true)]
    public string? CloseReason { get; set; }

    /// <summary>软删标记(FreeSql 全局过滤自动排除)</summary>
    [Column(Name = "IsDeleted")]
    public bool IsDeleted { get; set; }
}
