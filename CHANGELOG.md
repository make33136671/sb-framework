# Changelog

本仓库遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/) 和 [Keep a Changelog](https://keepachangelog.com/zh-CN/)。

## [Unreleased]

## [0.5.6] - 2026-10-13

> 数据库备份补齐「恢复」的另一半：此前只能备份/下载，无法把外部备份文件导回系统。`SB.Framework.Services 0.5.3` + `SB.Framework.WebApi 0.5.6` 配套发布。

### Added

- **备份文件上传导入**（`POST /Database/Import`）：支持系统导出的 `.zip` 备份包（内含 `.sql`）或裸 `.sql`，落盘后写入备份记录，随即可在列表中点「恢复」。
  - 上传只做**导入**，不直接覆盖数据库 —— 覆盖仍是独立的高危操作，需在列表中二次确认（输入库名）。
  - 导入后自动做一次**试运行校验**（解开压缩包、核对方言、检查恢复工具），把「文件坏了/方言不对」挡在恢复之前，而不是等安全备份跑完才失败。
  - 同一时间戳重复上传自动加序号，绝不覆盖已有备份。
- **方言识别与阻断**：按脚本头识别 mysqldump / pg_dump，跨库（MySQL ↔ PostgreSQL）的备份直接拒绝并说明原因 —— 两者 SQL 互不兼容，硬导会半截失败。
- **`POST /Database/Validate`**：对任意备份记录做只读校验（不触碰数据库）。

### Fixed

- **MySQL 恢复撞主键**（上传文件恢复场景暴露）：`mysqldump` 默认不生成 `DROP TABLE`（PG 的 `pg_dump --clean` 有），导入已有同名表的库会主键冲突/整批失败。现恢复 MySQL 备份时注入「按 information_schema 动态清空当前库表」前缀，脚本自身已带 `DROP TABLE` 时不重复注入。
- `IBackupTool` 新增 `Dialect` 属性（`mysql` / `postgresql`），供方言判断复用。

## [0.5.4] - 2026-09-04

> Swagger 全量回归后暴露的三处修复;SB.Framework.Services 0.5.1 与 WebApi 0.5.2~0.5.4 配套发布,tgvault-api 已在 PG 真库完成 183 端点全量 API 测试(0 个 5xx)。

### Fixed

- **Swagger 文档生成 500**(`SB.Framework.WebApi 0.5.2`):`AppSettingsController.Upload` 的 `IFormFile` 参数去掉显式 `[FromForm]`(Swashbuckle 6.9 禁止该组合)。
- **Swagger 歧义 method**(`0.5.3`):`SystemSettingController.List` 补 `[HttpGet]`(无显式 HTTP method 的 action 无法生成 OpenAPI)。
- **SystemSetting/SaveList 空参 500**(`0.5.4`):`settings=null` 直接进 JSON 反序列化抛 ArgumentNullException,补空值防线返回业务失败。
- **日志查看器读活跃日志 500**(`SB.Framework.Services 0.5.1`):Serilog 正在写的 ndjson 被独占锁定,`File.ReadAllLines` 抛 IOException;改用 `FileShare.ReadWrite` 共享读(LogViewer Read/Diag/下载链路通用)。

## [Unreleased]

## [0.5.0] - 2026-09-04

> **Breaking**:授权验证(License)体系整体移除,`.NoLicense` 双变体机制终结 —— 主包只发单一版本,派生项目把 `*.NoLicense` 引用改成正常包名并升到 0.5.0 即可(本项目内授权代码历史见 `archive/with-license` 分支)。开发期 ProjectReference 链上的派生项目无需引用 Analyzers 包(SB0001/0002/0003 随链生效)。

### Removed

- **授权验证体系整体移除** —— `SB.Framework.Core/License/` 全目录(验证中间件/RSA 验签/机器指纹/域名匹配/时间回退防护)、`SB.Feature.LicenseManage` 签发端项目、LicenseClient/Record/Request/VerifyLog 实体与 License DTO、LicenseClientService/HeartbeatTask/VerifyClient、VerificationController、`AppSettings.LicenseConfig`、RSA 公钥嵌入 MSBuild Target、`DISABLE_LICENSE` 编译开关与 `.NoLicense` 双变体 PackageId。完整历史代码保留在 `archive/with-license` 分支,日后新授权方案从此分支取材。
- 安装向导返回值 `licenseEnabled` 恒 false(字段保留避免前端炸);系统设置不再出现「授权设置」分类;「授权信息」菜单移除。

### Changed

- **多数据库兼容** —— 实体清理 27 处 `DbType` 方言硬编码(24 处 timestamp/3 处 text),同一套实体 MySQL/PG 通吃(FreeSql 按 DataType 自动映射);新增分析器 **SB0003** 编译期禁止 `[Column]` 写 `DbType`。
- **分析器传递修复** —— Core 对 Analyzers 的 ProjectReference 曾带 `PrivateAssets=all`,SB0001/0002 实际只对 Services 生效;现各项目直接引用 Analyzers,SB 规则覆盖全部框架与派生项目,并清掉了暴露出的存量违规(AppSettings/Setup/AppController/AccessToken 系列 DateTime.Now→AppTime,SetupController 安装接口补 `[AllowJsonBody]`)。
- **IBackgroundTaskManager 无条件注册** —— 任务管理器不再裹在 isInstalled 分支内(仅依赖 ILogger);任务运行器仍只在已安装且非冒烟时装配。派生项目 HostedService/Controller 任何状态都能构造注入,不再出现未安装态 DI 校验崩溃。
- 打包脚本移除双变体构建,`pack-push.sh` 单变体出包。

### Added

- **SB.Framework.WebTest 永久功能测试台** —— 真实业务场景映射框架全部能力(认证三态/订单状态机/软删双路/后台任务/FlowEngine 双流/ApiKey 全链),自带零构建测试台 UI(wwwroot/test.html),PG 真库端到端验证通过。

## [0.4.9] - 2026-06-14

> 仅 `SB.Framework.WebApi`（含 `.NoLicense`）改动，其余包（Core/Models/Services/Analyzers）不变，派生项目只需把 `SB.Framework.WebApi(.NoLicense)` 升到 `0.4.9`。

### Added

- **部署预检门控（零宕机拦坏包）** — `AppController.Deploy` 在原地覆盖 `/app` 之前，先把待部署 zip 解压到临时 staging、拷入线上配置，以冒烟模式（`SB_SMOKE_TEST=1`）用临时端口拉起新包并轮询 `/health`（最长 45s）。冒烟不通过直接拒绝部署，线上进程从未被动过、不掉线。可用环境变量 `SB_DEPLOY_SMOKE=0` 关闭。
- **冒烟模式** — `SbFrameworkExtensions.IsSmokeTest`：`SB_SMOKE_TEST=1` 时跳过 `SeedDatabase`（DB 迁移）与后台任务运行器（含 License 心跳），但保留 `IBackgroundTaskManager` 的 DI 注册（否则注入它的控制器在冒烟实例里解析失败，会假性拦截部署）。临时实例只读探活，不污染生产。
- **部署标记** — 部署成功触发重启前在 `/app` 落 `.deploying` 标记，供独立看门狗（派生项目侧 `pm-deploy-guard`）感知"刚部署"，健康未恢复时自动回滚。
- 设计与运维说明见 `docs/AI_Deploy_Resilience.md`。

### Changed

- `BackgroundTaskSetup.AddBackgroundTasks(bool runTasks = true)` 增加 `runTasks` 形参（冒烟模式传 `false` 只注册管理器、不启动任务运行器）。默认行为不变，向后兼容。
- 部署原子替换的"受保护文件"清单（`keepConfig=true` 时不覆盖）新增根目录 **`installed.lock`**，防止误打包的安装锁把线上打回未安装态（原有 `appsettings.json` / `appsettings.*.json` 保护不变）。

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
