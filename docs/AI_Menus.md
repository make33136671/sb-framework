# SB Framework 菜单功能使用规范（AI_Menus.md）

> 本文档面向 AI 与派生项目开发者。派生项目“菜单丢失”问题的根因几乎都是：**绕开框架的菜单声明机制，手写菜单数据与框架互相覆盖**。读完本文遵守规范即可避免。
> 所有结论均给出代码依据（文件 / 方法），基于当前 sb-framework 源码。

---

## 1. 菜单工作机制

### 1.1 数据流（唯一正道）

```
IModuleInitializer.GetMenus()            ← 菜单唯一声明来源（C# 代码）
        │  模块发现：SetupService.DiscoverModuleInitializers()
        │  （扫描顺序：框架 Services → SB.Feature.*（短名字典序）→ 项目程序集最后）
        ▼
安装期：SetupService.SeedData → EnsureMenus
        仅当 SB_Menu 表为空时执行；已有菜单则跳过，只补 super 角色关联
        ▼
启动期：SbFrameworkExtensions（isInstalled && !smoke）→ DatabaseSeeder.SeedDatabase
        → SyncModuleMenus（DatabaseSeeder.cs:75）每次启动幂等执行
        ▼
数据库 SB_Menu 表（SB.Framework.Models/Menu.cs）+ SB_AdminRoleMenu（角色授权）
        ▼
前端 GET /Menu/All（MenuController → AdminService.MenuAll）
        按角色查 AdminRoleMenu → 补全祖先菜单 → 组装 VbenMenu 树（空 Component 自动转 "PARENT"）
        用户级缓存 30 分钟（cacheKey: menu:user:{adminId}:role:{roleId}）
        ▼
sb-framework-admin apps/web-antd/src/router/access.ts
        getAllMenusApi() → generateAccessible
        pageMap = import.meta.glob('../views/**/*.vue')
        layoutMap = { LAYOUT: BasicLayout, PARENT: ParentLayout, ... }
        ▼
packages/utils generate-routes-backend.ts convertRoutes()
        Component 命中 layoutMap → 布局组件；否则按 views 相对路径查 pageMap（自动补 .vue）
        查不到 → console.error('route component is invalid') + 兜底 not-found 组件（404 根因之一）
```

### 1.2 同步规则（SyncModuleMenus，DatabaseSeeder.cs:75-163）

| 规则 | 行为 | 依据 |
|------|------|------|
| **Path 是唯一键** | 新增/更新/匹配菜单全部按 `Path` 精确匹配，与 Id 无关 | `EnsureMenu`（DatabaseSeeder.cs:169） |
| **Phase1 / Phase2** | `ParentPath == null` 先建顶级（parentId=0）；`ParentPath != null` 后挂载到已有父菜单 | DatabaseSeeder.cs:144-157 |
| **ParentPath 找不到父** | **仅 Log.Warning 后静默跳过**，菜单不会出现，也不报错 | DatabaseSeeder.cs:151-154 |
| **只增改、不删未声明菜单** | 同步只 Insert 新菜单 / Update 已存在菜单；**任何模块没声明的菜单行永远不会被同步删除** | `EnsureMenu` 全文 |
| **更新覆盖的字段** | `ParentId / Name / Component / Title / Icon / OrderNo / Redirect / HideInMenu`（有差异才 Update） | DatabaseSeeder.cs:172-187 |
| **不覆盖的字段** | `IgnoreKeepAlive`（MenuDefinition 里没有此字段）；`IsDeleted`（软删状态不会自动恢复） | DatabaseSeeder.cs:177-187 |
| **禁用模块菜单清理** | 配置了 `Modules:Disabled` 时，收集被禁用模块声明的全部 Path，**物理删除**这些菜单及 AdminRoleMenu 关联 | DatabaseSeeder.cs:81-107 |
| **Path 重复自愈** | SB_Menu 出现同 Path 多条时，保留 Id 最小一条，物理删除其余及其角色关联 | DatabaseSeeder.cs:113-133 |
| **角色授权** | 新增与已存在的菜单都会自动补 `super` 角色的 AdminRoleMenu；**其他角色的分配不受同步影响** | DatabaseSeeder.cs:192-196、219-220 |
| **软删行占坑** | 同步查询用 `DisableGlobalFilter()`，软删行也参与 Path 匹配 → 在菜单管理里删除的框架菜单（软删），重启后**不会复活、也不会重建新行** | MenuService.Del + EnsureMenu |

### 1.3 模块发现与覆盖/禁用

