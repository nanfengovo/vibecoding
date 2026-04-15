#!/usr/bin/env python3
from __future__ import annotations

import json
import socket
import smtplib
import ssl
import sys
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from email.message import EmailMessage
from pathlib import Path
from typing import Any, Optional
from urllib import error, request
from urllib.parse import quote, unquote, urlparse

import requests
from market_data import (
    DashboardSnapshot,
    PremiumAssessment,
    assess_premium_ratio,
    format_market_cap_billions,
    format_price,
)

try:
    import socks
except ImportError:  # pragma: no cover - optional at source level, required in app bundle
    socks = None

APP_NAME = "Copper Pulse"
CONFIG_FILENAME = "notification_config.json"
STATE_FILENAME = "notification_state.json"
DEFAULT_COOLDOWN_MINUTES = 240
NOTIFY_STATE_ORDER = ["undervalued", "recovery", "fair_value", "overvalued"]

STATE_ALIASES = {
    "undervalued": "undervalued",
    "低估": "undervalued",
    "recovery": "recovery",
    "修复": "recovery",
    "修复区": "recovery",
    "fair_value": "fair_value",
    "正常": "fair_value",
    "正常估值": "fair_value",
    "overvalued": "overvalued",
    "高估": "overvalued",
}


def _app_config_dir() -> Path:
    if sys.platform == "darwin":
        return Path.home() / "Library" / "Application Support" / APP_NAME

    return Path.home() / ".config" / APP_NAME.lower().replace(" ", "-")


def _default_config() -> dict[str, Any]:
    return {
        "cooldown_minutes": DEFAULT_COOLDOWN_MINUTES,
        "notify_on_states": ["undervalued", "fair_value", "overvalued"],
        "proxy": {
            "enabled": False,
            "mode": "system",
            "proxy_type": "http",
            "host": "127.0.0.1",
            "port": 7890,
            "username": "",
            "password": "",
        },
        "smtp": {
            "enabled": False,
            "host": "smtp.example.com",
            "port": 465,
            "use_ssl": True,
            "starttls": False,
            "username": "",
            "password": "",
            "sender": "alerts@example.com",
            "receivers": ["receiver@example.com"],
        },
        "wecom": {
            "enabled": False,
            "webhook_url": "",
            "mentioned_mobile_list": [],
            "mentioned_list": [],
        },
        "feishu": {
            "enabled": False,
            "webhook_url": "",
        },
    }


def _coerce_bool(value: Any) -> bool:
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return bool(value)
    if isinstance(value, str):
        return value.strip().lower() in {"1", "true", "yes", "on", "enabled", "y"}
    return False


def _coerce_int(value: Any, default: int, *, minimum: int, maximum: int) -> int:
    try:
        coerced = int(value)
    except (TypeError, ValueError):
        coerced = default
    return max(minimum, min(maximum, coerced))


def _split_text_list(value: Any) -> list[str]:
    if isinstance(value, str):
        items = value.replace("，", ",").replace("；", "\n").replace(";", "\n").replace(",", "\n").splitlines()
    elif isinstance(value, list):
        items = []
        for item in value:
            items.extend(_split_text_list(item))
    else:
        items = []

    normalized: list[str] = []
    seen: set[str] = set()
    for item in items:
        text = str(item).strip()
        if not text or text in seen:
            continue
        normalized.append(text)
        seen.add(text)
    return normalized


def _strip_all_whitespace(value: Any) -> str:
    if value is None:
        return ""
    return "".join(str(value).split())


def _normalize_proxy_mode(value: Any) -> str:
    return "custom" if str(value or "").strip().lower() == "custom" else "system"


def _normalize_proxy_type(value: Any) -> str:
    return "socks5" if str(value or "").strip().lower() in {"socks5", "socks5h"} else "http"


