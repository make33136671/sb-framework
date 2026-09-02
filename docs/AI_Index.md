# SB Documentation Index

## Documents

| File | Content |
|------|---------|
| **[AI_Conventions](AI_Conventions.md)** | **写代码前必读** — API/toast、UI/颜色/时间组件、命名/路由/时区/longtext、后台任务、系统设置、CloudConfig、构建参数、Git 提交 |
| [AI_Architecture](AI_Architecture.md) | 技术栈、工程拓扑、一键装配、插件框架、子系统实现细节（DB/Cache/Password/Email/License/2FA/BG-Task）、配置项 |
| [AI_Downstream_Guide](AI_Downstream_Guide.md) | **派生项目接入指南** — NuGet 消费、实体/服务/控制器、override 服务、排除 Controller、模块装配、升级 |
| [AI_API_Reference](AI_API_Reference.md) | 24 个 Controller 全接口（参数、Auth；含插件 Controller 与已知缺陷标注） |
| [AI_Models](AI_Models.md) | 实体字段定义 + 枚举 |
| [AI_Build_Deploy](AI_Build_Deploy.md) | 构建命令、安装向导、systemd 部署、日志、新功能工作流 |
| [AI_Nginx_Upload_Static](AI_Nginx_Upload_Static.md) | **Docker nginx 配置专题** — 上传大小限制全链路（nginx 默认 1MB 坑）、静态文件 alias 映射、上传目录卷持久化 |
| [AI_License_System](AI_License_System.md) | License 子系统专题（生成、验证、安全防线） |
| [AI_Plugin_Architecture](AI_Plugin_Architecture.md) | 插件化架构设计 v2（发现/清单/数据装配三层） |
| [AI_BaGet_Feed](AI_BaGet_Feed.md) / [AI_Verdaccio_Feed](AI_Verdaccio_Feed.md) | NuGet / npm 私有源配置 |
| [AI_Migration_Pitfalls](AI_Migration_Pitfalls.md) | **迁移踩坑指南** — 后端迁移、Docker 部署、前端迁移、禁用模块、安装向导的常见问题与修复方案 |
| [AI_Menus](AI_Menus.md) | **菜单功能使用规范** — 菜单机制/同步规则、派生项目铁律（禁止手写菜单数据）、标准示例、丢失排查表 |
| [AI_Brand_Assets](AI_Brand_Assets.md) | **品牌资产生成指南** — SB 品牌风格图标自动生成（安装向导默认/运维页重生成/代码调用）、产物清单、字体与中文降级 |

> 前端规范见 [`sb-framework-admin/CLAUDE.md`](../../sb-framework-admin/CLAUDE.md) 和 [`sb-framework-admin/AI_Conventions.md`](../../sb-framework-admin/AI_Conventions.md)（与本仓库 AI_Conventions.md 镜像）

## 快速参考（详细规范全部在 AI_Conventions）

- 端口：api=6010, admin=6012（预留范围 6010-6019）
- Git 远程：`sb-framework` + `sb-framework-admin` on `gitlab.saibo.club`
- NuGet 源：`https://nuget.saibo.club/v3/index.json`（saibo-baget）
- 测试账号：`admin / 111111`
