## 变更摘要

<!-- 1-2 句话说本次 MR 做了什么 -->

## 类型

- [ ] feat（新功能）
- [ ] fix（修 bug）
- [ ] refactor（重构）
- [ ] docs（文档）
- [ ] chore（依赖 / 构建 / CI）
- [ ] perf（性能）
- [ ] breaking change（破坏性变更，必须升主版本）

## Checklist（基座项目专属）

- [ ] 没有引入派生业务关键字（Project / Requirement / TaskItem / etc.）
- [ ] 命名空间在 `SB.Framework.*` 下
- [ ] 表名（如新增 Entity）前缀 `sb_*`
- [ ] `DateTime.Now` / `DateTime.UtcNow` / `DateTime.Today` 用了 `AppTime.*`
- [ ] commit message 是中文 Conventional Commits 格式
- [ ] 本地 `dotnet build sb-framework.sln` 通过
- [ ] 如有公共接口变更，已更新 `CHANGELOG.md`
- [ ] 如有规范变更，已更新 `docs/naming-conventions.md`

## 测试

<!-- 怎么验证这个 MR 是对的？跑了什么命令？看了什么响应？ -->

## 影响范围

<!-- 哪些派生项目会受这个 MR 影响？是否破坏性变更？ -->

## 相关 Issue / 设计文档

Closes #
