# Request

用户希望把当前项目成功运行起来，优先保证 `docker compose` 启动链路可用，并能访问前后端页面与 API。

# Current Behavior

当前仓库存在多处会阻塞启动的问题：

- `docker-compose.yml` 中的 SQL Server 健康检查依赖固定的 `sqlcmd` 路径，且未兼容新版本客户端的证书参数。
- 后端容器与前端容器的健康检查都使用 `curl`，但镜像内未确保安装该命令。
- 后端程序未暴露 `/health` 端点，但 Dockerfile 与 Compose 都依赖该端点判断健康状态。
- 后端启用了 `UseHttpsRedirection()`，在当前仅暴露 HTTP 的容器部署方式下可能影响容器内部探活与前端反向代理访问。
- 前端 Dockerfile 在 `frontend` 构建上下文中复制 `pnpm-lock.yaml*`，但该目录下不存在锁文件，且构建流程依赖 `pnpm`，实际容易失败。
- 目前前端虽然已经可以通过本机 `vite` 启动并访问，但并未真正切换到 Docker 运行；当前 Compose 的前端镜像仍依赖 Node 构建阶段，而该阶段受 Docker Hub 拉取 `node:20-alpine` 超时影响。

# Proposed Change

做一组最小但完整的启动修复：

- 为后端新增 `/health` 端点。
- 去掉当前容器部署场景下不适配的 HTTPS 重定向。
- 在后端与前端运行镜像中补齐健康检查所需的 `curl`。
- 修正 SQL Server 健康检查命令，兼容不同 `sqlcmd` 路径与 TLS 选项。
- 将前端 Docker 构建改为基于 `npm` 的可执行流程，避免缺失 `pnpm-lock.yaml` 导致构建失败。
- 顺带移除 Compose 里已废弃的 `version` 字段，减少告警噪声。
- 如果容器内安装额外探活工具或时区包导致构建过慢，则优先保留 SQL Server 健康检查，去掉前后端非关键健康检查与非关键系统包安装，以缩短启动路径。
- 若宿主机 `1433` 端口已被其他数据库占用，则改用备用宿主机端口映射，避免干扰用户现有容器。
- 若宿主机 `5000` 端口已被系统服务占用，则为后端改用备用宿主机端口映射，同时保持容器内部端口与服务发现不变。
- 若前端改走本机 `vite` 开发服务，则其代理目标应指向实际生效的后端宿主机端口，而不是仓库原始假设的 `5000`。
- 本轮将前端改为“预先在宿主机构建 `dist`，由 Docker 中的 Nginx 运行静态产物”的模式。这样前端访问入口完全走 Docker，但不再依赖在线拉取 Node 基础镜像。

# Open Questions

- 未配置 LongBridge 凭据时，部分依赖行情接口的页面是否会出现空数据或报错；这预计不应阻塞基础服务启动。
- 本机 Docker 环境是否已就绪，以及拉取镜像/构建镜像时是否需要额外网络权限。

# Decision

先聚焦“能够成功启动并访问”这一目标，不扩大到第三方行情能力、通知服务联调或业务数据正确性修复。当前决定继续保留宿主机构建前端产物的步骤，但最终运行时以前端 Docker 容器为准，不再依赖本机 `vite` 常驻。

# Risks

- 去掉 `UseHttpsRedirection()` 会降低后端直接暴露在公网时的默认安全性，但当前 Compose 拓扑本身仅配置了 HTTP，保留该中间件反而更容易导致运行失败。
- 前端改用 `npm install` 会降低依赖安装的确定性，但当前仓库缺失 `frontend/package-lock.json`，这是最小可执行方案。
- SQL Server 镜像若未来再次调整内置工具路径，健康检查仍可能需要后续微调。

# Validation Plan

- 优先运行 `docker compose up -d --build`。
- 对前端先执行宿主机构建，再运行 `docker compose up -d --build frontend`。
- 检查 `docker compose ps`，确认三个服务状态正常。
- 访问或探测：
  - 前端 `http://localhost` 或本机备用地址
  - 后端健康检查地址
- 如启动失败，查看对应容器日志并做最小追加修复。
