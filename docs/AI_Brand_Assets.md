# SB 品牌资产生成与使用指南（AI_Brand_Assets.md）

> 框架按 SB 品牌风格（六边形节点 + 青→蓝渐变 `#22D3EE→#3B82F6` + 单字标），
> 从产品名直接生成派生项目全套图标与 Logo，无需设计师出图。
> 形态规范与 `sb-framework-chat/brand/cyber-sb` 品牌包一致。

## 一、能力总览

| 产物 | 路径 | 说明 |
|------|------|------|
| favicon.ico（16/32/48 多尺寸） | `/storage/favicon.ico` | 与上传 Logo 流程同路径，覆盖式更新 |
| favicon-16x16.png / favicon-32x32.png | `/storage/` | 透明底六边形 mark |
| apple-touch-icon.png（180） | `/storage/` | 同上 |
| android-chrome-192x192 / 512x512 | `/storage/` | 同上 |
| logo.png | `/storage/images/logo.png` | 应用大图（app-info.json 引用） |
| mark-dark / mark-light / mark-transparent.svg | `/storage/brand/` | 矢量原件，任意缩放不糊 |
| horizontal-dark.svg / horizontal-light.svg | `/storage/brand/` | 横版锁形（mark + 产品名），登录页/文档页眉 |
| horizontal-dark.png | `/storage/brand/` | 横版锁形位图 |
| icon-512-dark.png | `/storage/brand/` | 深色圆角底应用图标 |
| brand.json | `/storage/brand/` | 品牌规格（单字标/配色/生成时间），重新生成基准 |

## 二、三个使用入口

### 1. 安装向导（默认，零操作）

安装时**未上传 Logo** 即自动生成：单字标取自 `ProductEn`（单词首字母，最多 2 个，
如 `tgvault` → `TG`；`my-shop` → `MS`），产品名取 `ProductCn`。
上传了 Logo 则沿用原 FaviconGenerator 流程，两者产物路径完全一致。

### 2. admin 运维工具页（重新生成/微调）

`系统管理 → 开发工具 → 运维工具 → 品牌图标` Tab：

- 可改：单字标（1-3 字符）、渐变起止色
- 实时预览明暗 mark 与横版锁形；保存后自动刷新缓存
- 「打包下载 zip」导出 brand/ 全部矢量原件给设计/前端二次使用

接口（JwtAdmin 认证，`[FromForm]` 约定见 AI_Conventions）：

| 接口 | 方法 | 说明 |
|------|------|------|
| `/Brand/Current` | GET | 当前规格 + generated 标志 + 资产清单 |
| `/Brand/Regenerate` | POST | form: `monogram`（必填 1-3 字符）、`accentFrom`、`accentTo` |
| `/Brand/Download` | GET | brand/ 目录打包 zip |

### 3. 代码调用（派生项目后端）

```csharp
using SB.Framework.Core.Brand;

var spec = BrandIconGenerator.DefaultSpec("tgvault", "金库管理系统");
spec.AccentFrom = "#22D3EE";   // 可微调品牌色
spec.AccentTo   = "#3B82F6";
BrandIconGenerator.GenerateAll(storageDir, spec);   // 全套覆盖式生成

var current = BrandIconGenerator.LoadSpec(storageDir);   // 读取已生成规格
```

`storageDir` 约定 = `{ContentRootPath}/wwwroot/storage`（与 SetupService 一致）。

## 三、技术要点与边界

- **渲染**：纯 `SixLabors.ImageSharp.Drawing` 程序化绘制（六边形多边形 + 线性渐变画刷 +
  文字居中），无 System.Drawing，Docker Linux 容器可用；SVG 为字符串模板，零成本输出。
- **字体**：NuGet 包内嵌 `Roboto-Bold.ttf`（Apache 2.0 可再分发，含 license 文件），
  Docker 容器无系统字体也能正常渲染单字标与英文名。
- **中文降级**：内嵌字体暂无 CJK 字形。横版 PNG 检测到中文产品名且无 CJK 字体时，
  自动降级渲染英文名 + "SB FRAMEWORK" 副标（SVG 版不受影响，浏览器自带中文字体）。
  若需 PNG 出中文：向 `SB.Framework.Core/Brand/Fonts/` 增加中文字体文件
  （推荐 Noto Sans SC Bold，OFL 许可）并在 csproj EmbeddedResource 中追加即可，
  字体族名含 SC/CJK/Han/Hei 等关键字会被自动识别启用。
- **favicon 同步**：生成会覆盖 `/storage/favicon.ico` 等文件，前端/浏览器有 favicon 缓存，
  重新生成后需强刷（Ctrl+F5）才能看到变化。
- **与上传 Logo 的关系**：两个流程产物路径一致，随时可通过运维页重新生成切回品牌风格，
  也可重新上传 Logo 覆盖（安装向导重新安装时选择）。

## 四、派生项目接入清单

1. 什么都不做 —— 安装完成即有品牌风格 favicon（默认入口已生效）
2. 需要自定义单字标/配色 —— 运维工具页改后「重新生成」
3. 需要矢量原件（印刷/推广物料）—— 运维工具页「打包下载 zip」
4. 登录页使用横版锁形 —— `<img src="/storage/brand/horizontal-dark.svg">`
5. 前端 favicon 引用保持 `/storage/favicon.ico` 不变（生成器保证路径稳定）

## 五、代码位置

| 文件 | 职责 |
|------|------|
| `SB.Framework.Core/Brand/SbBrandSpec.cs` | 品牌规格参数（单字标/配色/明暗底） |
| `SB.Framework.Core/Brand/BrandSvgBuilder.cs` | SVG 矢量模板（mark / 横版锁形） |
| `SB.Framework.Core/Brand/BrandIconGenerator.cs` | 位图渲染 + 全套产物编排 + 规格读写 |
| `SB.Framework.Core/Brand/Fonts/Roboto-Bold.ttf` | 内嵌字体（Apache 2.0） |
| `SB.Framework.Core/Util/FaviconGenerator.cs` | 上传 Logo 流程 + 共用的多尺寸 ICO 编码 |
| `SB.Framework.WebApi/Controllers/BrandController.cs` | 运维页三个接口 |
| `SB.Framework.Services/SetupService.cs` | 安装向导自动生成钩子（第 7 步） |
| admin `views/brand/index.vue` | 品牌图标 Tab 页 |
