# Saibo 命名规范

> 全公司 saibo group 内所有项目共享。基座 / 派生应用 / 工具都必须遵守。

**版本**：v1.0 / 2026-05-11

---

## 0. 总体原则

| 原则 | 适用场景 |
|------|---------|
| **URL 友好** → `kebab-case` 或 `SNAKE_CASE` | GitLab 路径 / git remote / shell 变量 / 前端路由 |
| **.NET 友好** → `PascalCase` 含点分段 | C# namespace / 类名 / PackageId / csproj |
| **数据库友好** → `snake_case` 全小写 | 表名 / 列名 |
| **配置友好** → `PascalCase` | appsettings.json key |
| **域优先** | 组织 → 类别 → 应用 → 模块 → 业务域 |

---

## 1. GitLab Group

```
顶层 group     <organization>   →  saibo
二级 group     <category>       →  framework | apps | tooling | docs | infra | archive
```

**保留 group 列表**：
- `framework` — 基座 / 通用模块 / 独立基础设施
- `apps` — 派生应用
- `tooling` — 工具、脚手架、CLI
- `docs` — 文档站
- `infra` — 基础设施代码（Ansible / Terraform / etc.）
- `archive` — 已废弃但保留的项目

新增二级 group 必须先发 issue 讨论。

---

## 2. GitLab Project（仓库）

**模式**：`<category-prefix>-<role>[-<variant>]` 全小写 `kebab-case`

| 基座组 | 派生组 |
|--------|--------|
| `saibo/framework/sb-framework` | `saibo/apps/sb-apps-pm` |
| `saibo/framework/sb-framework-admin` | `saibo/apps/sb-apps-pm-admin` |
| `saibo/framework/sb-framework-chat` | `saibo/apps/sb-apps-pm-mcp` |
| `saibo/framework/sb-framework-cli` | `saibo/apps/sb-apps-erp` (未来) |

**仓库命名规则**：
- 必带 `sb-` 前缀（全局唯一性，方便未来跨组织 mirror）
- 派生应用以 `sb-apps-<app>` 起头（app 是应用代号，如 pm / erp / crm）
- 角色后缀必有：`api` 隐含（裸名表示后端）/ `admin`（前端）/ `chat` / `mcp` / `cli` / `docs`

---

## 3. .NET csproj / Assembly 名

**模式**：`Sb.<Category>.<Role>[.<Layer>][.<Variant>]` `PascalCase` 含点

```
Sb.Framework.Core              ← 基座，跨层共享原语
Sb.Framework.Models            ← 基座模型
Sb.Framework.Services          ← 基座服务 + hook 接口
Sb.Framework.WebApi            ← 基座 Web 基础设施
Sb.Framework.Analyzers         ← Roslyn 分析器
Sb.Framework.Chat              ← 基座 Chat 模块
Sb.Framework.Chat.Telegram     ← Chat 的 Telegram 适配器
Sb.Framework.DevHost           ← 调试启动壳（不发布）

Sb.Apps.Pm.Models              ← PM 派生模型
Sb.Apps.Pm.Services            ← PM 派生服务
Sb.Apps.Pm.WebApi              ← PM 派生 Web API（含启动壳）
```

**层名固定枚举**（不允许自由发挥）：

| 层 | 含义 |
|----|------|
| `Core` | 跨层共享原语（Util / Attribute / Extension） |
| `Models` | Entity / DTO / Enum |
| `Services` | 业务服务 |
| `WebApi` | ASP.NET Core Web 项目 |
| `Analyzers` | Roslyn 分析器 |
| `DevHost` | 调试启动壳（不发布 NuGet） |
| `Tests` | 测试工程 |

---

## 4. C# Namespace

= `RootNamespace`（csproj 设置）+ 子目录段 PascalCase

```csharp
// 工程: Sb.Apps.Pm.Models
// 文件: Projects/Project.cs
namespace Sb.Apps.Pm.Models.Projects;
public class Project { ... }
```

**业务域子目录名 = 复数 PascalCase**：`Projects` / `Requirements` / `Tasks` / `MindMaps`

---

## 5. NuGet PackageId

= **完全等同 Assembly 名**

```xml
<PackageId>Sb.Framework.WebApi</PackageId>
```

**版本号严格 SemVer**：

| 形态 | 用途 |
|------|------|
| `0.x.y-preview.N` | 开发期，破坏性变更频繁 |
| `0.x.y` | 内部稳定，API 未冻结 |
| `1.0.0` | 正式发布，API 冻结 |
| `1.x.y` | 向后兼容变更 |
| `2.0.0` | 不兼容大版本（必须 CHANGELOG.md 记录） |

---

## 6. 数据库表名

**模式**：`<scope>_<entity>` 全小写 `snake_case` 单数

| 类型 | 前缀 | 示例 |
|------|------|------|
| 基座 | `sb_` | `sb_admin` / `sb_menu` / `sb_login_log` / `sb_license_client` |
| 派生 PM | `sb_pm_` | `sb_pm_project` / `sb_pm_requirement` / `sb_pm_task_item` |
| 派生 ERP（未来） | `sb_erp_` | `sb_erp_order` / `sb_erp_customer` |

**特殊后缀**：
- `_log` — 历史/日志表（`sb_pm_task_item_log`）
- 关联表：`<entity1>_<entity2>`（`sb_admin_role_menu`）

**C# 代码端**：必须用 `[Table(Name = "...")]` 显式声明，**禁止依赖 FreeSql 自动推导**。

---

