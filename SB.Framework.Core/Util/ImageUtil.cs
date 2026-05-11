using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

namespace SB.Framework.Core.Util
{
    /// <summary>
    /// 图片处理工具
    /// </summary>
    public class ImageUtil
    {
        /// <summary>
        /// 在图片上加文字
        /// </summary>
        /// <param name="tplPath">背景图路径</param>
        /// <param name="newFilePath">新文件路径</param>
        /// <param name="text">文本</param>
        /// <param name="x">坐标x</param>
        /// <param name="y">坐标y</param>
        /// <param name="fontSize">字体大小</param>
        public void ImageDrawText(string tplPath, string newFilePath, string text, float x = 0, float y = 0, float fontSize = 60)
        {
            using (Image image = Image.Load(tplPath))
            {
                var fonts = new FontCollection();
                var mainFont = fonts.Add($"{Directory.GetCurrentDirectory()}/wwwroot/tpl/MicrosoftYaHei.ttf");

                image.Mutate(ctx => ctx.DrawText(text, new Font(fonts.Families.First(), fontSize), Color.Black, new PointF(x, y)));
                string filePath = newFilePath.Substring(0, newFilePath.LastIndexOf('/'));
                if (!Directory.Exists(filePath)) {
                    Directory.CreateDirectory(filePath);
                }

                image.Save(newFilePath);
            }
        }
    }
}
