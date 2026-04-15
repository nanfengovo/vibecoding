#!/usr/bin/env python3
from __future__ import annotations

import os
import time
from dataclasses import dataclass, field
from datetime import datetime
from typing import Any, Optional
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

import pandas as pd
import yfinance as yf

COPPER_TICKER = "HG=F"
SCCO_TICKER = "SCCO"
UPDATE_INTERVAL = 60

PREMIUM_MULTIPLIER = 4.2
PREMIUM_DIVISOR = 900 * 1e8
DEFAULT_TIMEZONE = "Asia/Shanghai"
PREMIUM_UNDERVALUE_THRESHOLD = 1.1
PREMIUM_FAIR_VALUE_THRESHOLD = 1.3
PREMIUM_OVERVALUED_THRESHOLD = 1.4
PREMIUM_SCALE_MAX = 1.6


@dataclass
class QuoteSnapshot:
    ticker: str
    label: str
    current_price: Optional[float]
    change_percent: Optional[float]
    previous_close: Optional[float]
    day_high: Optional[float]
    day_low: Optional[float]
    week52_high: Optional[float]
    week52_low: Optional[float]
    market_cap: Optional[float] = None
    trailing_pe: Optional[float] = None
    dividend_yield: Optional[float] = None
    beta: Optional[float] = None
    shares_outstanding: Optional[float] = None


@dataclass
class DashboardSnapshot:
    copper: QuoteSnapshot
    scco: QuoteSnapshot
    premium_ratio: Optional[float]
    refreshed_at: datetime
    timezone_name: str
    copper_history: pd.DataFrame = field(default_factory=pd.DataFrame)
    scco_history: pd.DataFrame = field(default_factory=pd.DataFrame)
    status_messages: list[str] = field(default_factory=list)


@dataclass(frozen=True)
class PremiumAssessment:
    key: str
    label: str
    note: str


def _safe_float(value: Any) -> Optional[float]:
    if value is None:
        return None

    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def _pick_first(source: dict[str, Any], *keys: str) -> Any:
    for key in keys:
        value = source.get(key)
        if value is not None:
            return value

    return None


def resolve_timezone_name(timezone_name: Optional[str] = None) -> str:
    candidate = timezone_name or os.getenv("TZ") or DEFAULT_TIMEZONE

    try:
        ZoneInfo(candidate)
        return candidate
    except ZoneInfoNotFoundError:
        return DEFAULT_TIMEZONE


def configure_runtime_timezone(timezone_name: Optional[str] = None) -> str:
    resolved_name = resolve_timezone_name(timezone_name)
    os.environ["TZ"] = resolved_name

    if hasattr(time, "tzset"):
        time.tzset()

    return resolved_name


def calculate_premium(
    scco_price: Optional[float],
    shares_outstanding: Optional[float],
    copper_price: Optional[float],
) -> Optional[float]:
    if None in (scco_price, shares_outstanding, copper_price):
        return None

    if copper_price == 0:
        return None

    market_cap = scco_price * shares_outstanding
    return market_cap * PREMIUM_MULTIPLIER / PREMIUM_DIVISOR / copper_price


def assess_premium_ratio(premium_ratio: Optional[float]) -> PremiumAssessment:
    if premium_ratio is None:
        return PremiumAssessment("unavailable", "待计算", "当前字段不足，暂时无法得到可靠溢价率。")

    if premium_ratio < PREMIUM_UNDERVALUE_THRESHOLD:
        return PremiumAssessment("undervalued", "低估", "股价相对铜价仍偏保守，资源价值映射不足。")

    if premium_ratio < PREMIUM_FAIR_VALUE_THRESHOLD:
        return PremiumAssessment("recovery", "修复区", "估值仍在向合理区间靠拢，建议继续观察修复斜率。")

    if premium_ratio < PREMIUM_OVERVALUED_THRESHOLD:
        return PremiumAssessment("fair_value", "正常估值", "股价与铜价的资源价值映射回到相对正常区间。")

    return PremiumAssessment("overvalued", "高估", "溢价已经偏高，适合同时关注弹性与回落风险。")


def describe_premium_ratio(premium_ratio: Optional[float]) -> tuple[str, str]:
    assessment = assess_premium_ratio(premium_ratio)
    return assessment.label, assessment.note


def premium_scale_position(premium_ratio: Optional[float], maximum: float = PREMIUM_SCALE_MAX) -> float:
    if premium_ratio is None:
        return 0.0

    bounded = max(0.0, min(maximum, premium_ratio))
    if maximum <= 0:
        return 0.0

    return bounded / maximum


def format_price(value: Optional[float], digits: int = 2, unit: str = "") -> str:
    if value is None:
        return "--"

    return f"${value:,.{digits}f}{unit}"


def format_change_percent(value: Optional[float]) -> str:
    if value is None:
        return "--"

    return f"{value:+.2f}%"


def format_ratio_percent(value: Optional[float]) -> str:
    if value is None:
        return "--"

    return f"{value * 100:.2f}%"


def format_market_cap_billions(value: Optional[float]) -> str:
    if value is None:
        return "--"

    return f"${value / 1e9:,.2f}B"


def format_large_number(value: Optional[float], digits: int = 0, suffix: str = "") -> str:
    if value is None:
        return "--"

    return f"{value:,.{digits}f}{suffix}"


