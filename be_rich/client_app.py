#!/usr/bin/env python3
from __future__ import annotations

import pandas as pd
import streamlit as st
from streamlit_autorefresh import st_autorefresh
from typing import Optional

from market_data import (
    PREMIUM_MULTIPLIER,
    PREMIUM_SCALE_MAX,
    MarketDataService,
    QuoteSnapshot,
    describe_premium_ratio,
    format_change_percent,
    format_large_number,
    format_market_cap_billions,
    format_price,
    normalize_history_for_comparison,
    premium_scale_position,
)

st.set_page_config(
    page_title="Copper Pulse",
    layout="wide",
    initial_sidebar_state="expanded",
)


def inject_styles() -> None:
    st.markdown(
        """
        <style>
        @import url('https://fonts.googleapis.com/css2?family=Manrope:wght@400;600;700;800&family=Noto+Sans+SC:wght@400;500;700&display=swap');

        [data-testid="stAppViewContainer"] {
            background:
                radial-gradient(circle at top left, rgba(188, 120, 56, 0.12), transparent 32%),
                radial-gradient(circle at 85% 8%, rgba(40, 86, 116, 0.14), transparent 28%),
                linear-gradient(180deg, #f6f1e8 0%, #f1efe9 46%, #eef3f5 100%);
            color: #10212d;
        }

        [data-testid="stSidebar"] {
            background: rgba(247, 243, 235, 0.96);
            border-right: 1px solid rgba(16, 33, 45, 0.08);
        }

        .block-container {
            max-width: 1240px;
            padding-top: 1.2rem;
            padding-bottom: 2.2rem;
        }

        h1, h2, h3, h4, p, span, div, label {
            font-family: "Manrope", "Noto Sans SC", "PingFang SC", sans-serif !important;
        }

        .hero-shell {
            padding: 2rem 2rem 1.2rem 2rem;
            border-radius: 28px;
            background: linear-gradient(135deg, rgba(255, 250, 242, 0.9) 0%, rgba(235, 242, 247, 0.92) 100%);
            border: 1px solid rgba(16, 33, 45, 0.08);
            box-shadow: 0 18px 48px rgba(16, 33, 45, 0.08);
            overflow: hidden;
        }

        .hero-grid {
            display: grid;
            grid-template-columns: minmax(0, 1.6fr) minmax(280px, 0.9fr);
            gap: 1.4rem;
            align-items: start;
        }

        .eyebrow {
            margin: 0;
            letter-spacing: 0.18em;
            text-transform: uppercase;
            font-size: 0.72rem;
            color: #7a624f;
            font-weight: 700;
        }

        .hero-title {
            margin: 0.45rem 0 0 0;
            font-size: clamp(2.4rem, 4vw, 4.4rem);
            line-height: 0.94;
            font-weight: 800;
            letter-spacing: -0.04em;
            color: #10212d;
        }

        .hero-copy {
            margin: 1rem 0 0 0;
            max-width: 40rem;
            color: rgba(16, 33, 45, 0.78);
            font-size: 1rem;
            line-height: 1.7;
        }

        .hero-meta {
            padding: 1.2rem 1.2rem 1.1rem 1.2rem;
            border-radius: 22px;
            background: rgba(255, 255, 255, 0.54);
            border: 1px solid rgba(16, 33, 45, 0.08);
            backdrop-filter: blur(6px);
        }

        .hero-meta-item + .hero-meta-item {
            margin-top: 0.95rem;
            padding-top: 0.95rem;
            border-top: 1px solid rgba(16, 33, 45, 0.08);
        }

        .hero-meta-label {
            display: block;
            font-size: 0.72rem;
            letter-spacing: 0.16em;
            text-transform: uppercase;
            color: rgba(16, 33, 45, 0.5);
        }

        .hero-meta-value {
            display: block;
            margin-top: 0.4rem;
            font-size: 1rem;
            color: #10212d;
            font-weight: 700;
        }

        .hero-strip {
            margin-top: 1.3rem;
            display: grid;
            grid-template-columns: repeat(3, minmax(0, 1fr));
            gap: 0.9rem;
        }

        .hero-stat {
            padding: 1rem 1.05rem;
            border-radius: 20px;
            background: rgba(255, 255, 255, 0.62);
            border: 1px solid rgba(16, 33, 45, 0.08);
        }

        .hero-stat-label {
            display: block;
            font-size: 0.8rem;
            color: rgba(16, 33, 45, 0.58);
            margin-bottom: 0.4rem;
        }

        .hero-stat-value {
            display: block;
            font-size: 1.5rem;
            font-weight: 800;
            color: #10212d;
            line-height: 1.1;
        }

        .hero-stat-sub {
            display: block;
            margin-top: 0.35rem;
            font-size: 0.9rem;
            font-weight: 600;
        }

        .tone-up { color: #0f7b53; }
        .tone-down { color: #b64036; }
        .tone-flat { color: #5a6470; }

        .section-title {
            margin: 1.2rem 0 0.25rem 0;
            font-size: 1.25rem;
            font-weight: 800;
            color: #10212d;
        }

        .section-copy {
            margin: 0 0 0.8rem 0;
            color: rgba(16, 33, 45, 0.68);
            line-height: 1.6;
        }

        .premium-shell,
        .insight-shell {
            padding: 1.2rem;
            border-radius: 22px;
            background: rgba(255, 255, 255, 0.66);
            border: 1px solid rgba(16, 33, 45, 0.08);
            box-shadow: 0 10px 30px rgba(16, 33, 45, 0.04);
        }

        .premium-topline,
        .insight-item {
            display: flex;
            align-items: baseline;
            justify-content: space-between;
            gap: 1rem;
        }

        .premium-topline strong {
            font-size: 1.65rem;
            color: #10212d;
        }

        .premium-scale {
            margin: 1rem 0 0.5rem 0;
            display: flex;
            justify-content: space-between;
            font-size: 0.75rem;
            letter-spacing: 0.14em;
            text-transform: uppercase;
            color: rgba(16, 33, 45, 0.5);
        }

        .premium-bar {
            position: relative;
            height: 14px;
            border-radius: 999px;
            background: linear-gradient(
                90deg,
                #7c91a7 0%,
                #7c91a7 68.75%,
                #b79a6b 68.75%,
                #b79a6b 81.25%,
                #c8945a 81.25%,
                #c8945a 87.5%,
                #d25a49 87.5%,
                #d25a49 100%
            );
            overflow: hidden;
        }

        .premium-marker {
            position: absolute;
            top: -4px;
            width: 18px;
            height: 22px;
            border-radius: 12px;
            background: #10212d;
            box-shadow: 0 6px 18px rgba(16, 33, 45, 0.25);
            transform: translateX(-50%);
        }

        .premium-note {
            margin: 0.9rem 0 0 0;
            color: rgba(16, 33, 45, 0.76);
            line-height: 1.7;
        }

        .insight-shell {
            margin-top: 0.85rem;
        }

        .insight-item + .insight-item {
            margin-top: 0.9rem;
            padding-top: 0.9rem;
            border-top: 1px solid rgba(16, 33, 45, 0.08);
        }

        .insight-label {
            color: rgba(16, 33, 45, 0.56);
            font-size: 0.88rem;
        }

        .insight-value {
            color: #10212d;
            font-weight: 700;
            text-align: right;
        }

        [data-testid="stDataFrame"] {
            background: rgba(255, 255, 255, 0.62);
            border-radius: 18px;
            overflow: hidden;
        }

        @media (max-width: 900px) {
            .hero-grid,
            .hero-strip {
                grid-template-columns: 1fr;
            }

            .hero-shell {
                padding: 1.25rem;
            }
        }
        </style>
        """,
        unsafe_allow_html=True,
    )


