# Summary
本次变更完成了阅读器高亮回绘体验补强、AI 入口能力统一、记忆中心管理增强、记忆与知识库自动双向同步、后端记忆接口扩展与数据库幂等索引补齐；并完成前端 Vercel production 发布。Railway 后端/数据库发布因当前 CLI 无有效登录态暂未执行成功。

# Assumptions
1. 不改动现有环境变量与平台配置，仅在现有项目/服务上发布。
2. 记忆归档不删除知识库文档。
3. 自动同步以幂等更新为优先，避免重复写入。

# Chronological Updates
- 2026-04-26 00:00 初始化变更记录，确认范围、风险和验证计划。
- 2026-04-26 00:08 完成代码基线盘点，识别后端 `AiController` 与知识库同步为主要缺口。
- 2026-04-26 00:15 扩展后端 AI 契约：记忆分页筛选、更新、手动同步；`optimize-prompt` 增加 scene/context/knowledge/reader 参数。
- 2026-04-26 00:20 在 `KnowledgeService` 增加双向同步：memory -> knowledge 与 knowledge -> memory 自动幂等更新。
- 2026-04-26 00:22 数据库模型与引导脚本新增记忆来源与知识文档关联索引（Postgres/SQL Server 双路径）。
- 2026-04-26 00:24 前端 `MemoryCenter` 增加来源跳转与知识库跳转，`KnowledgeBase` 支持通过查询参数定位知识库。
- 2026-04-26 02:26 通过 `docker compose up -d --build` 在容器内完成后端 publish 与前端构建。
- 2026-04-26 02:28 前端发布至 Vercel production 成功。
- 2026-04-26 02:30 Railway CLI 发布受阻：当前会话 token 失效且非交互模式无法重新登录。
- 2026-04-26 02:31 针对 `knowledge_document` 记忆编辑增加“回写原文档”逻辑，后端容器重建并健康复检通过。

# Files Changed
- `backend/QuantTrading.Api/Controllers/AiController.cs`
- `backend/QuantTrading.Api/Services/AI/IAiAnalysisService.cs`
- `backend/QuantTrading.Api/Services/AI/OpenAiAnalysisService.cs`
- `backend/QuantTrading.Api/Services/Knowledge/IKnowledgeService.cs`
- `backend/QuantTrading.Api/Services/Knowledge/KnowledgeService.cs`
- `backend/QuantTrading.Api/Data/QuantTradingDbContext.cs`
- `backend/QuantTrading.Api/Services/Auth/DatabaseBootstrapService.cs`
- `frontend/src/views/ReaderViewer.vue`
- `frontend/src/views/MemoryCenter.vue`
- `frontend/src/views/KnowledgeBase.vue`
- `changes/2026-04-26-ai-reader-memory-sync-deploy/design.md`
- `changes/2026-04-26-ai-reader-memory-sync-deploy/update.md`

# Validation
- `dotnet build backend/QuantTrading.Api/QuantTrading.Api.csproj`：本机未安装 dotnet，改为容器内构建验证。
- `npm --prefix frontend run build`：通过。
- `docker compose up -d --build`：通过，后端镜像在容器内完成 `dotnet publish`。
- `docker compose build backend`：通过，增量改动后再次验证后端可编译。
- `curl -i http://localhost:15000/health`：返回 `HTTP/1.1 200 OK`，body 为 `healthy`。
- `vercel --prod --yes --cwd frontend`：通过，生产部署 URL 为 `https://frontend-4gdg3wsfd-nanfengovos-projects.vercel.app`。
- `railway whoami` / `railway status`：失败（Unauthorized，token 失效），需重新登录后才能执行后端/数据库远端发布。
