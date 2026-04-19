# Summary

本次工作用于修复当前项目的本地启动链路，目标是让 `docker compose` 可以成功构建并启动前端、后端与数据库服务。

# Assumptions

- 用户当前主要诉求是“项目先跑起来”，允许进行最小范围的启动修复。
- 未配置 LongBridge、邮件、飞书、企业微信等外部凭据时，系统仍应至少能完成基础启动。

# Chronological Updates

- 2026-04-18 20:12 CST：完成仓库结构、`docker-compose.yml`、README、Dockerfile、后端入口程序的初步检查，确认存在健康检查、容器依赖与前端构建链路问题。
- 2026-04-18 20:14 CST：创建变更记录目录与 `design.md` / `update.md`，明确本次仅聚焦本地启动链路修复。
- 2026-04-18 20:16 CST：修复 `docker-compose.yml` 中 SQL Server 健康检查，兼容 `/opt/mssql-tools18/bin/sqlcmd` 与 `/opt/mssql-tools/bin/sqlcmd`，并补充 `-C` 以适配新版默认 TLS 行为。
- 2026-04-18 20:16 CST：更新后端 Dockerfile，在运行镜像中安装 `curl`，避免容器健康检查命令缺失。
- 2026-04-18 20:16 CST：更新前端 Dockerfile，改为 `npm` 安装/构建流程，并在运行镜像中安装 `curl`，规避缺失 `pnpm-lock.yaml` 导致的构建失败。
- 2026-04-18 20:16 CST：更新 `Program.cs`，新增 `/health` 端点并移除当前容器部署场景下不适配的 HTTPS 重定向。
- 2026-04-18 20:20 CST：根据实际构建表现继续收敛启动路径，移除前后端非关键健康检查与对应镜像内额外装包步骤，减少构建阶段外部依赖。
- 2026-04-18 20:21 CST：移除后端镜像中的 `tzdata` 安装步骤，进一步降低构建耗时与外部源依赖。
- 2026-04-18 20:31 CST：发现宿主机 `1433` 端口已被现有容器 `sqlserver-dev` 占用，因此将本项目 SQL Server 的宿主机映射改为 `11433:1433`，避免影响现有环境。
- 2026-04-18 20:32 CST：发现宿主机 `5000` 端口被系统 `ControlCenter` 占用，因此将后端宿主机端口映射改为 `15000:5000`，避免与本机已有服务冲突。
- 2026-04-18 20:34 CST：为 `frontend/vite.config.ts` 增加 `VITE_API_TARGET` 兜底，默认代理到 `http://localhost:15000`，以适配当前本机运行的后端端口。
- 2026-04-18 20:40 CST：使用独立 npm 缓存目录完成前端依赖安装，生成 `frontend/package-lock.json`，绕开用户目录下历史 npm 缓存权限问题。
- 2026-04-18 20:47 CST：修复前端 `vue-tsc` 阶段的未使用变量、可空判断和类型收窄问题，并移除对额外 `uuid` 包的隐式依赖。
- 2026-04-18 20:51 CST：前端 `npm run build` 成功，随后以 `npm run dev -- --host 0.0.0.0 --port 18080` 启动本机开发服务器。
- 2026-04-18 20:52 CST：验证 `http://localhost:15000/health` 返回 `200 healthy`，验证 `http://localhost:18080/` 返回 `200`，验证 `http://localhost:18080/api/strategies` 经前端代理访问后端成功返回 `[]`。
- 2026-04-18 22:07 CST：根据用户要求继续把前端切换为 Docker 运行模式，决定采用“宿主机构建 `dist` + Docker 中 Nginx 运行”的方案，规避 Node 基础镜像拉取不稳定问题。
- 2026-04-18 22:09 CST：将 `frontend/Dockerfile` 改为纯运行时镜像，只复制 `dist` 与 `nginx.conf`；新增 `frontend/.dockerignore`，避免构建上下文携带 `node_modules` 和源码目录。

# Files Changed

- changes/2026-04-18-run-project/design.md
- changes/2026-04-18-run-project/update.md
- docker-compose.yml
- backend/QuantTrading.Api/Dockerfile
- frontend/Dockerfile
- backend/QuantTrading.Api/Program.cs
- frontend/vite.config.ts
- frontend/src/api/index.ts
- frontend/src/stores/app.ts
- frontend/src/views/Settings.vue
- frontend/src/views/StockDetail.vue
- frontend/src/views/StrategyEditor.vue
- frontend/src/views/Trades.vue
- frontend/src/views/Watchlist.vue
- frontend/package-lock.json

# Validation

- `docker compose up -d sqlserver backend --build` 成功启动数据库与后端。
- `docker compose ps` 显示：
  - `quant-sqlserver` 正常运行，宿主机端口 `11433`
  - `quant-backend` 正常运行，宿主机端口 `15000`
- `curl -i http://localhost:15000/health` 返回 `HTTP/1.1 200 OK` 与 `healthy`。
- `npm run build` 在 `frontend/` 下成功通过。
- 本机前端开发服务运行在 `http://localhost:18080/`。
- `curl -i http://localhost:18080/api/strategies` 返回 `HTTP/1.1 200 OK` 与空数组 `[]`。
