---
name: frontend-design
description: QuantTrading 前端 UI 重构与开发的统一设计系统和规范 (QuantGlass Pro)
---

# Frontend Design Skill - QuantGlass Pro

以后所有 UI 重构、页面新增、组件新增，都必须遵守本设计系统。

## 1. 产品定位
- QuantTrading 是一个美股量化交易、回测、策略、AI 研究和知识库系统。
- UI 应该像现代金融终端，而不是普通后台管理系统。
- 目标风格：**QuantGlass Pro**。

## 2. 设计原则
- **数据优先**：信息密度高，阅读不累。
- **深色优先**：默认以深色 (Dark-first) 为主。
- **专业克制**：不要花哨的“炫酷大屏”，强调金融终端的严谨。
- **毛玻璃质感 (Glassmorphism)**：使用半透明表面与虚化背景 (`backdrop-filter`)。
- **轻 3D 层次**：使用渐变边框和内发光（Border Glow）、悬浮阴影（Float Shadow）。
- **响应式优先**：兼顾 Desktop, Tablet, Mobile。
- **不破坏业务逻辑**：不改动现有 Pinia、API、路由逻辑。

## 3. 视觉规范
请参考 `src/styles/tokens.scss` 与 `src/styles/glass.scss`：
- **背景**：深蓝黑渐变体系 (`--qt-bg`, `--qt-bg-soft`)。
- **卡片**：半透明玻璃态 (`--qt-surface-glass`)。
- **边框**：克制的分割线 (`--qt-border`) 及卡片高光边框 (`--qt-border-glow`)。
- **阴影**：卡片基础阴影 (`--qt-shadow-card`)，Hover 悬浮阴影 (`--qt-shadow-float`)。
- **涨跌色**：上涨 `color-up` (`--qt-success`)，下跌 `color-down` (`--qt-danger`)。
- **图表颜色**：折线和面积图填充应使用涨跌色变量。

## 4. 组件规范
优先使用以下组件：
- **AppShell/PageShell**：所有新页面使用统一的外层 Container (`main-content`)。
- **Dashboard 组件**：使用 `MetricCard`, `StockCard`, `TimeframeTabs` 等抽离的小卡片。
- **通用玻璃卡片**：在元素上应用 `.glass-panel` 类。
- **表单控件**：由 Element Plus 提供，但通过 `theme.scss` 全局覆盖，不要在局部硬编码 Element Plus 样式。

## 5. 编码规范
- **优先使用 CSS Variables**：不要再写 `#111827`、`#3b82f6` 等硬编码颜色，改用 `var(--qt-bg)` 等。
- **避免重复样式**：重复的卡片布局一定要抽出 Vue 组件（如 `StockCard.vue`）。
- **不改业务逻辑**：不要误动 `stores/` 或 `api/`。
- **每次修改要说明影响范围**。

## 6. 响应式规范
- **Desktop (>= 1280px)**：完整 Sidebar + Header + 多列网格内容。
- **Tablet (768px - 1279px)**：两列网格，允许面板内部滚动。
- **Mobile (< 768px)**：隐藏 Sidebar，改用 Drawer 或平铺内容为单列。

## 7. 执行改造后的验收清单
- [ ] 检查暗色模式下是否有刺眼白块或未覆盖的白色背景。
- [ ] 检查 Element Plus 下拉框、DatePicker 弹窗的层级背景是否透明重叠（需定义 `solid` 背景）。
- [ ] 检查文字对比度是否达标，浅色文字不能被亮背景吞没。
- [ ] 检查手机端布局是否有横向溢出。
- [ ] 检查图表红绿涨跌语义是否正确。
