# SB Framework 迁移踩坑指南

> 基于 weixin 项目（微信会员管理系统）从旧架构迁移到 sb-framework 的实战经验总结。
> 适用于任何将独立项目迁移到 sb-framework 的场景。

---

## 一、后端迁移

### 1.1 项目结构

迁移时必须将旧的单体项目拆分为三层：

```
weixin-api/
  Sb.Apps.Weixin.Models/       # 实体层 — 实现 IEntity 接口
  Sb.Apps.Weixin.Services/     # 服务层 — IModuleInitializer 实现
  Sb.Apps.Weixin.WebApi/       # 控制器层 — [AllowJsonBody] 特性
```

**关键点**：
- Models 层的实体类必须实现 `IEntity` 接口，使用 FreeSql 数据注解
- Services 层的 CRUD 服务继承 `BaseCrudService<T>`
- Services 层的 `IModuleInitializer` 实现类负责注册实体、菜单、系统设置
- WebApi 层的控制器需要加 `[AllowJsonBody]` 特性以支持 JSON body 传参

### 1.2 字段命名规范

旧项目可能使用拼音字段名（如 `huiYuanBianHao`），迁移时必须改为英文：
- 使用 `Column("xxx")` 特性映射数据库列名
- 属性名使用 PascalCase 英文命名

### 1.3 SetupController 404 错误

**现象**：安装向导接口 `/Setup/Status`、`/Setup/Install` 等全部返回 404。

**根因**：sb-framework 的 `SetupController` 位于 `SB.Framework.WebApi` 程序集，但 MVC 默认不扫描该程序集的控制器。

**修复**：在 `SbFrameworkExtensions.cs` 中显式注册框架控制器：
```csharp
mvcBuilder.AddApplicationPart(typeof(SbFrameworkExtensions).Assembly);
```

---

## 二、Docker 部署

### 2.1 Docker 环境检测不可靠

**现象**：Docker 容器内 `SetupService` 无法正确识别 Docker 环境，导致安装向导仍显示 MySQL 选项、数据库密码不自动填充。

**根因**：单一检测方式（如仅检查环境变量）在某些 Docker 配置下失效。

**修复**：实现多重检测机制（任一命中即判定为 Docker 模式）：
1. `SB_IS_DOCKER=1` — 显式标志（start-api.sh 中设置）
2. `DOTNET_RUNNING_IN_CONTAINER=true` — 微软官方镜像内置
3. `/proc/1/cgroup` 包含 `docker` 或 `kubepods`
4. ContentRootPath 以 `/app/` 开头
5. 存在 `/.dockerenv` 文件

### 2.2 Redis 配置缺失导致安装失败

**现象**：安装完成后报错 `No password has been provided but the backend requires one (in SASL/SCRAM-SHA-256)`

**根因**：`DeployHints` API 没有返回 Redis 配置信息，前端未自动填充 Redis 连接字符串。

**修复**：
- 后端 `SetupService.GetDeployHints()` 添加 Redis 配置返回
- 前端 `setup.vue` 接收并自动填充 Redis 主机、端口、实例名

### 2.3 MySQL 选项未移除

**现象**：Docker 部署模式下安装向导仍显示 MySQL 数据库选项。

**根因**：前端 `setup.vue` 没有根据 `isDocker` 标志隐藏 MySQL 选项。

**修复**：Docker 模式下强制使用 PostgreSQL，前端隐藏 MySQL 选项。

### 2.4 站点路径配置错误

**现象**：安装向导中的站点路径不正确。

**根因**：`DeployHints` 返回的路径与 Docker 容器内实际路径不一致。

**修复**：Docker 模式下返回正确的容器内路径：
- API: `/app/api`
- Admin: `/app/admin-ui`
- H5: `/app/user-ui`

---

## 三、前端迁移（admin）

### 3.1 LAYOUT/PARENT 布局映射缺失 — 页面找不到

**现象**：admin 端多个菜单页面显示 "找不到页面"（404），如：
- `/system/devtools/deploy` — 部署管理
- `/system/devtools/log-viewer` — 运行日志
- `/system/devtools/login-log` — 登录日志
- `/system/devtools/background-task` — 后台任务
- `/system/setting` — 系统设置

而 `/system/menu`（菜单管理）等页面正常。

**根因**：前端 `src/router/access.ts` 的 `layoutMap` 缺少 `LAYOUT` 和 `PARENT` 映射。

