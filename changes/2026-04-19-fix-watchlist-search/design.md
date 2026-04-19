# Request

继续修复“股票监控失败”问题，目标是让关注列表至少可以稳定完成“搜索/添加/移除”闭环。

# Current Behavior

当前失败是多点叠加：

1. `stockApi.search` 参数名历史不一致，导致 `GET /api/stocks/search?query=...` 出现 `400`。
2. 前端关注列表 API 走 `/api/monitor/watchlist`，而后端真实路由在 `/api/stocks/watchlist`，导致新增/移除关注返回 `404`。
3. LongBridge 搜索使用了不存在的 `/v1/quote/search`，上游返回 `404000 api not found`，最终表现为“搜索结果为空、添加关注失败”。
4. SignalR 的订阅方法名不一致（前端 `SubscribeQuote`，后端 `SubscribeToSymbol`），导致行情订阅链路不稳定。

# Proposed Change

1. 保持 `StocksController.Search` 同时兼容 `keyword/query`。
2. 前端关注列表 API 切到 `/stocks/watchlist`，并在后端 `MonitorController` 增加 `/monitor/watchlist` 兼容路由，避免旧资源缓存继续报错。
3. `WatchlistService.AddToWatchlistAsync` 增加符号标准化与兜底创建逻辑，LongBridge 信息不可用时也允许先加入关注列表。
4. `LongBridgeService.SearchStocksAsync` 改为使用可用接口 `/v1/quote/get_security_list` + 本地过滤，并加短期缓存降低请求压力。
5. SignalR 增加 `SubscribeQuote/UnsubscribeQuote` 批量兼容方法，并在前端兼容两种 `QuoteUpdate` 参数格式。

# Decision

采用“功能可用优先 + 双向兼容”的策略：

1. 先保证用户操作不再被 400/404 阻断。
2. 对历史前端缓存、旧方法名做后端兼容，减少部署切换期间的不稳定。
3. 对外部行情接口不可用场景提供后端兜底，避免新增关注彻底失败。

# Risks

1. `get_security_list` 是市场列表接口，不是专门搜索接口，结果精确度低于专用搜索 API。
2. LongBridge 实时报价接口路径仍需后续按官方协议完善；本次先保证关注列表操作可用。

# Validation Plan

1. 前端构建通过。
2. 后端容器重建通过。
3. 接口验证：
   - `GET /api/stocks/search?keyword=AAPL` 返回 `200`
   - `GET /api/stocks/search?query=AAPL` 返回 `200`
   - `GET /api/monitor/watchlist` 与 `GET /api/stocks/watchlist` 都返回 `200`
   - `POST /api/stocks/watchlist` 可成功写入关注项（即使行情查询失败也不返回 404）
