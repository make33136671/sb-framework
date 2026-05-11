# SB Documentation Index

## Documents

| File | Content |
|------|---------|
| **[AI_Conventions](AI_Conventions.md)** | **写代码前必读** — API/toast、UI/颜色/时间组件、命名/路由/时区/longtext、后台任务、系统设置、CloudConfig、构建参数、Git 提交 |
| [AI_Architecture](AI_Architecture.md) | 技术栈、项目分层、子系统实现细节（DB/Cache/Password/Email/License/2FA/BG-Task）、配置项 |
| [AI_API_Reference](AI_API_Reference.md) | 19 个 Controller 全接口（参数、Auth） |
| [AI_Models](AI_Models.md) | 实体字段定义 + 枚举 |
| [AI_Build_Deploy](AI_Build_Deploy.md) | 构建命令、安装向导、systemd 部署、日志、新功能工作流 |
| [AI_Fork_Guide](AI_Fork_Guide.md) | 下游 fork、扩展业务代码、override 服务、排除 Controller、上游同步 |
| [AI_License_System](AI_License_System.md) | License 子系统专题（生成、验证、安全防线） |

> 前端规范见 [`sb-admin/CLAUDE.md`](../../sb-admin/CLAUDE.md) 和 [`sb-admin/AI_Conventions.md`](../../sb-admin/AI_Conventions.md)（与本仓库 AI_Conventions.md 镜像）

## 快速参考（详细规范全部在 AI_Conventions）

- 端口：api=6010, admin=6012（预留范围 6010-6019）
- Git 远程：`sb-api` + `sb-admin` on `gitlab.saibo.club`
- 数据库：MySQL `Saibo` (Dev) / `Test` 等 @ `192.168.10.100:3306`
- 测试账号：`admin / 111111`
