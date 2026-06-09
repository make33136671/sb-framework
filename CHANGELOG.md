# Changelog

本仓库遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/) 和 [Keep a Changelog](https://keepachangelog.com/zh-CN/)。

## [Unreleased]

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
