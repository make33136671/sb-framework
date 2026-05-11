using Microsoft.AspNetCore.Http;

namespace SB.Framework.Core.Expand
{
    public static class ExpandImageUrl
    {
        /// <summary>
        /// 相对路径 → 绝对 URL（如 /upload/xxx.png → http://host/upload/xxx.png）
        /// 已是绝对 URL 则原样返回
        /// </summary>
        public static string ToAbsoluteUrl(this string? path, HttpContext ctx)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (path.StartsWith("http://") || path.StartsWith("https://")) return path;
            var request = ctx.Request;
            return $"{request.Scheme}://{request.Host}{(path.StartsWith("/") ? "" : "/")}{path}";
        }

        /// <summary>
        /// 绝对 URL → 相对路径（如 http://host/upload/xxx.png → /upload/xxx.png）
        /// 已是相对路径则原样返回
        /// </summary>
        public static string ToRelativePath(this string? url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            if (!url.StartsWith("http://") && !url.StartsWith("https://")) return url;
            try
            {
                var uri = new Uri(url);
                return uri.AbsolutePath;
            }
            catch
            {
                return url;
            }
        }
    }
}
