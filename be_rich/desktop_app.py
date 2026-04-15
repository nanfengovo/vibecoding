#!/usr/bin/env python3
from __future__ import annotations

import queue
import sys
import threading
from typing import Optional

import pandas as pd
from PySide6.QtCore import QPointF, QRectF, Qt, QTimer, QUrl
from PySide6.QtGui import QColor, QDesktopServices, QFont, QPainter, QPainterPath, QPen
from PySide6.QtWidgets import (
    QApplication,
    QCheckBox,
    QComboBox,
    QFormLayout,
    QFrame,
    QGridLayout,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QLineEdit,
    QMainWindow,
    QPlainTextEdit,
    QPushButton,
    QScrollArea,
    QSizePolicy,
    QSpinBox,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
    QWidget,
)

from market_data import (
    PREMIUM_MULTIPLIER,
    PREMIUM_FAIR_VALUE_THRESHOLD,
    PREMIUM_OVERVALUED_THRESHOLD,
    PREMIUM_SCALE_MAX,
    PREMIUM_UNDERVALUE_THRESHOLD,
    DashboardSnapshot,
    MarketDataService,
    QuoteSnapshot,
    assess_premium_ratio,
    format_change_percent,
    format_large_number,
    format_market_cap_billions,
    format_price,
    normalize_history_for_comparison,
    premium_scale_position,
)
from notifications import NotificationResult, NotificationService


class LineChartWidget(QWidget):
    SERIES_COLORS = {
        "铜期货": QColor("#b56c37"),
        "SCCO": QColor("#214d68"),
    }

    def __init__(self, parent: Optional[QWidget] = None) -> None:
        super().__init__(parent)
        self._history = pd.DataFrame()
        self.setMinimumHeight(340)
        self.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Expanding)

    def set_history(self, history: pd.DataFrame) -> None:
        self._history = history
        self.update()

    def _draw_badge(
        self,
        painter: QPainter,
        x: float,
        y: float,
        color: QColor,
        label: str,
        value: Optional[float],
    ) -> None:
        delta_text = "--" if value is None else f"{value - 100:+.1f}%"
        text = f"{label}  {delta_text}"
        metrics = painter.fontMetrics()
        width = metrics.horizontalAdvance(text) + 26
        badge_rect = QRectF(x, y, width, 24)
        fill = QColor(255, 255, 255, 235)
        painter.setPen(Qt.NoPen)
        painter.setBrush(fill)
        painter.drawRoundedRect(badge_rect, 12, 12)
        painter.setBrush(color)
        painter.drawEllipse(QPointF(x + 12, y + 12), 4, 4)
        painter.setPen(QColor("#334854"))
        painter.drawText(
            QRectF(x + 22, y, width - 26, 24),
            Qt.AlignLeft | Qt.AlignVCenter,
            text,
        )

    def _draw_end_label(
        self,
        painter: QPainter,
        x: float,
        y: float,
        color: QColor,
        label: str,
        value: float,
    ) -> None:
        text = f"{label} {value:.1f}"
        metrics = painter.fontMetrics()
        width = metrics.horizontalAdvance(text) + 18
        label_rect = QRectF(x - width - 10, y - 12, width, 24)
        painter.setPen(Qt.NoPen)
        painter.setBrush(QColor(255, 255, 255, 245))
        painter.drawRoundedRect(label_rect, 12, 12)
        painter.setPen(color)
        painter.drawText(label_rect, Qt.AlignCenter, text)

    def paintEvent(self, _event) -> None:  # noqa: N802
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, True)

        rect = self.rect().adjusted(18, 12, -18, -18)
        painter.fillRect(rect, QColor("#fffdfa"))
        painter.setPen(QPen(QColor("#e1e7ea"), 1))
        painter.drawRoundedRect(rect, 18, 18)

        if self._history.empty:
            painter.setPen(QColor("#7a8892"))
            painter.setFont(QFont("PingFang SC", 13))
            painter.drawText(rect, Qt.AlignCenter, "当前没有可绘制的图表数据")
            return

        legend_rect = QRectF(rect.left() + 16, rect.top() + 10, rect.width() - 32, 28)
        plot_rect = rect.adjusted(52, 48, -26, -46)
        values = self._history.to_numpy().flatten()
        numeric_values = [float(item) for item in values if pd.notna(item)]
        if not numeric_values:
            painter.setPen(QColor("#7a8892"))
            painter.setFont(QFont("PingFang SC", 13))
            painter.drawText(rect, Qt.AlignCenter, "图表数据为空")
            return

        low = min(numeric_values)
        high = max(numeric_values)
        if abs(high - low) < 1e-6:
            low -= 1
            high += 1
        padding = (high - low) * 0.18
        low -= padding
        high += padding

        painter.setFont(QFont("PingFang SC", 10))
        grid_pen = QPen(QColor("#edf1f3"), 1)
        label_pen = QColor("#788690")
        for step in range(5):
            ratio = step / 4
            y = plot_rect.top() + plot_rect.height() * ratio
            value = high - (high - low) * ratio
            painter.setPen(grid_pen)
            painter.drawLine(plot_rect.left(), y, plot_rect.right(), y)
            painter.setPen(label_pen)
            painter.drawText(
                QRectF(plot_rect.left() - 46, y - 9, 40, 18),
                Qt.AlignRight | Qt.AlignVCenter,
                f"{value:.1f}",
            )

        timestamps = list(self._history.index)
        if timestamps:
            marker_specs = [
                (0, "起点"),
                (len(timestamps) // 2, "中段"),
                (len(timestamps) - 1, "最新"),
            ]
            seen_indices: set[int] = set()
            for idx, _marker in marker_specs:
                if idx in seen_indices:
                    continue
                seen_indices.add(idx)
                x = plot_rect.left() + plot_rect.width() * (idx / max(len(timestamps) - 1, 1))
                dash_pen = QPen(QColor("#eef1f3"), 1)
                dash_pen.setDashPattern([2, 5])
                painter.setPen(dash_pen)
                painter.drawLine(QPointF(x, plot_rect.top()), QPointF(x, plot_rect.bottom()))
                painter.setPen(QColor("#8b99a2"))
                stamp = timestamps[idx]
                time_text = stamp.strftime("%H:%M") if hasattr(stamp, "strftime") else str(stamp)
                painter.drawText(
                    QRectF(x - 28, plot_rect.bottom() + 10, 56, 14),
                    Qt.AlignCenter | Qt.AlignVCenter,
                    time_text,
                )

        baseline = 100.0
        if low <= baseline <= high:
            y = plot_rect.bottom() - (baseline - low) / (high - low) * plot_rect.height()
            dash_pen = QPen(QColor("#cfd7dc"), 1)
            dash_pen.setDashPattern([4, 4])
            painter.setPen(dash_pen)
            painter.drawLine(plot_rect.left(), y, plot_rect.right(), y)
            painter.setPen(QColor("#7d8b94"))
            painter.drawText(
                QRectF(plot_rect.right() - 86, y - 20, 78, 16),
                Qt.AlignRight | Qt.AlignVCenter,
                "基准 100",
            )

        count = max(len(self._history.index) - 1, 1)
        end_labels: list[tuple[float, float, QColor, str, float]] = []
        badge_x = legend_rect.left()
        for label, color in self.SERIES_COLORS.items():
            if label not in self._history.columns:
                continue

            series = self._history[label].tolist()
            points: list[QPointF] = []
            last_valid: Optional[tuple[float, float, float]] = None
            first_valid_value: Optional[float] = None
            for idx, value in enumerate(series):
                if pd.isna(value):
                    continue

                numeric_value = float(value)
                if first_valid_value is None:
                    first_valid_value = numeric_value
                x = plot_rect.left() + plot_rect.width() * (idx / count)
                y = plot_rect.bottom() - (numeric_value - low) / (high - low) * plot_rect.height()
                points.append(QPointF(x, y))
                last_valid = (x, y, numeric_value)

            if len(points) >= 2:
                glow_pen = QPen(QColor(color.red(), color.green(), color.blue(), 55), 7)
                glow_pen.setCapStyle(Qt.RoundCap)
                glow_pen.setJoinStyle(Qt.RoundJoin)
                path = QPainterPath(points[0])
                for point in points[1:]:
                    path.lineTo(point)
                painter.setBrush(Qt.NoBrush)
                painter.setPen(glow_pen)
                painter.drawPath(path)
                painter.setPen(QPen(color, 3))
                painter.drawPath(path)

            if last_valid is not None:
                x, y, value = last_valid
                painter.setBrush(color)
                painter.setPen(Qt.NoPen)
                painter.drawEllipse(QPointF(x, y), 4, 4)
                painter.setPen(color)
                painter.setFont(QFont("PingFang SC", 10, QFont.Bold))
                end_labels.append((x, y, color, label, value))
                self._draw_badge(painter, badge_x, legend_rect.top(), color, label, value)
                badge_x += painter.fontMetrics().horizontalAdvance(f"{label}  {value - 100:+.1f}%") + 44

        if len(end_labels) > 1:
            end_labels.sort(key=lambda item: item[1])
            adjusted: list[tuple[float, float, QColor, str, float]] = []
            previous_y: Optional[float] = None
            for x, y, color, label, value in end_labels:
                if previous_y is not None and abs(y - previous_y) < 26:
                    y = previous_y + 26
                y = min(max(plot_rect.top() + 12, y), plot_rect.bottom() - 12)
                adjusted.append((x, y, color, label, value))
                previous_y = y
            end_labels = adjusted

        for x, y, color, label, value in end_labels:
            self._draw_end_label(painter, x, y, color, label, value)


class PremiumBarWidget(QWidget):
    def __init__(self, parent: Optional[QWidget] = None) -> None:
        super().__init__(parent)
        self._ratio: Optional[float] = None
        self.setFixedHeight(54)

    def set_ratio(self, ratio: Optional[float]) -> None:
        self._ratio = ratio
        self.update()

    def paintEvent(self, _event) -> None:  # noqa: N802
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, True)
        rect = self.rect().adjusted(6, 12, -6, -8)
        clip_path = QPainterPath()
        clip_path.addRoundedRect(QRectF(rect), 8, 8)

        segments = [
            (0.0, PREMIUM_UNDERVALUE_THRESHOLD, QColor("#6b8090"), "低估"),
            (PREMIUM_UNDERVALUE_THRESHOLD, PREMIUM_FAIR_VALUE_THRESHOLD, QColor("#b39a74"), "修复"),
            (PREMIUM_FAIR_VALUE_THRESHOLD, PREMIUM_OVERVALUED_THRESHOLD, QColor("#c38c55"), "正常"),
            (PREMIUM_OVERVALUED_THRESHOLD, PREMIUM_SCALE_MAX, QColor("#bd5a4a"), "高估"),
        ]

        painter.save()
        painter.setClipPath(clip_path)
        painter.setPen(Qt.NoPen)
        for start, end, color, _label in segments:
            start_x = rect.left() + rect.width() * (start / PREMIUM_SCALE_MAX)
            end_x = rect.left() + rect.width() * (end / PREMIUM_SCALE_MAX)
            painter.setBrush(color)
            painter.drawRect(QRectF(start_x, rect.top(), end_x - start_x, rect.height()))
        painter.restore()

        painter.setPen(QColor("#9eb3c0"))
        painter.setFont(QFont("PingFang SC", 9))
        for start, end, _color, label in segments:
            center_ratio = ((start + end) / 2) / PREMIUM_SCALE_MAX
            center_x = rect.left() + rect.width() * center_ratio
            painter.drawText(
                QRectF(center_x - 24, 0, 48, 12),
                Qt.AlignCenter | Qt.AlignVCenter,
                label,
            )

        if self._ratio is None:
            return

        position = max(0.02, min(0.98, premium_scale_position(self._ratio, PREMIUM_SCALE_MAX)))
        x = rect.left() + rect.width() * position
        painter.setPen(QPen(QColor("#f7f1ea"), 3))
        painter.drawLine(QPointF(x, rect.top() - 6), QPointF(x, rect.bottom() + 8))
        painter.setBrush(QColor("#f7f1ea"))
        painter.setPen(Qt.NoPen)
        painter.drawEllipse(QPointF(x, rect.bottom() + 7), 6, 6)