- **覆盖**：发现器用 `ModuleName` 做 key，后扫描的覆盖先扫描的。项目程序集永远最后扫 → 派生项目同名 `ModuleName` 模块覆盖框架/feature 版本。
- **禁用**：黑名单 `Modules:Disabled`（**根级配置**，不是 `Features.Modules.Disabled`）匹配 `ModuleKey`（默认=实现类名）或具体类名，不分大小写。被禁用模块的菜单启动时被物理清理（见 1.2）。
- **实例化**：初始化器通过 `RuntimeHelpers.GetUninitializedObject` 创建，**不执行构造函数**。`GetMenus()` 必须是纯静态声明，不得依赖注入的实例字段（IModuleInitializer.cs 注释明示）。

---

## 2. 铁律规范

### 2.1 禁止事项（❌）

| # | 禁止 | 后果 / 依据 |
|---|------|------------|
| 1 | **手写 SQL / JSON 菜单数据、写独立 seeder 直接 Insert SB_Menu** | 与启动同步互相覆盖：同 Path 被同步 Update 改回声明值；不同 Path 的脏数据永远残留（同步不删未声明菜单） |
| 2 | **顶级菜单 Path / Name 与框架重复**（`/system`、`/dashboard`、`/email`、`/member`、`/backup-receive` 及其子路径） | 同 Path 菜单被同步强制改回框架的 Name/Component/OrderNo/ParentId，业务菜单“消失” |
| 3 | **声明菜单但前端不建对应 views 组件文件** | generate-routes-backend.ts 查不到 pageMap → console.error + not-found 兜底，页面 404 |
| 4 | **前端 fork 丢失 `access.ts` 的 `LAYOUT` / `PARENT` layoutMap 映射** | 中间层容器与一级菜单渲染失败（历史踩坑见 AI_Migration_Pitfalls.md §3.1） |
| 5 | **`ParentPath` 指向不存在 / 拼错的 Path** | 同步仅 Warning 后跳过（DatabaseSeeder.cs:153），菜单静默丢失 |
| 6 | **`GetMenus()` 里读取注入字段 / 做数据库查询** | 初始化器不跑构造函数，字段为 null 直接崩 |
| 7 | **在 `GetMenus()` 之外动态拼菜单**（运行时按条件改库） | 每次启动被同步重置；条件差异应放在声明里 |
| 8 | **用 `ParentPath = ""` 表示顶级** | Phase1 判定是 `ParentPath == null`，空字符串会进 Phase2 找父 `""` 失败被跳过 |
| 9 | **升级时修改已发布菜单的 `Path`** | 旧行残留（同步不删）+ 新行新建 → 菜单重复；Path 是身份标识，只改 ParentPath/Title 等 |
| 10 | **手改数据库 / 菜单管理页修改声明菜单的字段、拖动层级** | 下次启动全部被重置（覆盖字段清单见 1.2） |

### 2.2 必须事项（✅）

1. **菜单唯一来源是 `IModuleInitializer.GetMenus()`**。业务模块在自己的 Service 或独立 `XxxInitializer : IModuleInitializer` 中声明。
2. **顶级菜单 Path 用独立业务前缀**：如派生产品 tgvault 用 `/tgvault`、`/tgvault-xxx`；子菜单以父 Path 为前缀：`/tgvault/products`。禁止占用框架路径。
3. **Name 全局唯一、PascalCase、带模块前缀**：如 `TgvaultProduct` / `TgvaultProductEdit`。它是 vue-router 的 route name，前端缺 name 直接 console.error，重名会破坏 keep-alive 与导航。
4. **Component 与前端 views 目录一一对应**：`"/tgvault/product/index"` ↔ `apps/web-antd/src/views/tgvault/product/index.vue`；路径分隔用 `/`，不要带 `.vue`（会自动补）。
5. **顶级菜单 `Component = "LAYOUT"`**；二/三层容器菜单 `Component = ""` 即可（出 API 时自动转 `"PARENT"`），前端 layoutMap 必须有 `PARENT → ParentLayout`。
6. **OrderNo 约定**：框架已占用顶级档位 `0`(Dashboard)、`10`(Member)、`80`(Email)、`95`(云端备份)、`100`(System)。业务顶级菜单用 **20–70 区间或 100 之后**；子菜单从 `1` 起步、留间隙便于插入。
7. **图标用 Iconify 名称、保持 `ion:` 系列**（框架主用 Ionicons）：如 `ion:grid-outline`、`ion:mail-outline`；同一模块内前缀保持一致。
8. **编辑/详情页配 `HideInMenu = true`** 并给独立 Path（参考 AdminService.cs:128）。
9. **挂载到框架菜单（如开发工具）必须用 `ParentPath`**，Path 仍保持全局绝对路径（`/system/xxx`），见 §3 示例二。

---

## 3. 标准代码示例

