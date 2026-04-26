# Summary

本次变更用于补齐“本地 SQL Server -> Railway Postgres”的完整数据迁移能力，避免公网环境只同步到部分配置和列表数据。

# Assumptions

1. 本地 SQL Server 容器 `quant-sqlserver` 持续可用。
2. 公网后端通过 Railway 正常运行，并可通过 HTTPS 调用。
3. 迁移期间允许使用一次性的管理令牌。

# Chronological Updates

- 2026-04-24 23:xx: 创建变更文档，确认需要补齐完整数据库迁移能力，而不是继续手工同步个别数据。
- 2026-04-24 23:xx: 盘点本地业务表数量，确认本地核心持久化数据为 `SystemConfigs=27`、`Stocks=10`、`MonitorRules=3`、`Strategies=1`、`Trades=1`、`Accounts=1`。
- 2026-04-24 23:xx: 新增受 `Admin:MigrationToken` 保护的导入/摘要接口，计划通过远程后端写入 Railway Postgres，绕开本机直连 Railway Postgres 的连接问题。
- 2026-04-24 23:xx: 新增本地迁移脚本 `scripts/migrate_local_db_to_public_backend.py`，通过 `sqlcmd + FOR JSON PATH` 导出本地 SQL Server 数据，再调用公网导入接口。

# Files Changed

- `changes/2026-04-24-public-db-migration/design.md`
- `changes/2026-04-24-public-db-migration/update.md`
- `backend/QuantTrading.Api/Controllers/AdminMigrationController.cs`
- `scripts/migrate_local_db_to_public_backend.py`

# Validation

- 待补充：后端镜像构建、Railway 部署、迁移执行、迁移前后计数对比。
