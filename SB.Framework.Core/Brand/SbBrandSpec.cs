namespace SB.Framework.Core.Brand
{
    /// <summary>
    /// SB 品牌规格参数（生成图标/Logo 的全部可调项）
    /// </summary>
    public class SbBrandSpec
    {
        /// <summary>单字标（1-3 个大写字母/字符，如 "TG"、"SB"）</summary>
        public string Monogram { get; set; } = "SB";

        /// <summary>产品中文名（横版锁形、app-info 用）</summary>
        public string ProductCn { get; set; } = "管理系统";

        /// <summary>产品英文名</summary>
        public string ProductEn { get; set; } = "sb";

        /// <summary>渐变起点色（品牌青）</summary>
        public string AccentFrom { get; set; } = "#22D3EE";

        /// <summary>渐变终点色（品牌蓝）</summary>
        public string AccentTo { get; set; } = "#3B82F6";

        /// <summary>深色背景（品牌深空色）</summary>
        public string DarkBg { get; set; } = "#0A0E1A";

        /// <summary>浅色背景</summary>
        public string LightBg { get; set; } = "#F8FAFC";

        /// <summary>单字标颜色（渐变上的深色字）</summary>
        public string MonoColor { get; set; } = "#0A1124";

        /// <summary>生成时间（ISO8601 UTC）</summary>
        public string GeneratedAt { get; set; } = "";
    }
}
