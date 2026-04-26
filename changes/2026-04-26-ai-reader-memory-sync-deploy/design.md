# Request
用户要求在现有项目内完成图书阅读与全链路 AI 联动升级，并上线到现有 Vercel（前端）与 Railway（后端+数据库）环境。核心目标包括：阅读器划线即时可视化回绘、四个 AI 入口能力统一（模型选择/提示词优化/记忆写入）、记忆中心管理能力增强、记忆与知识库自动双向同步、后端 API 与数据库幂等升级、上线验证且不破坏现有配置。

# Current Behavior
1. 阅读器已具备基础划线保存与列表，但需要确认 PDF TextLayer 回绘在初始化/翻页/增删后都稳定触发。
2. 前端多个 AI 入口已有部分能力，但能力覆盖不一致。
3. 记忆中心页面已存在基础查询与编辑，但来源跳转、关联可视化与管理细节仍可补强。
4. 后端 `AiMemoryRecord` 字段已扩展，但 `AiController` 记忆接口仍是旧版（缺分页筛选、更新、手动同步），`optimize-prompt` 未接入场景上下文字段。
5. 知识库服务当前主要处理文档导入/检索分片，尚未完整落实“知识库文档与记忆自动双向同步”。
6. 数据库引导脚本已包含多数字段与索引，需补齐一致性并验证幂等。

# Proposed Change
1. 前端阅读器
- 保持后端划线契约不变。
- 确保 `PdfEngine.setHighlights()` 基于 `locator + selectedText` 回绘。
- 在阅读器初始化、翻页、保存划线、删除划线后统一触发回绘，保证“保存后立即可见 + 刷新恢复”。

2. 前端 AI 入口统一
- 继续复用 `providerModel` 公共能力。
- 在 `AiChat`、`ReaderViewer`、`KnowledgeBase`、`StockDetail` 确保具备：模型源与模型选择、提示词优化、保存为记忆（按场景带来源与模型元数据）。

3. 记忆中心增强
- 保持现有筛选/编辑/归档。
- 增加来源展示与可跳转能力，完善知识库关联可见性。

4. 后端 API
- 扩展 `GET /api/ai/memories` 支持分页与筛选参数。
- 扩展 `POST /api/ai/memories` 接收联动字段并默认触发同步。
- 新增 `PUT /api/ai/memories/{id}` 与 `POST /api/ai/memories/{id}/sync`。
- 扩展 `POST /api/ai/optimize-prompt` 接收 `scene/contextText/knowledgeBaseId/readerContext`。

5. 自动双向同步
- 记忆写入/更新时自动同步到知识库文档（memory -> knowledge）。
- 知识库文档导入/更新时自动生成或更新记忆（knowledge -> memory）。
- 同步采用幂等键（如 `sourceRef`/文档 ID）避免重复灌入。
- 保留归档语义：归档记忆不删除知识库文档。

6. 数据库与部署
- 在 `DatabaseBootstrapService` 确保 Postgres / SQL Server 双路径的列补齐与索引补齐语句完整且幂等。
- 先本地 build 验证，再执行 Vercel 前端与 Railway 后端发布，保持现有环境变量与配置不变。

# Open Questions
1. 记忆同步目标知识库：当 `knowledgeBaseId` 为空时，使用用户默认知识库；若不存在则创建默认知识库。
2. 记忆同步文档标题是否需要携带类型与时间戳：采用“标题优先，否则按类型+时间生成”策略。
3. 知识库导入生成记忆的长度上限：默认截断到可控长度，避免上下文爆炸。

# Decision
1. 在不破坏现有 API 兼容的前提下增量扩展字段与接口。
2. 同步逻辑放在 `KnowledgeService` 中集中实现，`AiController` 通过服务调用，减少重复逻辑。
3. `LoadMemoryContext` 继续统一拉取可用记忆（包括知识库同步记忆），仅通过数量与长度裁剪控量。
4. 部署采用当前项目绑定，不改动既有 Vercel/Railway 配置项与密钥。

# Risks
1. 同步逻辑若幂等键不充分，可能重复写入知识库文档或记忆。
2. 记忆中心展示新增字段后，若后端返回空值处理不当会出现前端渲染异常。
3. Railway 发布若触发数据库 schema 补齐异常，可能影响启动。
4. Vercel 部署若项目链接错误可能误发到其他项目。

# Validation Plan
1. `dotnet build backend/QuantTrading.Api/QuantTrading.Api.csproj`
2. `npm --prefix frontend run build`
3. 验证 API：记忆列表筛选分页、创建/更新/同步接口、提示词优化场景参数。
4. 验证阅读器：保存划线即时高亮，刷新/翻页后可恢复。
5. 验证双向同步：
- 创建记忆后产生知识库关联文档。
- 导入/更新知识库文档后产生或更新关联记忆。
6. 发布后检查：Railway `/health` 与关键 API；Vercel 生产站点四个 AI 入口可用。
