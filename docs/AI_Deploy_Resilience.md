# 部署韧性：预检门控 + 看门狗自动回滚

> 解决"部署后 API 崩溃访问不通 → 必须 SSH 进终端救"的死锁。

## 根因

部署器（`AppController.Deploy`）就是被部署的进程本身。流程：新 dll 原地覆盖 `/app` → `GracefulStop` → supervisord 重启同一 dll。新包启动即崩时，supervisord 按 `autorestart + startretries=10` 重试耗尽后标 FATAL，API 彻底没了——而唯一的部署/回滚通道又是这个 API，于是死锁，只能 SSH。

**铁律**：判断好坏 + 回滚的那个东西，不能是 API 自己。

## 三层方案（全部基于现有"原地部署 + deploy_archive"布局，零目录重排、零 symlink）

### ① 预检门控（治本·切换前拦截坏包）
`Deploy` 在真正覆盖 `/app` 之前：
1. 把上传的 zip 解压到**临时 staging 目录**（`/app/.smoke-<guid>`，不碰线上）。
2. 从 `/app` 拷入运行所需的 `appsettings*.json` / `installed.lock` / `client.key`。
3. 用临时端口以**冒烟模式**启动：`dotnet <staging>/<dll> --urls http://127.0.0.1:<ephemeral>`，环境变量 `SB_SMOKE_TEST=1`。
4. 轮询 `http://127.0.0.1:<ephemeral>/health`，绿了 = 新包能正常启动。
5. 杀掉冒烟进程，清理 staging。
6. **只有冒烟通过**才执行原有的 `ExtractApiAtomic` 原地替换 + `GracefulStop` 重启；不通过直接返回失败，`/app` 从未被动过，线上 API 不掉。

**冒烟模式（`SB_SMOKE_TEST=1`）必须关掉的副作用**（否则临时实例会污染生产）：
- `SeedDatabase()`（DB 迁移）— 跳过
- 后台任务系统（`AddBackgroundTasks`）— 不注册；**License 心跳本身是后台任务，一并停掉**
- 即冒烟实例只构建 Web 宿主 + 映射 `/health`，只读探活，不写库、不发心跳、不跑定时任务。

### ② 看门狗自动回滚（兜底·独立于 API 存活）
supervisord 里新增独立进程 `pm-deploy-guard`（shell 循环，与 API 完全解耦，脚本烤进镜像 `/usr/local/bin`，不依赖 `/app` 内容）：
- 持续轮询 `127.0.0.1:6020/health`。
- 检测到"刚部署"（`/app/.deploying` 标记文件，由 Deploy 落下）后进入观察窗口：若 `WAIT` 秒内健康未恢复，**自动从 `deploy_archive` 取上一份 `Deployed` zip 解压回 `/app` + `supervisorctl restart pm-api`**，并落 `/app/logs/auto-rollback.log` + 发通知。
- API 进程死光它还活着，替你做掉原本"去终端"的动作。

### ③ 回滚弹药（last-good）
复用现成的 `deploy_archive/{target}/`：每次成功部署后旧 job 自动转 `RolledBack` 且 zip 仍在。看门狗回滚时直接解压"上一份 Deployed"。**无需额外快照、无需目录重排。**

## 数据库迁移红线
**代码回滚安全，跨 schema 迁移回滚不安全。** 本项目有 `DbMigrationScript`：
- 冒烟模式**绝不迁移**（已在 ① 关掉 SeedDatabase）。
- 真实迁移仍在 flip 后的正式启动时跑（现状）。
- 自动回滚是**纯代码回滚**：若新版已经迁移成功、之后才崩，看门狗回滚到旧码会形成"新库+旧码"。此时看门狗**只回滚代码 + 大声告警要求人工核对 DB**，不尝试反向改 schema。
- 实践建议：破坏性/不兼容迁移单独走 `DbMigrationScript` 审核执行，不要塞进"启动即迁移"。

## 落地清单
| 层 | 改动点 |
|---|---|
| ① | `SbFrameworkExtensions`：`SB_SMOKE_TEST` 门控 SeedDatabase + 后台任务；`AppController.Deploy`：覆盖前先 `SmokeTest(zip)` |
| ② | `pm-deploy-guard.sh`（烤进镜像）+ `supervisord.conf` 新增 program |
| ③ | 复用 `DeployJobService` / `deploy_archive`，看门狗解压上一份 Deployed |
| 配套 | `Dockerfile` COPY 看门狗脚本；Deploy 落 `/app/.deploying` 标记 |

## 未来可选（本期不做）
蓝绿双端口零重启：同时跑新旧两个端口实例，nginx upstream 切换，连最后几秒重启 blip 都消除。当前单进程 supervisord 模型下重启 blip 仅数秒，性价比暂不做。