def tone_class(change_percent: Optional[float]) -> str:
    if change_percent is None:
        return "tone-flat"

    if change_percent > 0:
        return "tone-up"

    if change_percent < 0:
        return "tone-down"

    return "tone-flat"


def build_quote_table(quote: QuoteSnapshot, is_equity: bool = False) -> pd.DataFrame:
    rows = [
        ("当前价格", format_price(quote.current_price, digits=2 if is_equity else 4)),
        ("涨跌幅", format_change_percent(quote.change_percent)),
        ("昨收价格", format_price(quote.previous_close, digits=2 if is_equity else 4)),
        ("日内高点", format_price(quote.day_high, digits=2 if is_equity else 4)),
        ("日内低点", format_price(quote.day_low, digits=2 if is_equity else 4)),
        ("52 周高点", format_price(quote.week52_high, digits=2)),
        ("52 周低点", format_price(quote.week52_low, digits=2)),
    ]

    if is_equity:
        rows.extend(
            [
                ("市值", format_market_cap_billions(quote.market_cap)),
                ("市盈率", "--" if quote.trailing_pe is None else f"{quote.trailing_pe:.2f}"),
                (
                    "股息率",
                    "--" if quote.dividend_yield is None else f"{quote.dividend_yield * 100:.2f}%",
                ),
                ("Beta", "--" if quote.beta is None else f"{quote.beta:.3f}"),
                ("总股本", format_large_number(quote.shares_outstanding, suffix=" 股")),
            ]
        )

    return pd.DataFrame(rows, columns=["指标", "数值"])


