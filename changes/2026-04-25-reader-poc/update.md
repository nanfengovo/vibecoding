# Summary

Reader PoC 已完成后端能力与前端页面落地，并打通 AI、知识库、信息采集联动。

# Chronological Updates

- 新增 Reader 后端模型：`ReaderBook`、`ReaderProgress`、`ReaderHighlight`。
- 在 `DbContext` 注册 Reader 表与索引，并为 PostgreSQL 文本列映射补齐 Reader 字段。
- 在 `DatabaseBootstrapService` 增加 Postgres/SQL Server 的 Reader 幂等建表与索引 SQL。
- 新增 `IReaderService` / `ReaderService`：支持上传图书、采集导入、内容读取、进度与划线管理。
- 新增 `ReaderController`：开放 `/api/reader/*` 读写接口。
- `Program.cs` 注册 Reader 服务。
- AI 接口扩展 `readerContext`，并在 `BuildChatPrompt` 注入阅读上下文。
- 前端新增 `readerApi` 与 Reader 类型定义。
- 新增页面：
  - `ReaderShelf.vue`（书架、上传、删除、打开）
  - `ReaderViewer.vue`（阅读、目录跳转、进度、划线、AI/知识库联动）
- 新增 `KoodoAdapter` 桥接层：优先尝试 Koodo runtime，失败自动回退 EPUB/PDF/MD 本地引擎。
- 信息采集页新增“转为阅读材料”按钮，直连 `readerApi.importCrawlerDocument`。
- 新增第三方合规目录 `third_party/koodo-reader/`，收录 AGPL 许可证与归属说明。
- 修复上传链路：`readerApi.uploadBook` 显式使用 `multipart/form-data`，确保后端能正确接收文件流。
- 优化阅读页稳态：新增离开页面时进度强制落盘与初始化失败回退提示，避免阅读进度丢失。
- 修复容器部署后的打开失败：为后端增加 `storage` 持久化卷，避免容器重建后上传文件丢失。
- 优化错误可观测性：`GET /api/reader/books/{id}/content` 在文件丢失时返回明确消息（410），前端显示具体原因并提示重新上传。
- 修复 PDF 阅读页无法选中文本：本地 PDF 引擎改为 `canvas + PDF.js TextLayer` 叠加渲染，选中文本后通过 `selectionchange` 自动回填右侧联动面板。
- PDF 固定走本地 PDF.js 引擎，避免后续 Koodo runtime 成功加载时回到不可选中的 canvas-only 渲染路径。
- AI 联动失败时前端会显示明确错误提示，避免按钮恢复后用户不知道失败原因。
- 修复 nginx 对 `.mjs` worker 的 MIME 类型：`pdf.worker.min.mjs` 现在以 `application/javascript` 返回。
- 2026-04-26 01:52 CST：进入二期联动改造，目标包含“PDF 划线可视化回绘、四个 AI 入口统一模型/提示词优化/记忆写入、记忆中心与知识库双向自动同步、数据库增量字段与索引升级”。

# Files Changed (Key)

- `backend/QuantTrading.Api/Models/Reader.cs`
- `backend/QuantTrading.Api/Services/Reader/IReaderService.cs`
- `backend/QuantTrading.Api/Services/Reader/ReaderService.cs`
- `backend/QuantTrading.Api/Controllers/ReaderController.cs`
- `backend/QuantTrading.Api/Services/AI/IAiAnalysisService.cs`
- `backend/QuantTrading.Api/Services/AI/OpenAiAnalysisService.cs`
- `backend/QuantTrading.Api/Controllers/AiController.cs`
- `backend/QuantTrading.Api/Data/QuantTradingDbContext.cs`
- `backend/QuantTrading.Api/Services/Auth/DatabaseBootstrapService.cs`
- `backend/QuantTrading.Api/Program.cs`
- `frontend/src/lib/reader/koodoAdapter.ts`
- `frontend/src/views/ReaderShelf.vue`
- `frontend/src/views/ReaderViewer.vue`
- `frontend/src/views/Crawler.vue`
- `frontend/nginx.conf`
- `frontend/src/router/index.ts`
- `frontend/src/api/index.ts`
- `frontend/src/types/index.ts`
- `frontend/src/types/epubjs.d.ts`
- `third_party/koodo-reader/LICENSE-AGPL-3.0.txt`
- `third_party/koodo-reader/NOTICE.md`

# Validation

- 本地尝试后端构建命令时，当前环境缺少 `dotnet` CLI（`command not found`），暂无法在该环境执行后端编译验证。
- 前端依赖已通过 `npm --prefix frontend install epubjs pdfjs-dist` 安装，并已实际执行 `npm --prefix frontend run build` 成功。
- 2026-04-26 已再次执行 `npm --prefix frontend run build` 成功，并通过 `docker compose up -d --build frontend` 重建本地容器。
- 已确认容器产物包含 `reader-pdf-text-layer`、`selectionchange` 和 PDF.js `TextLayer` 相关逻辑。
- 已确认 `/assets/pdf.worker.min-*.mjs` 返回 `Content-Type: application/javascript`。
