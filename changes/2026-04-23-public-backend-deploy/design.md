# Request
将后端从本地临时隧道模式迁移为可公网访问的稳定部署，避免 `trycloudflare` 快速隧道失效导致线上前端不可用。

# Current Behavior
线上前端部署在 Vercel，后端仍依赖本地 `http://localhost:15000` 加 `trycloudflare` 临时隧道。临时隧道没有稳定性保证，且后端进程运行在本机，机器休眠、网络抖动或进程退出都会导致公网接口不可用。

# Proposed Change
为后端补齐容器云部署能力：

- 保留现有 Docker 本地运行方式。
- 新增公网容器部署 Dockerfile，使应用可监听云平台注入的 `PORT`。
- 增强数据库连接配置，支持云平台常见的 `DATABASE_URL` / `POSTGRES_URL`。
- 提供 Railway 和 Render 的配置即代码文件，便于使用托管 Postgres 与容器运行后端。
- 保留 Vercel 前端作为入口，公网后端部署成功后只需将 `BACKEND_API_BASE_URL` 指向稳定后端域名。

# Open Questions
当前机器没有 Railway/Fly/Render/Cloudflare named tunnel 登录凭证，因此无法直接创建云资源。需要用户登录其中一个容器平台，或提供平台 token，才能执行真正的云端创建与发布。

# Decision
优先准备 Railway 和 Render 两条通用路径。Railway 适合 CLI 一键发布；Render 适合通过 GitHub Blueprint 创建服务。两者都能运行 Docker 后端并使用托管 Postgres。

实际部署动作需要先完成平台登录。当前已验证 Railway CLI 返回 `Unauthorized`，因此本轮先提交可部署配置和本地镜像验证结果。

# Risks
- 新云数据库初始为空，若不迁移本地 `SystemConfigs` 表，部分设置会依赖环境变量兜底。
- 长桥和 AI 的密钥不能提交到仓库，只能写入云平台环境变量。
- Railway/Render 免费或低价规格可能有休眠、资源限制或冷启动，生产稳定性仍取决于套餐。

# Validation Plan
- 本地构建公网 Dockerfile。
- 验证后端可通过 `PORT` 启动并响应 `/health`。
- 验证 PostgreSQL URL 可被转换为 Npgsql 连接串。
- 云平台登录后，部署并验证 `/api/config`、`/api/config/test/longbridge`、`/api/ai/chat`。
