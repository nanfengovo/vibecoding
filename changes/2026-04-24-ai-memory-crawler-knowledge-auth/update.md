# Summary

本记录跟踪 AI 记忆、信息采集、知识库和权限改造的实现过程。

# Assumptions

第一版使用系统级长桥/OpenAI 配置，不引入用户私有 API Key。默认管理员用户通过环境变量或启动默认值创建，历史数据迁移到默认管理员名下。知识库检索先用关键词和文本匹配。

# Chronological Updates

- 2026-04-24 00:00：创建变更文档，确认不覆盖现有长桥/OpenAI 配置，采用增量建表和默认管理员迁移策略。
- 2026-04-24 00:08：新增用户、AI 会话/记忆、爬虫和知识库实体定义，并补充 JWT 认证依赖。
- 2026-04-24 00:12：为策略、交易、账户、持仓、监控规则、告警、回测和复盘实体增加可空 `UserId`，用于增量迁移和用户隔离。
- 2026-04-24 00:15：在 `DbContext` 注册用户、用户关注、AI 会话、AI 记忆、爬虫和知识库表，并补充索引与 PostgreSQL 文本列映射。
- 2026-04-24 00:24：新增密码哈希、JWT、当前用户和启动数据库引导服务；引导服务会幂等补表补列、创建默认管理员并迁移旧业务数据归属。
- 2026-04-24 00:28：在应用启动流程接入 JWT 鉴权和数据库引导服务，保持原有 `EnsureCreated` 兼容路径。
- 2026-04-24 00:32：新增 `/api/auth/login`、`/api/auth/me` 和管理员 `/api/users` 接口。
- 2026-04-24 00:40：将关注列表改为用户维度的 `UserWatchlistItems`，保留旧 `Stocks.IsWatched` 数据自动迁移到默认管理员。
- 2026-04-25 00:00：复查实现状态，发现前端登录/JWT、AI 会话后端化、爬虫定时、知识库/采集页面、部分旧业务接口鉴权和用户隔离仍未补齐；决定本轮补全并用 Docker 构建验证。
- 2026-04-25 00:08：开始补后端权限面：系统配置改为管理员访问，旧业务控制器加登录保护，回测、复盘、监控服务按当前用户过滤；爬虫补 Quartz 定时任务。
- 2026-04-25 00:18：开始补前端认证链路：新增登录态存储、Axios JWT 注入、路由守卫、用户菜单，并扩展 AI、爬虫和知识库 API 类型。
- 2026-04-25 00:34：新增信息采集、知识库、用户管理前端页面；AI Chat 会话改为后端加载/创建/删除，并发送 `sessionId` 和 `useMemory`。
- 2026-04-25 00:42：禁用破坏性管理员导入接口，配置读取继续脱敏，长桥 `AppKey` 也纳入 masked-preserve 逻辑。

# Files Changed

- `backend/QuantTrading.Api/QuantTrading.Api.csproj`
- `backend/QuantTrading.Api/Models/Auth.cs`
- `backend/QuantTrading.Api/Models/AiMemory.cs`
- `backend/QuantTrading.Api/Models/CrawlerKnowledge.cs`
- `backend/QuantTrading.Api/Data/QuantTradingDbContext.cs`
- `backend/QuantTrading.Api/Services/Auth/*`
- `backend/QuantTrading.Api/Program.cs`
- `backend/QuantTrading.Api/Controllers/AuthController.cs`
- `backend/QuantTrading.Api/Controllers/BacktestsController.cs`
- `backend/QuantTrading.Api/Controllers/ConfigController.cs`
- `backend/QuantTrading.Api/Controllers/CrawlerController.cs`
- `backend/QuantTrading.Api/Controllers/KnowledgeBasesController.cs`
- `backend/QuantTrading.Api/Controllers/MonitorController.cs`
- `backend/QuantTrading.Api/Controllers/ReviewsController.cs`
- `backend/QuantTrading.Api/Controllers/StocksController.cs`
- `backend/QuantTrading.Api/Services/Monitor/WatchlistService.cs`
- `backend/QuantTrading.Api/Jobs/CrawlerJob.cs`
- `frontend/src/api/index.ts`
- `frontend/src/router/index.ts`
- `frontend/src/stores/auth.ts`
- `frontend/src/stores/aiChat.ts`
- `frontend/src/views/Login.vue`
- `frontend/src/views/Crawler.vue`
- `frontend/src/views/KnowledgeBase.vue`
- `frontend/src/views/Users.vue`

# Validation

- 2026-04-25：`npm run build` 通过，完成 `vue-tsc` 和 Vite 生产构建。
- 2026-04-25：`docker build -t quant-backend-check ./backend/QuantTrading.Api` 通过，后端在 Docker SDK 镜像内完成 `dotnet publish`。
- 2026-04-25：`docker compose up -d --build sqlserver backend frontend` 通过，SQL Server、后端和前端容器均启动。
- 2026-04-25：`GET /health` 返回 200；未登录访问 `/api/ai/sessions` 返回 401；管理员登录成功；携带 JWT 可访问 `/api/auth/me`、`/api/ai/sessions`、`/api/crawler/sources`。
- 2026-04-25：携带 JWT 可创建知识库并导入 Markdown 文档；`/api/config` 返回长桥和 OpenAI 密钥脱敏值。
