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