### 示例一：业务模块顶级菜单（独立 LAYOUT）

```csharp
// Sb.Apps.Tgvault.Services/TgvaultInitializer.cs
using SB.Framework.Models;

public sealed class TgvaultInitializer : IModuleInitializer
{
    public string ModuleName => "TGVault";     // 全局唯一；同名会覆盖框架/feature 模块
    public int InitOrder => 100;               // 排在框架(0-50)/feature(60-80)之后

    public List<MenuDefinition> GetMenus() => new()
    {
        new MenuDefinition
        {
            Name = "Tgvault",                             // PascalCase，全局唯一
            Path = "/tgvault",                            // 独立业务前缀，禁止用 /system 等
            Component = "LAYOUT",                         // 顶级固定 LAYOUT
            Title = "TGVault",
            Icon = "ion:cube-outline",                    // ion: 系列
            OrderNo = 20,                                 // 避开 0/10/80/95/100
            Children = new()
            {
                new() { Name = "TgvaultProduct",  Path = "/tgvault/product",  Component = "/tgvault/product/index", Title = "产品管理", Icon = "ion:pricetag-outline", OrderNo = 1 },
                new() { Name = "TgvaultProductEdit", Path = "/tgvault/product/edit", Component = "/tgvault/product/edit", Title = "编辑产品", OrderNo = 2, HideInMenu = true },
            }
        },
    };
}
```

> 参照真实样本：`SB.Feature.BackupReceive/BackupReceiveInitializer.cs`、`SB.Framework.Services/EmailService.cs:36`。

### 示例二：挂载到框架已有菜单（ParentPath）

```csharp
public List<MenuDefinition> GetMenus() => new()
{
    new MenuDefinition
    {
        Name = "TgvaultTools",
        ParentPath = "/system/devtools",          // 必须精确等于父菜单 Path（AdminService.cs 定义的 DevTools）
        Path = "/system/tgvault-tools",           // Path 仍是全局绝对路径，且必须保持稳定
        Component = "/tgvault/tools/index",       // ↔ views/tgvault/tools/index.vue
        Title = "Vault 工具",
        Icon = "ion:build-outline",
        OrderNo = 20,
    },
};
```

> 参照真实样本：`LogViewerService.cs:18-26`（挂 `/system/devtools`）、`AccessTokenInitializer.cs:17-42`（Path 保持不变，只改 ParentPath 即可重新挂载，同步走 Update 不会产生重复菜单）。

---

## 4. 前端配套要求（sb-framework-admin）

| 要求 | 说明 | 依据 |
|------|------|------|
| **views 组件必须存在** | `Component = "/tgvault/product/index"` ↔ `apps/web-antd/src/views/tgvault/product/index.vue`。缺文件 → 控制台 `route component is invalid` + not-found 页 | generate-routes-backend.ts |
| **layoutMap 必须保留 `LAYOUT` / `PARENT`** | fork/merge 前端基座时不得删改 `access.ts` 的 `layoutMap`；`PARENT → ParentLayout`（仅渲染 `<RouterView>`） | apps/web-antd/src/router/access.ts |
| **无需手写路由** | 前端不写任何菜单/路由文件，全部由后端 `/Menu/All` 驱动 | access.ts（fetchMenuListAsync = getAllMenusApi） |
| **调试入口** | 「系统管理 → 菜单管理」（`/system/menu`）与「菜单树」（`/system/menu-tree`）可查看库内实际数据；浏览器控制台搜 `route component is invalid` / `route name is required` 定位坏菜单 | views/menu/、generate-routes-backend.ts |
| **缓存感知** | 用户菜单缓存 30 分钟；手动改库/改代码后重新登录或清 Redis `menu:user:*` 再验证，避免误判“没生效” | AdminService.MenuAll |

---

## 5. 常见坑与排查表

**症状：菜单丢失 / 页面 404，按序排查：**

