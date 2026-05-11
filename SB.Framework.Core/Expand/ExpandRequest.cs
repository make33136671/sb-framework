using Microsoft.AspNetCore.Http;

namespace SB.Framework.Core.Expand
{
    public static class ExpandHttpContext
    {
        /// <summary>
        /// 当前请求的Host域名
        /// </summary>
        public static string RequestHost(this HttpContext context)
        {
            string host = $"{context.Request.Host}";
            return host;
        }

        /// <summary>
        /// 当前请求的Host域名包含Scheme
        /// </summary>
        public static string RequestHostWithScheme(this HttpContext context)
        {
            var scheme = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault()
                         ?? context.Request.Scheme;

            // 优先从 X-Forwarded-Host 获取（反向代理传递的 $http_host，含端口）
            var forwardedHost = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedHost))
            {
                // 解析端口，80/443 默认端口不拼
                var parts = forwardedHost.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out var port)
                    && port != 80 && port != 443)
                    return $"{scheme}://{forwardedHost}";

                return $"{scheme}://{parts[0]}";
            }

            // fallback: 直接用 Request.Host
            var hostname = context.Request.Host.Host;
            var reqPort = context.Request.Host.Port;
            if (reqPort.HasValue && reqPort.Value != 80 && reqPort.Value != 443)
                return $"{scheme}://{hostname}:{reqPort.Value}";

            return $"{scheme}://{hostname}";
        }

        /// <summary>
        /// 当前请求的Origin
        /// </summary>
        public static string RequestOrigin(this HttpContext context)
        {
            context.Request.Headers.TryGetValue("origin", out Microsoft.Extensions.Primitives.StringValues origin);

            if (origin.Count<=0) {
                context.Request.Headers.TryGetValue("referer", out origin);
            }

            if (origin.Count <= 0)
            {
                return "";
            }

            string host = string.Join(",", origin);

            host = host.Replace("https://", "").Replace("http://", "").Replace("/","");

            return host;
        }

      
    }
}