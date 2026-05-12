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

                // 把 ex.Message 带到响应(不带堆栈),admin UI 能直接看到具体错,无需 ssh 翻日志。
                // framework 内部网部署,Message 暴露 acceptable;堆栈仍只进日志。
                var result = Result.Fail($"服务器内部错误: {ex.Message}");
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
