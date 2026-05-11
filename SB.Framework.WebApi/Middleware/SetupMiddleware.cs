using System.Text.Json;

namespace SB.Framework.WebApi.Middleware
{
    public class SetupMiddleware
    {
        private static bool? _isInstalled;
        private readonly RequestDelegate _next;
        private readonly string _lockFilePath;

        private static readonly HashSet<string> AllowedPrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "/setup",
            "/admin/setup",
            "/api/setup",
            "/health",
            "/favicon.ico"
        };

        public SetupMiddleware(RequestDelegate next, IWebHostEnvironment env)
        {
            _next = next;
            _lockFilePath = Path.Combine(env.ContentRootPath, "installed.lock");
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (IsInstalled())
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value ?? "";

            // 放行安装相关路径和静态文件
            if (IsAllowedPath(path))
            {
                await _next(context);
                return;
            }

            // API 请求返回 503 JSON
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase) ||
                context.Request.Headers.Accept.ToString().Contains("application/json"))
            {
                context.Response.StatusCode = 503;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    code = -1,
                    type = "setup_required",
                    message = "系统尚未安装，请先完成安装向导",
                    redirectUrl = "/setup/index.html"
                }));
                return;
            }

            // 浏览器请求 302 重定向
            context.Response.Redirect("/setup/index.html");
        }

        private bool IsInstalled()
        {
            if (_isInstalled.HasValue)
                return _isInstalled.Value;

            _isInstalled = File.Exists(_lockFilePath);
            return _isInstalled.Value;
        }

        private static bool IsAllowedPath(string path)
        {
            foreach (var prefix in AllowedPrefixes)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // 静态文件（有扩展名的路径）
            var ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext))
                return true;

            return false;
        }

        /// <summary>
        /// 安装完成后更新内存缓存，使中间件立即放行
        /// </summary>
        public static void MarkAsInstalled()
        {
            _isInstalled = true;
        }
    }
}
