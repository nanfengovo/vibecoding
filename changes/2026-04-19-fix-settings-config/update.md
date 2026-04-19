# Summary

本次变更用于修复设置页配置保存与长桥连接测试失败的问题，并打通“保存后立即测试”的配置链路。

# Assumptions

假设设置页当前主要使用统一的 `GET /api/config` 与 `PUT /api/config` 结构化接口，因此优先修复这一条链路，而不是强制前端改回按分类提交。

# Chronological Updates

- 2026-04-19 16:37 +0800：查看 `ConfigController`、设置页和 nginx 配置，确认保存接口路径不匹配导致 `405`。
- 2026-04-19 16:38 +0800：查看通知与 LongBridge 服务，确认 `test/longbridge` 被错误当作通知测试处理，且 LongBridge 仅读取启动配置。
- 2026-04-19 16:43 +0800：重写 `ConfigController` 的统一配置读写逻辑，增加结构化 `GET /api/config`、`PUT /api/config`，并让 `test/longbridge` 走 `ILongBridgeService`。
- 2026-04-19 16:44 +0800：改造 `LongBridgeService`，在每次请求前从数据库和配置文件动态读取长桥与代理设置，支持保存后立即生效，并新增 `TestConnectionAsync`。
- 2026-04-19 16:46 +0800：重建后端容器并用 `curl` 验证，确认 `GET /api/config` 返回结构化数据，`PUT /api/config` 返回 `200 OK`，LongBridge 测试接口已进入真实连接校验流程。
- 2026-04-19 16:49 +0800：读取后端日志与数据库中的长桥配置，确认请求已带上保存后的凭据，但 LongBridge 返回 `401102 token verification failed`，并指向 Access Token 格式/类型问题。
- 2026-04-19 16:55 +0800：对当前保存的 Access Token 做本地结构化检查与四组鉴权实验，确认该值以 `m_` 开头；保留前缀时上游返回 `token is malformed`，去掉前缀后返回 `public key not found for kid`，说明当前值不是可被 OpenAPI 接受的 Legacy Access Token。
- 2026-04-19 16:57 +0800：补充后端错误透传、异常 token 提示和前端精确报错展示；设置页新增 Access Token 来源说明。
- 2026-04-19 16:59 +0800：重建前后端容器并再次验证，`POST /api/config/test/longbridge` 现返回明确消息：当前 Access Token 不是 LongBridge OpenAPI 所需的 Legacy Access Token。
- 2026-04-19 17:04 +0800：对照官方 `openapi-go` SDK 进一步核验，确认真正根因是后端把 Legacy 鉴权误写成了 `Bearer token + 自定义签名`；官方要求 Legacy 模式使用原始 `authorization` 头值并按 canonical request 规则生成 `x-api-signature`。
- 2026-04-19 17:08 +0800：按官方算法修复 `LongBridgeService` 的签名和鉴权实现，重新构建并验证 `POST /api/config/test/longbridge` 返回 `200 OK`。
- 2026-04-19 17:12 +0800：继续跟进用户现场反馈，发现测试链路还叠加了两个实现问题：
  - .NET 将 `"/v1/asset/account"` 误判为 `file:///...` 绝对 URI，需要显式限制只接受 `http/https` 绝对地址；
  - 数据库中保存的 `BaseUrl` 实际为文档地址 `https://open.longbridge.com/sdk`，不是 OpenAPI 网关地址。
- 2026-04-19 17:15 +0800：为 LongBridge BaseUrl 增加保存时与运行时的双重归一化逻辑，自动把文档地址纠正为 `https://openapi.longbridge.com` / `https://openapi.longbridge.cn`。
- 2026-04-19 17:16 +0800：前端设置页补充“测试前自动保存当前长桥配置”，并重建前后端容器；最终 `POST /api/config/test/longbridge` 稳定返回 `200 OK`。

# Files Changed

- `changes/2026-04-19-fix-settings-config/design.md`
- `changes/2026-04-19-fix-settings-config/update.md`
- `backend/QuantTrading.Api/Controllers/ConfigController.cs`
- `backend/QuantTrading.Api/Services/LongBridge/ILongBridgeService.cs`
- `backend/QuantTrading.Api/Services/LongBridge/LongBridgeService.cs`
- `frontend/src/views/Settings.vue`

# Validation

- `frontend`: `npm run build` 成功。
- `backend`: 本机缺少 `dotnet` CLI，改用 `docker compose build backend` 成功。
- 运行态验证：
  - `GET http://localhost/api/config` 返回 `200 OK`，且 JSON 结构与前端 `SystemConfig` 对齐。
  - `PUT http://localhost/api/config` 携带 `{"longBridge":{"baseUrl":"https://openapi.longportapp.com"}}` 返回 `200 OK`。
  - `POST http://localhost/api/config/test/longbridge` 最终返回 `200 OK`，响应为 `{"success":true,"message":"LongBridge 连接成功"}`。
  - 定位过程中的关键对照实验：
    - 按旧实现发送 `Authorization: Bearer <token>`：LongBridge 返回 `401102 token is malformed`
    - 去掉 `Bearer` 但沿用旧签名：LongBridge 返回 `403201 signature invalid`
    - 按官方 SDK 的 Legacy 鉴权规则发送：LongBridge 返回 `200 success`
    - 修复鉴权后再次排查，发现旧库数据里的 `BaseUrl=https://open.longbridge.com/sdk` 会返回静态站 `NoSuchKey`，修正为 `https://openapi.longbridge.com` 后恢复正常。
  - 最终结论：问题不在凭据本身，而在本项目的 LongBridge 鉴权实现与官方 SDK 不一致。
