# AGENTS.md 模板（项目级协作约定）

> 说明：本文件只保留长期规则与工程协作约定。详细业务背景请维护在 `docs/agent/BUSINESS_CONTEXT.md`。当前任务进度请写入 `docs/agent/handoffs/{task-slug}.md`，不要写入本文件。

## 项目背景

- 项目名称：`<项目名>`
- 技术栈：`C# / ASP.NET Core / ABP / Vue`
- 业务域：`工业调度（RCS / AGV / AMA / STK / PLC）`
- 目标：`<一句话描述系统目标>`

## Codex 工作原则

- 先读文档再改代码，先确认需求再实施。
- 优先小步提交，避免一次性大改。
- 不在未知业务规则下“猜改”核心逻辑。
- 涉及调度状态机、设备联动、第三方协议时，先查 `BUSINESS_CONTEXT.md` 与 `docs/agent/decisions/`。

## 修改代码前必须读取的文档

- `AGENTS.md`
- `docs/agent/PROJECT_CONTEXT.md`
- `docs/agent/BUSINESS_CONTEXT.md`
- `docs/agent/handoffs/{task-slug}.md`
- `docs/agent/decisions/YYYY-MM-DD-{decision-slug}.md`（任务相关）
- `docs/agent/task-history/YYYY-MM-DD-{task-slug}.md`（任务相关）

## 后端约定

- 语言与框架：`C# + ASP.NET Core + ABP`。
- 分层原则：应用层不直接耦合基础设施细节；领域规则集中管理。
- DTO 与实体转换保持显式，避免隐式副作用。
- 涉及任务状态流转时，必须补充状态流转验证与异常处理。

## 前端约定

- 前端框架：`Vue`。
- API 类型与字段命名应与后端契约一致。
- 关键调度页面（任务流、设备状态、告警）改动需明确回归点。
- 不在前端硬编码业务状态含义，统一来源于后端/配置。

## 第三方系统对接约定

- 对接对象：`RCS / AGV / AMA / STK / PLC`。
- 接口变更必须记录在 `docs/agent/decisions/` 与 `BUSINESS_CONTEXT.md`。
- 外部系统异常（超时、重试、幂等等）必须有降级或补偿策略。
- 协议字段、枚举含义、状态码解释必须可追溯。

## 禁止事项

- 禁止在未确认影响范围时直接修改核心调度规则。
- 禁止把密钥、token、连接串写入仓库文档。
- 禁止只在聊天里说明关键决策而不落文档。
- 禁止用临时补丁绕过长期架构问题且不留记录。

## 构建/测试命令占位

```bash
# Backend
dotnet restore
dotnet build
# dotnet test

# Frontend
pnpm install
pnpm build
# pnpm test
```

## 文档维护规则

- `AGENTS.md`：长期稳定规则，保持简短。
- `PROJECT_CONTEXT.md`：项目级上下文与任务索引，避免任务细节堆积。
- `BUSINESS_CONTEXT.md`：维护稳定业务边界、规则、系统职责。
- `handoffs/{task-slug}.md`：当前任务交接，每轮会话结束前更新。
- `decisions/YYYY-MM-DD-{decision-slug}.md`：单决策单文件 ADR。
- `task-history/YYYY-MM-DD-{task-slug}.md`：任务历史单文件记录。
- 若要修改 `AGENTS.md` 或 `BUSINESS_CONTEXT.md`，先说明原因并等待用户确认。
