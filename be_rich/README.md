# be_rich

容器内开发 + 桌面客户端版的铜价与 SCCO 联动监控项目。

## 现在包含什么

- `copper_market_monitor.py`: 终端版监控脚本
- `desktop_app.py`: 基于 Qt 的原生桌面客户端
- `client_app.py`: Streamlit 浏览器版界面
- `market_data.py`: CLI 与客户端共用的数据服务
- `启动铜价监控.command`: 双击启动入口
- `scripts/build_macos_app.sh`: macOS `.app` 打包脚本
- `desktop-requirements.txt`: 桌面端依赖
- `docker-compose.yml`: 一键启动客户端
- `.devcontainer/devcontainer.json`: 容器内开发配置

## 双击启动

推荐直接双击：

- `启动铜价监控.command`
- 或构建完成后的 `Copper Pulse.app`

如果 `Copper Pulse.app` 还不存在，`启动铜价监控.command` 会先自动打包，再打开桌面客户端。

## 构建 macOS App

```bash
./scripts/build_macos_app.sh
```

打包成功后，桌面客户端路径为：

- `Copper Pulse.app`

## 桌面模式源码运行

```bash
python3 -m venv .venv
source .venv/bin/activate
pip install -r desktop-requirements.txt
python desktop_app.py
```

## 浏览器版客户端

```bash
docker compose up --build
```

启动后访问 [http://localhost:8501](http://localhost:8501)

浏览器版现在采用“主工作区控制栏 + 标签页”结构：

- 顶部主工作区直接配置图表窗口、刷新间隔、自动刷新和手动刷新
- 中部通过标签页切换 `行情总览 / 通知与代理 / 计算与说明`
- 解锁通知工作区后，会自动暂停行情自动刷新，避免编辑长表单时被页面重跑打断

## 免费公网部署

推荐拆成两部分：

- 浏览器版：部署 `streamlit_app.py` 到 Streamlit Community Cloud，得到公网网址
- 桌面版：继续保留 `Copper Pulse.app`，可作为 GitHub 仓库附件或 Release 资源分发

这样可以同时满足：

- 任何人直接打开网址查看浏览器版监控台
- 需要原生客户端的人继续下载桌面版使用

如果你要把项目收进自己的代码合集仓库，例如 `vibecoding`：

- 建议新建仓库 `nanfengovo/vibecoding`
- 将当前项目放在子目录 `be_rich/`
- 浏览器版入口仍然使用 `be_rich/streamlit_app.py`
- 桌面版源码与打包脚本保留在 `be_rich/` 目录下

如果你准备在公网启用网页端配置工作区，建议同时在 Streamlit Cloud 的 Secrets 中加入：

```toml
admin_passcode = "replace-with-your-own-passcode"
```

对应示例文件也放在仓库里：

- `.streamlit/secrets.example.toml`

配置后，网页端的“通知与代理工作区”会自动变成口令保护模式；未输入口令时，页面仍可查看行情，但不会展示 SMTP、Webhook 和代理密码等敏感字段。

## 容器内开发

如果你使用 VS Code / Cursor Dev Container：

1. 打开 `交易程序/be_rich`
2. 选择 “Reopen in Container”
3. 容器启动后运行：

```bash
streamlit run client_app.py --server.address=0.0.0.0 --server.port=8501
```

## 时间与宿主机一致性

- 容器通常共享宿主机时钟，不需要在容器里主动改系统时间
- 镜像默认设置 `TZ=Asia/Shanghai`
- `docker-compose.yml` 和 `.devcontainer` 都挂载了宿主机 `/etc/localtime`
- 应用内展示的更新时间会显式标注当前时区

## 估值阈值

- `< 1.1`: 低估
- `1.1 - 1.3`: 修复区
- `1.3 - 1.4`: 正常估值
- `>= 1.4`: 高估

## 自动通知配置

- 桌面版和 CLI 共用同一套通知服务
- 首次运行后会自动生成配置文件：
  `~/Library/Application Support/Copper Pulse/notification_config.json`
- 浏览器版现在也能直接编辑同一套通知配置，并支持“保存配置 / 从文件重载 / 保存并发送测试”
- 桌面版右侧现在可以直接配置通知表单，不需要手改 JSON
- 支持在桌面端配置统一代理，Gmail SMTP、企业微信和飞书会共用同一套代理出口
- 代理支持两种来源：
  - 系统代理
  - 自定义代理（HTTP / SOCKS5）
- 支持三类通道：
  - SMTP 邮件
  - 企业微信机器人 webhook
  - 飞书机器人 webhook
- 默认只会对“低估 / 正常估值 / 高估”三种明确状态发通知
- 同一状态默认有 240 分钟冷却时间，避免自动刷新时重复刷屏
- 桌面版支持“保存配置 / 重载表单 / 发送测试 / 打开配置文件”
- 若浏览器版部署在免费公网环境，本地文件持久化在平台重启后可能失效；建议把管理员口令放进 Secrets，并把网页端配置视作“可在线调整”的运行时设置

## 终端模式

如果你仍然想用命令行版本：

```bash
python copper_market_monitor.py
```
