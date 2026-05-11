using Microsoft.AspNetCore.Http;

namespace SB.Framework.Core.Expand
{
    public static class ExpandRequest
    {
        /// <summary>
        /// 获取客户端IP地址
        /// </summary>
        public static string GetClientIP(this HttpRequest request)
        {
            var ip4 = request.Headers["X-Forwarded-For"].FirstOrDefault();

            if (string.IsNullOrEmpty(ip4))
            {
                ip4 = request.Headers["X-Real-IP"].FirstOrDefault();
            }

            if (string.IsNullOrEmpty(ip4))
            {
                ip4 = request.HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString();
            }

            return ip4 ?? "";
        }
    }
}