后端菜单数据中：
- 一级菜单（如 System）的 `Component` 是 `"LAYOUT"` — 需要 `BasicLayout`
- 中间层级（如 DevTools）的 `Component` 是 `"PARENT"` — 需要 `ParentLayout`（仅含 `<RouterView>`）

没有这两个映射时，`generateAccessible` 的组件匹配返回 not-found。

**为什么 menu 页面正常**：`generateAccessible` 对有子路由的一级路由会删除 component，所以二级菜单仍可渲染。但三级菜单（DevTools → deploy）因为布局链不完整导致渲染失败。

**修复**：在 `access.ts` 的 `layoutMap` 中添加：
```typescript
import { defineComponent, h } from 'vue';
import { RouterView } from 'vue-router';

const ParentLayout = defineComponent({
  name: 'ParentLayout',
  render() { return h(RouterView); },
});

const layoutMap = {
  BasicLayout,
  IFrameView,
  LAYOUT: BasicLayout,       // 一级菜单布局
  PARENT: ParentLayout,      // 中间层级容器
};
```

### 3.2 缺少框架页面组件

**现象**：部分菜单页面找不到，因为 weixin-admin 前端项目缺少 sb-framework 自带的管理页面。

**根因**：sb-framework 的 `AdminService`、`EmailService`、`MemberService` 等在后端注册了菜单，但前端没有对应的 Vue 页面组件。

**修复**：从参考项目（如 tgvault）复制缺失的页面和组件：
- `views/deploy/List.vue` — 部署管理
- `views/log-viewer/List.vue` — 运行日志
- `views/login-log/List.vue` — 登录日志
- `views/background-task/List.vue` — 后台任务
- `views/system-setting/` — 系统设置
- `views/ops-tools/index.vue` — 运维工具
- `components/Upload/` — 上传组件（依赖 `api/core/upload.ts`）

### 3.3 依赖包缺失导致构建失败

**现象**：复制页面后 `pnpm build` 报错 `Rollup failed to resolve import`。

**根因**：缺少页面引用的第三方依赖。

**修复**：在 `apps/web-antd/package.json` 中添加：
- `monaco-editor@^0.55.1` — 代码编辑器
- `@guolao/vue-monaco-editor@^1.5.5` — Monaco Vue 封装
- `spark-md5@^3.0.2` — 分片上传 MD5
- `md-editor-v3@^6.4.1` — Markdown 编辑器

### 3.4 turbo 缓存导致构建产物不更新

**现象**：修改源码后 `pnpm turbo build` 使用缓存，构建产物未更新。

**修复**：使用 `npx vite build` 直接构建，绕过 turbo 缓存。

### 3.5 前端构建产物未同步到 Docker 目录

**现象**：修复后页面仍然找不到，因为 Docker 目录里的 admin-ui 是旧版本。

**根因**：构建前端后没有将 `dist/` 复制到 `weixin-docker/admin-ui/html/`。

**修复**：每次构建后同步：
```bash
rm -rf weixin-docker/admin-ui/html/*
cp -r apps/web-antd/dist/* weixin-docker/admin-ui/html/
```

---

## 四、禁用模块菜单残留

### 4.1 禁用模块后菜单仍显示

**现象**：在 `appsettings.json` 中配置了 `Modules:Disabled: ["Email", "Member"]`，但邮件管理和会员管理菜单仍然显示。

**根因**：`DatabaseSeeder.SyncModuleMenus` 方法只负责新增和更新菜单，**从不删除**被禁用模块的旧菜单。菜单在首次安装时已经写入数据库，禁用模块只是不再注册新菜单，但旧菜单记录仍在数据库中。

**修复**：在 `DatabaseSeeder.cs` 的 `SyncModuleMenus` 方法中添加禁用模块菜单清理逻辑：
1. 获取所有模块（含被禁用的）
2. 收集被禁用模块的所有菜单路径（递归含子菜单）
3. 从数据库物理删除这些菜单及其角色关联（`AdminRoleMenu`）

