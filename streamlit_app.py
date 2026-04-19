import os
from typing import Any

import pandas as pd
import requests
import streamlit as st

st.set_page_config(page_title="QuantTrading Cloud", page_icon="📈", layout="wide")

st.title("QuantTrading Cloud")
st.caption("Streamlit 轻量入口：查看关注列表、行情与交易记录")

backend_url = st.secrets.get("BACKEND_API_URL", os.getenv("BACKEND_API_URL", "")).rstrip("/")
frontend_url = st.secrets.get("FRONTEND_URL", os.getenv("FRONTEND_URL", "")).rstrip("/")
timeout = float(st.secrets.get("HTTP_TIMEOUT", os.getenv("HTTP_TIMEOUT", "20")))

if not backend_url:
    st.error("缺少 BACKEND_API_URL，请在 Streamlit Secrets 中配置后重试。")
    st.stop()


def fetch_json(path: str, params: dict[str, Any] | None = None):
    response = requests.get(f"{backend_url}{path}", params=params, timeout=timeout)
    response.raise_for_status()
    return response.json()


def safe_number(value: Any, fallback: float = 0.0) -> float:
    try:
        return float(value)
    except Exception:
        return fallback


col1, col2, col3 = st.columns([2, 1, 1])
with col1:
    st.write(f"**Backend**: `{backend_url}`")
with col2:
    if st.button("刷新数据"):
        st.rerun()
with col3:
    if frontend_url:
        st.link_button("打开前端", frontend_url)

tabs = st.tabs(["关注列表", "交易记录", "系统状态"])

with tabs[0]:
    try:
        watchlist = fetch_json("/api/stocks/watchlist")
        if not isinstance(watchlist, list):
            watchlist = []
    except Exception as ex:
        st.error(f"读取关注列表失败: {ex}")
        watchlist = []

    rows = []
    for item in watchlist:
        symbol = str(item.get("symbol", "")).upper()
        name = item.get("name", symbol)
        notes = item.get("notes", "")
        current = None
        change_pct = None

        if symbol:
            try:
                quote = fetch_json(f"/api/stocks/{symbol}/quote")
                current = safe_number(quote.get("current", quote.get("price", 0)))
                prev = safe_number(quote.get("previousClose", quote.get("prevClose", 0)))
                change_pct = ((current - prev) / prev * 100.0) if prev else 0.0
            except Exception:
                pass

        rows.append(
            {
                "代码": symbol,
                "名称": name,
                "现价": current,
                "涨跌幅(%)": change_pct,
                "备注": notes,
            }
        )

    if rows:
        df = pd.DataFrame(rows)
        st.dataframe(df, use_container_width=True, hide_index=True)
    else:
        st.info("暂无关注数据")

with tabs[1]:
    limit = st.slider("读取条数", min_value=20, max_value=500, value=100, step=20)
    try:
        trades = fetch_json("/api/trades", params={"limit": limit})
        if not isinstance(trades, list):
            trades = []
    except Exception as ex:
        st.error(f"读取交易记录失败: {ex}")
        trades = []

    normalized = []
    for row in trades:
        qty = safe_number(row.get("filledQuantity", row.get("quantity", 0)))
        price = safe_number(row.get("filledPrice", row.get("price", 0)))
        normalized.append(
            {
                "时间": row.get("filledAt") or row.get("createdAt"),
                "股票": str(row.get("symbol", "")).upper(),
                "方向": row.get("side"),
                "数量": qty,
                "价格": price,
                "金额": qty * price,
                "状态": row.get("status"),
                "订单ID": row.get("orderId"),
            }
        )

    if normalized:
        st.dataframe(pd.DataFrame(normalized), use_container_width=True, hide_index=True)
    else:
        st.info("暂无交易记录")

with tabs[2]:
    try:
        health = requests.get(f"{backend_url}/health", timeout=timeout)
        if health.ok:
            st.success(f"后端健康检查通过: {health.text}")
        else:
            st.warning(f"后端健康检查异常: HTTP {health.status_code}")
    except Exception as ex:
        st.error(f"无法访问后端健康检查: {ex}")

    st.markdown("### 免费数据库建议")
    st.markdown("- Neon Postgres（免费额度）")
    st.markdown("- Supabase Postgres（免费额度）")
    st.markdown("将连接串写入 `DB_CONNECTION`，并设置 `DB_PROVIDER=postgres`。")
