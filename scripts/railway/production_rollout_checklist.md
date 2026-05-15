# Railway 现网发布清单（固定资源）

- Project: `7ebabaad-4217-4592-a930-d21492c72f1d`
- Backend Service: `eb802fa1-6546-4cf8-8ff6-7a93d14bc8ce`
- Database Service: `ebdaafc7-c4e6-4b98-9880-e693f74cc361`
- Environment: `cf2751d7-bf30-456e-a7ef-99667ee24d5b`

## 1) 发布前冻结

1. 确认 `DATABASE_URL` 指向项目内数据库服务。
2. 确认数据库备份可用。
3. 导出关键配置快照（`openai`/`longbridge`/`aiorchestrator`）。

## 2) 应用先发，能力默认关闭

推荐环境变量：

```env
Ai__Orchestrator__DefaultMode=legacy
Ai__Orchestrator__McpToolExecutionEnabled=false
Ai__Trace__ExposePublicTrace=false
Ai__Trace__AuditTraceEnabled=false
```

## 3) 先做 schema，不建向量索引

```bash
psql "$DATABASE_URL" -f scripts/db/vector_schema.sql
```

## 4) 低峰期单独执行 HNSW

```bash
psql "$DATABASE_URL" -f scripts/db/vector_indexes_hnsw.sql
```

- 必须 `CONCURRENTLY` 且不要放事务。
- 小规格实例按“一次一个索引”执行。

## 5) 灰度切换

1. `legacy` -> `shadow`（24h）
2. `shadow` -> `fallback`（小流量到全量）
3. 评估后再考虑 `maf`

## 6) 回滚

- 一键回退：`Ai__Orchestrator__DefaultMode=legacy`
- 保留 schema/index，不做破坏性回滚。