def render_hero(snapshot, auto_refresh: bool, refresh_interval: int, chart_label: str) -> None:
    premium_label, premium_note = describe_premium_ratio(snapshot.premium_ratio)
    premium_value = "--" if snapshot.premium_ratio is None else f"{snapshot.premium_ratio:.4f}"

    st.markdown(
        f"""
        <section class="hero-shell">
            <div class="hero-grid">
                <div>
                    <p class="eyebrow">Python Container Client</p>
                    <h1 class="hero-title">铜价与 SCCO<br/>联动监控台</h1>
                    <p class="hero-copy">
                        把原来的终端脚本整理成适合容器内开发的轻量客户端，
                        重点呈现价格、溢价率、盘中联动与时间语义，方便在同一视图里做观察和判断。
                    </p>
                </div>
                <div class="hero-meta">
                    <div class="hero-meta-item">
                        <span class="hero-meta-label">最后刷新</span>
                        <span class="hero-meta-value">{snapshot.refreshed_at.strftime('%Y-%m-%d %H:%M:%S')}</span>
                    </div>
                    <div class="hero-meta-item">
                        <span class="hero-meta-label">运行时区</span>
                        <span class="hero-meta-value">{snapshot.timezone_name}</span>
                    </div>
                    <div class="hero-meta-item">
                        <span class="hero-meta-label">刷新策略</span>
                        <span class="hero-meta-value">{'自动刷新' if auto_refresh else '手动刷新'} / {refresh_interval} 秒</span>
                    </div>
                    <div class="hero-meta-item">
                        <span class="hero-meta-label">图表窗口</span>
                        <span class="hero-meta-value">{chart_label}</span>
                    </div>
                </div>
            </div>
            <div class="hero-strip">
                <div class="hero-stat">
                    <span class="hero-stat-label">纽约铜期货</span>
                    <span class="hero-stat-value">{format_price(snapshot.copper.current_price, digits=4)}</span>
                    <span class="hero-stat-sub {tone_class(snapshot.copper.change_percent)}">{format_change_percent(snapshot.copper.change_percent)}</span>
                </div>
                <div class="hero-stat">
                    <span class="hero-stat-label">SCCO 股价</span>
                    <span class="hero-stat-value">{format_price(snapshot.scco.current_price, digits=2)}</span>
                    <span class="hero-stat-sub {tone_class(snapshot.scco.change_percent)}">{format_change_percent(snapshot.scco.change_percent)}</span>
                </div>
                <div class="hero-stat">
                    <span class="hero-stat-label">溢价率区间</span>
                    <span class="hero-stat-value">{premium_value}</span>
                    <span class="hero-stat-sub tone-flat">{premium_label} · {premium_note}</span>
                </div>
            </div>
        </section>
        """,
        unsafe_allow_html=True,
    )


def render_premium_panel(snapshot) -> None:
    premium_label, premium_note = describe_premium_ratio(snapshot.premium_ratio)
    marker_position = max(3.0, min(97.0, premium_scale_position(snapshot.premium_ratio, PREMIUM_SCALE_MAX) * 100))
    display_value = "--" if snapshot.premium_ratio is None else f"{snapshot.premium_ratio:.4f}"

    st.markdown(
        f"""
        <div class="premium-shell">
            <div class="premium-topline">
                <span>溢价率温度带</span>
                <strong>{display_value}</strong>
            </div>
            <div class="premium-scale">
                <span>低估</span>
                <span>修复</span>
                <span>正常</span>
                <span>高估</span>
            </div>
            <div class="premium-bar">
                <span class="premium-marker" style="left: {marker_position:.2f}%"></span>
            </div>
            <p class="premium-note">
                <strong>{premium_label}</strong><br/>
                {premium_note}
            </p>
        </div>
        """,
        unsafe_allow_html=True,
    )