```csharp
// 清理被禁用模块的菜单
if (disabledModules.Length > 0)
{
    var allModules = SetupService.DiscoverModuleInitializers(catalog, null);
    var activeModuleKeys = new HashSet<string>(modules.Select(m => m.ModuleKey));
    var disabledMods = allModules.Where(m => !activeModuleKeys.Contains(m.ModuleKey)).ToList();
    var disabledPaths = new HashSet<string>();
    foreach (var mod in disabledMods)
        foreach (var def in mod.GetMenus())
            CollectMenuPaths(def, disabledPaths);

    if (disabledPaths.Count > 0)
    {
        var staleMenus = fsql.Select<Menu>().DisableGlobalFilter()
            .Where(m => disabledPaths.Contains(m.Path)).ToList();
        if (staleMenus.Count > 0)
        {
            var staleIds = staleMenus.Select(m => m.Id).ToList();
            fsql.Delete<AdminRoleMenu>().Where(rm => staleIds.Contains(rm.MenuId)).ExecuteAffrows();
            fsql.Delete<Menu>().Where(m => staleIds.Contains(m.Id)).ExecuteAffrows();
        }
    }
}
```

### 4.2 appsettings.json 配置结构错误

**现象**：配置了禁用模块但未生效。

**根因**：配置嵌套层级错误。`Features.Modules.Disabled` 是错误的路径，框架读取的是根级别的 `Modules:Disabled`。

**修复**：正确配置结构：
```json
{
  "Features": { "Disabled": [] },
  "Modules": { "Disabled": ["Email", "Member"] }
}
```

---

## 五、安装向导问题

### 5.1 前端未进入安装界面

**现象**：首次启动时 admin 前端不显示安装向导。

**根因**：缺少安装向导相关的组件、路由和路由守卫。

**修复**：
1. 添加安装向导 API 接口（`api/core/setup.ts`）
2. 添加安装向导视图组件（`views/_core/setup/setup.vue`）
3. 在路由守卫（`router/guard.ts`）中添加安装状态检测，未安装时重定向到安装页

### 5.2 Docker 模式下数据库密码未自动填充

**现象**：安装向导中数据库密码字段为空。

**根因**：`DeployHints` API 返回的密码字段未在前端正确接收和填充。

**修复**：前端 `setup.vue` 接收 `hints.database.password` 并赋值给密码输入框。

---

## 六、快速排查清单

迁移项目后按以下顺序排查：

| 步骤 | 检查项 | 命令/方法 |
|------|--------|-----------|
| 1 | 后端编译 | `dotnet build` |
| 2 | Setup 接口可达 | `curl /Setup/Status` 不返回 404 |
| 3 | 安装向导显示 | 浏览器打开 admin，自动跳转安装页 |
| 4 | Docker 环境检测 | `DeployHints` 返回 `isDocker: true` |
| 5 | 数据库密码填充 | 安装向导中密码字段有值 |
| 6 | MySQL 选项移除 | Docker 模式下不显示 MySQL |
| 7 | 前端编译无错误 | `npx vite build` 成功 |
| 8 | 前端构建产物同步 | `weixin-docker/admin-ui/html/` 包含最新 dist |
| 9 | LAYOUT/PARENT 映射 | `access.ts` 的 `layoutMap` 包含这两个键 |
| 10 | 禁用模块菜单清理 | 数据库中无被禁用模块的菜单记录 |
| 11 | 所有菜单可打开 | 逐个导航到每个菜单页面，无 "返回首页" |
| 12 | tar.gz 打包 | `tar -czf sb-xxx-docker.tar.gz weixin-docker/` |

---

## 七、涉及的关键文件

| 文件 | 作用 |
|------|------|
| `SB.Framework.WebApi/Extensions/SbFrameworkExtensions.cs` | 框架初始化，注册控制器 |
| `SB.Framework.WebApi/Extensions/DatabaseSeeder.cs` | 数据库同步，菜单管理 |
| `SB.Framework.Services/SetupService.cs` | 安装向导，Docker 检测 |
| `SB.Framework.Services/AdminService.cs` | 系统管理菜单注册 |
| `SB.Framework.Services/EmailService.cs` | 邮件模块菜单注册 |
| `SB.Framework.Services/MemberService.cs` | 会员模块菜单注册 |
| 前端 `src/router/access.ts` | 路由生成，layoutMap 映射 |
| 前端 `src/router/guard.ts` | 路由守卫，安装状态检测 |
| 前端 `src/views/_core/setup/setup.vue` | 安装向导页面 |
| 前端 `src/views/deploy/List.vue` | 部署管理页面 |
| 项目 `appsettings.json` | 模块禁用配置 |
| Docker `scripts/start-api.sh` | API 启动脚本，环境变量设置 |
