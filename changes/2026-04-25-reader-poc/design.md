# Request

实现图书阅读 PoC：首期支持 EPUB/PDF，本地上传与采集文档转阅读材料，并与 AI、知识库、信息采集联动。

# Current Behavior

系统已有 AI Chat、知识库、信息采集模块，但没有图书阅读域模型、阅读 API 和阅读页面。

PDF 阅读器已经支持通过 PDF.js `TextLayer` 选中文字并保存划线记录，但保存后当前阅读画布没有把已保存划线绘制回 PDF 页面，用户会误以为“保存划线没生效”。

# Proposed Change

新增 Reader 领域（`ReaderBook` / `ReaderProgress` / `ReaderHighlight`），提供上传、内容读取、进度持久化、划线批注等 API；前端新增书架页与阅读页，并通过 `KoodoAdapter` 统一阅读引擎桥接（EPUB/MD 优先尝试 Koodo 运行时，失败自动回退本地实现；PDF 固定走本地 PDF.js 文本层实现，保证页面文字可选中并可联动 AI/知识库）。

阅读页联动：

- 选中内容后可“问 AI”（带 `readerContext`）
- 选中内容可“保存为记忆”
- 选中内容可“入知识库”

PDF 划线展示：

- `KoodoAdapter` 增加轻量 `setHighlights` 桥接接口；
- PDF 本地引擎保存并缓存本书划线列表；
- 每次加载划线、保存划线、删除划线或翻页渲染完成后，按当前页 `locator` 与 `selectedText` 在 PDF.js `TextLayer` 中做文本匹配；
- 命中的文字 span 添加半透明背景色，用于展示已保存划线。

信息采集联动：

- 采集文档支持“一键转为阅读材料”

合规与治理：

- 新增 `third_party/koodo-reader` 目录收录 AGPL 许可证和来源声明
- 阅读页增加 Open Source Notices 入口

# Risks

- 部分环境无法直接加载 Koodo ESM 运行库时，将自动回退本地阅读引擎实现（EPUB/PDF/MD）；
- PDF 需要文本层才能支持浏览器原生选中；扫描版 PDF 或无文本层 PDF 仍无法提取可选文本；
- PDF 划线回绘采用文本匹配方式，遇到复杂断字、特殊编码或扫描版 PDF 时，后端记录可保存，但页面底色可能无法精确命中；
- 后端运行环境若无 `dotnet` CLI，无法在本地直接执行编译验证。

# Validation Plan

- 后端构建：`dotnet build backend/QuantTrading.Api/QuantTrading.Api.csproj`
- 前端构建：`npm --prefix frontend run build`
- 手工验证上传 EPUB/PDF、采集转阅读、进度恢复、划线 CRUD、AI/知识库联动与用户隔离。