def render_insight_panel(snapshot) -> None:
    dividend_yield = "--" if snapshot.scco.dividend_yield is None else f"{snapshot.scco.dividend_yield * 100:.2f}%"
    beta = "--" if snapshot.scco.beta is None else f"{snapshot.scco.beta:.3f}"

    st.markdown(
        f"""
        <div class="insight-shell">
            <div class="insight-item">
                <span class="insight-label">铜价区间</span>
                <span class="insight-value">{format_price(snapshot.copper.day_low, digits=4)} 至 {format_price(snapshot.copper.day_high, digits=4)}</span>
            </div>
            <div class="insight-item">
                <span class="insight-label">SCCO 市值</span>
                <span class="insight-value">{format_market_cap_billions(snapshot.scco.market_cap)}</span>
            </div>
            <div class="insight-item">
                <span class="insight-label">SCCO 总股本</span>
                <span class="insight-value">{format_large_number(snapshot.scco.shares_outstanding, suffix=' 股')}</span>
            </div>
            <div class="insight-item">
                <span class="insight-label">股息率 / Beta</span>
                <span class="insight-value">{dividend_yield} / {beta}</span>
            </div>
            <div class="insight-item">
                <span class="insight-label">公式快照</span>
                <span class="insight-value">市值 × {PREMIUM_MULTIPLIER} / 900亿 / 铜价</span>
            </div>
        </div>
        """,
        unsafe_allow_html=True,
    )


def render_section_heading(title: str, body: str) -> None:
    st.markdown(f"<div class='section-title'>{title}</div>", unsafe_allow_html=True)
    st.markdown(f"<p class='section-copy'>{body}</p>", unsafe_allow_html=True)


def main() -> None:
    inject_styles()

    chart_options = {
        "日内 15 分钟": ("1d", "15m"),
        "五日 1 小时": ("5d", "1h"),
    }

    with st.sidebar:
        st.markdown("## 监控配置")
        auto_refresh = st.toggle("自动刷新", value=True)
        refresh_interval = st.slider("刷新间隔（秒）", min_value=30, max_value=300, value=60, step=30)
        chart_label = st.selectbox("图表窗口", list(chart_options.keys()), index=0)
        manual_refresh = st.button("立即刷新", type="primary", use_container_width=True)
        st.caption(
            "容器默认使用 `Asia/Shanghai`，并建议挂载宿主机 `/etc/localtime`，"
            "让本地时间语义和宿主机尽量保持一致。"
        )

    if manual_refresh:
        st.rerun()

    if auto_refresh:
        st_autorefresh(interval=refresh_interval * 1000, key="copper-pulse-refresh")

    period, interval = chart_options[chart_label]

    with st.spinner("同步最新行情中..."):
        snapshot = MarketDataService().fetch_dashboard(period=period, interval=interval)

    render_hero(snapshot, auto_refresh=auto_refresh, refresh_interval=refresh_interval, chart_label=chart_label)

    if snapshot.status_messages:
        for message in snapshot.status_messages:
            st.warning(message)

    chart_col, side_col = st.columns([1.55, 1], gap="large")

    with chart_col:
        render_section_heading(
            "盘中联动曲线",
            "把铜期货和 SCCO 的开窗首个有效价格归一化为 100，方便直接看斜率和联动方向。",
        )
        relative_history = normalize_history_for_comparison(
            {
                "铜期货": snapshot.copper_history,
                "SCCO": snapshot.scco_history,
            }
        )
        if relative_history.empty:
            st.info("当前没有可用于绘图的日内数据。")
        else:
            st.line_chart(relative_history, height=320, use_container_width=True)
            st.caption("归一化基准 = 图表窗口内首个有效价格。")

    with side_col:
        render_section_heading(
            "溢价与监控要点",
            "先看估值温度，再看铜价区间、股本与股息率这些会直接影响解释力的字段。",
        )
        render_premium_panel(snapshot)
        render_insight_panel(snapshot)

    copper_col, scco_col = st.columns(2, gap="large")

    with copper_col:
        render_section_heading(
            "纽约铜期货快照",
            "聚焦价格、涨跌和区间位置，适合快速确认铜价当天的交易重心。",
        )
        st.dataframe(
            build_quote_table(snapshot.copper, is_equity=False),
            use_container_width=True,
            hide_index=True,
        )

    with scco_col:
        render_section_heading(
            "SCCO 快照",
            "除价格外，保留市值、股本、股息率和 Beta，帮助理解股价端的资源映射。",
        )
        st.dataframe(
            build_quote_table(snapshot.scco, is_equity=True),
            use_container_width=True,
            hide_index=True,
        )

    with st.expander("计算逻辑与容器时间说明"):
        st.markdown(
            f"""
            - 溢价率公式：`(SCCO 股价 × 总股本) × {PREMIUM_MULTIPLIER} / 900亿 / 铜期货价格`
            - 客户端与 CLI 共用同一份 `market_data.py` 数据服务，避免口径漂移。
            - 容器环境默认使用 `TZ=Asia/Shanghai`，并建议挂载宿主机 `/etc/localtime`。
            - Docker 容器通常共享宿主机时钟，因此这里重点保证的是“本地时间语义”一致，而不是在容器内修改系统时钟。
            """
        )


if __name__ == "__main__":
    main()
