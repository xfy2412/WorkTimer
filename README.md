# WorkTimer ⌚

开机自启动的半透明桌面悬浮窗计时器，记录每日工作时长。关机/重启后自动询问是否续接上次进度，适用于工作中需要重启电脑但不想丢失计时的情况。

## 功能

- **半透明悬浮窗** — 默认 15% 透明度，鼠标穿透不干扰工作，悬停 2 秒后亮起可交互
- **暂停/继续** — 左键单击切换，暂停时琥珀色闪烁提醒
- **关机续接** — 退出时不归档会话，下次开机弹出续接提示（10 秒倒计时）
- **心跳机制** — 每 30 秒写入心跳，脏关机也能精确计算暂停时长
- **系统托盘** — 托盘图标常驻，双击唤出/隐藏窗口
- **拖拽移动** — 顶部手柄始终可拖拽，穿透模式下也可
- **位置记忆** — 窗口位置自动保存恢复
- **详细日志** — `%LOCALAPPDATA%\WorkTimer\log.txt`

## 快速开始

```bash
dotnet run --project src\WorkTimer.Overlay
```

首次运行自动创建 SQLite 数据库并开启新会话。

## 项目结构

```
WorkTimer/
├── src/
│   ├── WorkTimer.Core/       数据模型 + SQLite + 会话管理
│   ├── WorkTimer.Overlay/    透明悬浮窗 + 托盘 + 续接逻辑
│   └── WorkTimer.Settings/   统计/配置窗口（待开发）
├── generated-images/          Logo 源文件
└── docs/plans/               设计文档
```

## 技术栈

- .NET 8 WPF
- Microsoft.Data.Sqlite
- Hardcodet.NotifyIcon.Wpf
- System.Text.Json

## License

MIT
