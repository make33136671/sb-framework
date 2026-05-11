# 贡献 sb-framework

> 在提 MR 前必读。基座项目对规范严格 — 否则会污染所有派生项目。

## 1. 基座代码三不准

| 禁止 | 检查机制 |
|------|---------|
| **不准引入派生业务关键字** — 基座代码中不允许出现 `Project` / `Requirement` / `TaskItem` / `MindMap` / 等 PM 专有概念 | CI `scripts/check-pollution.sh` |
| **不准改 `SB.Framework.*` 顶层命名空间** | Code Review |
| **不准在基座表用 `sb_pm_*` / `sb_erp_*` 等派生前缀** — 基座表只能 `sb_<entity>` | Roslyn `SB0010` 分析器 |

## 2. PR 必经流程

1. **从 `main` 拉分支**：`feature/<short>` / `fix/<issue>-<short>` / `refactor/<short>`
2. **本地通过编译 + 测试**：`dotnet build sb-framework.sln`
3. **提交规范**：中文 Conventional Commits（`feat(license): xxx` / `fix(cache): xxx`）
4. **MR 描述按模板填写**（GitLab 自动加载 `.gitlab/merge_request_templates/default.md`）
5. **至少 1 个 approver**（CODEOWNERS 自动指派）
6. **CI 全绿才能合**：编译 ✓ + 测试 ✓ + 污染检查 ✓ + 命名空间检查 ✓

## 3. 版本号策略

- `0.x.y-preview.N` — 开发期（破坏性变更频繁）
- `0.x.y` — 内部稳定（仍未冻结 API）
- `1.0.0` — 正式发布（API 冻结，破坏性变更必须升主版本）
- `<major>.<minor>.<patch>[-<prerelease>.<N>]` — 严格 SemVer

**发版触发**：在 main 上打 tag `v0.1.0-preview.2` → GitLab CI 自动 `dotnet pack` + push 到 group NuGet feed。

## 4. 命名空间扩展 — 加新功能时

| 场景 | 放哪 |
|------|------|
| 新增工具方法 | `SB.Framework.Core/Util/` |
| 新增中间件 | `SB.Framework.Core/Middleware/` |
| 新增基础 Entity（如 `AuditLog`） | `SB.Framework.Models/` |
| 新增基础 Service | `SB.Framework.Services/` |
| 新增基础 Controller / 装配 hook | `SB.Framework.WebApi/Controllers/` 或 `Extensions/` |
| 新增 Roslyn 分析器 | `SB.Framework.Analyzers/`（必须注册 `SB<NNNN>` 错误码） |

**没把握时**：跑 `git grep -rn "<新名字>"` 看是否跟派生项目（在 NuGet 包消费方仓库里）冲突。

## 5. 派生业务怎么办

派生业务（PM / ERP / CRM ...）**不在本仓库**。如果你写的代码涉及"项目 / 需求 / 订单"等具体业务概念，应该贡献到对应的派生仓库（`saibo/apps/sb-apps-<app>`），不是这里。

判断：
- 我加的功能 / 类 / 表，**所有派生项目都用得上吗**？
  - 是 → 基座（本仓库）
  - 不是 → 派生仓库

## 6. License key 处理

- `license_*.key` 不入仓（`.gitignore` 已排除 private key）
- 开发环境用 `Directory.Build.props` 配置的默认 key dir（项目根上一层 `..`）
- CI / 生产构建用 `SB_LICENSE_KEY_DIR` 环境变量

## 7. 跟上游同步

本仓库**就是**上游 — 没有更上游了。所有 PR 直接合到 main。

## 8. 文档同步

任何**对外接口变更**（公共类签名 / Controller endpoint / hook 接口）必须同步：
- `CHANGELOG.md`
- 如涉及命名规范变更：`docs/naming-conventions.md`

## 9. 不允许的 commit

- 直推 main（main 受保护）
- 跳过 CI（`--no-verify` 等）
- 上传敏感信息（key / token / 数据库 password）
- 在 commit message 用占位符（"WIP" / "fix" 没 scope）