def _smtp_network_hint(host: str, port: int, proxy: Optional[ResolvedProxyConfig] = None) -> str:
    host_text = str(host or "").strip().lower()
    base = f"SMTP 会话在建连阶段超时（{host}:{port}），当前网络或代理可能拦截了邮件端口"
    if proxy is not None:
        base += f"；当前已启用 {proxy.display}"
    if "gmail" in host_text:
        return base + "；如果本地代理已经开启但仍超时，通常是代理规则未真正放行 Gmail SMTP，建议优先改用企业微信 / 飞书 webhook，或切换到可访问 Gmail SMTP 的网络"
    return base


@dataclass
class ResolvedProxyConfig:
    mode: str
    proxy_type: str
    host: str
    port: int
    username: str = ""
    password: str = ""

    @property
    def display(self) -> str:
        return f"{self.proxy_type.upper()} {self.host}:{self.port}"

    @property
    def requests_url(self) -> str:
        auth = ""
        if self.username:
            auth = quote(self.username)
            if self.password:
                auth += ":" + quote(self.password)
            auth += "@"

        scheme = "socks5h" if self.proxy_type == "socks5" else "http"
        return f"{scheme}://{auth}{self.host}:{self.port}"


def _open_proxy_socket(proxy: ResolvedProxyConfig, host: str, port: int, timeout: Optional[float]) -> socket.socket:
    if socks is None:
        raise ValueError("当前环境缺少 PySocks，无法通过代理建立 SMTP 连接")

    proxy_type = socks.SOCKS5 if proxy.proxy_type == "socks5" else socks.HTTP
    sock = socks.socksocket()
    sock.set_proxy(
        proxy_type,
        addr=proxy.host,
        port=proxy.port,
        username=proxy.username or None,
        password=proxy.password or None,
        rdns=proxy.proxy_type == "socks5",
    )
    if timeout is not None:
        sock.settimeout(timeout)
    sock.connect((host, port))
    return sock


class _ProxySMTP(smtplib.SMTP):
    def __init__(self, *args: Any, proxy: Optional[ResolvedProxyConfig] = None, **kwargs: Any) -> None:
        self._proxy = proxy
        super().__init__(*args, **kwargs)

    def _get_socket(self, host: str, port: int, timeout: float) -> socket.socket:
        if self._proxy is None:
            return super()._get_socket(host, port, timeout)
        return _open_proxy_socket(self._proxy, host, port, timeout)


class _ProxySMTP_SSL(smtplib.SMTP_SSL):
    def __init__(self, *args: Any, proxy: Optional[ResolvedProxyConfig] = None, **kwargs: Any) -> None:
        self._proxy = proxy
        super().__init__(*args, **kwargs)

    def _get_socket(self, host: str, port: int, timeout: float) -> socket.socket:
        if self._proxy is None:
            return super()._get_socket(host, port, timeout)
        raw_socket = _open_proxy_socket(self._proxy, host, port, timeout)
        return self.context.wrap_socket(raw_socket, server_hostname=host)


@dataclass
class NotificationConfigStatus:
    config_path: Path
    enabled_channels: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    @property
    def summary(self) -> str:
        if self.enabled_channels:
            return "已启用通知: " + " / ".join(self.enabled_channels)

        return "通知未启用，可直接在桌面端配置通道"

    @property
    def detail(self) -> str:
        warning_text = ""
        if self.warnings:
            warning_text = " | " + " | ".join(self.warnings)

        return f"配置文件: {self.config_path}{warning_text}"


@dataclass
class NotificationResult:
    attempted: bool
    sent: bool
    summary: str
    details: list[str] = field(default_factory=list)


