---
name: project-handoff
description: Use this skill when the user mentions handoff, context handoff, 新窗口继续开发, 上下文压缩, 交接文档, AGENTS.md, PROJECT_CONTEXT.md, BUSINESS_CONTEXT.md, docs/agent/handoffs, docs/agent/decisions, docs/agent/task-history, or asks to initialize, read, summarize, or update persistent project context for Codex development sessions.
---

# Project Handoff

用于在 Codex 长会话、上下文压缩、新开线程之间维护“可落地到仓库”的项目记忆，避免关键状态只留在聊天记录里。

## 触发信号

当用户提到以下任一关键词或意图时启用本技能：
- handoff / context handoff
- 新窗口继续开发 / 新线程继续 / 继续上次开发
- 上下文压缩 / 交接文档
- AGENTS.md / PROJECT_CONTEXT.md / BUSINESS_CONTEXT.md
- docs/agent/handoffs / docs/agent/decisions / docs/agent/task-history
- 初始化、读取、总结、更新项目长期上下文

## 上下文文件分工

- `AGENTS.md`：长期协作规则（稳定，禁止记录当前任务进度）
- `docs/agent/PROJECT_CONTEXT.md`：项目全局上下文与任务索引（稳定框架 + 任务映射）
- `docs/agent/BUSINESS_CONTEXT.md`：长期业务边界与业务规则（禁止临时开发状态）
- `docs/agent/handoffs/{task-slug}.md`：当前任务交接与下一步
- `docs/agent/decisions/YYYY-MM-DD-{decision-slug}.md`：单条 ADR 决策
- `docs/agent/task-history/YYYY-MM-DD-{task-slug}.md`：单次任务开发记录

## Workflow A: 初始化上下文文件

当用户说：
- 初始化项目上下文
- 创建交接文件
- setup project handoff
- 创建 AGENTS.md 和 docs/agent 文件

执行步骤：
1. 检查仓库根目录是否存在 `AGENTS.md`。
2. 检查是否存在 `docs/agent/`。
3. 若不存在则创建基础结构：
   - `AGENTS.md`
   - `docs/agent/PROJECT_CONTEXT.md`
   - `docs/agent/BUSINESS_CONTEXT.md`
   - `docs/agent/handoffs/`
   - `docs/agent/decisions/`
   - `docs/agent/task-history/`
4. 若用户已提供 `task-slug`，为该任务创建：
   - `docs/agent/handoffs/{task-slug}.md`
   - `docs/agent/task-history/YYYY-MM-DD-{task-slug}.md`（可选，首次收尾时再建）
   - 必要时创建对应 ADR：`docs/agent/decisions/YYYY-MM-DD-{decision-slug}.md`
5. 若文件已存在，禁止直接覆盖：
   - 仅补齐缺失章节；或
   - 先询问用户是否执行合并。
6. 禁止默认创建单一共享文件 `docs/agent/HANDOFF.md`、`docs/agent/DECISIONS.md`、`docs/agent/TASK_HISTORY.md`。
7. 完成后输出：
   - 文件树
   - 每个文件用途
   - 下一步使用方法

## Workflow B: 新线程启动读取上下文

当用户说：
- 继续上次开发
- 新窗口继续
- 读取上下文
- 先看交接文档
- resume from handoff

执行步骤：
1. 先不要改代码。
2. 识别当前任务 `task-slug`（优先从用户输入、`PROJECT_CONTEXT.md` 任务索引、最近 handoff 文件名推断）。
3. 依次读取：
   - `AGENTS.md`
   - `docs/agent/PROJECT_CONTEXT.md`
   - `docs/agent/BUSINESS_CONTEXT.md`
   - `docs/agent/handoffs/{task-slug}.md`
   - 当前任务相关 ADR：`docs/agent/decisions/*{decision-slug}.md`（按任务关联读取）
4. 执行仓库状态检查：
   - `git status`
   - 当前分支名
   - 未提交 diff 摘要
5. 用中文输出统一状态总结：
   - 当前项目背景
   - 当前需求目标
   - 已完成内容
   - 未完成内容
   - 当前风险点
   - 最近修改文件
   - 下一步建议
6. 若无法唯一确定 `task-slug`，先列出候选任务并等待用户确认，禁止自动改业务代码。
7. 总结后等待用户确认，禁止自动开始改业务代码。

## Workflow C: 旧线程结束前更新交接

当用户说：
- 更新 handoff
- 做交接
- 上下文快满了
- 准备新开窗口
- 结束前整理上下文

执行步骤：
1. 执行 `git status`。
2. 查看当前未提交 diff 摘要。
3. 确认当前任务 `task-slug`，仅读取与本任务相关文件：
   - `docs/agent/handoffs/{task-slug}.md`
   - `docs/agent/task-history/YYYY-MM-DD-{task-slug}.md`（不存在则创建）
   - 本任务相关 ADR 文件：`docs/agent/decisions/YYYY-MM-DD-{decision-slug}.md`
4. 只更新当前任务自己的文档，禁止改其他任务文件：
   - `handoffs/{task-slug}.md`：当前任务进度、最近修改文件、未完成事项、下一线程起点
   - `task-history/YYYY-MM-DD-{task-slug}.md`：本轮开发记录
   - `decisions/YYYY-MM-DD-{decision-slug}.md`：本轮新增 ADR（如有）
5. `AGENTS.md` 与 `BUSINESS_CONTEXT.md` 默认不更新；若确需修改，必须先说明原因并等待用户确认。
6. 严禁写入敏感信息：密码、token、密钥、连接串、个人隐私。
7. 更新后输出：
   - 修改了哪些文档
   - 下一个 Codex 线程建议启动提示词
   - 当前代码是否有未提交修改

## 更新策略（必须遵守）

1. 不要把聊天记录当作长期项目记忆。
2. 长期上下文必须落到仓库文档。
3. `AGENTS.md` 只放长期稳定规则，不放当前任务进度。
4. `PROJECT_CONTEXT.md` 放项目级结构信息与任务索引，不放临时细节。
5. `BUSINESS_CONTEXT.md` 只放稳定业务规则，不放临时开发状态。
6. `handoffs/{task-slug}.md` 放当前任务交接，禁止多任务共用一个 handoff 文件。
7. `decisions/YYYY-MM-DD-{decision-slug}.md` 每个决策一个 ADR 文件。
8. `task-history/YYYY-MM-DD-{task-slug}.md` 每个任务会话一条历史记录文件。
9. 默认不修改 `AGENTS.md`/`BUSINESS_CONTEXT.md`；若要修改，必须先说明原因并等待用户确认。
10. 每次修改业务代码且影响流程时，必须提醒用户是否同步更新当前任务 handoff/decision/task-history 文档。

## 并行冲突规避

- 多线程并行时，必须先绑定唯一 `task-slug`，再写文档。
- 一个线程只能写自己的 `handoffs/{task-slug}.md` 与对应 `task-history/`、`decisions/` 文件。
- 禁止跨任务追加到共享日志，避免合并冲突与上下文串线。
- 发现同名任务文件由其他线程活跃修改时，应暂停并请用户确认是否改用新 `task-slug`。

## 输出风格

- 默认中文输出，结构清晰、可执行。
- 先给状态总结，再给建议动作。
- 若发现交接文档缺失或过期，先提示风险，再建议修复步骤。
