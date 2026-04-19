# Request

修复股票详情页图表不显示问题。当前 `/stock/AAPL.US` 页面请求 `GET /api/stocks/AAPL.US/quote` 返回 `404`，K 线区域为空白。

# Current Behavior

1. 后端 `LongBridgeService` 的 `quote/realtime` 与 `quote/candlesticks` 调用路径在当前环境下无法返回有效数据，导致：
   - `GET /api/stocks/{symbol}/quote` 返回 `404`
   - `GET /api/stocks/{symbol}/kline` 返回空数组
2. 前端 `StockDetail.vue` 预期字段为 `current/time`，而后端返回字段偏向 `price/timestamp`，存在契约不一致。
3. 前端 `getKline` 发送参数是 `limit`，后端控制器读取的是 `count`。

# Proposed Change

1. 后端行情服务增加兜底数据源：
   - 优先尝试现有 LongBridge 调用
   - 失败时回退到可访问的 Stooq 数据接口（最新行情 CSV + 历史数据页面解析）
2. 后端 `StocksController.GetKline` 同时兼容 `count` 与 `limit` 参数。
3. 前端 `stockApi.getKline` 改用 `count` 参数。
4. 前端 `StockDetail.vue` 做字段兼容映射：
   - quote: `current/price`
   - kline: `time/timestamp`
   保证图表和价格信息能显示。

# Decision

采用“后端兜底 + 前端兼容”组合修复，优先恢复页面可见数据与交互稳定性。

# Risks

1. Stooq 历史数据来自页面解析，结构变更会影响解析逻辑，需要保留降级容错。
2. 分钟级 K 线暂无稳定免费数据源，先降级到日/周/月历史数据，后续再接入正式分钟行情源。

# Validation Plan

1. 构建前端并重建后端容器。
2. 验证接口：
   - `GET /api/stocks/AAPL.US/quote` 返回 `200`
   - `GET /api/stocks/AAPL.US/kline?period=M&limit=200` 返回非空
3. 页面验证：`/stock/AAPL.US` K 线图可见且价格区不再是全 `-`。
