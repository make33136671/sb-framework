using System;

namespace SB.Framework.Core.Attribute
{
    /// <summary>
    /// 白名单标记：允许该 action / controller 使用 [FromBody]（JSON 请求体）。
    ///
    /// 背景：saibo 系 admin 接口默认约定走 [FromForm]（form-urlencoded），
    /// 前端统一用 api.postForm。BanFromBodyAnalyzer (SB0002) 会把未标记的 [FromBody]
    /// 当作编译错误，防止前后端 Content-Type 不一致导致参数接成 null（典型如登录报"密码错误"）。
    ///
    /// 仅当请求体确实是复杂嵌套对象 / 数组（用表单别扭），才在该 action（或其所在 controller）
    /// 显式标注本特性开白名单。标注本身即是一次"我确认这里要走 JSON"的有意识决策，
    /// 同时提醒前端对应接口必须用 api.post（JSON）而非 api.postForm。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class AllowJsonBodyAttribute : System.Attribute
    {
        /// <summary>开白名单的理由（可选，写明为什么这个接口必须走 JSON）。</summary>
        public string? Reason { get; set; }

        public AllowJsonBodyAttribute() { }

        public AllowJsonBodyAttribute(string reason) => Reason = reason;
    }
}
