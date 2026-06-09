# Changelog

本仓库遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/) 和 [Keep a Changelog](https://keepachangelog.com/zh-CN/)。

## [Unreleased]

## [0.4.6] - 2026-06-10

### Fixed

- 修复 `.NoLicense` 变体（`0.4.4` / `0.4.5`）实际未关闭授权的问题。这两个版本顶着 `.NoLicense` 包名，但产出的 `SB.Framework.WebApi.dll` 仍走 `#if !DISABLE_LICENSE` 分支，注册 `MissingKeyLicenseService` 并挂载 `LicenseMiddleware`，导致下游（如 PM）无 token 的非白名单请求被 `403`（`license_invalid`「授权系统已启用但未检测到公钥」）。`0.4.4`/`0.4.5`（`774cc24`/`5c066e0`）仅 bump 版本号重发，未干净重建 / 未正确传 `-p:DisableLicense=true`，发布的包仍带授权拦截。
- 本版基于当前 HEAD 源码重打（`-p:DisableLicense=true` 同时切换 `PackageId` 后缀、`DISABLE_LICENSE` 编译符号、空公钥嵌入三者，机制一致），删 `bin/obj` 干净重建。`NullLicenseService`（`IsEnabled=false`）使 `UseLicense()` 不再挂载中间件，授权确认关闭。

### Changed

- 全套包版本 `0.4.3 → 0.4.6`（Core / Models / Services / Analyzers / WebApi），统一对齐重发 NoLicense 变体。

## [0.4.3] - 2026-06-10

### Fixed

- 修复 0.4.2 包内程序集版本号未同步的问题。0.4.2 的 NuGet 包（含 NoLicense 变体）实际打入的 `SB.Framework.Core.dll` / `SB.Framework.Models.dll` 程序集版本仍为 `0.4.1.0`（重打包时复用了旧 `bin/obj`，未干净重建），与包版本 `0.4.2` 不一致。任何对着 0.4.2 引用程序集编译的消费者（如 `SB.Feature.AccessToken 0.1.3` 引用 `Core 0.4.2.0`），运行时在 `Assembly.GetExportedTypes()` 解析阶段抛 `FileNotFoundException: SB.Framework.Core, Version=0.4.2.0`（CLR 只能向高版本 roll-forward，不能向低版本回退），导致 PM 生产启动崩溃循环（supervisor BACKOFF）。
- 本版删除 `bin/obj` 干净重建后重新打包发布；程序集版本正确为 `0.4.3.0`，与包版本一致。
- BaGet（`loicsharma/baget`）默认不支持同版本覆盖（同版本 push 被静默跳过），故 bump 到 `0.4.3` 而非重推 `0.4.2`。派生项目升级到 `0.4.3` 即可。

### Added

- AI 诊断日志接口 — `GET /admin/LogViewer/Diag`
  - 支持 MasterKey 鉴权（`X-Master-Key`），配合 Claude Code 端 `saibo-log-diagnostics` skill 使用
  - 参数:`date`(yyyyMMdd,逗号分隔多个,默认今天)/ `level`(info/warning/error/all,默认 `warning,error`)/ `tail`(每文件尾部 N 行,默认 1000)/ `contains`(关键字过滤,大小写不敏感)
  - 响应:`application/x-ndjson`(直接日志流,无 Result wrapper)
  - 摘要放响应头 `X-Diag-Dates/Levels/Tail/Files/FilesScanned/TotalLines/ReturnedLines/Contains`,不污染 body
- `LogViewerService.QueryDiag` 方法 + `DiagQueryResult` 类型

### Changed

- Serilog 日志输出格式从纯文本切换为 NDJSON(CLEF,`Serilog.Formatting.Compact.CompactJsonFormatter`)
  - 文件后缀 `.txt` → `.ndjson`,文件命名仍为 `{level}-{yyyyMMdd}.ndjson`
  - 字段标准化:`@t`(ISO 8601 UTC) / `@mt`(消息模板) / `@l`(级别,Information 默认省略) / `@x`(异常) / `SourceContext`(模块来源,用于 framework vs 派生项目责任归属) / 自定义属性
  - `LogViewerService.GetFiles` 同时列 `*.ndjson` 和 `*.txt`,保留归档目录里旧 `.txt` 文件可读
- `appsettings.json` / `appsettings.Development.json` Serilog 配置同步更新
- `SB.Feature.AccessToken` 0.1.3 → 0.1.4：对着框架 `0.4.3` 重新编译（引用 `Core/Models 0.4.3.0`）；API Key 随机串编码由 base64url 改为 hex（纯 `0-9a-f`，shell 安全无特殊字符）。

## [0.4.2] - 2026-06-09

### Fixed

- 修复 `*.NoLicense` 变体包未真正关闭授权的问题。已发布的 `SB.Framework.WebApi.NoLicense 0.4.1` 系用旧源码打包，运行时仍按授权版逻辑执行 `MissingKeyLicenseService`（`IsEnabled=true`），导致引用该包的派生项目（PM 生产）所有非白名单 `/admin/*` 请求被 `LicenseMiddleware` 拦截返回 HTTP 403「授权系统已启用但未检测到公钥」。
- 本版从当前源码（`SbFrameworkExtensions` 中 `#if !DISABLE_LICENSE … #else NullLicenseService()` 门已正确）重新打包发布；NoLicense 变体 `IsEnabled=false`，`UseLicense()` 正确跳过中间件。
- 仅版本号 bump（0.4.1 → 0.4.2），无源码改动；双变体（正常版 + NoLicense）一并重新发布到 BaGet。派生项目升级到 `0.4.2` 即可。

## [0.1.0-preview.1] - 2026-05-11

### Added

- 仓库初始化：从原 `sb-api` 仓库迁移代码到新 `saibo/framework/sb-framework` 仓库
- 5 个 NuGet 包工程
  - `Sb.Framework.Core` — 基础工具
  - `Sb.Framework.Models` — 基础模型
  - `Sb.Framework.Services` — 基础服务 + 扩展点
  - `Sb.Framework.WebApi` — Web API 基础设施 + AddSbFramework/UseSbFramework 装配
  - `Sb.Framework.Analyzers` — Roslyn 编译期约束
- 2 个新 hook 扩展点
  - `IFreeSqlConfigurator` — 派生项目配置 FreeSql（GlobalFilter / Mapper 等）
  - `IStartupSyncHook` — 派生项目注入启动后异步任务
- 调试启动壳 `Sb.Framework.DevHost`
- 治理基础设施
  - `README.md` / `CONTRIBUTING.md` / `CODEOWNERS`
  - `.gitlab-ci.yml`（编译 + 命名规范检查 + 自动打包 + tag 推 NuGet）
  - MR / Issue 模板
  - `docs/naming-conventions.md` — 12 维度命名规范