def normalize_history_for_comparison(histories: dict[str, pd.DataFrame]) -> pd.DataFrame:
    combined: Optional[pd.DataFrame] = None

    for label, frame in histories.items():
        if frame.empty:
            continue

        series = frame.set_index("timestamp")["close"].sort_index().dropna()
        if series.empty:
            continue

        baseline = series.iloc[0]
        if baseline == 0:
            continue

        normalized = (series / baseline * 100).rename(label)

        if combined is None:
            combined = normalized.to_frame()
        else:
            combined = combined.join(normalized, how="outer")

    if combined is None:
        return pd.DataFrame()

    return combined.sort_index().ffill().dropna(how="all")


class MarketDataService:
    def __init__(self, timezone_name: Optional[str] = None) -> None:
        self.timezone_name = configure_runtime_timezone(timezone_name)
        self.timezone = ZoneInfo(self.timezone_name)

    def now(self) -> datetime:
        return datetime.now(self.timezone)

    def _build_quote_snapshot(self, ticker: str, label: str, info: dict[str, Any]) -> QuoteSnapshot:
        return QuoteSnapshot(
            ticker=ticker,
            label=label,
            current_price=_safe_float(_pick_first(info, "regularMarketPrice", "currentPrice")),
            change_percent=_safe_float(_pick_first(info, "regularMarketChangePercent")),
            previous_close=_safe_float(_pick_first(info, "previousClose", "regularMarketPreviousClose")),
            day_high=_safe_float(_pick_first(info, "dayHigh", "regularMarketDayHigh")),
            day_low=_safe_float(_pick_first(info, "dayLow", "regularMarketDayLow")),
            week52_high=_safe_float(_pick_first(info, "fiftyTwoWeekHigh")),
            week52_low=_safe_float(_pick_first(info, "fiftyTwoWeekLow")),
            market_cap=_safe_float(_pick_first(info, "marketCap")),
            trailing_pe=_safe_float(_pick_first(info, "trailingPE")),
            dividend_yield=_safe_float(_pick_first(info, "dividendYield")),
            beta=_safe_float(_pick_first(info, "beta")),
            shares_outstanding=_safe_float(
                _pick_first(info, "sharesOutstanding", "impliedSharesOutstanding")
            ),
        )

    def fetch_quote(self, ticker: str, label: str) -> tuple[QuoteSnapshot, list[str]]:
        try:
            info = yf.Ticker(ticker).info or {}
        except Exception as exc:
            empty_snapshot = self._build_quote_snapshot(ticker, label, {})
            return empty_snapshot, [f"{label} 行情获取失败: {exc}"]

        if not info:
            empty_snapshot = self._build_quote_snapshot(ticker, label, {})
            return empty_snapshot, [f"{label} 行情返回为空。"]

        return self._build_quote_snapshot(ticker, label, info), []

    def fetch_history(
        self,
        ticker: str,
        label: str,
        period: str = "1d",
        interval: str = "15m",
    ) -> tuple[pd.DataFrame, list[str]]:
        empty_history = pd.DataFrame(columns=["timestamp", "close"])

        try:
            history = yf.Ticker(ticker).history(
                period=period,
                interval=interval,
                auto_adjust=False,
                prepost=False,
            )
        except Exception as exc:
            return empty_history, [f"{label} 图表数据获取失败: {exc}"]

        if history.empty:
            return empty_history, [f"{label} 图表数据为空。"]

        frame = history.reset_index()
        time_column = "Datetime" if "Datetime" in frame.columns else "Date"

        timestamps = pd.to_datetime(frame[time_column], utc=True, errors="coerce")
        close_prices = (
            pd.to_numeric(frame["Close"], errors="coerce")
            if "Close" in frame.columns
            else pd.Series(dtype="float64")
        )

        clean = pd.DataFrame(
            {
                "timestamp": timestamps.dt.tz_convert(self.timezone).dt.tz_localize(None),
                "close": close_prices,
            }
        ).dropna()

        if clean.empty:
            return empty_history, [f"{label} 图表数据清洗后为空。"]

        return clean, []

    def fetch_dashboard(
        self,
        period: str = "1d",
        interval: str = "15m",
    ) -> DashboardSnapshot:
        copper, copper_messages = self.fetch_quote(COPPER_TICKER, "纽约铜期货")
        scco, scco_messages = self.fetch_quote(SCCO_TICKER, "南方铜业")
        copper_history, copper_history_messages = self.fetch_history(
            COPPER_TICKER,
            "纽约铜期货",
            period=period,
            interval=interval,
        )
        scco_history, scco_history_messages = self.fetch_history(
            SCCO_TICKER,
            "南方铜业",
            period=period,
            interval=interval,
        )

        premium_ratio = calculate_premium(
            scco.current_price,
            scco.shares_outstanding,
            copper.current_price,
        )

        return DashboardSnapshot(
            copper=copper,
            scco=scco,
            premium_ratio=premium_ratio,
            refreshed_at=self.now(),
            timezone_name=self.timezone_name,
            copper_history=copper_history,
            scco_history=scco_history,
            status_messages=[
                *copper_messages,
                *scco_messages,
                *copper_history_messages,
                *scco_history_messages,
            ],
        )
