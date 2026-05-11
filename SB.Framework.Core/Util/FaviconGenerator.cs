using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using System.Text.Json;

namespace SB.Framework.Core.Util
{
    /// <summary>
    /// 从单张源图生成网站所需的全套图标文件
    /// </summary>
    public static class FaviconGenerator
    {
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
        /// 从 Base64 字符串生成全套图标到指定目录
        /// </summary>
        public static void GenerateFromBase64(string base64, string storageDir)
        {
            var commaIdx = base64.IndexOf(',');
            var raw = commaIdx >= 0 ? base64[(commaIdx + 1)..] : base64;
            var bytes = Convert.FromBase64String(raw);

            using var sourceImage = Image.Load(bytes);
            Generate(sourceImage, storageDir);
        }

        /// <summary>
        /// 从文件路径生成全套图标
        /// </summary>
        public static void GenerateFromFile(string sourceFilePath, string storageDir)
        {
            using var sourceImage = Image.Load(sourceFilePath);
            Generate(sourceImage, storageDir);
        }

        private static void Generate(Image sourceImage, string storageDir)
        {
            Directory.CreateDirectory(storageDir);
            var imagesDir = Path.Combine(storageDir, "images");
            Directory.CreateDirectory(imagesDir);

            // 1. 保存原始 logo（等比缩放到最大 512px）
            using (var logo = sourceImage.Clone(ctx =>
            {
                if (sourceImage.Width > 512 || sourceImage.Height > 512)
                    ctx.Resize(new ResizeOptions
                    {
                        Size = new Size(512, 512),
                        Mode = ResizeMode.Max
                    });
            }))
            {
                logo.SaveAsPng(Path.Combine(imagesDir, "logo.png"));
            }

            // 2. 生成各尺寸 PNG
            foreach (var (fileName, size) in PngIcons)
            {
                using var resized = sourceImage.Clone(ctx =>
                    ctx.Resize(new ResizeOptions
                    {
                        Size = new Size(size, size),
                        Mode = ResizeMode.Max
                    }));
                resized.SaveAsPng(Path.Combine(storageDir, fileName));
            }

            // 3. 生成 favicon.ico（多尺寸 PNG 嵌入，手动写 ICO 格式）
            GenerateIco(sourceImage, Path.Combine(storageDir, "favicon.ico"));
        }

        /// <summary>
        /// 手动生成多尺寸 favicon.ico（ICO 格式：6字节头 + N*16字节目录 + N*PNG数据块）
        /// ImageSharp 3.x 不含 IcoEncoder，使用 BinaryWriter 手写
        /// </summary>
        private static void GenerateIco(Image source, string outputPath)
        {
            var pngEncoder = new PngEncoder();
            var pngBlocks = new List<byte[]>();

            foreach (var size in IcoSizes)
            {
                using var resized = source.Clone(ctx =>
                    ctx.Resize(new ResizeOptions
                    {
                        Size = new Size(size, size),
                        Mode = ResizeMode.Max
                    }));

                using var ms = new MemoryStream();
                resized.Save(ms, pngEncoder);
                pngBlocks.Add(ms.ToArray());
            }

            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fs);

            // ICO 文件头 (6 bytes)
            writer.Write((ushort)0);                  // 保留，必须为 0
            writer.Write((ushort)1);                  // 类型：1 = ICO
            writer.Write((ushort)IcoSizes.Length);    // 图像数量

            // 计算每个 PNG 块的偏移量
            // 文件头 6 bytes + 目录条目 16 bytes * N
            int dataOffset = 6 + 16 * IcoSizes.Length;

            // 目录条目 (每个 16 bytes)
            for (int i = 0; i < IcoSizes.Length; i++)
            {
                int size = IcoSizes[i];
                byte sizeField = size >= 256 ? (byte)0 : (byte)size; // 256 用 0 表示
                writer.Write(sizeField);              // 宽度
                writer.Write(sizeField);              // 高度
                writer.Write((byte)0);                // 调色板颜色数（0=不使用调色板）
                writer.Write((byte)0);                // 保留
                writer.Write((ushort)1);              // 颜色平面数
                writer.Write((ushort)32);             // 每像素位数
                writer.Write((uint)pngBlocks[i].Length); // 图像数据大小
                writer.Write((uint)dataOffset);       // 图像数据偏移
                dataOffset += pngBlocks[i].Length;
            }

            // 写入各 PNG 数据块
            foreach (var block in pngBlocks)
            {
                writer.Write(block);
            }
        }

        /// <summary>
        /// 生成 app-info.json
        /// 包含产品基本信息，前端运行时拉取展示（左上角 Logo / 标题 / 授权页产品信息块等）
        /// </summary>
        public static void GenerateAppInfo(string storageDir, string nameEn, string nameCn, string domain = "")
        {
            var appInfo = new
            {
                nameEn,
                nameCn,
                domain,
                logo = "/storage/images/logo.png",
                favicon = "/storage/favicon.ico"
            };

            var json = JsonSerializer.Serialize(appInfo, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            File.WriteAllText(Path.Combine(storageDir, "app-info.json"), json);
        }
    }
}
