# PROJECT_CONTEXT.md 模板

> 用于记录项目级稳定上下文与任务索引。不要在此记录临时开发细节、聊天摘要、线程内临时状态。

## 项目概览

- 项目名称：`<项目名>`
- 仓库定位：`<一句话描述>`
- 主要技术栈：`C# / ASP.NET Core / ABP / Vue`
- 关键外部系统：`RCS / AGV / AMA / TM / STK / PLC`

## 代码结构速览

- 后端目录：`<backend-path>`
- 前端目录：`<frontend-path>`
- 关键模块：`<module-a> / <module-b> / <module-c>`

## 任务索引（并行安全）

- task-slug：`<task-slug-a>`
  - handoff：`docs/agent/handoffs/<task-slug-a>.md`
  - task-history：`docs/agent/task-history/YYYY-MM-DD-<task-slug-a>.md`
  - decisions：
    - `docs/agent/decisions/YYYY-MM-DD-<decision-slug-1>.md`
    - `docs/agent/decisions/YYYY-MM-DD-<decision-slug-2>.md`
- task-slug：`<task-slug-b>`
  - handoff：`docs/agent/handoffs/<task-slug-b>.md`
  - task-history：`docs/agent/task-history/YYYY-MM-DD-<task-slug-b>.md`
  - decisions：
    - `docs/agent/decisions/YYYY-MM-DD-<decision-slug-3>.md`

## 协作约束

- 一个线程只维护一个 `task-slug` 的文档集合。
- 新开任务先创建唯一 `task-slug`，再创建对应 handoff 文件。
- 不要把多个任务写进同一个 handoff/历史文件。

## 维护规则

- 可更新：任务索引映射、稳定模块说明。
- 不可写入：临时进度、未确认结论、敏感信息。
- 若要改动全局规则，先更新 `AGENTS.md` 并经用户确认。
