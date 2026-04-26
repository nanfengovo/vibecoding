# Request

用户希望将本地 SQL Server 中的业务数据完整迁移到公网 Railway Postgres，使公网后端与本地状态尽量一致，而不是只有配置、自选股、监控规则等部分数据。

# Current Behavior

当前公网后端已经部署到 Railway，前端也已切换到公网后端。

现状存在两个问题：

1. Railway Postgres 无法方便地从本机直接使用 `psql` 进行稳定导入。
2. 之前只同步了配置、自选股、监控规则，未完整同步 `Strategies`、`Accounts`、`Trades` 等表。

另外，当前模型中的若干 `nvarchar(max)` 已通过 `OnModelCreating` 做了 PostgreSQL 兼容修复，但尚缺少完整的数据迁移通道。

# Proposed Change

增加一个受令牌保护的后端导入入口，仅在配置了 `Admin:MigrationToken` 时可用：

1. 提供 `/api/admin/migration/import` 接口。
2. 接口接收本地数据库导出的完整业务数据快照。
3. 接口在事务中按依赖顺序清空并重建目标表数据。
4. 对 PostgreSQL 的 identity/sequence 做重置，确保导入后继续新增记录不会主键冲突。

同时新增一个本地迁移脚本：

1. 从本地 SQL Server 容器中读取所有业务表数据。
2. 组装为统一 JSON 负载。
3. 使用迁移令牌调用公网导入接口。
4. 导入完成后执行基础校验。

迁移完成后可移除 Railway 上的 `Admin__MigrationToken`，使该入口失效。

# Open Questions

1. 是否需要把完全无业务数据的空表也显式导入。
2. 是否需要保留该迁移入口长期存在。

# Decision

1. 空表也纳入快照结构，但允许为空数组。
2. 迁移入口保留在代码中，但只有设置了 `Admin__MigrationToken` 才可访问；迁移结束后建议删除该环境变量。
3. 迁移以“全量替换”为原则，不做复杂增量合并，保证公网与本地一致性更高。

# Risks

1. 全量替换会覆盖公网现有业务表数据。
2. 若导入顺序不正确，外键关系可能失败。
3. 若序列未重置，后续新增记录可能出现主键冲突。
4. 若导出脚本对 SQL Server JSON 序列化处理不当，可能出现字段缺失或时间格式偏差。

# Validation Plan

1. 本地构建后端镜像，确认新增代码可编译。
2. 在 Railway 设置迁移令牌并部署新版本。
3. 运行迁移脚本将本地数据导入公网。
4. 对比本地与公网以下表的数量：`Stocks`、`Strategies`、`Trades`、`Accounts`、`SystemConfigs`、`MonitorRules`。
5. 校验公网前端页面与接口表现正常，包括配置、自选股、策略、AI/LongBridge 链路。
