# Changelog

本仓库遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/) 和 [Keep a Changelog](https://keepachangelog.com/zh-CN/)。

## [Unreleased]

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