class NotificationService:
    def __init__(self) -> None:
        self.config_dir = _app_config_dir()
        self.config_path = self.config_dir / CONFIG_FILENAME
        self.state_path = self.config_dir / STATE_FILENAME
        self._ensure_files()

    def _ensure_files(self) -> None:
        self.config_dir.mkdir(parents=True, exist_ok=True)

        if not self.config_path.exists():
            self.config_path.write_text(
                json.dumps(_default_config(), ensure_ascii=False, indent=2),
                encoding="utf-8",
            )

        if not self.state_path.exists():
            self.state_path.write_text("{}", encoding="utf-8")

    def _read_json(self, path: Path, fallback: dict[str, Any]) -> dict[str, Any]:
        try:
            content = path.read_text(encoding="utf-8").strip()
            if not content:
                return dict(fallback)
            value = json.loads(content)
            if not isinstance(value, dict):
                return dict(fallback)
            return value
        except (OSError, json.JSONDecodeError):
            return dict(fallback)

    def _write_json(self, path: Path, payload: dict[str, Any]) -> None:
        path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")

    def _normalize_config(self, config: dict[str, Any]) -> dict[str, Any]:
        defaults = _default_config()
        proxy_raw = config.get("proxy", {}) if isinstance(config.get("proxy"), dict) else {}
        smtp_raw = config.get("smtp", {}) if isinstance(config.get("smtp"), dict) else {}
        wecom_raw = config.get("wecom", {}) if isinstance(config.get("wecom"), dict) else {}
        feishu_raw = config.get("feishu", {}) if isinstance(config.get("feishu"), dict) else {}
        notify_states = self._canonical_states(config.get("notify_on_states", defaults["notify_on_states"]))
        ordered_states = [state for state in NOTIFY_STATE_ORDER if state in notify_states]
        if not ordered_states:
            ordered_states = list(defaults["notify_on_states"])

        return {
            "cooldown_minutes": _coerce_int(
                config.get("cooldown_minutes", defaults["cooldown_minutes"]),
                DEFAULT_COOLDOWN_MINUTES,
                minimum=5,
                maximum=24 * 60,
            ),
            "notify_on_states": ordered_states,
            "proxy": {
                "enabled": _coerce_bool(proxy_raw.get("enabled", defaults["proxy"]["enabled"])),
                "mode": _normalize_proxy_mode(proxy_raw.get("mode", defaults["proxy"]["mode"])),
                "proxy_type": _normalize_proxy_type(proxy_raw.get("proxy_type", defaults["proxy"]["proxy_type"])),
                "host": str(proxy_raw.get("host", defaults["proxy"]["host"]) or "").strip(),
                "port": _coerce_int(proxy_raw.get("port", defaults["proxy"]["port"]), 7890, minimum=1, maximum=65535),
                "username": str(proxy_raw.get("username", defaults["proxy"]["username"]) or "").strip(),
                "password": str(proxy_raw.get("password", defaults["proxy"]["password"]) or "").strip(),
            },
            "smtp": {
                "enabled": _coerce_bool(smtp_raw.get("enabled", defaults["smtp"]["enabled"])),
                "host": str(smtp_raw.get("host", defaults["smtp"]["host"]) or "").strip(),
                "port": _coerce_int(smtp_raw.get("port", defaults["smtp"]["port"]), 465, minimum=1, maximum=65535),
                "use_ssl": _coerce_bool(smtp_raw.get("use_ssl", defaults["smtp"]["use_ssl"])),
                "starttls": _coerce_bool(smtp_raw.get("starttls", defaults["smtp"]["starttls"])),
                "username": str(smtp_raw.get("username", defaults["smtp"]["username"]) or "").strip(),
                "password": _strip_all_whitespace(smtp_raw.get("password", defaults["smtp"]["password"])),
                "sender": str(smtp_raw.get("sender", defaults["smtp"]["sender"]) or "").strip(),
                "receivers": _split_text_list(smtp_raw.get("receivers", defaults["smtp"]["receivers"])),
            },
            "wecom": {
                "enabled": _coerce_bool(wecom_raw.get("enabled", defaults["wecom"]["enabled"])),
                "webhook_url": str(wecom_raw.get("webhook_url", defaults["wecom"]["webhook_url"]) or "").strip(),
                "mentioned_mobile_list": _split_text_list(
                    wecom_raw.get("mentioned_mobile_list", defaults["wecom"]["mentioned_mobile_list"])
                ),
                "mentioned_list": _split_text_list(
                    wecom_raw.get("mentioned_list", defaults["wecom"]["mentioned_list"])
                ),
            },
            "feishu": {
                "enabled": _coerce_bool(feishu_raw.get("enabled", defaults["feishu"]["enabled"])),
                "webhook_url": str(feishu_raw.get("webhook_url", defaults["feishu"]["webhook_url"]) or "").strip(),
            },
        }

    def load_config(self) -> dict[str, Any]:
        self._ensure_files()
        config = self._read_json(self.config_path, _default_config())
        normalized = self._normalize_config(config)
        if config != normalized:
            self._write_json(self.config_path, normalized)
        return normalized

    def save_config(self, config: dict[str, Any]) -> dict[str, Any]:
        self._ensure_files()
        normalized = self._normalize_config(config)
        self._write_json(self.config_path, normalized)
        return normalized

    def _config_warnings(self, config: dict[str, Any]) -> list[str]:
        warnings: list[str] = []
        proxy_config = config.get("proxy", {})
        if bool(proxy_config.get("enabled")):
            if str(proxy_config.get("mode")) == "custom":
                if not proxy_config.get("host") or not proxy_config.get("port"):
                    warnings.append("自定义代理配置不完整")
            else:
                try:
                    self._resolve_proxy_config(config)
                except ValueError as exc:
                    warnings.append(str(exc))

        smtp_config = config.get("smtp", {})
        if bool(smtp_config.get("enabled")):
            if not smtp_config.get("host") or not smtp_config.get("sender") or not smtp_config.get("receivers"):
                warnings.append("邮件配置不完整")

        wecom_config = config.get("wecom", {})
        if bool(wecom_config.get("enabled")) and not wecom_config.get("webhook_url"):
            warnings.append("企业微信 webhook 未填写")

        feishu_config = config.get("feishu", {})
        if bool(feishu_config.get("enabled")) and not feishu_config.get("webhook_url"):
            warnings.append("飞书 webhook 未填写")

        return warnings

    def describe_config(self) -> NotificationConfigStatus:
        config = self.load_config()
        warnings = self._config_warnings(config)
        enabled_channels = self._enabled_channels(config)

        if not enabled_channels:
            warnings.append("当前所有通道默认关闭")

        return NotificationConfigStatus(
            config_path=self.config_path,
            enabled_channels=enabled_channels,
            warnings=warnings,
        )

    def _enabled_channels(self, config: dict[str, Any]) -> list[str]:
        enabled: list[str] = []
        if bool(config.get("smtp", {}).get("enabled")):
            enabled.append("邮件")
        if bool(config.get("wecom", {}).get("enabled")):
            enabled.append("企业微信")
        if bool(config.get("feishu", {}).get("enabled")):
            enabled.append("飞书")
        return enabled

    def _load_state(self) -> dict[str, Any]:
        return self._read_json(self.state_path, {})

    def _save_state(self, assessment: PremiumAssessment, snapshot: DashboardSnapshot) -> None:
        self._write_json(
            self.state_path,
            {
                "last_state": assessment.key,
                "last_label": assessment.label,
                "last_ratio": snapshot.premium_ratio,
                "last_sent_at": snapshot.refreshed_at.isoformat(),
            },
        )

    def _canonical_states(self, values: list[Any]) -> set[str]:
        normalized: set[str] = set()
        for value in values:
            key = STATE_ALIASES.get(str(value).strip())
            if key:
                normalized.add(key)
        return normalized

    def _proxy_requests_map(self, proxy: Optional[ResolvedProxyConfig]) -> Optional[dict[str, str]]:
        if proxy is None:
            return None
        proxy_url = proxy.requests_url
        return {"http": proxy_url, "https": proxy_url}

    def _parse_proxy_url(self, proxy_url: str, *, mode: str) -> ResolvedProxyConfig:
        parsed = urlparse(str(proxy_url or "").strip())
        scheme = str(parsed.scheme or "").strip().lower()
        if scheme in {"http", "https"}:
            proxy_type = "http"
            default_port = 80
        elif scheme in {"socks5", "socks5h"}:
            proxy_type = "socks5"
            default_port = 1080
        else:
            raise ValueError(f"当前代理类型不受支持: {proxy_url}")

        host = parsed.hostname or ""
        if not host:
            raise ValueError(f"代理地址缺少主机名: {proxy_url}")

        return ResolvedProxyConfig(
            mode=mode,
            proxy_type=proxy_type,
            host=host,
            port=parsed.port or default_port,
            username=unquote(parsed.username or ""),
            password=unquote(parsed.password or ""),
        )

    def _resolve_proxy_config(self, config: dict[str, Any]) -> Optional[ResolvedProxyConfig]:
        proxy_config = config.get("proxy", {})
        if not bool(proxy_config.get("enabled")):
            return None

        mode = _normalize_proxy_mode(proxy_config.get("mode"))
        if mode == "system":
            proxies = request.getproxies()
            proxy_url = ""
            for key in ("https", "http", "all"):
                proxy_url = str(proxies.get(key) or "").strip()
                if proxy_url:
                    break

            if not proxy_url:
                raise ValueError("已启用系统代理，但当前未检测到系统代理")

            return self._parse_proxy_url(proxy_url, mode="system")

        host = str(proxy_config.get("host") or "").strip()
        port = int(proxy_config.get("port") or 0)
        if not host or not port:
            raise ValueError("已启用自定义代理，但 host 或 port 未填写")

        return ResolvedProxyConfig(
            mode="custom",
            proxy_type=_normalize_proxy_type(proxy_config.get("proxy_type")),
            host=host,
            port=port,
            username=str(proxy_config.get("username") or "").strip(),
            password=str(proxy_config.get("password") or "").strip(),
        )

    def describe_proxy(self, config: Optional[dict[str, Any]] = None) -> str:
        loaded = config or self.load_config()
        proxy_config = loaded.get("proxy", {})
        if not bool(proxy_config.get("enabled")):
            return "代理: 直连"

        try:
            resolved = self._resolve_proxy_config(loaded)
        except ValueError as exc:
            return f"代理: {exc}"

        source_label = "系统代理" if resolved.mode == "system" else "自定义代理"
        return f"代理: {source_label} ({resolved.display})"

    def _should_send(
        self,
        config: dict[str, Any],
        snapshot: DashboardSnapshot,
        assessment: PremiumAssessment,
    ) -> tuple[bool, str]:
        notify_states = self._canonical_states(config.get("notify_on_states", []))
        if notify_states and assessment.key not in notify_states:
            return False, f"当前区间“{assessment.label}”不在通知名单内"

        state = self._load_state()
        last_state = state.get("last_state")
        last_sent_at_raw = state.get("last_sent_at")
        cooldown_minutes = int(config.get("cooldown_minutes") or DEFAULT_COOLDOWN_MINUTES)

        if last_state != assessment.key:
            return True, "状态发生变化，准备发送通知"

        if not last_sent_at_raw:
            return True, "没有历史发送记录，准备发送通知"

        try:
            last_sent_at = datetime.fromisoformat(last_sent_at_raw)
        except ValueError:
            return True, "历史发送时间格式异常，重新发送通知"

        if snapshot.refreshed_at >= last_sent_at + timedelta(minutes=cooldown_minutes):
            return True, f"冷却时间已超过 {cooldown_minutes} 分钟"

        return False, f"同一状态仍在冷却中（{cooldown_minutes} 分钟）"

    def _build_messages(
        self,
        snapshot: Optional[DashboardSnapshot],
        assessment: Optional[PremiumAssessment],
        *,
        is_test: bool = False,
    ) -> tuple[str, str, str]:
        if snapshot is None or assessment is None:
            now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
            subject = f"{APP_NAME} 测试通知"
            text = f"{APP_NAME} 测试通知\n时间: {now}\n说明: 配置已经生效，可以继续填写真实通知渠道。"
            markdown = f"**{APP_NAME} 测试通知**\n> 时间: {now}\n> 说明: 配置已经生效，可以继续填写真实通知渠道。"
            return subject, text, markdown

        title_prefix = "测试通知" if is_test else assessment.label
        premium_value = "--" if snapshot.premium_ratio is None else f"{snapshot.premium_ratio:.4f}"
        subject = f"{APP_NAME} {title_prefix} | 溢价率 {premium_value}"
        text = "\n".join(
            [
                f"{APP_NAME} {title_prefix}",
                f"状态: {assessment.label}",
                f"溢价率: {premium_value}",
                f"说明: {assessment.note}",
                f"更新时间: {snapshot.refreshed_at.strftime('%Y-%m-%d %H:%M:%S')} ({snapshot.timezone_name})",
                f"纽约铜期货: {format_price(snapshot.copper.current_price, digits=4)}",
                f"SCCO 股价: {format_price(snapshot.scco.current_price, digits=2)}",
                f"SCCO 市值: {format_market_cap_billions(snapshot.scco.market_cap)}",
            ]
        )
        markdown = "\n".join(
            [
                f"**{APP_NAME} {title_prefix}**",
                f"> 状态: {assessment.label}",
                f"> 溢价率: {premium_value}",
                f"> 说明: {assessment.note}",
                f"> 更新时间: {snapshot.refreshed_at.strftime('%Y-%m-%d %H:%M:%S')} ({snapshot.timezone_name})",
                f"> 纽约铜期货: {format_price(snapshot.copper.current_price, digits=4)}",
                f"> SCCO 股价: {format_price(snapshot.scco.current_price, digits=2)}",
                f"> SCCO 市值: {format_market_cap_billions(snapshot.scco.market_cap)}",
            ]
        )
        return subject, text, markdown

    def _send_email(self, config: dict[str, Any], subject: str, text: str) -> str:
        smtp_config = config.get("smtp", {})
        host = str(smtp_config.get("host") or "").strip()
        username = str(smtp_config.get("username") or "").strip()
        password = _strip_all_whitespace(smtp_config.get("password"))
        sender = str(smtp_config.get("sender") or username).strip()
        receivers = [str(item).strip() for item in smtp_config.get("receivers", []) if str(item).strip()]
        port = int(smtp_config.get("port") or 465)
        use_ssl = bool(smtp_config.get("use_ssl", True))
        starttls = bool(smtp_config.get("starttls", False))
        proxy = self._resolve_proxy_config(config)

        if not host or not sender or not receivers:
            raise ValueError("SMTP 配置不完整，需要至少填写 host、sender、receivers")

        message = EmailMessage()
        message["Subject"] = subject
        message["From"] = sender
        message["To"] = ", ".join(receivers)
        message.set_content(text)

        ssl_context = ssl.create_default_context()
        try:
            if use_ssl:
                with _ProxySMTP_SSL(host, port, timeout=15, context=ssl_context, proxy=proxy) as server:
                    if username:
                        server.login(username, password)
                    server.send_message(message)
            else:
                with _ProxySMTP(host, port, timeout=15, proxy=proxy) as server:
                    server.ehlo()
                    if starttls:
                        server.starttls(context=ssl_context)
                        server.ehlo()
                    if username:
                        server.login(username, password)
                    server.send_message(message)
        except (socket.timeout, TimeoutError):
            raise ValueError(_smtp_network_hint(host, port, proxy))
        except smtplib.SMTPServerDisconnected as exc:
            text_exc = str(exc)
            if "timed out" in text_exc.lower():
                raise ValueError(_smtp_network_hint(host, port, proxy))
            raise

        return "邮件发送成功"

    def _post_json(self, config: dict[str, Any], url: str, payload: dict[str, Any]) -> dict[str, Any]:
        proxy = self._resolve_proxy_config(config)
        session = requests.Session()
        session.trust_env = False
        response = session.post(
            url,
            json=payload,
            headers={"Content-Type": "application/json; charset=utf-8"},
            timeout=15,
            proxies=self._proxy_requests_map(proxy),
        )
        response.raise_for_status()
        body = response.text.strip()
        if not body:
            return {}
        parsed = json.loads(body)
        return parsed if isinstance(parsed, dict) else {}

    def _send_wecom(self, config: dict[str, Any], text: str) -> str:
        wecom_config = config.get("wecom", {})
        webhook_url = str(wecom_config.get("webhook_url") or "").strip()
        if not webhook_url:
            raise ValueError("企业微信 webhook_url 未配置")

        payload = {
            "msgtype": "text",
            "text": {"content": text},
        }

        mentioned_list = [str(item).strip() for item in wecom_config.get("mentioned_list", []) if str(item).strip()]
        mentioned_mobile_list = [
            str(item).strip()
            for item in wecom_config.get("mentioned_mobile_list", [])
            if str(item).strip()
        ]
        if mentioned_list or mentioned_mobile_list:
            payload["text"]["mentioned_list"] = mentioned_list
            payload["text"]["mentioned_mobile_list"] = mentioned_mobile_list

        response_payload = self._post_json(config, webhook_url, payload)
        errcode = response_payload.get("errcode")
        if errcode not in (None, 0, "0"):
            raise ValueError(f"企业微信返回异常: {response_payload}")

        return "企业微信发送成功"

    def _send_feishu(self, config: dict[str, Any], text: str) -> str:
        feishu_config = config.get("feishu", {})
        webhook_url = str(feishu_config.get("webhook_url") or "").strip()
        if not webhook_url:
            raise ValueError("飞书 webhook_url 未配置")

        response_payload = self._post_json(
            config,
            webhook_url,
            {
                "msg_type": "text",
                "content": {"text": text},
            },
        )

        status_code = response_payload.get("StatusCode", response_payload.get("code"))
        if status_code not in (None, 0, "0"):
            raise ValueError(f"飞书返回异常: {response_payload}")

        return "飞书发送成功"

    def _dispatch(self, config: dict[str, Any], subject: str, text: str, markdown: str) -> NotificationResult:
        details: list[str] = []
        sent = False
        attempted = False

        channels = [
            ("smtp", "邮件", lambda: self._send_email(config, subject, text)),
            ("wecom", "企业微信", lambda: self._send_wecom(config, text)),
            ("feishu", "飞书", lambda: self._send_feishu(config, text)),
        ]

        for key, label, sender in channels:
            channel_config = config.get(key, {})
            if not bool(channel_config.get("enabled")):
                continue

            attempted = True
            try:
                result = sender()
                sent = True
                details.append(f"{label}: {result}")
            except (
                ValueError,
                OSError,
                smtplib.SMTPException,
                error.URLError,
                json.JSONDecodeError,
                requests.RequestException,
            ) as exc:
                details.append(f"{label}: {exc}")

        if not attempted:
            return NotificationResult(
                attempted=False,
                sent=False,
                summary="通知未启用，当前不会自动发送消息",
                details=details,
            )

        if sent:
            return NotificationResult(
                attempted=True,
                sent=True,
                summary="通知已发送",
                details=details,
            )

        return NotificationResult(
            attempted=True,
            sent=False,
            summary="通知发送失败，请检查通道配置",
            details=details,
        )

    def notify_if_needed(self, snapshot: DashboardSnapshot) -> NotificationResult:
        self._ensure_files()
        config = self.load_config()
        assessment = assess_premium_ratio(snapshot.premium_ratio)

        if assessment.key == "unavailable":
            return NotificationResult(
                attempted=False,
                sent=False,
                summary="当前溢价率不可用，不触发通知",
            )

        should_send, reason = self._should_send(config, snapshot, assessment)
        if not should_send:
            return NotificationResult(
                attempted=False,
                sent=False,
                summary=reason,
            )

        subject, text, markdown = self._build_messages(snapshot, assessment)
        result = self._dispatch(config, subject, text, markdown)
        if result.sent:
            self._save_state(assessment, snapshot)
        elif result.details:
            result.summary = f"{reason}，但通道返回错误"
        else:
            result.summary = reason

        return result

    def send_test_notification(self, snapshot: Optional[DashboardSnapshot] = None) -> NotificationResult:
        self._ensure_files()
        config = self.load_config()

        assessment: Optional[PremiumAssessment] = None
        if snapshot is not None:
            assessment = assess_premium_ratio(snapshot.premium_ratio)

        subject, text, markdown = self._build_messages(snapshot, assessment, is_test=True)
        return self._dispatch(config, subject, text, markdown)
