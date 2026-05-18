#!/usr/bin/env python3
import argparse
import base64
import json
import subprocess
import sys
import urllib.error
import urllib.request
from typing import Optional


TABLES = [
    "Accounts",
    "Backtests",
    "MonitorAlerts",
    "MonitorRules",
    "NotificationLogs",
    "Positions",
    "Reviews",
    "StockKlines",
    "StockQuotes",
    "Stocks",
    "Strategies",
    "StrategyExecutions",
    "SystemConfigs",
    "Trades",
]


def run_sqlcmd_json(table_name: str) -> list:
    if table_name == "SystemConfigs":
        query = """
SET NOCOUNT ON;
SET TEXTSIZE 2147483647;
SELECT
    [Id],
    [Key],
    CAST('' AS XML).value(
        'xs:base64Binary(xs:hexBinary(sql:column("ValueHex")))',
        'varchar(max)'
    ) AS [ValueBase64],
    [Category],
    [Description],
    [IsEncrypted],
    [UpdatedAt]
FROM (
    SELECT
        [Id],
        [Key],
        master.dbo.fn_varbintohexstr(CONVERT(varbinary(max), ISNULL([Value], N''))) AS [ValueHex],
        [Category],
        [Description],
        [IsEncrypted],
        [UpdatedAt]
    FROM QuantTrading.dbo.SystemConfigs
) AS src
FOR JSON PATH, INCLUDE_NULL_VALUES;
"""
    else:
        query = f"""
SET NOCOUNT ON;
SET TEXTSIZE 2147483647;
SELECT * FROM QuantTrading.dbo.{table_name} FOR JSON PATH, INCLUDE_NULL_VALUES;
"""

    result = subprocess.run(
        [
            "docker",
            "exec",
            "-i",
            "quant-sqlserver",
            "/bin/bash",
            "-lc",
            'SQLCMD=/opt/mssql-tools18/bin/sqlcmd; [ -x "$SQLCMD" ] || SQLCMD=/opt/mssql-tools/bin/sqlcmd; "$SQLCMD" -C -S localhost -U sa -P "$SA_PASSWORD" -i /dev/stdin -y 0 -Y 0 -w 65535',
        ],
        input=query,
        check=True,
        capture_output=True,
        text=True,
    )

    raw = result.stdout.replace("\r", "").replace("\n", "").strip()
    if not raw:
        return []

    start = raw.find("[")
    end = raw.rfind("]")
    if start == -1 or end == -1 or end < start:
        raise RuntimeError(f"未能从表 {table_name} 的输出中解析 JSON。原始输出前 500 字符：{raw[:500]}")

    rows = json.loads(raw[start : end + 1])
    if table_name == "SystemConfigs":
        for row in rows:
            row["Value"] = base64.b64decode(row.pop("ValueBase64", "")).decode("utf-8")
    return rows


def export_dataset() -> dict:
    dataset = {}
    for table in TABLES:
        rows = run_sqlcmd_json(table)
        dataset[table[0].lower() + table[1:]] = rows
        print(f"[export] {table}: {len(rows)} rows", file=sys.stderr)
    return dataset


def request_json(url: str, token: str, method: str = "GET", payload: Optional[dict] = None) -> dict:
    headers = {
        "X-Migration-Token": token,
    }
    data = None
    if payload is not None:
        headers["Content-Type"] = "application/json"
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8")

    request = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=300) as response:
            body = response.read().decode("utf-8")
            return json.loads(body) if body else {}
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"{method} {url} 失败: {exc.code} {detail}") from exc


def main() -> int:
    parser = argparse.ArgumentParser(description="将本地 SQL Server 的 QuantTrading 数据迁移到公网后端。")
    parser.add_argument("--remote-base-url", required=True, help="公网后端地址，例如 https://quanttrading-api-production.up.railway.app")
    parser.add_argument("--token", required=True, help="迁移令牌，对应后端 Admin:MigrationToken")
    args = parser.parse_args()

    remote_base_url = args.remote_base_url.rstrip("/")
    dataset = export_dataset()

    before = request_json(f"{remote_base_url}/api/admin/migration/summary", args.token)
    print(f"[remote-before] {json.dumps(before, ensure_ascii=False)}", file=sys.stderr)

    after = request_json(
        f"{remote_base_url}/api/admin/migration/import",
        args.token,
        method="POST",
        payload=dataset,
    )
    print(f"[remote-after] {json.dumps(after, ensure_ascii=False)}", file=sys.stderr)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
