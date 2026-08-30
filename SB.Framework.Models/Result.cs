using System.Text.Json.Serialization;

namespace SB.Framework.Models
{
    /// <summary>
    /// 统一响应结果
    /// </summary>
    public class Result
    {
        /// <summary>
        /// 状态码 (0=成功, 非0=失败)
        /// </summary>
        public int Code { get; set; }

        /// <summary>
        /// 响应消息
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// 响应数据
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Data { get; set; }

        /// <summary>
        /// 成功 — 返回数据
        /// </summary>
        public static Result Success(object data)
            => new() { Code = 0, Message = "ok", Data = data };

        /// <summary>
        /// 成功 — 返回消息 + 可选数据
        /// </summary>
        public static Result Success(string message, object? data = null)
            => new() { Code = 0, Message = message, Data = data };

        /// <summary>
        /// 失败
        /// </summary>
        public static Result Fail(string message = "操作失败", int code = -1)
            => new() { Code = code, Message = message };

        /// <summary>
        /// 分页 — items + total 扁平放入 data
        /// </summary>
        public static Result Page(object items, long total, string message = "ok")
            => new() { Code = 0, Message = message, Data = new { items, total } };
    }
}
