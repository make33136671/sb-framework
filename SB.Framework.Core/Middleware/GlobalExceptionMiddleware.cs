using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Models;

namespace SB.Framework.Core.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "请求 {Method} {Path} 发生未处理异常", context.Request.Method, context.Request.Path);

                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json; charset=utf-8";

                // 是否在响应中包含 ex.Message 由 AppSettings.ExposeErrorDetails 控制(默认 false,安全)。
                // 生产环境留 false 防止攻击者借错误细节探测系统;调试时改 true 让 admin UI 直接看到具体错。
                // 堆栈仍只进日志,不入 response。
                var exposeDetails = AppGlobal.AppSettings?.ExposeErrorDetails ?? false;
                var result = exposeDetails
                    ? Result.Fail($"服务器内部错误: {ex.Message}")
                    : Result.Fail("服务器内部错误");
                // 配 camelCase + 不转义 unicode,跟 controller 正常返回的 Result 格式对齐
                // (老版用 JsonSerializer 默认 PascalCase + UnicodeEscape,中文消息成 "服务...")
                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                });
                await context.Response.WriteAsync(json);
            }
        }
    }
}