class StatCard(QFrame):
    def __init__(self, title: str, *, dark: bool = False, parent: Optional[QWidget] = None) -> None:
        super().__init__(parent)
        self.setObjectName("darkCard" if dark else "lightCard")
        self.setFrameShape(QFrame.NoFrame)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(22, 18, 22, 18)
        layout.setSpacing(8)

        title_label = QLabel(title)
        title_label.setObjectName("cardTitleDark" if dark else "cardTitle")
        layout.addWidget(title_label)

        self.value_label = QLabel("--")
        self.value_label.setObjectName("cardValueDark" if dark else "cardValue")
        layout.addWidget(self.value_label)

        self.sub_label = QLabel("--")
        self.sub_label.setWordWrap(True)
        self.sub_label.setObjectName("cardSubDark" if dark else "cardSub")
        layout.addWidget(self.sub_label)
        layout.addStretch(1)

    def set_values(self, value: str, sub: str) -> None:
        self.value_label.setText(value)
        self.sub_label.setText(sub)


class TablePanel(QFrame):
    def __init__(self, title: str, body: str, parent: Optional[QWidget] = None) -> None:
        super().__init__(parent)
        self.setObjectName("panel")

        layout = QVBoxLayout(self)
        layout.setContentsMargins(18, 18, 18, 18)
        layout.setSpacing(12)

        title_label = QLabel(title)
        title_label.setObjectName("panelTitle")
        layout.addWidget(title_label)

        body_label = QLabel(body)
        body_label.setWordWrap(True)
        body_label.setObjectName("panelBody")
        layout.addWidget(body_label)

        self.table = QTableWidget(0, 2)
        self.table.setHorizontalHeaderLabels(["指标", "数值"])
        self.table.horizontalHeader().setSectionResizeMode(0, QHeaderView.Stretch)
        self.table.horizontalHeader().setSectionResizeMode(1, QHeaderView.ResizeToContents)
        self.table.verticalHeader().hide()
        self.table.setAlternatingRowColors(False)
        self.table.setShowGrid(False)
        self.table.setFocusPolicy(Qt.NoFocus)
        self.table.setSelectionMode(QTableWidget.NoSelection)
        self.table.setEditTriggers(QTableWidget.NoEditTriggers)
        self.table.setObjectName("metricsTable")
        layout.addWidget(self.table)

    def set_rows(self, rows: list[tuple[str, str]]) -> None:
        self.table.setRowCount(len(rows))
        for row_index, (metric, value) in enumerate(rows):
            metric_item = QTableWidgetItem(metric)
            value_item = QTableWidgetItem(value)
            metric_item.setTextAlignment(Qt.AlignLeft | Qt.AlignVCenter)
            value_item.setTextAlignment(Qt.AlignRight | Qt.AlignVCenter)
            self.table.setItem(row_index, 0, metric_item)
            self.table.setItem(row_index, 1, value_item)
        self.table.resizeRowsToContents()


class InsightRow(QFrame):
    def __init__(self, title: str, parent: Optional[QWidget] = None) -> None:
        super().__init__(parent)
        self.setObjectName("insightCard")
        layout = QVBoxLayout(self)
        layout.setContentsMargins(12, 12, 12, 12)
        layout.setSpacing(4)

        self.title_label = QLabel(title)
        self.title_label.setObjectName("insightTitle")
        layout.addWidget(self.title_label)

        self.value_label = QLabel("--")
        self.value_label.setWordWrap(True)
        self.value_label.setObjectName("insightValue")
        layout.addWidget(self.value_label)

    def set_value(self, value: str) -> None:
        self.value_label.setText(value)


