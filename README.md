# Dango

`Dango` 是一个多人联机桌面宠物项目的开发代号。

项目希望让每位用户拥有一只代表自己的“团子”。团子平时生活在 Windows 桌面上；当好友进入同一房间后，多只团子会在一个持续存在的共享空间中相遇、陪伴并自动互动。

Phase 0 工作区与技术基线已通过自动门禁并由用户验收。Phase 1 正在实施：P1-001 场景与资产契约、P1-002A 控制中心、P1-003 WorkerW 壁纸层、P2D-006 单团子动态动作以及 P1-006 `Wallpaper/Window` 正式显示模式均已完成对应验收。多人功能尚未实现。阶段证据见 [本地验证入口](docs/verification/README.md)。

## 构建与验证

开发机要求和 SDK 锁定策略见 [开发环境](docs/development-environment.md)。从仓库根运行：

```powershell
pwsh -NoProfile -File .\eng\Validate-PlaceholderAssets.ps1
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test -c Release --no-build
```

P1-004 固定的 Box2D native 依赖、来源/许可证/哈希和显式重建方式见 [Box2D native 依赖](eng/native/box2d/README.md)。普通 .NET 构建与应用运行不会联网下载 native 文件。

Windows x64 self-contained 发布和干净目录启动方式见 [本地验证](docs/verification/README.md)。当前 WPF 程序默认使用 WorkerW/Shell 承载的 `Wallpaper`，也可在控制中心显式切换到标准、可缩放、有任务栏入口的 `Window`。两种宿主共享同一 Core 团子实体；旧透明覆盖层只保留历史设计和验收记录，不再参与运行。

运行当前 Phase 1 原型：

```powershell
dotnet run --project .\src\Dango.Desktop\Dango.Desktop.csproj -c Release
```

通过托盘菜单或双击托盘图标可以打开“Dango 控制中心”，其中提供显示模式、显示/隐藏、交互与键盘控制、壁纸层对象层级、图形化人工清单、本地草稿、隐私安全诊断和 Markdown 导出；关闭控制中心后 Dango 继续在托盘运行。当前 P1-006 证据见 [壁纸层与普通窗口验证记录](docs/verification/phase-1-p1-006-window-modes.md)。

## 当前文档

- [AI Agent 工作区规则](AGENTS.md)：项目红线、验证门禁、Git 权限与文档同步规则。
- [开发环境](docs/development-environment.md)：.NET SDK 锁定、Windows/PowerShell 工具要求与环境核验命令。
- [产品企划概要 v0.1](docs/product-brief-v0.1.md)：定位、用户、价值、市场参照与长期方向。
- [产品设计基线 v0.1](docs/product-design-v0.1.md)：对象规则、房间权限、体验流程与首版边界。
- [服务端设计基线 v0.1](docs/server-design-v0.1.md)：200 人在线容量、实时同步、部署与扩容。
- [开发工作流 v0.1](docs/development-workflow-v0.1.md)：任务、分支、Worktree、提交和验收流程。
- [分阶段开发计划 v0.1](docs/development-plan-v0.1.md)：Phase 0–5 的范围、交付物与阶段门禁。
- [Phase 1 桌面生命感原型详细设计](docs/phase-1-desktop-life-prototype-design.md)：已批准的桌面宿主对比、场景与资产边界、性能门槛和分层验收。
- [Phase 1 实施计划](docs/phase-1-implementation-plan.md)：已批准的 P0-000 环境准备、P0-001 工作区脚手架与 P1 系列顺序任务；P1-002A 在 P1-003 前实现控制中心和图形化验收入口。
- [Dango 控制中心 UI 设计](docs/phase-1-control-center-ui-design.md)：已批准、完成实现并通过人工验收的正式控制中心、Phase 1 图形化验收入口和未来扩展边界。
- [架构决策记录](docs/adr/README.md)：已批准的重要技术决策、后果与重新评估条件。

## 当前调研

- [Windows 客户端技术栈专项调研](docs/research/2026-08-31-windows-client-stack-comparison.md)：比较 .NET/WPF、Godot、Avalonia、Tauri、Electron、Unity 和 WinUI 3，推荐结论已经批准并记录为 [ADR 0001](docs/adr/0001-windows-client-foundation.md)。
- [服务端技术栈专项调研](docs/research/2026-08-31-server-stack-comparison.md)：比较 ASP.NET Core、Go、Node.js 和 Rust；推荐结论与扩展边界已经批准并记录为 [ADR 0002](docs/adr/0002-server-foundation-and-scaling.md)。
- [动画与渲染管线专项调研](docs/research/2026-09-01-animation-rendering-pipeline-comparison.md)：比较逐帧、分层位图、骨骼网格、矢量和程序化动画，以及 WPF、SkiaSharp、Direct2D 和 Godot 渲染路径；推荐结论已经批准并记录为 [ADR 0003](docs/adr/0003-animation-assets-and-rendering-validation.md)。

## 当前边界

- 首发平台：Windows
- 项目性质：个人作品，暂不以盈利为目标
- 首版核心：桌面常驻、多人房间、好友权限、严格同步、轻量互动
- 房间基线：默认个人房间不占额度；每人最多另建 3 个共同房间
- 服务端基线：大陆单节点、2 核 4 GB 起步、峰值 200 人在线
- 暂不包含：移动端、公开广场、随机匹配、自由绘画上传、复杂养成与 AI 对话