| 步骤 | 检查项 | 方法 / 判定 |
|------|--------|------------|
| 1 | 菜单代码是否在 `GetMenus()` 里声明 | grep 业务程序集 `GetMenus`；没有声明 = 根本没进同步管线 |
| 2 | 启动日志同步结果 | 搜 `菜单同步: 新增/更新`；出现 `父路径 ... 未找到，跳过` = ParentPath 拼错（步骤 3） |
| 3 | ParentPath 是否精确等于父菜单 Path | 与 AdminService.cs 里定义的父级 Path 逐字符比对（含大小写、斜杠） |
| 4 | SB_Menu 表里有没有该行、字段对不对 | 菜单管理页或 `SELECT * FROM SB_Menu WHERE Path='...'`；有行但字段是框架值 = Path 与框架撞车（步骤 5） |
| 5 | Path 是否与框架/其他模块重复 | 对照 §2.1-2 框架占用清单；重复时同步会把字段改回对方声明值 |
| 6 | 是否被“禁用模块清理”删掉 | 检查 `appsettings.json` 根级 `Modules:Disabled`（不是 `Features.Modules.Disabled`，见 AI_Migration_Pitfalls.md §4.2）；类名/ModuleKey 命中即被物理删除 |
| 7 | 模块是否被同名覆盖 | ModuleName 与框架/feature 模块同名 → 你的 GetMenus 被覆盖或你覆盖了别人 |
| 8 | super 角色能否看到 | super 自动授权；非 super 角色需在角色管理里分配 AdminRoleMenu |
| 9 | 前端 views 文件是否存在 | `views` + Component 路径拼出实际文件；缺 → not-found + console.error |
| 10 | access.ts layoutMap 是否含 LAYOUT/PARENT | 缺 → 一级/中间层菜单整链 404（AI_Migration_Pitfalls.md §3.1） |
| 11 | 浏览器控制台报错 | `route name is invalid`/`route component is invalid` 直接定位坏菜单 Name/Component |
| 12 | 缓存是否过期 | 30 分钟用户菜单缓存；改库后重登再测 |
| 13 | 手动删过的菜单（软删） | 菜单管理删除是软删，重启不复活；需恢复 IsDeleted=0 或直接重建 |

---

## 6. 升级同步注意事项

1. **框架新增菜单如何进入已装系统**：升级 NuGet 包 → 重启 API。启动期 `SyncModuleMenus`（区别于安装期 `EnsureMenus` 的“表空才执行”）自动 Insert 新菜单并授权 super 角色，无需任何手工操作。
2. **每次启动会被同步覆盖的字段（手改无效）**：`ParentId`、`Name`、`Component`、`Title`、`Icon`、`OrderNo`、`Redirect`、`HideInMenu` —— 前提是该菜单 Path 出现在某个**启用模块**的 GetMenus() 里。
3. **不会被同步触碰的字段/数据**：
   - `IgnoreKeepAlive`（MenuDefinition 无此字段）；
   - 非 super 角色的 AdminRoleMenu 分配；
   - 未被任何启用模块声明的菜单行（同步只增改不删）；
   - 软删状态 `IsDeleted`（删除不会自动撤销，Path 被软删行占住时也不会重建新行）。
4. **禁用模块 = 菜单物理删除**：`Modules:Disabled` 命中的模块，其声明的全部菜单（含子菜单）启动时连同角色关联被物理清理；重新启用后菜单会自动重建，但**各角色的重新分配需要手工做**。
5. **改 Path 等于换菜单**：新 Path 走 Insert、旧 Path 行残留 → 菜单重复出现。清理重复只能靠菜单管理页手工删或 SQL 处理；重复 Path 并存时自愈逻辑只兜底“同 Path 多行”，救不了“新旧两个 Path”。
6. **验证顺序**：改完 GetMenus → 重启 API → 看启动日志“菜单同步完成，新增 X 个，更新 Y 个” → 重登前端确认。

---

## 附：容易忽略的机制点

1. **空 Component 自动转 "PARENT"**：中间层容器菜单在 GetMenus 里写 `Component=""` 即可，不必写 "PARENT" 字符串——但前端 layoutMap 必须有 `PARENT` 映射，否则三级菜单全 404。
2. **软删占坑**：同步含软删行匹配且 Update 不恢复 `IsDeleted`——菜单管理里删掉的框架菜单重启**不会复活**，其 Path 也永远不会被重建为新行。这是“菜单删了回不来”的真实根因。
3. **30 分钟用户级菜单缓存**：手动改库后验证会被缓存误导，容易造成“同步没生效”的误判。
4. **两个 key 的区别**：模块覆盖用 `ModuleName`（同名覆盖），禁用匹配用 `ModuleKey`/类名——派生项目若给模块起与框架相同的 ModuleName，会静默覆盖框架的 GetMenus，这是“框架菜单消失”的一种隐蔽路径。
5. **安装期与启动期递归深度不同**：安装期 `EnsureMenus` 只处理一层 Children，启动期 `EnsureMenuRecursive` 无限递归——三层及以上菜单结构实际依赖启动同步补全。
6. **ParentPath 语义陷阱**：Phase1 判定是 `ParentPath == null`，写 `""` 会被当 Phase2 处理并因找不到父而被静默跳过。
7. **同步只授权 super 角色**：其他角色的菜单分配完全是人工数据，禁用/重建菜单后需重新分配。
8. **smoke 模式跳过整个 SeedDatabase**，冒烟启动时菜单不同步属正常行为。
