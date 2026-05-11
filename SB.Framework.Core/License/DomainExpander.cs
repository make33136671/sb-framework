using System.Collections.Generic;
using System.Linq;

namespace SB.Framework.Core.License
{
    /// <summary>
    /// 签发时域名规范化 + 扩展工具 (B 方案).
    /// </summary>
    public static class DomainExpander
    {
        /// <summary>
        /// 按以下规则处理输入域名:
        /// - "example.com" (仅 1 个点, 非通配) → ["example.com", "*.example.com"]
        /// - "api.example.com" (2+ 个点) → ["api.example.com"]
        /// - "*.other.com" → ["*.other.com", "other.com"]（通配同时覆盖裸域，让用户输入意图显式化）
        /// 全部统一小写, 去空白, 去重.
        /// 注: DomainMatcher 仍是标准 wildcard 语义（*.foo.com 不匹配 foo.com）；
        /// 这里把"用户输入 *.foo.com 时同时覆盖 foo.com"的意图在签发时拆成两条独立条目。
        /// </summary>
        public static List<string> Expand(IEnumerable<string>? input)
        {
            if (input == null) return new List<string>();

            var result = new List<string>();
            foreach (var raw in input)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var d = raw.Trim().ToLowerInvariant();
                // 剥离端口号: "localhost:6013" → "localhost", "example.com:8080" → "example.com"
                var colonIdx = d.LastIndexOf(':');
                if (colonIdx > 0 && !d.StartsWith("*.") && d.Substring(colonIdx + 1).All(char.IsDigit))
                    d = d.Substring(0, colonIdx);
                result.Add(d);

                if (d.StartsWith("*."))
                {
                    // 通配输入: 补裸域。比如 *.ihmub.com 同时把 ihmub.com 加入允许列表
                    var bare = d[2..];
                    if (!string.IsNullOrWhiteSpace(bare))
                        result.Add(bare);
                }
                else if (d.Count(c => c == '.') == 1)
                {
                    // 裸域输入: 补通配。比如 ihmub.com 同时把 *.ihmub.com 加入允许列表
                    result.Add("*." + d);
                }
            }
            return result.Distinct().ToList();
        }
    }
}
