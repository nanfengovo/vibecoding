# Summary
本次变更聚焦于股票详情体验修复、Dashboard 真数据化、主题切换、AI 分析能力接入，以及低代码工作台搭建。

# Assumptions
- 现有后端 `SystemConfig` 可扩展新分类配置（如 `openai`）。
- `LongBridgeService` 与当前股票接口可为 AI 分析提供足够上下文。
- 低代码平台第一版以“单模块执行 + 模板保存”满足需求。

# Chronological Updates
- 2026-04-19 20:12: 创建变更目录与 `design.md`/`update.md`，开始实现。
- 2026-04-19 20:18: 完成现状排查，确认需要同时改造股票详情、Dashboard、主题系统、配置中心、AI接口与低代码工作台。
- 2026-04-19 20:33: 完成后端第一批改造：新增 `AiController`、`IAiAnalysisService/OpenAiAnalysisService`，并扩展 `ConfigController` 支持 OpenAI 配置与连接测试。
- 2026-04-19 20:36: 扩展 `StocksController` 与 `LongBridgeService`，支持 K 线 `start/end` 时间范围过滤以及 `Y` 周期映射。
- 2026-04-19 20:43: 重构 `MonitorController` 请求/响应映射，兼容前端 `symbols/conditions/notifications/checkInterval/isActive` 字段，避免监控规则创建后不可读不可用。
- 2026-04-19 20:53: 完成前端配置与能力接入：新增 OpenAI 设置页表单、`aiApi` 接口、主题状态存储、主布局主题切换入口。
- 2026-04-19 21:01: 重写 `StockDetail.vue`，加入 `Y` 周期、自定义时间范围、AI 分析卡片，并通过请求序列号机制修复页面切换并发请求 bug。
- 2026-04-19 21:07: 重写 `Dashboard.vue`，移除虚拟数据，改为真实关注股票多股看板 + 实盘统计来源。
- 2026-04-19 21:10: 新增 `LowCodeWorkbench.vue` 与路由入口，支持规则/交易/回测模块可视编辑、JSON 预览、执行与模板保存。
- 2026-04-19 21:26: 修复 `MonitorController` 序列化兼容问题：`conditions` 改为 `JsonElement` 透传，解决返回 `type:[]/value:[]` 异常结构；并过滤历史脏数据 `{"ValueKind":...}`。
- 2026-04-19 21:30: 增强 K 线多源回退策略：当 LongBridge/Yahoo 失败时，新增 Nasdaq 公共历史数据回退（US 股票），并支持按日数据聚合到周/月/年，保障图表稳定显示。
- 2026-04-19 21:31: 增加 LongBridge 网络自动回退：`.com` 握手失败时自动重试 `.cn` 域名，减少中国网络环境下的连接失败。
- 2026-04-19 21:34: 优化 LongBridge 连接测试逻辑，在主链路异常场景提供可用性降级判断，避免“测试链接长期不可用”影响配置体验。

# Files Changed
- `backend/QuantTrading.Api/Program.cs`
- `backend/QuantTrading.Api/appsettings.json`
- `backend/QuantTrading.Api/Controllers/ConfigController.cs`
- `backend/QuantTrading.Api/Controllers/StocksController.cs`
- `backend/QuantTrading.Api/Controllers/MonitorController.cs`
- `backend/QuantTrading.Api/Controllers/AiController.cs`
- `backend/QuantTrading.Api/Services/LongBridge/LongBridgeService.cs`
- `backend/QuantTrading.Api/Services/AI/IAiAnalysisService.cs`
- `backend/QuantTrading.Api/Services/AI/OpenAiAnalysisService.cs`
- `frontend/src/types/index.ts`
- `frontend/src/api/index.ts`
- `frontend/src/stores/app.ts`
- `frontend/src/layouts/MainLayout.vue`
- `frontend/src/main.ts`
- `frontend/src/styles/main.scss`
- `frontend/src/router/index.ts`
- `frontend/src/views/Settings.vue`
- `frontend/src/views/StockDetail.vue`
- `frontend/src/views/Dashboard.vue`
- `frontend/src/views/LowCodeWorkbench.vue`

# Validation
- 后端容器构建：`docker compose build backend` 通过（仅保留已有 warning，无阻断错误）。
- 后端服务重启：`docker compose up -d backend` 成功。
- 前端容器构建与发布：`docker compose build frontend && docker compose up -d frontend` 成功，`http://localhost/settings` 返回前端页面。
- 监控规则接口回归：
  - `POST /api/monitor/rules` 成功创建，`conditions` 返回结构正确。
  - `GET /api/monitor/rules` 正常返回；历史异常条件已降级为空数组，避免前端渲染报错。
- 股票详情接口回归：
  - `GET /api/stocks/AAPL.US` 返回公司信息字段（市值/PE/EPS/52周高低等）。
  - `GET /api/stocks/AAPL.US/quote` 返回实时/准实时行情结构正常。
  - `GET /api/stocks/AAPL.US/kline` 在 `D/M/Y` 周期和 `start/end` 范围下均可返回数据（新增 Nasdaq 回退生效）。
- LongBridge 连通性回归：
  - `POST /api/config/test/longbridge` 返回 `200` + 成功消息，解决设置页“测试连接不通”问题。
  - 通过 Nginx 反向代理路径验证：`POST http://localhost/api/config/test/longbridge` 也返回 `200`。
- AI 分析接口回归：
  - 未启用 OpenAI 时，`POST /api/ai/analyze/stock/{symbol}` 返回明确错误提示（400 + message）。
  - `POST /api/config/test/openai` 返回可读失败原因（未启用/未配置），符合预期。
- 前端构建验证：`npm --prefix frontend run build` 通过。
