using System.Globalization;

namespace SB.Framework.Core.Brand
{
    /// <summary>
    /// SB 品牌 SVG 生成器（六边形节点 + 单字标，矢量原件，任意缩放不糊）
    /// 形态规范与 sb-framework-chat/brand/cyber-sb 品牌包一致：
    /// 左右尖六边形 + 青→蓝渐变 + 深色单字标。
    /// </summary>
    public static class BrandSvgBuilder
    {
        /// <summary>正方形 mark（徽标）— bg 为背景色，传 null 则透明底</summary>
        public static string BuildMark(SbBrandSpec spec, string? bg)
        {
            return $"""
                <svg viewBox="0 0 200 200" xmlns="http://www.w3.org/2000/svg">
                  <defs>
                    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
                      <stop offset="0" stop-color="{spec.AccentFrom}"/>
                      <stop offset="1" stop-color="{spec.AccentTo}"/>
                    </linearGradient>
                  </defs>
                  {RectBg(bg, 200, 200)}
                  <polygon points="{HexagonPoints(100, 100, 70)}" fill="url(#g)" stroke="url(#g)" stroke-width="18" stroke-linejoin="round"/>
                  <text x="100" y="103" text-anchor="middle" dominant-baseline="central"
                    font-family="'Inter','Roboto',system-ui,sans-serif" font-size="{MonoFontSize(spec.Monogram)}"
                    font-weight="800" letter-spacing="-1" fill="{spec.MonoColor}">{Escape(spec.Monogram)}</text>
                </svg>
                """;
        }

        /// <summary>横版锁形（徽标 + 产品名），用于登录页/文档页眉</summary>
        public static string BuildHorizontal(SbBrandSpec spec, string? bg, string titleColor, string subColor)
        {
            return $"""
                <svg viewBox="0 0 460 120" xmlns="http://www.w3.org/2000/svg">
                  <defs>
                    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
                      <stop offset="0" stop-color="{spec.AccentFrom}"/>
                      <stop offset="1" stop-color="{spec.AccentTo}"/>
                    </linearGradient>
                  </defs>
                  {RectBg(bg, 460, 120)}
                  <g transform="translate(0,2) scale(0.58)">
                    <polygon points="{HexagonPoints(100, 100, 70)}" fill="url(#g)" stroke="url(#g)" stroke-width="18" stroke-linejoin="round"/>
                    <text x="100" y="103" text-anchor="middle" dominant-baseline="central"
                      font-family="'Inter','Roboto',system-ui,sans-serif" font-size="{MonoFontSize(spec.Monogram)}"
                      font-weight="800" letter-spacing="-1" fill="{spec.MonoColor}">{Escape(spec.Monogram)}</text>
                  </g>
                  <text x="140" y="62" font-family="'Inter','PingFang SC','Microsoft YaHei',system-ui,sans-serif"
                    font-size="40" font-weight="700" fill="{titleColor}">{Escape(spec.ProductCn)}</text>
                  <text x="141" y="90" font-family="'Inter',system-ui,sans-serif" font-size="14"
                    font-weight="600" letter-spacing="6" fill="{subColor}">{Escape(spec.ProductEn.ToUpperInvariant())}</text>
                </svg>
                """;
        }

        /// <summary>左右尖六边形顶点（直径 R，与品牌包 200 盒 70 半径一致）</summary>
        public static string HexagonPoints(double cx, double cy, double r)
        {
            double[] angles = { 0, 60, 120, 180, 240, 300 };
            var pts = angles.Select(a =>
            {
                var rad = a * Math.PI / 180.0;
                return FormattableString.Invariant($"{(cx + r * Math.Cos(rad)).ToString("0.##", CultureInfo.InvariantCulture)},{(cy + r * Math.Sin(rad)).ToString("0.##", CultureInfo.InvariantCulture)}");
            });
            return string.Join(" ", pts);
        }

        /// <summary>单字标字号：1 字大、2 字标准、3 字缩小</summary>
        private static int MonoFontSize(string monogram) => monogram.Length switch
        {
            <= 1 => 84,
            2 => 66,
            _ => 46,
        };

        private static string RectBg(string? bg, int w, int h)
            => bg == null ? "" : $"""<rect width="{w}" height="{h}" rx="24" fill="{bg}"/>""";

        private static string Escape(string s)
            => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
