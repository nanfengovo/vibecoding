# Summary
本次变更聚焦主题切换、关注列表、监控规则编辑、交易记录筛选与显示、低代码规则流增强及移动端适配。

# Assumptions
- 默认沿用现有前后端接口，不做大规模数据库结构变更。
- 自动交易执行遵循“显式启用”原则，默认不自动下单。
- 模板导入导出以 JSON 文件形式实现。

# Chronological Updates
- 2026-04-19 21:37 已创建变更记录文件，开始进入实现阶段。
- 2026-04-19 21:42 已完成 `MainLayout.vue` 第一轮修复：统一主题变量颜色、修复深色模式局部亮色问题、增加移动端侧边栏收起/展开与遮罩关闭能力、优化头部与内容区在小屏下布局。
- 2026-04-19 21:48 已完成交易记录链路修复：
  - `tradeApi.list` 增加交易数据归一化，修复字段不一致导致的金额/佣金显示异常；
  - 增加 `status`、日期区间和关键字模糊筛选；
  - `Trades.vue` 筛选控件改为联动刷新，表格货币显示改为容错格式化，卡片和移动端布局适配增强。
- 2026-04-19 21:57 已重构 `LowCodeWorkbench.vue`：
  - 新增规则流拖拽编排（查询/公式/条件/交易/回测/通知步骤）；
  - 支持流程启用开关与自动交易开关；
  - 支持接口查询、公式变量计算、条件短路、条件满足后自动下单；
  - 支持模板保存、导入、导出、应用、删除；
  - 新增执行日志与 JSON 实时预览，并补充移动端适配。
- 2026-04-19 22:00 已补充 `Watchlist.vue`：
  - 页面加载时主动刷新关注列表与规则列表，减少“只显示 -”场景；
  - 卡片与文本颜色切换到主题变量，提升明暗切换一致性；
  - 增加移动端卡片单列与条件编辑器换行适配。
- 2026-04-19 22:03 已执行前端构建验证（`npm --prefix frontend run build`），构建成功。
- 2026-04-19 22:04 更新 `.gitignore`，补充 `frontend/dist`、`bin/obj`、`logs` 等构建产物忽略规则，避免提交冗余文件。
- 2026-04-19 22:24 已在项目目录初始化 git 仓库并完成提交，代码已推送到 `https://github.com/nanfengovo/vibecoding` 的 `master` 分支（提交 `00d097a`）。
- 2026-04-19 22:31 为部署准备增强：
  - 后端支持 `sqlserver/postgres` 双数据库驱动（便于接入 Neon/Supabase 免费 Postgres）；
  - 新增 `streamlit_app.py` + `requirements.txt` + `.streamlit/config.toml`，可直接用于 Streamlit Cloud 部署；
  - 更新 `README.md` 增加 Streamlit 部署与免费数据库配置说明；
  - 增加全局移动端样式兜底（`main.scss`）。
- 2026-04-19 22:33 再次执行前端构建验证通过；尝试执行后端 `dotnet build` 时环境缺少 `dotnet` 命令，未能完成本地后端编译验证。
- 2026-04-19 23:23 新增实时推送能力：
  - 后端新增 `RealtimePushService`，统一向 TradingHub 推送 `QuoteUpdate / TradeUpdate / Notification / MonitorAlert / StrategyReloaded`；
  - `WatchlistService` 在刷新行情后主动推送实时行情，且修正 `PreviousClose` 计算来源；
  - `MonitorService` 在触发规则后推送监控告警与站内通知；
  - `TradeService` 在下单/撤单后推送交易更新与通知；
  - `StrategiesController` 增加 `/api/strategies/{id}/reload` 接口，补全前端“热重载”调用缺口；
  - 前端 `signalr.ts` 增加断线后自动重订阅、手动断开不误重连、事件监听去重；
  - `app` store 新增交易/监控告警监听，增强通知可见性。
- 2026-04-19 23:24 已完成编译验证：
  - `npm --prefix frontend run build` 通过；
  - `docker compose build backend` 通过（存在历史 warning，不阻塞构建）。

# Files Changed
- `changes/2026-04-19-fix-ui-trade-lowcode/design.md`
- `changes/2026-04-19-fix-ui-trade-lowcode/update.md`
- `frontend/src/layouts/MainLayout.vue`
- `frontend/src/api/index.ts`
- `frontend/src/views/Trades.vue`
- `frontend/src/views/LowCodeWorkbench.vue`
- `frontend/src/views/Watchlist.vue`
- `.gitignore`
- `backend/QuantTrading.Api/QuantTrading.Api.csproj`
- `backend/QuantTrading.Api/Program.cs`
- `backend/QuantTrading.Api/appsettings.json`
- `.env.example`
- `streamlit_app.py`
- `requirements.txt`
- `.streamlit/config.toml`
- `README.md`
- `frontend/src/styles/main.scss`
- `backend/QuantTrading.Api/Services/Realtime/IRealtimePushService.cs`
- `backend/QuantTrading.Api/Services/Realtime/RealtimePushService.cs`
- `backend/QuantTrading.Api/Services/Monitor/WatchlistService.cs`
- `backend/QuantTrading.Api/Services/Monitor/MonitorService.cs`
- `backend/QuantTrading.Api/Services/Monitor/TradeService.cs`
- `backend/QuantTrading.Api/Controllers/StrategiesController.cs`
- `frontend/src/api/signalr.ts`
- `frontend/src/stores/app.ts`

# Validation
- 暂未执行构建验证，待代码改造完成后统一验证。
