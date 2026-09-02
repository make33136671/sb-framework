using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SB.Framework.Core.Util;
using System.Globalization;
using IOPath = System.IO.Path;
using System.Text.Json;

namespace SB.Framework.Core.Brand
{
    /// <summary>
    /// SB 品牌图标生成器 — 按品牌风格（六边形节点 + 青→蓝渐变 + 单字标）
    /// 从规格参数直接生成全套项目图标：favicon 集、应用图标、SVG 矢量原件、横版锁形。
    /// 纯 ImageSharp.Drawing 程序化绘制，无外部依赖，Docker Linux 容器可用。
    /// </summary>
    public static class BrandIconGenerator
    {
        private static readonly Lazy<FontCollection> FontCollectionLazy = new(() =>
        {
            var fc = new FontCollection();
            var asm = typeof(BrandIconGenerator).Assembly;
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("Roboto-Bold.ttf", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("SB.Framework.Core 未内嵌品牌字体 Roboto-Bold.ttf");
            using var stream = asm.GetManifestResourceStream(name)!;
            fc.Add(stream);
            return fc;
        });

        /// <summary>与 FaviconGenerator 输出文件名保持一致（覆盖式更新，引用方无感）</summary>
        private static readonly (string FileName, int Size)[] PngIcons =
        {
            ("favicon-16x16.png", 16),
            ("favicon-32x32.png", 32),
            ("apple-touch-icon.png", 180),
            ("android-chrome-192x192.png", 192),
            ("android-chrome-512x512.png", 512),
        };

        private static readonly int[] IcoSizes = { 16, 32, 48 };

        /// <summary>
        /// 从 ProductEn 推导默认规格：取单词首字母（最多 2 个）作单字标，如 tgvault → TG
        /// </summary>
        public static SbBrandSpec DefaultSpec(string? productEn, string? productCn)
        {
            var en = (productEn ?? "sb").Trim();
            var parts = en.Split(new[] { '-', '_', ' ', '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
            string monogram;
            if (parts.Length >= 2)
                monogram = new string(new[] { char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[1][0]) });
            else
                monogram = en.Length >= 2 ? en[..2].ToUpperInvariant() : en.ToUpperInvariant();

            if (string.IsNullOrEmpty(monogram)) monogram = "SB";
            return new SbBrandSpec
            {
                Monogram = monogram,
                ProductEn = en,
                ProductCn = string.IsNullOrWhiteSpace(productCn) ? "管理系统" : productCn!,
                GeneratedAt = AppTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            };
        }

        /// <summary>
        /// 生成全套品牌图标：
        /// 根目录 = favicon 集（与上传 Logo 流程同路径，覆盖式）；
        /// brand/ = SVG 原件（明暗 mark + 横版锁形）、PNG 大图、brand.json 规格。
        /// </summary>
        public static SbBrandSpec GenerateAll(string storageDir, SbBrandSpec spec)
        {
            Directory.CreateDirectory(storageDir);
            var brandDir = IOPath.Combine(storageDir, "brand");
            var imagesDir = IOPath.Combine(storageDir, "images");
            Directory.CreateDirectory(brandDir);
            Directory.CreateDirectory(imagesDir);

            // 1. favicon 集（透明底六边形 mark）
            foreach (var (fileName, size) in PngIcons)
                using (var img = RenderMark(size, spec, dark: null))
                    img.SaveAsPng(IOPath.Combine(storageDir, fileName));

            // 2. favicon.ico（复用 FaviconGenerator 的多尺寸 ICO 编码）
            using (var src = RenderMark(256, spec, dark: null))
                FaviconGenerator.GenerateIco(src, IOPath.Combine(storageDir, "favicon.ico"));

            // 3. 应用大图（深色圆角底）+ logo.png（images/logo.png 与上传流程路径一致）
            using (var icon512 = RenderMark(512, spec, dark: spec.DarkBg))
            {
                icon512.SaveAsPng(IOPath.Combine(brandDir, "icon-512-dark.png"));
                using var logo = icon512.Clone(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(512, 512),
                    Mode = ResizeMode.Max,
                }));
                logo.SaveAsPng(IOPath.Combine(imagesDir, "logo.png"));
            }

            // 4. SVG 矢量原件
            File.WriteAllText(IOPath.Combine(brandDir, "mark-dark.svg"), BrandSvgBuilder.BuildMark(spec, spec.DarkBg));
            File.WriteAllText(IOPath.Combine(brandDir, "mark-light.svg"), BrandSvgBuilder.BuildMark(spec, spec.LightBg));
            File.WriteAllText(IOPath.Combine(brandDir, "mark-transparent.svg"), BrandSvgBuilder.BuildMark(spec, null));
            File.WriteAllText(IOPath.Combine(brandDir, "horizontal-dark.svg"), BrandSvgBuilder.BuildHorizontal(spec, spec.DarkBg, "#F1F5F9", "#94A3B8"));
            File.WriteAllText(IOPath.Combine(brandDir, "horizontal-light.svg"), BrandSvgBuilder.BuildHorizontal(spec, spec.LightBg, "#0F172A", "#64748B"));

            // 5. 横版锁形 PNG（登录页页眉用，2x 清晰度）
            using (var hImg = RenderHorizontal(920, 240, spec, spec.DarkBg, "#F1F5F9", "#94A3B8"))
                hImg.SaveAsPng(IOPath.Combine(brandDir, "horizontal-dark.png"));

            // 6. 规格 JSON（运维页读取回显 + 重新生成基准）
            var json = JsonSerializer.Serialize(spec, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            File.WriteAllText(IOPath.Combine(brandDir, "brand.json"), json);

            return spec;
        }

        /// <summary>读取已生成的品牌规格；未生成过返回 null</summary>
        public static SbBrandSpec? LoadSpec(string storageDir)
        {
            var path = IOPath.Combine(storageDir, "brand", "brand.json");
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<SbBrandSpec>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        }

        /// <summary>渲染六边形 mark（dark=null 透明底；dark=色值时深色圆角底）</summary>
        private static Image RenderMark(int size, SbBrandSpec spec, string? dark)
        {
            var img = new Image<Rgba32>(size, size);
            var brush = GradientBrush(size, spec);
            var font = MainFont(size * (spec.Monogram.Length switch { <= 1 => 0.46f, 2 => 0.36f, _ => 0.25f }));

            img.Mutate(ctx =>
            {
                if (dark != null)
                {
                    var color = Color.Parse(dark);
                    ctx.Fill(color);                                   // 全幅底
                    ctx.Fill(color, Squircle(size, size * 0.085f));    // 圆角裁边（叠加同色，导出带透明角）
                }

                // 六边形：fill + 同笔刷描边（品牌包 fill=stroke=渐变，线帽圆角视觉）
                var poly = HexPolygon(size);
                ctx.Fill(brush, poly);
                ctx.Draw(new SolidPen(brush, size * 0.09f), poly);

                // 单字标：正中，垂直水平双向居中
                var options = new RichTextOptions(font)
                {
                    Origin = new PointF(size / 2f, size / 2f),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                ctx.DrawText(options, spec.Monogram, Color.Parse(spec.MonoColor));
            });
            return img;
        }

        /// <summary>渲染横版锁形 PNG（mark + 产品名 + 英文副标）。
        /// 内嵌 Roboto 无 CJK 字形：产品名为中文且未挂载中文字体时，自动降级用英文名，避免豆腐块。</summary>
        private static Image RenderHorizontal(int w, int h, SbBrandSpec spec, string bg, string titleColor, string subColor)
        {
            var img = new Image<Rgba32>(w, h);
            img.Mutate(ctx =>
            {
                ctx.Fill(Color.Parse(bg));

                var markSize = h * 0.9f;
                var mark = RenderMark((int)markSize, spec, null);
                ctx.DrawImage(mark, new Point((int)(h * 0.05f), (int)(h * 0.05f)), 1f);

                var titleFont = MainFont(h * 0.30f);
                var subFont = MainFont(h * 0.11f);
                var tx = h * 0.05f + markSize + h * 0.18f;

                var title = spec.ProductCn;
                var sub = spec.ProductEn.ToUpperInvariant();
                var firstChar = title.Length > 0 ? title[0] : 'A';
                if (!CanRender(firstChar))
                {
                    // 首字符无字形（如中文且未内嵌 CJK 字体）→ 英文名降级
                    title = sub;
                    sub = "SB FRAMEWORK";
                }

                ctx.DrawText(new RichTextOptions(titleFont)
                {
                    Origin = new PointF(tx, h * 0.46f),
                    VerticalAlignment = VerticalAlignment.Center,
                }, title, Color.Parse(titleColor));

                ctx.DrawText(new RichTextOptions(subFont)
                {
                    Origin = new PointF(tx + 2, h * 0.75f),
                    VerticalAlignment = VerticalAlignment.Center,
                }, sub, Color.Parse(subColor));
            });
            return img;
        }

        // ---- 几何 ----

        private static Polygon HexPolygon(int size)
        {
            var cx = size / 2f;
            var cy = size / 2f;
            var r = size * 0.35f;                       // 200 盒 R=70 等比
            var pts = new List<PointF>();
            for (var i = 0; i < 6; i++)
            {
                var rad = (60 * i) * Math.PI / 180.0;
                pts.Add(new PointF((float)(cx + r * Math.Cos(rad)), (float)(cy + r * Math.Sin(rad))));
            }
            return new Polygon(new LinearLineSegment(pts.ToArray()));
        }

        /// <summary>超椭圆（squircle）圆角矩形路径，n=4.5 近似 iOS 风格圆角</summary>
        private static Polygon Squircle(int size, float radius)
        {
            const float n = 4.5f;
            var a = size / 2f - 1f;
            var b = size / 2f - 1f;
            var pts = new List<PointF>();
            const int steps = 128;
            for (var i = 0; i < steps; i++)
            {
                var t = 2 * Math.PI * i / steps;
                var ct = Math.Cos(t);
                var st = Math.Sin(t);
                var x = a * Math.Sign(ct) * Math.Pow(Math.Abs(ct), 2 / n);
                var y = b * Math.Sign(st) * Math.Pow(Math.Abs(st), 2 / n);
                pts.Add(new PointF((float)(size / 2f + x), (float)(size / 2f + y)));
            }
            return new Polygon(new LinearLineSegment(pts.ToArray()));
        }

        private static LinearGradientBrush GradientBrush(int size, SbBrandSpec spec)
            => new(new PointF(0, 0), new PointF(size, size),
                GradientRepetitionMode.None,
                new ColorStop(0f, Color.Parse(spec.AccentFrom)),
                new ColorStop(1f, Color.Parse(spec.AccentTo)));

        private static Font MainFont(float sizePx)
        {
            var family = FontCollectionLazy.Value.Families.First();
            return family.CreateFont(Math.Max(8f, sizePx), FontStyle.Bold);
        }

        /// <summary>当前内嵌字体能否渲染该字符：
        /// 缺字形时字体回退 .notdef 兜底字形，TryGetGlyphs 恒 true 不可用，
        /// 故按“CJK 区段字符 + 已注册字体族名是否含 CJK 字体”启发式判断</summary>
        private static bool CanRender(char c)
        {
            var isCjk = c is >= (char)0x2E80 and <= (char)0x9FFF   // CJK 部首/符号/统一表意
                or >= (char)0x3400 and <= (char)0x4DBF             // 扩展 A
                or >= (char)0xF900 and <= (char)0xFAFF             // 兼容表意
                or >= (char)0x3000 and <= (char)0x303F             // CJK 标点
                or >= (char)0xFF00 and <= (char)0xFFEF;            // 全角字符
            if (!isCjk) return true;

            return FontCollectionLazy.Value.Families.Any(f =>
            {
                var n = f.Name;
                return n.Contains("CJK", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("SC", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("Han", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("Hei", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("Song", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("Ming", StringComparison.OrdinalIgnoreCase);
            });
        }
    }
}
