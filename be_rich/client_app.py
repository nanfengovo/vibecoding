#!/usr/bin/env python3
from __future__ import annotations

import os
from html import escape

import pandas as pd
import streamlit as st
from streamlit_autorefresh import st_autorefresh
from typing import Any, Optional

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
from notifications import NOTIFY_STATE_ORDER, NotificationService

st.set_page_config(
    page_title="Copper Pulse",
    layout="wide",
    initial_sidebar_state="expanded",
)

STATE_LABELS = {
    "undervalued": "低估",
    "recovery": "修复区",
    "fair_value": "正常估值",
    "overvalued": "高估",
}

PROXY_MODE_LABELS = {
    "system": "系统代理",
    "custom": "自定义代理",
}

PROXY_TYPE_LABELS = {
    "http": "HTTP",
    "socks5": "SOCKS5",
}

SMTP_SECURITY_LABELS = {
    "ssl": "SSL / SMTPS",
    "starttls": "STARTTLS",
    "none": "不加密",
}

ADMIN_PASSCODE_SECRET_KEY = "admin_passcode"
ADMIN_PASSCODE_ENV_KEY = "COPPER_PULSE_ADMIN_PASSCODE"


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

        .workspace-shell {
            margin-top: 1.8rem;
            padding: 1.4rem 1.4rem 0.6rem 1.4rem;
            border-radius: 26px;
            background: linear-gradient(180deg, rgba(255, 253, 249, 0.86) 0%, rgba(238, 243, 246, 0.9) 100%);
            border: 1px solid rgba(16, 33, 45, 0.08);
            box-shadow: 0 14px 34px rgba(16, 33, 45, 0.06);
        }

        .workspace-kicker {
            margin: 0;
            letter-spacing: 0.16em;
            text-transform: uppercase;
            font-size: 0.72rem;
            color: #7a624f;
            font-weight: 800;
        }

        .workspace-title {
            margin: 0.5rem 0 0 0;
            font-size: 1.8rem;
            line-height: 1.1;
            font-weight: 800;
            color: #10212d;
        }

        .workspace-copy {
            margin: 0.7rem 0 1rem 0;
            max-width: 48rem;
            color: rgba(16, 33, 45, 0.74);
            line-height: 1.7;
        }

        .workspace-chip-row {
            display: flex;
            flex-wrap: wrap;
            gap: 0.6rem;
            margin: 0 0 0.8rem 0;
        }

        .workspace-chip {
            display: inline-flex;
            align-items: center;
            gap: 0.35rem;
            padding: 0.45rem 0.8rem;
            border-radius: 999px;
            background: rgba(255, 255, 255, 0.68);
            border: 1px solid rgba(16, 33, 45, 0.08);
            color: rgba(16, 33, 45, 0.78);
            font-size: 0.82rem;
            font-weight: 700;
        }

        .workspace-chip strong {
            color: #10212d;
            font-size: 0.84rem;
        }

        .workspace-note {
            margin: 0 0 1rem 0;
            color: rgba(16, 33, 45, 0.62);
            line-height: 1.7;
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


def read_admin_passcode() -> str:
    try:
        from_secrets = str(st.secrets[ADMIN_PASSCODE_SECRET_KEY]).strip()
    except Exception:
        from_secrets = ""

    return from_secrets or str(os.getenv(ADMIN_PASSCODE_ENV_KEY, "")).strip()


def smtp_security_mode_from_config(smtp_config: dict[str, Any]) -> str:
    if bool(smtp_config.get("use_ssl")):
        return "ssl"
    if bool(smtp_config.get("starttls")):
        return "starttls"
    return "none"


def smtp_security_flags(mode: str) -> tuple[bool, bool]:
    if mode == "ssl":
        return True, False
    if mode == "starttls":
        return False, True
    return False, False


def join_lines(values: list[str]) -> str:
    return "\n".join(str(value).strip() for value in values if str(value).strip())


def render_workspace_banner(config: dict[str, Any], notification_service: NotificationService, *, unlocked: bool) -> None:
    enabled_channels = notification_service._enabled_channels(config)
    state_text = " / ".join(STATE_LABELS.get(item, item) for item in config.get("notify_on_states", []))
    access_text = "已解锁" if unlocked else "受口令保护"
    chips = [
        ("通道", " / ".join(enabled_channels) if enabled_channels else "未启用"),
        ("触发区间", state_text or "默认策略"),
        ("冷却", f"{int(config.get('cooldown_minutes') or 0)} 分钟"),
        ("代理", notification_service.describe_proxy(config).replace("代理: ", "")),
        ("访问", access_text),
    ]

    chip_markup = "".join(
        [
            f"<span class='workspace-chip'><span>{escape(label)}</span><strong>{escape(value)}</strong></span>"
            for label, value in chips
        ]
    )

    st.markdown(
        f"""
        <section class="workspace-shell">
            <p class="workspace-kicker">Web Admin Surface</p>
            <h2 class="workspace-title">通知与代理工作区</h2>
            <p class="workspace-copy">
                网页端现在也能直接编辑邮件、企业微信、飞书和代理配置。
                为了适合公网访问，这块区域与公开行情面板分离，并支持用管理员口令锁住敏感字段。
            </p>
            <div class="workspace-chip-row">{chip_markup}</div>
            <p class="workspace-note">
                免费公网部署时，建议在 Streamlit secrets 里设置 <code>admin_passcode</code>。
                如果未设置，网页端仍可配置，但任何能访问页面的人都可能改动通知设置。
            </p>
        </section>
        """,
        unsafe_allow_html=True,
    )


def render_admin_overview(config: dict[str, Any], notification_service: NotificationService) -> None:
    status = notification_service.describe_config()
    enabled_channels = status.enabled_channels
    metric_col_1, metric_col_2, metric_col_3, metric_col_4 = st.columns(4)
    metric_col_1.metric("已启用通道", len(enabled_channels), " / ".join(enabled_channels) if enabled_channels else "未启用")
    metric_col_2.metric("冷却时间", f"{int(config.get('cooldown_minutes') or 0)} 分钟")
    metric_col_3.metric("触发区间", len(config.get("notify_on_states", [])), " / ".join(
        STATE_LABELS.get(item, item) for item in config.get("notify_on_states", [])
    ))
    metric_col_4.metric("代理状态", "开启" if bool(config.get("proxy", {}).get("enabled")) else "直连")

    st.caption(notification_service.describe_proxy(config))

    if status.warnings:
        for warning in status.warnings:
            st.warning(warning)
    else:
        st.success("当前配置结构完整，可以直接进行测试发送。")

    st.info(
        "浏览器版会把配置写到运行环境本地文件中。对免费公网实例来说，这种持久化通常能满足日常使用，"
        "但在平台重启或重新部署后，仍可能需要重新确认配置。"
    )

    st.code(
        'admin_passcode = "replace-with-your-own-passcode"',
        language="toml",
    )
    st.caption(
        "在 Streamlit Community Cloud 中，把上面的配置写入应用的 Secrets 即可自动启用管理员口令。"
    )
    st.caption(f"当前运行环境配置文件：`{notification_service.config_path}`")


def render_admin_settings(snapshot, notification_service: NotificationService, config: dict[str, Any]) -> None:
    smtp_config = config.get("smtp", {})
    proxy_config = config.get("proxy", {})
    wecom_config = config.get("wecom", {})
    feishu_config = config.get("feishu", {})

    with st.form("notification-admin-form", clear_on_submit=False):
        st.markdown("#### 发送策略")
        strategy_col, state_col = st.columns([0.8, 1.2], gap="large")
        cooldown_minutes = strategy_col.number_input(
            "冷却时间（分钟）",
            min_value=5,
            max_value=24 * 60,
            value=int(config.get("cooldown_minutes") or 240),
            step=5,
            key="admin_cooldown_minutes",
        )
        notify_on_states = state_col.multiselect(
            "触发区间",
            options=NOTIFY_STATE_ORDER,
            default=config.get("notify_on_states", []),
            format_func=lambda item: STATE_LABELS.get(item, item),
            help="默认只在低估 / 正常估值 / 高估三种明确状态发通知。",
            key="admin_notify_on_states",
        )

        st.markdown("#### 代理出口")
        proxy_toggle_col, proxy_note_col = st.columns([0.7, 1.3], gap="large")
        proxy_enabled = proxy_toggle_col.toggle(
            "启用代理",
            value=bool(proxy_config.get("enabled")),
            key="admin_proxy_enabled",
        )
        proxy_mode = proxy_note_col.radio(
            "代理来源",
            options=list(PROXY_MODE_LABELS.keys()),
            index=0 if str(proxy_config.get("mode")) == "system" else 1,
            format_func=lambda item: PROXY_MODE_LABELS[item],
            horizontal=True,
            key="admin_proxy_mode",
        )
        proxy_type = st.radio(
            "代理类型",
            options=list(PROXY_TYPE_LABELS.keys()),
            index=0 if str(proxy_config.get("proxy_type")) != "socks5" else 1,
            format_func=lambda item: PROXY_TYPE_LABELS[item],
            horizontal=True,
            key="admin_proxy_type",
        )
        proxy_host_col, proxy_port_col, proxy_user_col, proxy_password_col = st.columns(4, gap="medium")
        proxy_host = proxy_host_col.text_input(
            "主机",
            value=str(proxy_config.get("host") or "127.0.0.1"),
            key="admin_proxy_host",
        )
        proxy_port = proxy_port_col.number_input(
            "端口",
            min_value=1,
            max_value=65535,
            value=int(proxy_config.get("port") or 7890),
            step=1,
            key="admin_proxy_port",
        )
        proxy_username = proxy_user_col.text_input(
            "用户名",
            value=str(proxy_config.get("username") or ""),
            key="admin_proxy_username",
        )
        proxy_password = proxy_password_col.text_input(
            "密码",
            value=str(proxy_config.get("password") or ""),
            type="password",
            key="admin_proxy_password",
        )
        st.caption(
            "选择“系统代理”时，发送动作会优先读取当前运行环境暴露的代理设置；"
            "如果你已经明确知道本地端口，公网部署通常更适合直接填写自定义代理。"
        )

        st.markdown("#### 邮件")
        email_toggle_col, email_security_col = st.columns([0.7, 1.3], gap="large")
        smtp_enabled = email_toggle_col.toggle(
            "启用邮件通知",
            value=bool(smtp_config.get("enabled")),
            key="admin_smtp_enabled",
        )
        smtp_security_mode = email_security_col.selectbox(
            "安全方式",
            options=list(SMTP_SECURITY_LABELS.keys()),
            index=list(SMTP_SECURITY_LABELS.keys()).index(smtp_security_mode_from_config(smtp_config)),
            format_func=lambda item: SMTP_SECURITY_LABELS[item],
            key="admin_smtp_security_mode",
        )
        smtp_host_col, smtp_port_col = st.columns([1.4, 0.6], gap="medium")
        smtp_host = smtp_host_col.text_input(
            "SMTP 主机",
            value=str(smtp_config.get("host") or ""),
            key="admin_smtp_host",
        )
        smtp_port = smtp_port_col.number_input(
            "端口",
            min_value=1,
            max_value=65535,
            value=int(smtp_config.get("port") or 465),
            step=1,
            key="admin_smtp_port",
        )
        smtp_user_col, smtp_password_col = st.columns(2, gap="medium")
        smtp_username = smtp_user_col.text_input(
            "用户名",
            value=str(smtp_config.get("username") or ""),
            key="admin_smtp_username",
        )
        smtp_password = smtp_password_col.text_input(
            "密码 / App Password",
            value=str(smtp_config.get("password") or ""),
            type="password",
            key="admin_smtp_password",
        )
        smtp_sender = st.text_input(
            "发件人",
            value=str(smtp_config.get("sender") or ""),
            key="admin_smtp_sender",
        )
        smtp_receivers = st.text_area(
            "收件人",
            value=join_lines(list(smtp_config.get("receivers", []))),
            help="每行一个邮箱，也支持逗号分隔；保存时会自动归一化。",
            height=110,
            key="admin_smtp_receivers",
        )

        channel_col_1, channel_col_2 = st.columns(2, gap="large")
        with channel_col_1:
            st.markdown("#### 企业微信")
            wecom_enabled = st.toggle(
                "启用企业微信",
                value=bool(wecom_config.get("enabled")),
                key="admin_wecom_enabled",
            )
            wecom_webhook = st.text_input(
                "Webhook",
                value=str(wecom_config.get("webhook_url") or ""),
                key="admin_wecom_webhook",
            )
            wecom_mentions = st.text_area(
                "提醒账号",
                value=join_lines(list(wecom_config.get("mentioned_list", []))),
                help="每行一个账号，或直接输入 @all。",
                height=96,
                key="admin_wecom_mentions",
            )
            wecom_mobiles = st.text_area(
                "提醒手机号",
                value=join_lines(list(wecom_config.get("mentioned_mobile_list", []))),
                help="每行一个手机号。",
                height=96,
                key="admin_wecom_mobiles",
            )

        with channel_col_2:
            st.markdown("#### 飞书")
            feishu_enabled = st.toggle(
                "启用飞书",
                value=bool(feishu_config.get("enabled")),
                key="admin_feishu_enabled",
            )
            feishu_webhook = st.text_input(
                "Webhook",
                value=str(feishu_config.get("webhook_url") or ""),
                key="admin_feishu_webhook",
            )
            st.caption("飞书机器人只需要 webhook 即可；测试消息会直接发送到对应群。")

        action_col_1, action_col_2 = st.columns(2, gap="medium")
        save_clicked = action_col_1.form_submit_button(
            "保存配置",
            type="primary",
            use_container_width=True,
        )
        save_test_clicked = action_col_2.form_submit_button(
            "保存并发送测试",
            use_container_width=True,
        )

    reload_col, lock_col = st.columns([1, 1], gap="medium")
    if reload_col.button("从文件重载当前配置", use_container_width=True, key="admin_reload_config"):
        st.rerun()
    if lock_col.button("锁定配置工作区", use_container_width=True, key="admin_lock_workspace"):
        st.session_state["copper_pulse_admin_unlocked"] = False
        st.rerun()

    if save_clicked or save_test_clicked:
        use_ssl, starttls = smtp_security_flags(smtp_security_mode)
        payload = {
            "cooldown_minutes": int(cooldown_minutes),
            "notify_on_states": notify_on_states,
            "proxy": {
                "enabled": proxy_enabled,
                "mode": proxy_mode,
                "proxy_type": proxy_type,
                "host": proxy_host,
                "port": int(proxy_port),
                "username": proxy_username,
                "password": proxy_password,
            },
            "smtp": {
                "enabled": smtp_enabled,
                "host": smtp_host,
                "port": int(smtp_port),
                "use_ssl": use_ssl,
                "starttls": starttls,
                "username": smtp_username,
                "password": smtp_password,
                "sender": smtp_sender,
                "receivers": smtp_receivers,
            },
            "wecom": {
                "enabled": wecom_enabled,
                "webhook_url": wecom_webhook,
                "mentioned_list": wecom_mentions,
                "mentioned_mobile_list": wecom_mobiles,
            },
            "feishu": {
                "enabled": feishu_enabled,
                "webhook_url": feishu_webhook,
            },
        }

        try:
            saved_config = notification_service.save_config(payload)
            st.success("配置已保存，网页端与桌面端现在共用这份通知设置。")
            if save_test_clicked:
                result = notification_service.send_test_notification(snapshot=snapshot)
                if result.sent:
                    st.success(result.summary)
                elif result.attempted:
                    st.error(result.summary)
                else:
                    st.warning(result.summary)

                if result.details:
                    st.markdown("\n".join([f"- {detail}" for detail in result.details]))

            warning_status = notification_service.describe_config()
            if warning_status.warnings:
                for warning in warning_status.warnings:
                    st.warning(warning)

            st.caption(notification_service.describe_proxy(saved_config))
        except OSError as exc:
            st.error(f"配置保存失败：{exc}")


def render_admin_workspace(snapshot) -> None:
    render_section_heading(
        "通知与代理工作区",
        "把浏览器版升级为真正可操作的监控台：公开区域继续看盘，管理员区域负责通知、代理与测试发送。",
    )

    try:
        notification_service = NotificationService()
        config = notification_service.load_config()
    except OSError as exc:
        st.error(f"通知服务初始化失败：{exc}")
        return

    admin_passcode = read_admin_passcode()
    passcode_configured = bool(admin_passcode)
    unlocked_key = "copper_pulse_admin_unlocked"

    if not passcode_configured:
        st.session_state[unlocked_key] = True

    unlocked = bool(st.session_state.get(unlocked_key, False))
    render_workspace_banner(config, notification_service, unlocked=unlocked)

    if passcode_configured and not unlocked:
        with st.form("web-admin-unlock-form", clear_on_submit=True):
            unlock_value = st.text_input(
                "管理员口令",
                type="password",
                placeholder="输入管理员口令后再编辑配置",
                key="admin_unlock_passcode",
            )
            unlock_clicked = st.form_submit_button("解锁配置工作区", use_container_width=True)

        if unlock_clicked:
            if unlock_value == admin_passcode:
                st.session_state[unlocked_key] = True
                st.success("管理员口令验证通过，配置工作区已解锁。")
                st.rerun()
            else:
                st.error("口令不正确，当前仍保持只读状态。")

        st.info("当前处于只读模式。解锁后才会显示 SMTP、Webhook 和代理账户等敏感字段。")
        return

    if not passcode_configured:
        st.warning(
            "当前未设置管理员口令。网页端仍可正常保存与测试配置，但如果这是公网地址，任何访问者都可能改动通知设置。"
        )
    else:
        st.success("管理员口令已启用，当前会话已解锁。")

    overview_tab, settings_tab = st.tabs(["工作区概览", "通知与代理设置"])
    with overview_tab:
        render_admin_overview(config, notification_service)

    with settings_tab:
        render_admin_settings(snapshot, notification_service, config)


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

    render_admin_workspace(snapshot)

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
