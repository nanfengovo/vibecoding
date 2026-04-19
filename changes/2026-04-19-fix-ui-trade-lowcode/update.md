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

# Files Changed
- `changes/2026-04-19-fix-ui-trade-lowcode/design.md`
- `changes/2026-04-19-fix-ui-trade-lowcode/update.md`
- `frontend/src/layouts/MainLayout.vue`
- `frontend/src/api/index.ts`
- `frontend/src/views/Trades.vue`
- `frontend/src/views/LowCodeWorkbench.vue`
- `frontend/src/views/Watchlist.vue`
- `.gitignore`

# Validation
- 暂未执行构建验证，待代码改造完成后统一验证。
