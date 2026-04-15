# Summary

本次变更将命令行行情脚本升级为带容器开发配置的 Python 客户端，并补齐时间语义对齐方案。

# Assumptions

- 当前优先交付 macOS 下可双击启动的桌面客户端
- 宿主机期望使用 `Asia/Shanghai` 本地时间语义
- 不引入复杂前端框架，优先保证 Python 内聚和容器体验
- “微信通知”默认按企业微信机器人 webhook 方案落地

# Chronological Updates

- 2026-04-14 先完成现状检查，确认项目仅包含 CLI 脚本、Dockerfile 和 requirements.txt
- 2026-04-14 建立变更记录，准备开始数据层、客户端界面和容器开发配置改造
- 2026-04-14 抽离 `market_data.py`，统一处理时区、行情抓取、图表历史数据和溢价率计算
- 2026-04-14 新增 `client_app.py` 与 `.streamlit/config.toml`，实现浏览器客户端和主题样式
- 2026-04-14 重构 `copper_market_monitor.py`，让终端版复用统一数据服务
- 2026-04-14 更新 `Dockerfile`、`docker-compose.yml` 与 `.devcontainer/devcontainer.json`，补齐容器运行与容器内开发流程
- 2026-04-14 为了便于宿主机本地校验，将类型标注调整为 Python 3.9 也可解析，但运行镜像仍保持 Python 3.11
- 2026-04-14 完成本地语法校验、devcontainer JSON 校验、compose 配置解析、镜像构建以及容器启动联通验证
- 2026-04-14 根据“需要双击启动”的新反馈，将交付重心从浏览器客户端切换为原生桌面客户端与 `.app` 打包
- 2026-04-14 首版 Tkinter 桌面 app 在 macOS 26 上因系统 `Tk 8.5` 初始化崩溃，已根据崩溃栈调整技术路线
- 2026-04-14 将 `desktop_app.py` 重写为 PySide6 版本，实现原生桌面窗口、自动刷新、联动曲线、溢价温度带和行情快照表
- 2026-04-14 新增 `desktop-requirements.txt`，将桌面端依赖与浏览器端依赖拆分
- 2026-04-14 更新 `scripts/build_macos_app.sh` 与 `启动铜价监控.command`，继续提供 macOS 打包与双击启动入口
- 2026-04-14 重新执行打包并验证启动，交付新的 `Copper Pulse.app`
- 2026-04-14 复核根目录 `Copper Pulse.app`，确认最终产物中已不再包含 `_tkinter` / `Tk.framework`，并直接执行应用二进制验证其可持续运行
- 2026-04-14 将桌面端字体切换为系统自带字体族，消除 Qt 对缺失 `SF Pro Text` 的回退警告，并重新打包验证
- 2026-04-14 追加估值阈值调整需求，准备将 `<1.1`、`>1.3`、`>=1.4` 规则同步到共享数据层与界面提示
- 2026-04-14 追加可配置通知需求，计划为桌面端与 CLI 共用 SMTP / 企业微信 / 飞书通知服务，并增加本地配置文件与冷却控制
- 2026-04-14 新增 `notifications.py`，实现本地通知配置文件、状态冷却、SMTP 邮件、企业微信 webhook、飞书 webhook 和测试通知能力
- 2026-04-14 更新 `desktop_app.py`，增加通知配置入口、测试发送按钮与自动通知状态展示
- 2026-04-14 更新 `client_app.py` 与 `market_data.py`，将估值阈值和温度带同步为低估 / 修复区 / 正常估值 / 高估
- 2026-04-14 更新 `copper_market_monitor.py` 与 `README.md`，让 CLI 共用通知服务并补充配置说明
- 2026-04-14 根据桌面端截图反馈，开始重构右侧深色信息区，准备把说明、状态和通知配置拆成更清晰的层级
- 2026-04-14 计划为 `notifications.py` 补充可视化配置读写接口，并让 `desktop_app.py` 直接提供通知配置表单、保存与测试能力
- 2026-04-14 更新 `notifications.py`，新增通知配置归一化、桌面表单读写和配置完整性提示
- 2026-04-14 重构 `desktop_app.py` 右侧面板，加入滚动容器、分段式深色卡片、可视化通知表单以及保存 / 重载 / 测试动作
- 2026-04-14 更新 `README.md`，将通知说明改为桌面端可直接配置
- 2026-04-14 根据最新截图反馈，继续优化 SMTP“安全方式”控件表达，并准备增强盘中联动曲线的图例、时间刻度和末端标签
- 2026-04-14 将 SMTP 安全方式改为单选下拉框，避免深色面板里复选框表达不清
- 2026-04-14 重绘 `LineChartWidget`，补充顶部图例、时间刻度、末端标签避让和更稳定的边距处理
- 2026-04-15 根据 Gmail 实际配置反馈，准备兼容带空格的 App Password 输入，并用用户当前配置做真实发送验证
- 2026-04-15 真实测试 Gmail SMTP 后确认：当前环境 TCP 端口可连，但 SMTP 欢迎语和 TLS 握手阶段均超时，因此继续补充更明确的网络层报错提示
- 2026-04-15 直接写入用户本机 `~/Library/Application Support/Copper Pulse/notification_config.json` 做真实验证，确认 Gmail 用户名、发件人和去空格后的 16 位 App Password 均已生效，但 `smtp.gmail.com:587` 仍在 SMTP 会话阶段超时
- 2026-04-15 针对 Gmail 主机补充更明确的兜底提示，建议在当前网络下优先使用企业微信 / 飞书 webhook，或切换到可访问 Gmail SMTP 的网络
- 2026-04-15 用户确认本机可以使用本地代理，因此开始为通知服务补充代理支持，并计划让 Gmail SMTP 与 webhook 共享一套代理配置
- 2026-04-15 为通知服务新增统一代理配置，支持系统代理 / 自定义代理以及 HTTP / SOCKS5 两类代理类型，并把桌面端通知面板改成“代理 + 通道”的配置顺序
- 2026-04-15 本机探测到 `127.0.0.1:7890` 正在监听，`7891 / 1080 / 1081 / 9090` 等常见 SOCKS 端口未开放
- 2026-04-15 直接对 `127.0.0.1:7890` 发送 `CONNECT smtp.gmail.com:587` 与 `CONNECT smtp.gmail.com:465`，代理均返回 `HTTP/1.1 200 Connection established`
- 2026-04-15 继续沿代理隧道读取 Gmail SMTP 欢迎语与 TLS 握手时，`587` 的 banner 和 `465` 的 SSL 握手依然超时，说明应用已能走代理，但当前本地 HTTP 代理并未真正把 Gmail SMTP 会话转发打通
- 2026-04-15 开始整理 GitHub / 免费公网部署所需结构，补充 `.gitignore`、`streamlit_app.py` 与 README 的公网部署说明
- 2026-04-15 确认本地仓库已连接 `origin https://github.com/younglou/be_rich.git`，同时发现当前机器未登录 `gh`
- 2026-04-15 通过 GitHub 连接器确认用户账户 `nanfengovo` 已安装 GitHub App，但当前工具链仍缺少“直接创建仓库 / 直接 fork 到子目录”的能力
- 2026-04-15 用户确认希望把桌面端的可配置能力继续搬到浏览器版，因此开始检查 `vibecoding/be_rich` 中实际部署的 Streamlit 入口与通知模块
- 2026-04-15 确认 `client_app.py` 当前仍只有公共行情看板，而 `notifications.py` 已具备通知配置持久化、代理、测试发送与配置摘要能力
- 2026-04-15 更新 `design.md`，补充“公共监控面板 + 管理员配置工作区”的网页端设计，以及 `admin_passcode` 保护策略与免费公网持久化风险说明
- 2026-04-15 更新 `client_app.py`，在浏览器版新增“通知与代理工作区”，并把通知、代理、测试发送直接接到现有 `NotificationService`
- 2026-04-15 为网页端增加 `admin_passcode` 读取逻辑，优先支持 Streamlit secrets，其次支持 `COPPER_PULSE_ADMIN_PASSCODE` 环境变量
- 2026-04-15 浏览器版配置区采用“公共行情 + 管理员配置工作区”分层布局，未解锁时仅保留概要信息，不展示 SMTP / webhook / 代理密码等敏感字段
- 2026-04-15 更新 `.gitignore` 与 `.streamlit/secrets.example.toml`，补充网页端口令保护所需的示例配置
- 2026-04-15 更新 `README.md`，补充 Streamlit Cloud 上的管理员口令配置说明，以及浏览器版通知配置能力的使用方式
- 2026-04-15 根据 Streamlit Community Cloud 的报错反馈，修复浏览器版管理表单的重复控件 ID 问题，为 `用户名 / 密码 / Webhook / 端口` 等同名控件补充显式 `key`
- 2026-04-15 同步为解锁表单与工作区按钮补充稳定 `key`，避免云端在重跑时再次触发 `StreamlitDuplicateElementId`

