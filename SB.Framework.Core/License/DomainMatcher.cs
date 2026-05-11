using System.Collections.Generic;

namespace SB.Framework.Core.License
{
    /// <summary>
    /// 域名匹配工具 — 支持通配符 *.example.com 和精确匹配.
    /// 签发时 DomainExpander 负责扩展根域名为通配, 这里只做最终匹配.
    /// </summary>
    public static class DomainMatcher
    {
        /// <summary>
        /// host 是否匹配 patterns 中任一条目.
        /// - "*.example.com" 匹配 a.example.com 和 x.y.example.com, 不匹配 example.com 本身
        /// - "example.com" 精确匹配 example.com
        /// host 和 pattern 都先小写, host 去掉 ":port".
        /// </summary>
        public static bool Match(List<string> patterns, string? host)
        {
            if (patterns == null || patterns.Count == 0 || string.IsNullOrWhiteSpace(host))
                return false;

            var h = host.ToLowerInvariant();
            var colonIdx = h.IndexOf(':');
            if (colonIdx > 0) h = h[..colonIdx];

            foreach (var p in patterns)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var pat = p.Trim().ToLowerInvariant();

                if (pat.StartsWith("*."))
                {
                    var suffix = pat[2..];
                    if (h.EndsWith("." + suffix)) return true;
                }
                else
                {
                    if (h == pat) return true;
                }
            }
            return false;
        }
    }
}
