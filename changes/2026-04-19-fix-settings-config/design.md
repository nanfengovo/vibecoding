# Request

修复设置页中的两个问题：

1. 保存长桥等配置时，前端请求 `PUT /api/config` 返回 `405 Method Not Allowed`。
2. 点击“测试连接”时，请求 `POST /api/config/test/longbridge` 返回 `400 Bad Request`。

# Current Behavior

当前前端 `frontend/src/api/index.ts` 将整份 `SystemConfig` 直接发送到 `PUT /api/config`，但后端 `ConfigController` 只提供 `PUT /api/config/{category}`，因此保存时接口路径不匹配。

同时，前端设置页使用的是驼峰命名和结构化对象，后端返回的是按分类和键名分组的字典，前端没有做映射，保存和读取都依赖了错误的数据形状。

长桥“测试连接”接口虽然命中了 `POST /api/config/test/longbridge`，但当前后端只把 `test/{channel}` 当作通知渠道测试来处理，`NotificationService` 并不支持 `longbridge`，因此固定返回失败。此外，`LongBridgeService` 只读取进程启动时的配置，不读取数据库中刚刚保存的设置，导致“保存后立即测试”这条链路也不成立。

# Proposed Change

后端增加一个面向设置页的统一配置读写层：

1. `GET /api/config` 直接返回前端需要的 `SystemConfig` 结构。
2. `PUT /api/config` 接收前端的局部配置对象，并在控制器中映射到 `SystemConfigs` 表。
3. 保留 `PUT /api/config/{category}` 兼容现有分类更新方式。

针对长桥测试：

1. 在 `ConfigController` 中新增专门的 LongBridge 测试分支，不再复用通知测试逻辑。
2. `LongBridgeService` 在发送请求前动态读取数据库中的长桥和代理配置，确保刚保存的配置可以立即生效。
3. `LongBridgeService` 提供轻量级测试方法，通过一次真实的只读接口调用验证凭据和连通性。

前端同步收敛 API 调用方式：

1. `configApi.update` 继续发送局部对象，但后端改为支持该形状。
2. 设置页加载配置时直接消费统一结构，不再依赖错误的浅合并行为。

# Open Questions

是否需要把邮件、飞书、企业微信配置也全部映射为统一结构并支持掩码字段保留。

# Decision

统一在后端控制器中完成“数据库键值配置 <-> 前端结构化配置”的双向映射，并补充 LongBridge 专属测试能力。这样可以最小化前端改动，同时让现有其他依赖 `SystemConfigs` 的服务继续复用数据库配置。

# Risks

配置键名历史上存在不一致，例如前端使用 `smtpHost`，后端邮件服务读取 `SmtpServer`。映射时如果遗漏，会造成保存成功但运行时读取不到值。

`LongBridgeService` 从单例服务改为每次请求读数据库后，需要避免缓存旧值或与 `HttpClient` 的固定 `BaseAddress` 冲突。

# Validation Plan

1. 运行前端构建，确认 TypeScript 类型和设置页代码通过。
2. 运行后端构建，确认控制器和服务改动编译通过。
3. 通过代码检查确认：
   - `PUT /api/config` 已被后端支持；
   - `POST /api/config/test/longbridge` 不再走通知测试；
   - LongBridge 请求会读取数据库中的最新配置。