# Files Changed

- `market_data.py`
- `notifications.py`
- `desktop_app.py`
- `desktop-requirements.txt`
- `client_app.py`
- `.gitignore`
- `.streamlit/secrets.example.toml`
- `.streamlit/config.toml`
- `.devcontainer/devcontainer.json`
- `docker-compose.yml`
- `copper_market_monitor.py`
- `Dockerfile`
- `requirements.txt`
- `README.md`
- `changes/2026-04-14-python-client-container/design.md`
- `scripts/build_macos_app.sh`
- `启动铜价监控.command`
- `Copper Pulse.app`（构建产物）

# Validation

- 使用 `env PYTHONPYCACHEPREFIX=/tmp/pycache python3 -m py_compile market_data.py copper_market_monitor.py client_app.py` 完成语法校验
- 使用 `env PYTHONPYCACHEPREFIX=/tmp/pycache python3 -m py_compile market_data.py copper_market_monitor.py client_app.py desktop_app.py` 完成桌面端语法校验
- 使用 `python3 -m json.tool .devcontainer/devcontainer.json` 校验 devcontainer JSON
- 使用 `docker compose config` 校验 compose 文件可解析
- 使用 `docker compose build` 完成镜像构建
- 使用 `docker compose up -d` 启动容器，并通过 `curl -I http://localhost:8501` 获得 `HTTP/1.1 200 OK`
- 验证结束后已执行 `docker compose down` 清理临时容器与网络
- 使用 `./scripts/build_macos_app.sh` 安装桌面端构建依赖并成功生成根目录 `Copper Pulse.app`
- 检查 `Copper Pulse.app/Contents/Info.plist`，确认生成的是 `APPL` 类型的 macOS 应用包
- 检查根目录 `Copper Pulse.app`，确认未匹配到 `_tkinter`、`Tk.framework` 或 `Tcl.framework`
- 直接执行 `Copper Pulse.app/Contents/MacOS/Copper Pulse`，命令在 4 秒超时前保持运行，说明应用已进入 Qt 事件循环而非启动即崩溃
- 再次执行 `Copper Pulse.app/Contents/MacOS/Copper Pulse`，确认字体回退警告已消失；当前仅剩打包环境自带 Python 3.9 / LibreSSL 触发的 `urllib3` 非阻塞告警
- 使用 `env PYTHONPYCACHEPREFIX=/tmp/pycache python3 -m py_compile market_data.py notifications.py desktop_app.py client_app.py copper_market_monitor.py` 校验本次新增阈值与通知相关代码
- 使用项目 `.desktop-build-venv` 验证样例阈值输出，确认 `0.95 -> 低估`、`1.2 -> 修复区`、`1.35 -> 正常估值`、`1.45 -> 高估`
- 使用项目 `.desktop-build-venv` 初始化 `NotificationService`，确认自动生成 `~/Library/Application Support/Copper Pulse/notification_config.json`
- 使用隔离 `HOME` + `QT_QPA_PLATFORM=offscreen` 初始化 `CopperPulseWindow`，验证桌面端表单可保存 SMTP / 企业微信配置，并正确回写 `notify_on_states` 与冷却时间
- 再次执行 `./scripts/build_macos_app.sh`，确认新样式和 UI 配置能力已重新打包进 `Copper Pulse.app`
- 使用 `QT_QPA_PLATFORM=offscreen` 直接执行 `Copper Pulse.app/Contents/MacOS/Copper Pulse`，命令在 4 秒超时前保持运行；当前仅观察到打包环境自带 Python 3.9 / LibreSSL 触发的 `urllib3` 非阻塞告警
- 使用 `QT_QPA_PLATFORM=offscreen` 初始化桌面窗口，确认 SMTP 安全方式控件显示为 3 个明确选项
- 使用离屏渲染脚本输出联动曲线预览图，确认顶部图例、底部时间刻度和末端标签均已绘制且无遮挡
- 使用项目 `.desktop-build-venv` 直接读取并更新用户本机 `~/Library/Application Support/Copper Pulse/notification_config.json`，确认 Gmail 密码已被归一化为 16 位无空格字符串
- 使用项目 `.desktop-build-venv` 对用户当前 Gmail 配置执行真实 `_send_email`，结果稳定复现为 `SMTP 会话在建连阶段超时（smtp.gmail.com:587）`
- 使用 `env PYTHONPYCACHEPREFIX=/tmp/pycache python3 -m py_compile notifications.py desktop_app.py copper_market_monitor.py client_app.py market_data.py` 校验本次代理支持改动无语法错误
- 使用项目 `.desktop-build-venv/bin/pip install -r desktop-requirements.txt` 安装 `PySocks` 并确认桌面端依赖完整
- 使用项目 `.desktop-build-venv` 探测本机代理端口，确认仅 `127.0.0.1:7890` 处于监听状态
- 使用项目 `.desktop-build-venv` 直接向 `127.0.0.1:7890` 发送 `CONNECT smtp.gmail.com:587/465`，确认代理返回 `HTTP/1.1 200 Connection established`
- 使用项目 `.desktop-build-venv` 在代理隧道建立后继续读取 `587` 的 SMTP banner 与 `465` 的 SSL 握手，结果均超时，确认问题在于当前代理规则未真正放行 Gmail SMTP 会话
- 使用项目 `.desktop-build-venv` 通过 `NotificationService.save_config(...)` 将代理配置持久化到用户本机 `~/Library/Application Support/Copper Pulse/notification_config.json`
- 使用项目 `.desktop-build-venv` 再次对持久化后的代理配置执行真实 `_send_email`，确认错误文案已包含 `当前已启用 HTTP 127.0.0.1:7890`
- 使用 `QT_QPA_PLATFORM=offscreen` 初始化 `CopperPulseWindow`，确认桌面端代理表单已正确加载 `custom / http / 127.0.0.1 / 7890`
- 使用 `env PYTHONPYCACHEPREFIX=/tmp/pycache python3 -m py_compile client_app.py notifications.py market_data.py streamlit_app.py` 校验网页端新增配置工作区后的 Python 语法
- 使用项目 `.desktop-build-venv/bin/python` 在隔离 `HOME` 下执行 `NotificationService.save_config(...)`，确认浏览器表单会提交的原始字符串可被正确归一化为 SMTP 收件人列表、Gmail 无空格密码、企业微信提醒账号与手机号列表
- 使用 `env PYTHONPYCACHEPREFIX=/tmp/pycache python3 -m py_compile client_app.py streamlit_app.py` 复核重复控件 ID 修复后的网页端语法
