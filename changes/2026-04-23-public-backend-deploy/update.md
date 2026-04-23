# Summary
为后端公网部署补齐容器平台配置和生产连接兼容能力。

# Assumptions
公网后端将部署到 Railway、Render 或同类可运行 Docker 常驻服务的平台；Vercel 继续托管前端。

# Chronological Updates
- 2026-04-23 22:16：确认当前机器只有 Vercel 登录态，没有 Railway/Fly/Render 登录态；Vercel 不适合作为 ASP.NET Core 常驻容器后端。
- 2026-04-23 22:21：新增公网容器 Dockerfile、Railway/Render 配置，并增强后端对 `DATABASE_URL` / `POSTGRES_URL` 的兼容。
- 2026-04-23 22:29：本地完成 `Dockerfile.backend` 构建，并用 `PORT=28080` 启动容器验证 `/health`。
- 2026-04-23 22:30：补充 README 公网部署说明和 Vercel 前端切换公网后端步骤。
- 2026-04-23 22:34：调整数据库连接判断，确保云平台只提供 Postgres URL 时也会优先使用 Postgres。

# Files Changed
- `.dockerignore`
- `Dockerfile.backend`
- `railway.toml`
- `render.yaml`
- `README.md`
- `backend/QuantTrading.Api/Program.cs`

# Validation
- `docker build -f Dockerfile.backend -t quanttrading-api-public:test .` 通过两次。
- 使用 `PORT=28080` 启动测试容器，`GET /health` 返回 `healthy`。
