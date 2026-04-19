# Summary

本次变更将“股票监控失败”拆成多处兼容点一起修复，目标是恢复关注列表可操作性。

# Assumptions

当前优先级是恢复“搜索、添加、移除关注”闭环，实时行情与策略监控精度优化可后续继续迭代。

# Chronological Updates

- 2026-04-19 17:20 +0800：查看 `StocksController`、`Watchlist.vue` 与 `frontend/src/api/index.ts`，确认前端发送 `query`，后端仅读取 `keyword`，导致 `/api/stocks/search` 返回 400。
- 2026-04-19 17:22 +0800：修复 `stockApi.search` 的参数名为 `keyword`，并让 `StocksController.Search` 同时兼容 `keyword` 与旧的 `query`。
- 2026-04-19 17:35 +0800：继续排查发现关注列表 API 前后端路由不一致。前端走 `/monitor/watchlist`，后端实现为 `/stocks/watchlist`。
- 2026-04-19 17:40 +0800：修改前端 `monitorApi` 关注列表接口到 `/stocks/watchlist`，并在后端 `MonitorController` 增加 `/monitor/watchlist` 兼容路由。
- 2026-04-19 17:45 +0800：为 `WatchlistService.AddToWatchlistAsync` 增加符号标准化与兜底创建逻辑，外部行情元数据拉取失败时不再直接返回“Stock not found”。
- 2026-04-19 17:50 +0800：重构 `LongBridgeService.SearchStocksAsync`，由无效的 `/v1/quote/search` 改为 `/v1/quote/get_security_list` + 本地过滤，并加入短时缓存。
- 2026-04-19 17:55 +0800：补齐 SignalR 方法名兼容（`SubscribeQuote/UnsubscribeQuote`）并让前端兼容两种 `QuoteUpdate` 事件参数格式。

# Files Changed

- `changes/2026-04-19-fix-watchlist-search/design.md`
- `changes/2026-04-19-fix-watchlist-search/update.md`
- `frontend/src/api/index.ts`
- `backend/QuantTrading.Api/Controllers/StocksController.cs`
- `backend/QuantTrading.Api/Controllers/MonitorController.cs`
- `backend/QuantTrading.Api/Services/Monitor/IMonitorService.cs`
- `backend/QuantTrading.Api/Services/Monitor/WatchlistService.cs`
- `backend/QuantTrading.Api/Services/LongBridge/LongBridgeService.cs`
- `backend/QuantTrading.Api/Hubs/TradingHub.cs`
- `frontend/src/api/signalr.ts`

# Validation

已完成：

1. 前端构建通过：`npm run build`。
2. 后端镜像重建并重启容器通过：`docker compose build backend && docker compose up -d backend`。
3. 接口验证通过：
   - `GET /api/stocks/search?keyword=AAPL` => `200`（可返回 `AAPL.US` 等结果）
   - `GET /api/stocks/search?query=AAPL` => `200`
   - `GET /api/stocks/watchlist` => `200`
   - `GET /api/monitor/watchlist` => `200`
   - `POST /api/stocks/watchlist` 与 `POST /api/monitor/watchlist` 均可成功写入
   - 搜索接口首查约 3.8s（拉取市场列表并入缓存），后续缓存命中约 0.1s
4. 验证后已回滚测试关注项，当前关注列表恢复为空。
