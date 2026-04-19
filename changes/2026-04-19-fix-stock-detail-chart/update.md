# Summary

修复股票详情页图表与价格信息不显示问题。

# Assumptions

当前目标是恢复详情页可见数据；分钟级别行情精度可后续优化。

# Chronological Updates

- 2026-04-19 18:56 +0800：确认 `/api/stocks/AAPL.US/quote` 返回 `404`，`/kline` 返回空数组，详情页图表无法渲染。
- 2026-04-19 18:58 +0800：定位到后端 LongBridge 行情路径不可用 + 前端字段名与后端返回模型不一致 + `limit/count` 参数不一致。
- 2026-04-19 19:02 +0800：后端增加 Stooq 兜底数据源，补齐 quote/kline 回退逻辑；`StocksController.GetKline` 兼容 `limit`。
- 2026-04-19 19:04 +0800：前端 `StockDetail` 增加 quote/kline 字段兼容映射，`stockApi.getKline` 改为传 `count`。
- 2026-04-19 19:15 +0800：重建并重启 `frontend` 容器，确保线上运行的静态资源切换到最新打包产物。
- 2026-04-19 19:18 +0800：回归验证通过：`quote`、`kline`、`search`、`watchlist` 均返回正常状态码。

# Files Changed

- `backend/QuantTrading.Api/Controllers/StocksController.cs`
- `backend/QuantTrading.Api/Services/LongBridge/LongBridgeService.cs`
- `frontend/src/api/index.ts`
- `frontend/src/types/index.ts`
- `frontend/src/views/StockDetail.vue`
- `changes/2026-04-19-fix-stock-detail-chart/design.md`
- `changes/2026-04-19-fix-stock-detail-chart/update.md`

# Validation

1. 容器与静态资源
   - `docker compose build frontend` 成功。
   - `docker compose up -d frontend` 成功，`quant-frontend` 已重建。
   - 首页 `http://localhost` 引用新资源：`/assets/index-DruBGDkj.js`。

2. 接口验证
   - `GET http://localhost/api/stocks/AAPL.US/quote` -> `200`，返回 `price/open/high/low/volume/timestamp`。
   - `GET http://localhost/api/stocks/AAPL.US/kline?period=M&limit=200` -> `200`，返回非空 K 线数组。
   - `GET http://localhost/api/stocks/search?query=AAPL` -> `200`。
   - `POST http://localhost/api/stocks/watchlist`（AAPL.US）-> `200`；随后测试数据已删除，`DELETE` -> `204`。

3. 已知限制
   - 当前分钟级行情在兜底源上会降级到可用历史粒度（优先保障图表可见），后续可替换为正式分钟级数据源。
