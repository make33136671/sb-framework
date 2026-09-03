# SB.Framework.WebTest — 框架功能测试台

永久 Web 测试宿主:把 SB Framework 的全部能力映射成**真实业务场景**,并自带零构建测试台 UI,支持手动点击逐项验证。

## 启动

```bash
dotnet run --project SB.Framework.WebTest
# 浏览器自动打开 http://localhost:6099/test.html
```

首次启动系统未安装,页面顶部会提示先打开 `/setup/index.html` 走一遍**安装向导**(框架原生能力,同时被测):
连数据库 → 建表/菜单 → 生成品牌 → 写入 installed.lock。安装完成后回到测试台,所有场景可用。

> 默认测试账号:`admin / 111111`(安装向导里设置)。数据库/Redis 在 `appsettings.json` 配置。

## 测试台覆盖的框架能力 → 业务场景映射

| 卡片 | 映射的框架能力 |
|---|---|
| 🔐 认证与身份 | JWT 登录(AdminService.LoginAsync,与生产同路径)、Claims 解析、401 对照、AccessToken 插件 API Key 通道探针、登录限流 429 |
| 🛒 订单业务 | BaseCrudService 分页/软删/排序、ISoftDelete 全局过滤(+绕过直查)、业务状态机、ICacheService 带过期统计、操作人识别(匿名/JWT/API Key 三态) |
| ⚙️ 后台任务 | BaseBackgroundTask 轮询/熔断/RunOnce、IBackgroundTaskManager 启停、衍生项目任务注册姿势(WebTestTaskRegistration)、SYS_BackgroundTaskLog |
| ⚡ 缓存 | ICacheService 全方法,Provider 由 CacheConfig 切换(Memory/Redis/Null) |
| ✉️ 邮件 | 模板占位符渲染(不出网)、真实发送(IEmailService→EmailLog)、验证码发送/校验(IVerificationCodeService) |
| 🔀 FlowEngine | 图执行引擎:if 分支/loop/varGet/varSet/wait、自定义 IFlowNodeExecutor 扩展点;两个真实业务流(风控审批/批量发货) |
| 🧰 工具箱 | IRandomNameGenerator、AppTime 时区体系、ISpreadsheetReader 表格解析(真实文件上传) |
| 🩺 系统设施 | Feature 插件发现清单(Features:Disabled 黑名单验证)、FreeSql 连通与表计数、登录日志 |
| 📦 原生端点 | 上传直传(分片流程见 Swagger)、数据库备份、品牌图标生成 —— 直接调框架原生 Controller |

其余框架原生 Controller(Auth/Menu/SystemSetting/Upload/Database/Brand/LogViewer/Sql/Manager/Member/RealName/EmailConfig…)一律走 `/swagger` 手动测试,测试台不重复包装。

## 本项目自身的"被测点"

本项目同时是插件化机制的活样板:
- `WebTestInitializer : IModuleInitializer` — 声明 TEST_Order 表与"功能测试台"菜单(装向导自动建表/同步菜单;黑名单关掉 `WebTest` 后菜单自动清除);
- `OrderTimeoutTask : BaseBackgroundTask` + `WebTestTaskRegistration : IHostedService` — 衍生项目注册业务任务的标准姿势;
- csproj 引用 AccessToken / BackupReceive 插件包 — "引包即启用"。

## 文件结构

```
SB.Framework.WebTest/
├─ Program.cs                    # 两行框架装配 + UseDefaultFiles(打开即测试台)
├─ appsettings.json              # 测试环境配置(MySQL/Memory 缓存/限流 20 次方便压测)
├─ Entities/TestOrder.cs         # 模拟业务实体(IEntity + ISoftDelete)
├─ Services/TestOrderService.cs  # 业务服务(BaseCrudService 派生 + 状态机 + 缓存)
├─ Tasks/                        # 模拟后台任务 + 注册器
├─ Controllers/                  # 测试场景端点(/test/*)
└─ wwwroot/test.html             # 测试台 UI(单文件,无构建)
```
