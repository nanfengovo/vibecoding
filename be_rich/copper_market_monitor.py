#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
铜期货与 SCCO 股价监控脚本。

使用 Yahoo Finance 获取行情，并计算 SCCO 相对铜价的溢价率。
"""

from __future__ import annotations

import os
import time

from market_data import (
    PREMIUM_MULTIPLIER,
    UPDATE_INTERVAL,
    MarketDataService,
    QuoteSnapshot,
    assess_premium_ratio,
    calculate_premium,
    format_change_percent,
    format_large_number,
    format_market_cap_billions,
    format_price,
    format_ratio_percent,
)
from notifications import NotificationService


class CopperMarketMonitor:
    """铜市场命令行监控器"""

    def __init__(self) -> None:
        self.service = MarketDataService()
        self.notification_service = NotificationService()

    def display_copper_futures(self, quote: QuoteSnapshot) -> None:
        """显示铜期货数据"""
        print("\n" + "=" * 60)
        print("纽约铜期货 (HG=F) - COMEX")
        print("=" * 60)
        print(f"当前价格:    {format_price(quote.current_price, digits=4, unit=' /磅')}")
        print(f"涨跌幅度:    {format_change_percent(quote.change_percent)}")
        if quote.previous_close is not None:
            print(f"昨收价格:    {format_price(quote.previous_close, digits=4)}")
        if quote.day_high is not None:
            print(f"今日最高:    {format_price(quote.day_high, digits=4)}")
        if quote.day_low is not None:
            print(f"今日最低:    {format_price(quote.day_low, digits=4)}")
        if quote.week52_high is not None:
            print(f"52周最高:    {format_price(quote.week52_high, digits=2)}")
        if quote.week52_low is not None:
            print(f"52周最低:    {format_price(quote.week52_low, digits=2)}")

    def display_scco_stock(self, quote: QuoteSnapshot) -> None:
        """显示 SCCO 股票数据"""
        print("\n" + "=" * 60)
        print("南方铜业公司 (SCCO) - NYSE")
        print("=" * 60)
        print(f"当前股价:    {format_price(quote.current_price, digits=2)}")
        print(f"涨跌幅度:    {format_change_percent(quote.change_percent)}")
        if quote.previous_close is not None:
            print(f"昨收价格:    {format_price(quote.previous_close, digits=2)}")
        if quote.day_high is not None:
            print(f"今日最高:    {format_price(quote.day_high, digits=2)}")
        if quote.day_low is not None:
            print(f"今日最低:    {format_price(quote.day_low, digits=2)}")
        if quote.market_cap is not None:
            print(f"市值:        {format_market_cap_billions(quote.market_cap)}")
        if quote.trailing_pe is not None:
            print(f"市盈率:      {quote.trailing_pe:.2f}")
        if quote.dividend_yield is not None:
            print(f"股息率:      {quote.dividend_yield * 100:.2f}%")
        if quote.beta is not None:
            print(f"Beta系数:    {quote.beta:.3f}")
        if quote.shares_outstanding is not None:
            print(f"总股本:      {format_large_number(quote.shares_outstanding, suffix=' 股')}")

    def display_premium(self, snapshot_scco: QuoteSnapshot, snapshot_copper: QuoteSnapshot) -> None:
        """显示 SCCO 相对铜价的溢价率"""
        premium_ratio = calculate_premium(
            snapshot_scco.current_price,
            snapshot_scco.shares_outstanding,
            snapshot_copper.current_price,
        )
        if premium_ratio is None:
            print("\n溢价率:      当前无法计算")
            return

        print("\n" + "=" * 60)
        print("SCCO 相对铜价溢价率")
        print("=" * 60)
        print(f"  公式: (SCCO股价 × 总股本) × {PREMIUM_MULTIPLIER} / 900亿 / 铜期货价格")
        print(f"  SCCO股价:     {format_price(snapshot_scco.current_price, digits=2)}")
        print(
            "  总股本:       "
            f"{format_large_number(snapshot_scco.shares_outstanding, suffix=' 股')}"
        )
        print(f"  铜期货价格:   {format_price(snapshot_copper.current_price, digits=4, unit='/磅')}")
        print(f"  SCCO市值:     {format_market_cap_billions(snapshot_scco.market_cap)}")
        print(f"  溢价率:       {premium_ratio:.4f} ({format_ratio_percent(premium_ratio)})")
        assessment = assess_premium_ratio(premium_ratio)
        print(f"  估值判断:     {assessment.label}")
        print(f"  说明:         {assessment.note}")

    def display_status_messages(self, messages: list[str]) -> None:
        if not messages:
            return

        print("\n" + "=" * 60)
        print("数据提示")
        print("=" * 60)
        for message in messages:
            print(f"- {message}")

    def display_notification_result(self, summary: str) -> None:
        print("\n" + "=" * 60)
        print("通知状态")
        print("=" * 60)
        print(summary)

    def run_once(self) -> None:
        """执行单次数据获取和显示"""
        snapshot = self.service.fetch_dashboard()

        os.system("clear" if os.name != "nt" else "cls")

        print("\n" + "█" * 60)
        print("█" + " " * 18 + "铜市场实时监控工作台" + " " * 18 + "█")
        print("█" * 60)
        print(
            "更新时间: "
            f"{snapshot.refreshed_at.strftime('%Y-%m-%d %H:%M:%S')} "
            f"({snapshot.timezone_name})"
        )

        self.display_copper_futures(snapshot.copper)
        self.display_scco_stock(snapshot.scco)
        self.display_premium(snapshot.scco, snapshot.copper)
        self.display_status_messages(snapshot.status_messages)
        notification_result = self.notification_service.notify_if_needed(snapshot)
        self.display_notification_result(
            notification_result.summary
            if not notification_result.details
            else f"{notification_result.summary} | {' | '.join(notification_result.details)}"
        )

        print("\n" + "=" * 60)
        print(f"下次更新: {UPDATE_INTERVAL} 秒后 | 按 Ctrl+C 退出")
        print("=" * 60)

    def run_continuous(self) -> None:
        """持续监控模式"""
        print("\n启动实时监控模式...")
        print(f"更新间隔: {UPDATE_INTERVAL} 秒")
        print("按 Ctrl+C 停止监控\n")

        try:
            while True:
                self.run_once()
                time.sleep(UPDATE_INTERVAL)
        except KeyboardInterrupt:
            print("\n\n监控已停止")


def main() -> None:
    monitor = CopperMarketMonitor()
    monitor.run_once()


if __name__ == "__main__":
    main()
