using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Core;
using SB.Framework.Core.Brand;
using SB.Framework.Core.Util;
using System.IO.Compression;
using SB.Framework.Models;

namespace SB.Framework.WebApi.Controllers
{
    /// <summary>
    /// 品牌资产（/Brand/*，JwtAdmin 认证）。
    /// 按 SB 品牌风格（六边形 + 青→蓝渐变 + 单字标）生成/重新生成项目全套图标：
    /// favicon 集（storage 根，与安装向导产物同路径）+ brand/ 目录（SVG 原件、PNG 大图、brand.json 规格）。
    /// 使用文档见 docs/AI_Brand_Assets.md。
    /// </summary>
    [ApiController]
    [Authorize(AuthenticationSchemes = "JwtAdmin")]
    [Route("[controller]/[action]")]
    public class BrandController : Controller
    {
        private readonly IWebHostEnvironment _env;

        public BrandController(IWebHostEnvironment env)
        {
            _env = env;
        }

        private string StorageDir => Path.Combine(_env.ContentRootPath, "wwwroot", "storage");

        /// <summary>当前品牌规格与资产清单（未生成过时返回默认规格、generated=false）</summary>
        [HttpGet]
        public Result Current()
        {
            var spec = BrandIconGenerator.LoadSpec(StorageDir);
            if (spec == null)
                return Result.Success(new { generated = false, spec = BrandIconGenerator.DefaultSpec(null, null) });

            return Result.Success(new
            {
                generated = true,
                spec,
                assets = ListAssets(),
            });
        }

        /// <summary>
        /// 重新生成全套品牌图标（form-urlencoded）。
        /// monogram 必填（1-3 字符）；accentFrom/accentTo 可选（#22D3EE/#3B82F6 默认）。
        /// </summary>
        [HttpPost]
        public Result Regenerate(
            [FromForm] string monogram,
            [FromForm] string? accentFrom,
            [FromForm] string? accentTo)
        {
            var mono = (monogram ?? "").Trim().ToUpperInvariant();
            if (mono.Length is < 1 or > 3)
                return Result.Fail("单字标必须是 1-3 个字符");

            var current = BrandIconGenerator.LoadSpec(StorageDir)
                ?? BrandIconGenerator.DefaultSpec(null, null);
            current.Monogram = mono;
            if (!string.IsNullOrWhiteSpace(accentFrom)) current.AccentFrom = accentFrom!.Trim();
            if (!string.IsNullOrWhiteSpace(accentTo)) current.AccentTo = accentTo!.Trim();
            current.GeneratedAt = AppTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

            var saved = BrandIconGenerator.GenerateAll(StorageDir, current);
            return Result.Success(new
            {
                spec = saved,
                assets = ListAssets(),
            });
        }

        /// <summary>打包下载 brand/ 目录（zip，含 SVG 原件 / PNG / brand.json）</summary>
        [HttpGet]
        public IActionResult Download()
        {
            var brandDir = Path.Combine(StorageDir, "brand");
            if (!Directory.Exists(brandDir))
                return NotFound("品牌资产尚未生成");

            var ms = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in Directory.GetFiles(brandDir))
                {
                    zip.CreateEntryFromFile(file, Path.GetFileName(file));
                }
            }
            ms.Position = 0;
            return File(ms, "application/zip", "brand-assets.zip");
        }

        private object ListAssets()
        {
            var brandDir = Path.Combine(StorageDir, "brand");
            var files = Directory.Exists(brandDir)
                ? Directory.GetFiles(brandDir).Select(Path.GetFileName).Order().ToArray()
                : Array.Empty<string>();
            return new
            {
                preview = new[] { "/storage/brand/mark-dark.svg", "/storage/brand/mark-light.svg", "/storage/brand/horizontal-dark.svg" },
                files,
            };
        }
    }
}