## 7. 数据库列名

- **列名**：`snake_case` 全小写
- **C# 属性**：`PascalCase`

```csharp
[Column(Name = "is_deleted")]
public bool IsDeleted { get; set; }
```

> ⚠️ 现存代码部分用 PascalCase 列名（如 `IsDeleted`）— 历史遗留，新表统一新规范。

---

## 8. appsettings.json key

全 `PascalCase`，嵌套跟 .NET POCO 类对齐：

```json
{
  "ProductCn": "...",
  "DbConfig": {
    "DbType": "MySql",
    "Server": "127.0.0.1"
  },
  "ProjectAssemblyName": "Sb.Apps.Pm.Services"
}
```

---

## 9. 环境变量

`SCREAMING_SNAKE_CASE`，按域前缀：

```
SB_<DOMAIN>_<KEY>          # 基座
SB_PM_<DOMAIN>_<KEY>       # 派生 PM
SB_ERP_<DOMAIN>_<KEY>      # 派生 ERP（未来）
```

**例**：
```bash
SB_LICENSE_KEY_DIR=/path/to/keys
SB_NUGET_API_KEY=glpat-xxx
SB_PM_DB_PASSWORD=xxx
```

---

## 10. URL / API 路径

**后端 Controller**：
- 默认 `[Route("[controller]/[action]")]` 自动加 `admin/` 前缀 → `/admin/<Controller>/<Action>`
- 公开 API：`[Route("/api/[controller]/[action]")]`
- Action 名 `PascalCase`

**前端路由 + view 目录**：
- `kebab-case` 复数：`/projects` / `/requirements` / `/mind-maps`

---

## 11. Git 分支 / 标签

**分支**：

| 命名 | 用途 |
|------|------|
| `main` | 主分支（受保护，禁直推） |
| `feature/<short-desc>` | 功能开发 |
| `fix/<issue-id>-<short-desc>` | bug 修复（带 issue id） |
| `refactor/<short-desc>` | 重构 |
| `hotfix/<short-desc>` | 生产紧急修复 |
| `release/<version>` | 发版分支（仅需要 freeze 时） |

**标签**：

| 命名 | 用途 |
|------|------|
| `v0.1.0` / `v0.1.0-preview.1` | NuGet 包发版 |
| `pm-v1.2.3` | 派生应用 PM 部署版本 |
| `pm-v1.2.3-2026-05-11` | 派生应用日期版本（如需要） |

---

## 12. Commit Message

中文 Conventional Commits：

```
<type>(<scope>): <subject>

[optional body]

[optional footer]
```

**type 枚举**：
- `feat` 新功能
- `fix` bug 修复
- `refactor` 重构（不增/不减功能）
- `chore` 依赖/构建/CI
- `docs` 文档
- `test` 测试
- `build` 构建系统/发包
- `style` 代码风格（格式化等）
- `perf` 性能
- `revert` 撤回 commit

**scope** 可选：业务域或工程名。
- 基座：`feat(license): ...` / `fix(cache): ...` / `build(nuget): ...`
- 派生：`feat(projects): ...` / `fix(chat): ...`

**subject**：中文，≤ 50 字。

---

## 13. 规范的固化机制

| 规范 | 固化方式 |
|------|---------|
| Assembly / Namespace | `Sb.Framework.Analyzers` Roslyn 规则（编译期 fail） |
| 表前缀 | Analyzer `SB0010` 检查 `[Table(Name = "...")]` 必须 `sb_` 或 `sb_<app>_` 开头 |
| 派生业务关键字不入基座 | `.gitlab-ci.yml` 的 `lint:naming` job grep 检查 |
| commit message | git hook `commit-msg` 拦截不规范 commit |
| NuGet 发包版本一致性 | CI 校验 tag 跟 csproj `<Version>` 一致 |

任何规范变更必须：
1. 更新本文档
2. 同步更新 Analyzer / CI 脚本 / git hook
3. 提 MR 经 root 审批

---

## 14. 完整对照表（速查）

| 层级 | 基座（Framework） | 派生（Apps/PM） |
|------|-----------------|---------------|
| Group | `saibo/framework` | `saibo/apps` |
| Project | `sb-framework` | `sb-apps-pm` |
| 仓库 URL | `git@gitlab.saibo.club:saibo/framework/sb-framework.git` | `git@gitlab.saibo.club:saibo/apps/sb-apps-pm.git` |
| Assembly | `Sb.Framework.WebApi` | `Sb.Apps.Pm.WebApi` |
| Namespace | `Sb.Framework.WebApi.Controllers` | `Sb.Apps.Pm.WebApi.Controllers.Projects` |
| NuGet | `Sb.Framework.WebApi` | （不发包，仅作为消费方） |
| 表名 | `sb_admin` | `sb_pm_project` |
| appsettings 顶级 key | `Sb.Framework`（如有） | `Sb.Apps.Pm`（如有） |
| 环境变量 | `SB_LICENSE_KEY_DIR` | `SB_PM_DB_PASSWORD` |
| URL admin | `/admin/Auth/Login` | `/admin/Project/List` |
| URL api | `/api/Setup/Status` | `/api/Project/Webhook` |
| Git 分支 | `feature/license-cache` | `feature/project-config-import` |
| Git tag | `v0.1.0-preview.1` | `pm-v1.2.3` |

---

## 修订历史

| 版本 | 日期 | 修订内容 |
|------|------|---------|
| v1.0 | 2026-05-11 | 初版，确立 saibo group 治理基线 |