class CopperPulseWindow(QMainWindow):
    CHART_WINDOWS = {
        "日内 15 分钟": ("1d", "15m"),
        "五日 1 小时": ("5d", "1h"),
    }

    def __init__(self) -> None:
        super().__init__()
        self.setWindowTitle("Copper Pulse")
        self.resize(1460, 940)
        self.setMinimumSize(1280, 820)

        self.snapshot_queue: "queue.Queue[tuple[str, object]]" = queue.Queue()
        self.fetch_in_progress = False
        self.notification_in_progress = False
        self.current_snapshot: Optional[DashboardSnapshot] = None
        self.notification_service = NotificationService()
        self.notification_config = self.notification_service.load_config()
        self.notification_config_status = self.notification_service.describe_config()

        self.queue_timer = QTimer(self)
        self.queue_timer.timeout.connect(self._drain_snapshot_queue)
        self.queue_timer.start(120)

        self.auto_refresh_timer = QTimer(self)
        self.auto_refresh_timer.setSingleShot(True)
        self.auto_refresh_timer.timeout.connect(self.refresh_data)

        self._build_ui()
        self._apply_styles()
        self._load_notification_form(self.notification_config)

        QTimer.singleShot(240, self.refresh_data)

    def _build_ui(self) -> None:
        central = QWidget()
        self.setCentralWidget(central)

        outer = QVBoxLayout(central)
        outer.setContentsMargins(24, 22, 24, 22)
        outer.setSpacing(18)

        header_layout = QHBoxLayout()
        outer.addLayout(header_layout)

        title_col = QVBoxLayout()
        header_layout.addLayout(title_col, stretch=1)

        eyebrow = QLabel("COPPER PULSE")
        eyebrow.setObjectName("eyebrow")
        title_col.addWidget(eyebrow)

        title = QLabel("铜价与 SCCO 桌面监控台")
        title.setObjectName("heroTitle")
        title_col.addWidget(title)

        subtitle = QLabel("原生桌面窗口，适合直接双击启动；左侧看联动曲线，右侧看溢价区间与关键解释变量。")
        subtitle.setWordWrap(True)
        subtitle.setObjectName("heroBody")
        title_col.addWidget(subtitle)

        controls = QGridLayout()
        controls.setHorizontalSpacing(12)
        controls.setVerticalSpacing(6)
        header_layout.addLayout(controls)

        controls.addWidget(self._small_label("图表窗口"), 0, 0)
        self.chart_window_box = QComboBox()
        self.chart_window_box.addItems(list(self.CHART_WINDOWS))
        self.chart_window_box.currentIndexChanged.connect(lambda _idx: self.refresh_data())
        controls.addWidget(self.chart_window_box, 1, 0)

        controls.addWidget(self._small_label("刷新间隔"), 0, 1)
        self.refresh_interval_box = QComboBox()
        self.refresh_interval_box.addItems(["30", "60", "120", "300"])
        self.refresh_interval_box.setCurrentText("60")
        self.refresh_interval_box.currentIndexChanged.connect(lambda _idx: self._arm_auto_refresh())
        controls.addWidget(self.refresh_interval_box, 1, 1)

        self.auto_refresh_box = QCheckBox("自动刷新")
        self.auto_refresh_box.setChecked(True)
        self.auto_refresh_box.stateChanged.connect(lambda _state: self._arm_auto_refresh())
        controls.addWidget(self.auto_refresh_box, 1, 2)

        self.refresh_button = QPushButton("立即刷新")
        self.refresh_button.clicked.connect(self.refresh_data)
        controls.addWidget(self.refresh_button, 1, 3)

        meta_layout = QHBoxLayout()
        outer.addLayout(meta_layout)

        self.refresh_meta_label = QLabel("等待首次同步")
        self.refresh_meta_label.setObjectName("metaLabel")
        meta_layout.addWidget(self.refresh_meta_label)

        meta_layout.addStretch(1)

        self.status_label = QLabel("准备就绪")
        self.status_label.setObjectName("statusLabel")
        meta_layout.addWidget(self.status_label)

        stat_row = QHBoxLayout()
        stat_row.setSpacing(16)
        outer.addLayout(stat_row)

        self.copper_card = StatCard("纽约铜期货")
        self.scco_card = StatCard("SCCO 股价")
        self.premium_card = StatCard("溢价率温度带", dark=True)
        stat_row.addWidget(self.copper_card)
        stat_row.addWidget(self.scco_card)
        stat_row.addWidget(self.premium_card)

        workspace = QHBoxLayout()
        workspace.setSpacing(18)
        outer.addLayout(workspace, stretch=1)

        left = QVBoxLayout()
        left.setSpacing(18)
        workspace.addLayout(left, stretch=10)

        right_panel = QFrame()
        right_panel.setObjectName("darkPanel")
        right_panel.setMinimumWidth(360)
        right_panel.setMaximumWidth(430)
        workspace.addWidget(right_panel, stretch=4)

        chart_panel = QFrame()
        chart_panel.setObjectName("panel")
        chart_layout = QVBoxLayout(chart_panel)
        chart_layout.setContentsMargins(18, 18, 18, 18)
        chart_layout.setSpacing(12)
        chart_layout.addWidget(self._panel_title("盘中联动曲线"))
        chart_layout.addWidget(self._panel_body("将窗口内的首个有效价格归一化为 100，用一张图直接比较铜期货与 SCCO 的日内斜率和分歧。"))
        self.chart_widget = LineChartWidget()
        chart_layout.addWidget(self.chart_widget)
        left.addWidget(chart_panel, stretch=6)

        tables = QHBoxLayout()
        tables.setSpacing(18)
        left.addLayout(tables, stretch=5)

        self.copper_table = TablePanel(
            "纽约铜期货快照",
            "聚焦价格、涨跌幅与区间位置，适合快速确认当日交易重心。",
        )
        self.scco_table = TablePanel(
            "SCCO 快照",
            "补充市值、总股本、股息率和 Beta，帮助理解股价端映射。",
        )
        tables.addWidget(self.copper_table)
        tables.addWidget(self.scco_table)

        right_outer = QVBoxLayout(right_panel)
        right_outer.setContentsMargins(0, 0, 0, 0)
        right_outer.setSpacing(0)

        right_scroll = QScrollArea()
        right_scroll.setObjectName("darkScroll")
        right_scroll.setFrameShape(QFrame.NoFrame)
        right_scroll.setWidgetResizable(True)
        right_scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        right_outer.addWidget(right_scroll)

        right_content = QWidget()
        right_content.setObjectName("darkPanelContent")
        right_scroll.setWidget(right_content)

        right_layout = QVBoxLayout(right_content)
        right_layout.setContentsMargins(18, 18, 18, 18)
        right_layout.setSpacing(14)

        summary_section, summary_layout = self._dark_section_frame()
        summary_layout.addWidget(self._dark_kicker("PREMIUM"))
        summary_layout.addWidget(self._dark_title("估值摘要"))
        summary_layout.addWidget(self._dark_body("先判断当前所处区间，再用几项核心变量解释这次偏离是不是值得跟。"))

        self.premium_label = QLabel("待计算")
        self.premium_label.setObjectName("premiumTag")
        summary_layout.addWidget(self.premium_label)

        self.premium_note = QLabel("正在等待有效行情字段。")
        self.premium_note.setObjectName("premiumNote")
        self.premium_note.setWordWrap(True)
        summary_layout.addWidget(self.premium_note)

        self.premium_scale_hint = QLabel("低估 < 1.1    修复 1.1-1.3    正常 1.3-1.4    高估 >= 1.4")
        self.premium_scale_hint.setObjectName("premiumHint")
        self.premium_scale_hint.setWordWrap(True)
        summary_layout.addWidget(self.premium_scale_hint)

        self.premium_bar = PremiumBarWidget()
        summary_layout.addWidget(self.premium_bar)
        right_layout.addWidget(summary_section)

        insight_section, insight_layout = self._dark_section_frame()
        insight_layout.addWidget(self._dark_kicker("DRIVERS"))
        insight_layout.addWidget(self._dark_title("关键解释变量"))
        insight_layout.addWidget(self._dark_body("把最影响溢价映射的字段压缩成一个可快速扫读的监控区。"))

        insight_grid = QGridLayout()
        insight_grid.setHorizontalSpacing(10)
        insight_grid.setVerticalSpacing(10)
        insight_layout.addLayout(insight_grid)

        self.insight_rows: dict[str, InsightRow] = {}
        for index, (key, title_text) in enumerate(
            (
                ("copper_range", "铜价区间"),
                ("market_cap", "SCCO 市值"),
                ("shares", "SCCO 总股本"),
                ("dividend_beta", "股息率 / Beta"),
            )
        ):
            row = InsightRow(title_text)
            self.insight_rows[key] = row
            insight_grid.addWidget(row, index // 2, index % 2)

        formula_row = InsightRow("公式快照")
        self.insight_rows["formula"] = formula_row
        insight_layout.addWidget(formula_row)

        self.message_label = QLabel("数据层已连接，等待首次刷新。")
        self.message_label.setWordWrap(True)
        self.message_label.setObjectName("messageLabel")
        insight_layout.addWidget(self.message_label)
        right_layout.addWidget(insight_section)

        notification_section, notification_layout = self._dark_section_frame()
        notification_layout.addWidget(self._dark_kicker("ALERTS"))
        notification_layout.addWidget(self._dark_title("通知配置"))
        notification_layout.addWidget(self._dark_body("直接在桌面端维护代理、邮件、企业微信和飞书设置，保存后立即参与下一轮自动判断。"))

        self.notification_config_label = QLabel(self.notification_config_status.summary)
        self.notification_config_label.setWordWrap(True)
        self.notification_config_label.setObjectName("notificationSummary")
        notification_layout.addWidget(self.notification_config_label)

        self.notification_scope_label = QLabel("--")
        self.notification_scope_label.setWordWrap(True)
        self.notification_scope_label.setObjectName("notificationScope")
        notification_layout.addWidget(self.notification_scope_label)

        self.notification_detail_label = QLabel(self.notification_config_status.detail)
        self.notification_detail_label.setWordWrap(True)
        self.notification_detail_label.setObjectName("notificationDetail")
        notification_layout.addWidget(self.notification_detail_label)

        general_card, general_layout = self._dark_subsection_frame()
        general_layout.addWidget(self._section_caption("发送规则"))

        cooldown_row = QHBoxLayout()
        cooldown_row.setSpacing(10)
        cooldown_row.addWidget(self._dark_field_label("冷却时间"))
        self.cooldown_spin = self._create_dark_spinbox(5, 24 * 60, suffix=" 分钟")
        cooldown_row.addWidget(self.cooldown_spin)
        cooldown_row.addStretch(1)
        general_layout.addLayout(cooldown_row)

        general_layout.addWidget(self._dark_field_label("触发区间"))
        state_grid = QGridLayout()
        state_grid.setHorizontalSpacing(8)
        state_grid.setVerticalSpacing(6)
        self.notify_state_boxes: dict[str, QCheckBox] = {}
        for index, (key, label_text) in enumerate(
            (
                ("undervalued", "低估"),
                ("recovery", "修复区"),
                ("fair_value", "正常估值"),
                ("overvalued", "高估"),
            )
        ):
            box = QCheckBox(label_text)
            box.setObjectName("darkCheck")
            self.notify_state_boxes[key] = box
            state_grid.addWidget(box, index // 2, index % 2)
        general_layout.addLayout(state_grid)
        notification_layout.addWidget(general_card)

        proxy_card, proxy_layout = self._dark_subsection_frame("notificationChannelCard")
        self.proxy_enabled_box = QCheckBox("启用本地代理")
        self.proxy_enabled_box.setObjectName("darkCheck")
        proxy_layout.addWidget(self.proxy_enabled_box)
        proxy_layout.addWidget(self._dark_body("适合 Gmail SMTP 和 webhook 通道统一走本机代理出口。"))
        proxy_form = QFormLayout()
        proxy_form.setContentsMargins(0, 0, 0, 0)
        proxy_form.setSpacing(8)
        proxy_form.setLabelAlignment(Qt.AlignLeft | Qt.AlignTop)
        proxy_form.setFieldGrowthPolicy(QFormLayout.AllNonFixedFieldsGrow)
        self.proxy_mode_box = self._create_dark_select(
            [
                ("系统代理", "system"),
                ("自定义", "custom"),
            ]
        )
        self.proxy_type_box = self._create_dark_select(
            [
                ("HTTP", "http"),
                ("SOCKS5", "socks5"),
            ]
        )
        self.proxy_host_input = self._create_dark_input("127.0.0.1")
        self.proxy_port_spin = self._create_dark_spinbox(1, 65535)
        self.proxy_username_input = self._create_dark_input("用户名（可选）")
        self.proxy_password_input = self._create_dark_input("密码（可选）", password=True)
        proxy_form.addRow(self._dark_field_label("代理来源"), self.proxy_mode_box)
        proxy_form.addRow(self._dark_field_label("代理类型"), self.proxy_type_box)
        proxy_form.addRow(self._dark_field_label("主机"), self.proxy_host_input)
        proxy_form.addRow(self._dark_field_label("端口"), self.proxy_port_spin)
        proxy_form.addRow(self._dark_field_label("用户名"), self.proxy_username_input)
        proxy_form.addRow(self._dark_field_label("密码"), self.proxy_password_input)
        proxy_layout.addLayout(proxy_form)
        self.proxy_hint_label = self._dark_body("代理状态: 当前通知将直连发送。")
        proxy_layout.addWidget(self.proxy_hint_label)
        self.proxy_enabled_box.toggled.connect(lambda _checked: self._sync_proxy_form_state())
        self.proxy_mode_box.currentIndexChanged.connect(lambda _index: self._sync_proxy_form_state())
        notification_layout.addWidget(proxy_card)

        smtp_card, smtp_layout = self._dark_subsection_frame("notificationChannelCard")
        self.smtp_enabled_box = QCheckBox("启用邮件通知")
        self.smtp_enabled_box.setObjectName("darkCheck")
        smtp_layout.addWidget(self.smtp_enabled_box)
        smtp_layout.addWidget(self._dark_body("适合接收结构化提醒，支持 SSL 或 STARTTLS。"))
        smtp_form = QFormLayout()
        smtp_form.setContentsMargins(0, 0, 0, 0)
        smtp_form.setSpacing(8)
        smtp_form.setLabelAlignment(Qt.AlignLeft | Qt.AlignTop)
        smtp_form.setFieldGrowthPolicy(QFormLayout.AllNonFixedFieldsGrow)
        self.smtp_host_input = self._create_dark_input("smtp.example.com")
        self.smtp_port_spin = self._create_dark_spinbox(1, 65535)
        self.smtp_username_input = self._create_dark_input("用户名")
        self.smtp_password_input = self._create_dark_input("密码", password=True)
        self.smtp_sender_input = self._create_dark_input("alerts@example.com")
        self.smtp_receivers_input = self._create_dark_textarea("多个收件人可用逗号或换行分隔")
        self.smtp_security_box = self._create_dark_select(
            [
                ("SSL / SMTPS", "ssl"),
                ("STARTTLS", "starttls"),
                ("不加密", "plain"),
            ]
        )
        smtp_form.addRow(self._dark_field_label("SMTP 主机"), self.smtp_host_input)
        smtp_form.addRow(self._dark_field_label("端口"), self.smtp_port_spin)
        smtp_form.addRow(self._dark_field_label("用户名"), self.smtp_username_input)
        smtp_form.addRow(self._dark_field_label("密码"), self.smtp_password_input)
        smtp_form.addRow(self._dark_field_label("发件人"), self.smtp_sender_input)
        smtp_form.addRow(self._dark_field_label("加密方式"), self.smtp_security_box)
        smtp_form.addRow(self._dark_field_label("收件人"), self.smtp_receivers_input)
        smtp_layout.addLayout(smtp_form)
        notification_layout.addWidget(smtp_card)

        wecom_card, wecom_layout = self._dark_subsection_frame("notificationChannelCard")
        self.wecom_enabled_box = QCheckBox("启用企业微信")
        self.wecom_enabled_box.setObjectName("darkCheck")
        wecom_layout.addWidget(self.wecom_enabled_box)
        wecom_layout.addWidget(self._dark_body("适合团队提醒，支持 webhook 和提醒名单。"))
        wecom_form = QFormLayout()
        wecom_form.setContentsMargins(0, 0, 0, 0)
        wecom_form.setSpacing(8)
        wecom_form.setLabelAlignment(Qt.AlignLeft | Qt.AlignTop)
        wecom_form.setFieldGrowthPolicy(QFormLayout.AllNonFixedFieldsGrow)
        self.wecom_webhook_input = self._create_dark_textarea("https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=...")
        self.wecom_mentions_input = self._create_dark_input("@all 或用户账号，多个值用逗号分隔")
        self.wecom_mobile_input = self._create_dark_input("手机号，多个值用逗号分隔")
        wecom_form.addRow(self._dark_field_label("Webhook"), self.wecom_webhook_input)
        wecom_form.addRow(self._dark_field_label("提醒账号"), self.wecom_mentions_input)
        wecom_form.addRow(self._dark_field_label("提醒手机号"), self.wecom_mobile_input)
        wecom_layout.addLayout(wecom_form)
        notification_layout.addWidget(wecom_card)

        feishu_card, feishu_layout = self._dark_subsection_frame("notificationChannelCard")
        self.feishu_enabled_box = QCheckBox("启用飞书")
        self.feishu_enabled_box.setObjectName("darkCheck")
        feishu_layout.addWidget(self.feishu_enabled_box)
        feishu_layout.addWidget(self._dark_body("适合团队群提醒，支持机器人 webhook。"))
        feishu_form = QFormLayout()
        feishu_form.setContentsMargins(0, 0, 0, 0)
        feishu_form.setSpacing(8)
        feishu_form.setLabelAlignment(Qt.AlignLeft | Qt.AlignTop)
        feishu_form.setFieldGrowthPolicy(QFormLayout.AllNonFixedFieldsGrow)
        self.feishu_webhook_input = self._create_dark_textarea("https://open.feishu.cn/open-apis/bot/v2/hook/...")
        feishu_form.addRow(self._dark_field_label("Webhook"), self.feishu_webhook_input)
        feishu_layout.addLayout(feishu_form)
        notification_layout.addWidget(feishu_card)

        actions_grid = QGridLayout()
        actions_grid.setHorizontalSpacing(10)
        actions_grid.setVerticalSpacing(10)
        notification_layout.addLayout(actions_grid)

        self.save_notification_button = QPushButton("保存配置")
        self.save_notification_button.setObjectName("primaryDarkButton")
        self.save_notification_button.clicked.connect(self._save_notification_config)
        actions_grid.addWidget(self.save_notification_button, 0, 0, 1, 2)

        self.reload_notification_button = QPushButton("重载表单")
        self.reload_notification_button.setObjectName("secondaryButton")
        self.reload_notification_button.clicked.connect(self._reload_notification_config)
        actions_grid.addWidget(self.reload_notification_button, 1, 0)

        self.test_notification_button = QPushButton("发送测试")
        self.test_notification_button.setObjectName("secondaryButton")
        self.test_notification_button.clicked.connect(self._send_test_notification)
        actions_grid.addWidget(self.test_notification_button, 1, 1)

        self.open_notification_button = QPushButton("打开配置文件")
        self.open_notification_button.setObjectName("ghostDarkButton")
        self.open_notification_button.clicked.connect(self._open_notification_config)
        actions_grid.addWidget(self.open_notification_button, 2, 0, 1, 2)

        self.notification_status_label = QLabel("通知状态: 等待下一次估值判断。")
        self.notification_status_label.setWordWrap(True)
        self.notification_status_label.setObjectName("notificationStatus")
        notification_layout.addWidget(self.notification_status_label)
        right_layout.addWidget(notification_section)

        footer = QLabel(
            f"计算公式: (SCCO 股价 × 总股本) × {PREMIUM_MULTIPLIER} / 900亿 / 铜期货价格    桌面端与 CLI 共用同一套数据口径。"
        )
        footer.setObjectName("footerLabel")
        outer.addWidget(footer)

    def _apply_styles(self) -> None:
        self.setStyleSheet(
            """
            QMainWindow, QWidget {
                background: #eef2f4;
                color: #102330;
                font-family: "PingFang SC", "Helvetica Neue";
            }
            QLabel#eyebrow {
                color: #8c6a4a;
                font-size: 11px;
                font-weight: 700;
                letter-spacing: 0.18em;
            }
            QLabel#heroTitle {
                color: #102330;
                font-size: 30px;
                font-weight: 800;
            }
            QLabel#heroBody {
                color: #5d6e7a;
                font-size: 12px;
            }
            QLabel#metaLabel {
                color: #4f5f69;
                font-size: 11px;
            }
            QLabel#statusLabel {
                color: #8c6a4a;
                font-size: 11px;
                font-weight: 700;
            }
            QLabel#smallLabel {
                color: #5d6e7a;
                font-size: 11px;
            }
            QComboBox, QCheckBox, QPushButton {
                font-size: 12px;
            }
            QComboBox {
                background: #ffffff;
                border: 1px solid #d6dee2;
                border-radius: 10px;
                padding: 8px 10px;
                min-width: 92px;
            }
            QCheckBox {
                color: #102330;
                spacing: 8px;
            }
            QPushButton {
                background: #d8a06a;
                color: #102330;
                border: none;
                border-radius: 12px;
                padding: 10px 18px;
                font-weight: 700;
            }
            QPushButton:hover {
                background: #c58d56;
            }
            QPushButton#primaryDarkButton {
                background: #d8a06a;
                color: #102330;
                border-radius: 12px;
                padding: 10px 16px;
                font-size: 12px;
                font-weight: 800;
            }
            QPushButton#primaryDarkButton:hover {
                background: #c88f58;
            }
            QPushButton#secondaryButton {
                background: rgba(255, 255, 255, 0.06);
                color: #f7f1ea;
                border: 1px solid rgba(245, 239, 231, 0.14);
                border-radius: 10px;
                padding: 8px 12px;
                font-size: 11px;
                font-weight: 700;
            }
            QPushButton#secondaryButton:hover {
                background: rgba(255, 255, 255, 0.11);
            }
            QPushButton#ghostDarkButton {
                background: transparent;
                color: #bfd0d8;
                border: 1px dashed rgba(191, 208, 216, 0.28);
                border-radius: 10px;
                padding: 8px 12px;
                font-size: 11px;
                font-weight: 700;
            }
            QPushButton#ghostDarkButton:hover {
                background: rgba(255, 255, 255, 0.04);
            }
            QFrame#lightCard {
                background: #fffaf4;
                border: 1px solid #dfe7eb;
                border-radius: 22px;
            }
            QFrame#darkCard {
                background: #102330;
                border-radius: 22px;
            }
            QFrame#darkPanel {
                background: qlineargradient(x1:0, y1:0, x2:1, y2:1, stop:0 #102330, stop:0.72 #0f202c, stop:1 #163445);
                border: 1px solid #1f3948;
                border-radius: 26px;
            }
            QScrollArea#darkScroll,
            QWidget#darkPanelContent {
                background: transparent;
            }
            QFrame#darkSection,
            QFrame#darkSubsection,
            QFrame#notificationChannelCard,
            QFrame#insightCard {
                background: rgba(255, 255, 255, 0.04);
                border: 1px solid rgba(255, 255, 255, 0.08);
                border-radius: 18px;
            }
            QLabel#cardTitle {
                color: #7f8f99;
                font-size: 11px;
                font-weight: 700;
            }
            QLabel#cardTitleDark {
                color: #9eb3c0;
                font-size: 11px;
                font-weight: 700;
                background: transparent;
            }
            QLabel#cardValue {
                color: #102330;
                font-size: 28px;
                font-weight: 800;
            }
            QLabel#cardValueDark {
                color: #f5efe7;
                font-size: 28px;
                font-weight: 800;
                background: transparent;
            }
            QLabel#cardSub {
                color: #b56c37;
                font-size: 12px;
                font-weight: 700;
            }
            QLabel#cardSubDark {
                color: #edd1b7;
                font-size: 12px;
                font-weight: 700;
                background: transparent;
            }
            QFrame#panel {
                background: #ffffff;
                border: 1px solid #dde5e9;
                border-radius: 22px;
            }
            QLabel#panelTitle {
                color: #102330;
                font-size: 17px;
                font-weight: 800;
                background: transparent;
            }
            QLabel#panelBody {
                color: #62737f;
                font-size: 11px;
                background: transparent;
            }
            QTableWidget#metricsTable {
                background: #ffffff;
                border: none;
                gridline-color: transparent;
                color: #102330;
                selection-background-color: #ffffff;
                alternate-background-color: #ffffff;
            }
            QHeaderView::section {
                background: #f4eee7;
                color: #6a7a86;
                border: none;
                padding: 8px;
                font-size: 11px;
                font-weight: 700;
            }
            QLabel#darkKicker {
                color: #d7a26d;
                font-size: 10px;
                font-weight: 800;
                letter-spacing: 0.16em;
                background: transparent;
            }
            QLabel#darkTitle {
                color: #f7f1ea;
                font-size: 20px;
                font-weight: 800;
                background: transparent;
            }
            QLabel#darkBody {
                color: #9eb3c0;
                font-size: 11px;
                background: transparent;
            }
            QLabel#premiumTag {
                color: #f6d6b1;
                font-size: 12px;
                font-weight: 800;
                background: rgba(216, 160, 106, 0.12);
                border: 1px solid rgba(216, 160, 106, 0.28);
                border-radius: 11px;
                padding: 6px 10px;
            }
            QLabel#premiumNote {
                color: #f7f1ea;
                font-size: 16px;
                font-weight: 800;
                background: transparent;
            }
            QLabel#premiumHint {
                color: #99afbc;
                font-size: 10px;
                background: transparent;
            }
            QLabel#insightTitle {
                color: #9eb3c0;
                font-size: 11px;
                background: transparent;
            }
            QLabel#insightValue {
                color: #f7f1ea;
                font-size: 12px;
                font-weight: 700;
                background: transparent;
            }
            QLabel#messageLabel {
                color: #d9e4ea;
                font-size: 10px;
                background: rgba(255, 255, 255, 0.05);
                border: 1px solid rgba(255, 255, 255, 0.08);
                border-radius: 12px;
                padding: 8px 10px;
            }
            QLabel#notificationSummary {
                color: #f7f1ea;
                font-size: 12px;
                font-weight: 800;
                background: rgba(216, 160, 106, 0.12);
                border: 1px solid rgba(216, 160, 106, 0.26);
                border-radius: 12px;
                padding: 8px 10px;
            }
            QLabel#notificationScope {
                color: #d4e0e6;
                font-size: 10px;
                background: transparent;
            }
            QLabel#notificationDetail {
                color: #bfd0d8;
                font-size: 10px;
                background: transparent;
            }
            QLabel#notificationStatus {
                color: #f7f1ea;
                font-size: 10px;
                background: rgba(255, 255, 255, 0.05);
                border: 1px solid rgba(255, 255, 255, 0.09);
                border-radius: 12px;
                padding: 10px 12px;
            }
            QLabel#sectionCaption,
            QLabel#darkFieldLabel {
                color: #9eb3c0;
                font-size: 10px;
                font-weight: 700;
                background: transparent;
            }
            QLineEdit#darkInput,
            QPlainTextEdit#darkTextArea,
            QSpinBox#darkSpinBox,
            QComboBox#darkSelect {
                background: rgba(7, 19, 28, 0.58);
                color: #f7f1ea;
                border: 1px solid rgba(255, 255, 255, 0.1);
                border-radius: 10px;
                padding: 8px 10px;
                selection-background-color: #c58d56;
            }
            QLineEdit#darkInput:focus,
            QPlainTextEdit#darkTextArea:focus,
            QSpinBox#darkSpinBox:focus,
            QComboBox#darkSelect:focus {
                border: 1px solid #d8a06a;
                background: rgba(7, 19, 28, 0.72);
            }
            QComboBox#darkSelect {
                min-width: 160px;
                padding-right: 26px;
            }
            QComboBox#darkSelect::drop-down {
                border: none;
                width: 24px;
            }
            QComboBox#darkSelect QAbstractItemView {
                background: #142733;
                color: #f7f1ea;
                border: 1px solid rgba(255, 255, 255, 0.12);
                selection-background-color: #d8a06a;
                selection-color: #102330;
            }
            QLineEdit#darkInput[echoMode="2"] {
                lineedit-password-character: 9679;
            }
            QSpinBox#darkSpinBox::up-button,
            QSpinBox#darkSpinBox::down-button {
                width: 18px;
                border: none;
                background: transparent;
            }
            QSpinBox#darkSpinBox::up-arrow,
            QSpinBox#darkSpinBox::down-arrow {
                width: 8px;
                height: 8px;
            }
            QCheckBox#darkCheck {
                color: #eef4f6;
                spacing: 8px;
                background: transparent;
            }
            QCheckBox#darkCheck::indicator {
                width: 14px;
                height: 14px;
                border-radius: 4px;
                border: 1px solid rgba(255, 255, 255, 0.18);
                background: rgba(255, 255, 255, 0.04);
            }
            QCheckBox#darkCheck::indicator:checked {
                background: #d8a06a;
                border: 1px solid #d8a06a;
            }
            QLabel#footerLabel {
                color: #6a7a86;
                font-size: 10px;
            }
            """
        )

    def _small_label(self, text: str) -> QLabel:
        label = QLabel(text)
        label.setObjectName("smallLabel")
        return label

    def _panel_title(self, text: str) -> QLabel:
        label = QLabel(text)
        label.setObjectName("panelTitle")
        return label

    def _panel_body(self, text: str) -> QLabel:
        label = QLabel(text)
        label.setObjectName("panelBody")
        label.setWordWrap(True)
        return label

    def _dark_title(self, text: str) -> QLabel:
        label = QLabel(text)
        label.setObjectName("darkTitle")
        return label

    def _dark_kicker(self, text: str) -> QLabel:
        label = QLabel(text)
        label.setObjectName("darkKicker")
        return label

    def _dark_body(self, text: str) -> QLabel:
        label = QLabel(text)
        label.setObjectName("darkBody")
        label.setWordWrap(True)
        return label

    def _section_caption(self, text: str) -> QLabel:
        label = QLabel(text)
        label.setObjectName("sectionCaption")
        return label

    def _dark_field_label(self, text: str) -> QLabel:
        label = QLabel(text)
        label.setObjectName("darkFieldLabel")
        label.setWordWrap(True)
        return label

    def _dark_section_frame(self) -> tuple[QFrame, QVBoxLayout]:
        frame = QFrame()
        frame.setObjectName("darkSection")
        layout = QVBoxLayout(frame)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.setSpacing(10)
        return frame, layout

    def _dark_subsection_frame(self, object_name: str = "darkSubsection") -> tuple[QFrame, QVBoxLayout]:
        frame = QFrame()
        frame.setObjectName(object_name)
        layout = QVBoxLayout(frame)
        layout.setContentsMargins(14, 14, 14, 14)
        layout.setSpacing(8)
        return frame, layout

    def _create_dark_input(self, placeholder: str, *, password: bool = False) -> QLineEdit:
        widget = QLineEdit()
        widget.setObjectName("darkInput")
        widget.setPlaceholderText(placeholder)
        if password:
            widget.setEchoMode(QLineEdit.Password)
        return widget

    def _create_dark_textarea(self, placeholder: str) -> QPlainTextEdit:
        widget = QPlainTextEdit()
        widget.setObjectName("darkTextArea")
        widget.setPlaceholderText(placeholder)
        widget.setFixedHeight(64)
        widget.setTabChangesFocus(True)
        return widget

    def _create_dark_spinbox(self, minimum: int, maximum: int, *, suffix: str = "") -> QSpinBox:
        widget = QSpinBox()
        widget.setObjectName("darkSpinBox")
        widget.setRange(minimum, maximum)
        if suffix:
            widget.setSuffix(suffix)
        return widget

    def _create_dark_select(self, items: list[tuple[str, str]]) -> QComboBox:
        widget = QComboBox()
        widget.setObjectName("darkSelect")
        for text, data in items:
            widget.addItem(text, data)
        return widget

    def _notify_state_label(self, key: str) -> str:
        mapping = {
            "undervalued": "低估",
            "recovery": "修复区",
            "fair_value": "正常估值",
            "overvalued": "高估",
        }
        return mapping.get(key, key)

    def _selected_notify_states(self) -> list[str]:
        return [key for key, box in self.notify_state_boxes.items() if box.isChecked()]

    def _format_notification_scope(self, config: dict[str, object]) -> str:
        states = config.get("notify_on_states", [])
        labels = [self._notify_state_label(str(state)) for state in states]
        cooldown = config.get("cooldown_minutes", 0)
        proxy_summary = self.notification_service.describe_proxy(config)  # type: ignore[arg-type]
        return f"触发区间: {' / '.join(labels)} | 冷却时间: {cooldown} 分钟 | {proxy_summary}"

    def _collect_proxy_form_data(self) -> dict[str, object]:
        return {
            "enabled": self.proxy_enabled_box.isChecked(),
            "mode": self.proxy_mode_box.currentData(),
            "proxy_type": self.proxy_type_box.currentData(),
            "host": self.proxy_host_input.text(),
            "port": self.proxy_port_spin.value(),
            "username": self.proxy_username_input.text(),
            "password": self.proxy_password_input.text(),
        }

    def _update_notification_summary(self, status_text: Optional[str] = None) -> None:
        self.notification_config_status = self.notification_service.describe_config()
        self.notification_config_label.setText(self.notification_config_status.summary)
        self.notification_scope_label.setText(self._format_notification_scope(self.notification_config))
        self.notification_detail_label.setText(self.notification_config_status.detail)
        if status_text is not None:
            self.notification_status_label.setText(status_text)

    def _sync_proxy_form_state(self) -> None:
        proxy_data = self._collect_proxy_form_data()
        enabled = bool(proxy_data.get("enabled"))
        custom_mode = proxy_data.get("mode") == "custom"

        self.proxy_mode_box.setEnabled(enabled)
        for widget in (
            self.proxy_type_box,
            self.proxy_host_input,
            self.proxy_port_spin,
            self.proxy_username_input,
            self.proxy_password_input,
        ):
            widget.setEnabled(enabled and custom_mode)

        proxy_summary = self.notification_service.describe_proxy({"proxy": proxy_data})
        if not enabled:
            self.proxy_hint_label.setText("代理状态: 当前通知将直连发送。")
        else:
            self.proxy_hint_label.setText(proxy_summary)

    def _load_notification_form(
        self,
        config: Optional[dict[str, object]] = None,
        status_text: Optional[str] = None,
    ) -> None:
        loaded = config or self.notification_service.load_config()
        self.notification_config = loaded

        proxy_config = loaded.get("proxy", {})
        smtp_config = loaded.get("smtp", {})
        wecom_config = loaded.get("wecom", {})
        feishu_config = loaded.get("feishu", {})
        notify_states = {str(item) for item in loaded.get("notify_on_states", [])}

        self.cooldown_spin.setValue(int(loaded.get("cooldown_minutes", 240)))
        for key, box in self.notify_state_boxes.items():
            box.setChecked(key in notify_states)

        self.proxy_enabled_box.setChecked(bool(proxy_config.get("enabled")))
        proxy_mode_index = self.proxy_mode_box.findData(str(proxy_config.get("mode", "system")))
        self.proxy_mode_box.setCurrentIndex(max(proxy_mode_index, 0))
        proxy_type_index = self.proxy_type_box.findData(str(proxy_config.get("proxy_type", "http")))
        self.proxy_type_box.setCurrentIndex(max(proxy_type_index, 0))
        self.proxy_host_input.setText(str(proxy_config.get("host", "")))
        self.proxy_port_spin.setValue(int(proxy_config.get("port", 7890)))
        self.proxy_username_input.setText(str(proxy_config.get("username", "")))
        self.proxy_password_input.setText(str(proxy_config.get("password", "")))

        self.smtp_enabled_box.setChecked(bool(smtp_config.get("enabled")))
        self.smtp_host_input.setText(str(smtp_config.get("host", "")))
        self.smtp_port_spin.setValue(int(smtp_config.get("port", 465)))
        self.smtp_username_input.setText(str(smtp_config.get("username", "")))
        self.smtp_password_input.setText(str(smtp_config.get("password", "")))
        self.smtp_sender_input.setText(str(smtp_config.get("sender", "")))
        self.smtp_receivers_input.setPlainText("\n".join(str(item) for item in smtp_config.get("receivers", [])))
        if bool(smtp_config.get("use_ssl")):
            security_value = "ssl"
        elif bool(smtp_config.get("starttls")):
            security_value = "starttls"
        else:
            security_value = "plain"
        security_index = self.smtp_security_box.findData(security_value)
        self.smtp_security_box.setCurrentIndex(max(security_index, 0))

        self.wecom_enabled_box.setChecked(bool(wecom_config.get("enabled")))
        self.wecom_webhook_input.setPlainText(str(wecom_config.get("webhook_url", "")))
        self.wecom_mentions_input.setText(", ".join(str(item) for item in wecom_config.get("mentioned_list", [])))
        self.wecom_mobile_input.setText(", ".join(str(item) for item in wecom_config.get("mentioned_mobile_list", [])))

        self.feishu_enabled_box.setChecked(bool(feishu_config.get("enabled")))
        self.feishu_webhook_input.setPlainText(str(feishu_config.get("webhook_url", "")))
        self._sync_proxy_form_state()
        self._update_notification_summary(status_text)

    def _collect_notification_form_data(self) -> Optional[dict[str, object]]:
        notify_states = self._selected_notify_states()
        if not notify_states:
            self.notification_status_label.setText("通知状态: 请至少勾选一个触发区间后再保存。")
            return None

        return {
            "cooldown_minutes": self.cooldown_spin.value(),
            "notify_on_states": notify_states,
            "proxy": self._collect_proxy_form_data(),
            "smtp": {
                "enabled": self.smtp_enabled_box.isChecked(),
                "host": self.smtp_host_input.text(),
                "port": self.smtp_port_spin.value(),
                "use_ssl": self.smtp_security_box.currentData() == "ssl",
                "starttls": self.smtp_security_box.currentData() == "starttls",
                "username": self.smtp_username_input.text(),
                "password": self.smtp_password_input.text(),
                "sender": self.smtp_sender_input.text(),
                "receivers": self.smtp_receivers_input.toPlainText(),
            },
            "wecom": {
                "enabled": self.wecom_enabled_box.isChecked(),
                "webhook_url": self.wecom_webhook_input.toPlainText(),
                "mentioned_list": self.wecom_mentions_input.text(),
                "mentioned_mobile_list": self.wecom_mobile_input.text(),
            },
            "feishu": {
                "enabled": self.feishu_enabled_box.isChecked(),
                "webhook_url": self.feishu_webhook_input.toPlainText(),
            },
        }

    def _persist_notification_form(self, success_status: str) -> bool:
        config = self._collect_notification_form_data()
        if config is None:
            return False

        try:
            saved = self.notification_service.save_config(config)
        except OSError as exc:
            self.notification_status_label.setText(f"通知状态: 保存配置失败：{exc}")
            return False

        self._load_notification_form(saved, success_status)
        return True

    def _refresh_notification_panel(self, status_text: Optional[str] = None) -> None:
        self._update_notification_summary(status_text)

    def _format_notification_result(self, result: NotificationResult) -> str:
        if result.details:
            return f"{result.summary} | " + " | ".join(result.details)
        return result.summary

    def _open_notification_config(self) -> None:
        self._refresh_notification_panel("通知状态: 已打开配置文件，可在外部编辑后回到窗口重载表单。")
        QDesktopServices.openUrl(QUrl.fromLocalFile(str(self.notification_service.config_path)))

    def _reload_notification_config(self) -> None:
        self._load_notification_form(
            self.notification_service.load_config(),
            "通知状态: 已从磁盘重载表单。",
        )

    def _save_notification_config(self) -> None:
        self._persist_notification_form("通知状态: 已保存配置，后续自动通知会按新规则执行。")

    def _send_test_notification(self) -> None:
        if self.notification_in_progress:
            self.notification_status_label.setText("通知状态: 仍有发送任务在进行中，请稍后再试。")
            return

        if not self._persist_notification_form("通知状态: 配置已保存，准备发送测试通知。"):
            return

        self.notification_in_progress = True
        self.notification_status_label.setText("通知状态: 正在发送测试通知...")
        worker = threading.Thread(
            target=self._notification_worker,
            args=(self.current_snapshot, True),
            daemon=True,
        )
        worker.start()

    def _schedule_notification(self, snapshot: DashboardSnapshot) -> None:
        if self.notification_in_progress:
            return

        self.notification_in_progress = True
        self.notification_status_label.setText("通知状态: 正在评估自动通知...")
        worker = threading.Thread(
            target=self._notification_worker,
            args=(snapshot, False),
            daemon=True,
        )
        worker.start()

    def _notification_worker(self, snapshot: Optional[DashboardSnapshot], is_test: bool) -> None:
        if is_test:
            result = self.notification_service.send_test_notification(snapshot)
        elif snapshot is not None:
            result = self.notification_service.notify_if_needed(snapshot)
        else:
            result = NotificationResult(
                attempted=False,
                sent=False,
                summary="没有可用快照，当前无法评估自动通知",
            )

        self.snapshot_queue.put(("notification", result))

    def refresh_data(self) -> None:
        if self.fetch_in_progress:
            return

        self.fetch_in_progress = True
        self.status_label.setText("同步中...")
        self.auto_refresh_timer.stop()

        window_label = self.chart_window_box.currentText()
        period, interval = self.CHART_WINDOWS[window_label]

        worker = threading.Thread(
            target=self._fetch_snapshot_worker,
            args=(period, interval),
            daemon=True,
        )
        worker.start()

    def _fetch_snapshot_worker(self, period: str, interval: str) -> None:
        try:
            snapshot = MarketDataService().fetch_dashboard(period=period, interval=interval)
        except Exception as exc:
            self.snapshot_queue.put(("error", f"刷新失败: {exc}"))
            return

        self.snapshot_queue.put(("snapshot", snapshot))

    def _drain_snapshot_queue(self) -> None:
        while not self.snapshot_queue.empty():
            kind, payload = self.snapshot_queue.get()
            if kind == "error":
                self.fetch_in_progress = False
                self.status_label.setText("刷新失败")
                self.message_label.setText(str(payload))
                self._arm_auto_refresh()
            elif kind == "snapshot":
                self.fetch_in_progress = False
                self._apply_snapshot(payload)  # type: ignore[arg-type]
            elif kind == "notification":
                self.notification_in_progress = False
                self._refresh_notification_panel(self._format_notification_result(payload))  # type: ignore[arg-type]

    def _apply_snapshot(self, snapshot: DashboardSnapshot) -> None:
        self.current_snapshot = snapshot
        assessment = assess_premium_ratio(snapshot.premium_ratio)
        premium_label, premium_note = assessment.label, assessment.note

        self.refresh_meta_label.setText(
            f"最后刷新: {snapshot.refreshed_at.strftime('%Y-%m-%d %H:%M:%S')} ({snapshot.timezone_name})"
        )
        self.status_label.setText("已同步")

        self.copper_card.set_values(
            format_price(snapshot.copper.current_price, digits=4),
            format_change_percent(snapshot.copper.change_percent),
        )
        self.scco_card.set_values(
            format_price(snapshot.scco.current_price, digits=2),
            format_change_percent(snapshot.scco.change_percent),
        )
        self.premium_card.set_values(
            "--" if snapshot.premium_ratio is None else f"{snapshot.premium_ratio:.4f}",
            premium_label,
        )

        self.premium_label.setText(premium_label)
        self.premium_note.setText(premium_note)
        self.premium_bar.set_ratio(snapshot.premium_ratio)

        messages = snapshot.status_messages or ["行情正常，未收到额外数据告警。"]
        self.message_label.setText(" | ".join(messages))

        self.insight_rows["copper_range"].set_value(
            f"{format_price(snapshot.copper.day_low, digits=4)} 至 {format_price(snapshot.copper.day_high, digits=4)}"
        )
        self.insight_rows["market_cap"].set_value(format_market_cap_billions(snapshot.scco.market_cap))
        self.insight_rows["shares"].set_value(format_large_number(snapshot.scco.shares_outstanding, suffix=" 股"))
        dividend_yield = "--" if snapshot.scco.dividend_yield is None else f"{snapshot.scco.dividend_yield * 100:.2f}%"
        beta = "--" if snapshot.scco.beta is None else f"{snapshot.scco.beta:.3f}"
        self.insight_rows["dividend_beta"].set_value(f"{dividend_yield} / {beta}")
        self.insight_rows["formula"].set_value(f"市值 × {PREMIUM_MULTIPLIER} / 900亿 / 铜价")

        relative_history = normalize_history_for_comparison(
            {
                "铜期货": snapshot.copper_history,
                "SCCO": snapshot.scco_history,
            }
        )
        self.chart_widget.set_history(relative_history)

        self.copper_table.set_rows(self._build_quote_rows(snapshot.copper, is_equity=False))
        self.scco_table.set_rows(self._build_quote_rows(snapshot.scco, is_equity=True))

        self._schedule_notification(snapshot)
        self._arm_auto_refresh()

    def _build_quote_rows(self, quote: QuoteSnapshot, *, is_equity: bool) -> list[tuple[str, str]]:
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
                    ("股息率", "--" if quote.dividend_yield is None else f"{quote.dividend_yield * 100:.2f}%"),
                    ("Beta", "--" if quote.beta is None else f"{quote.beta:.3f}"),
                    ("总股本", format_large_number(quote.shares_outstanding, suffix=" 股")),
                ]
            )

        return rows

    def _arm_auto_refresh(self) -> None:
        self.auto_refresh_timer.stop()
        if not self.auto_refresh_box.isChecked():
            return

        interval_seconds = int(self.refresh_interval_box.currentText())
        self.auto_refresh_timer.start(interval_seconds * 1000)

    def closeEvent(self, event) -> None:  # noqa: N802
        self.auto_refresh_timer.stop()
        self.queue_timer.stop()
        super().closeEvent(event)


def main() -> None:
    app = QApplication(sys.argv)
    app.setApplicationDisplayName("Copper Pulse")
    app.setStyle("Fusion")
    app.setFont(QFont("PingFang SC", 12))

    window = CopperPulseWindow()
    window.show()

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
