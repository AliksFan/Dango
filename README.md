# Dango

`Dango` 是一个多人联机桌面宠物项目的开发代号。

项目希望让每位用户拥有一只代表自己的“团子”。团子平时生活在 Windows 桌面上；当好友进入同一房间后，多只团子会在一个持续存在的共享空间中相遇、陪伴并自动互动。

当前阶段为产品企划、产品设计与技术验证规划，尚未进入正式开发。

## 当前文档

- [AI Agent 工作区规则](AGENTS.md)：项目红线、验证门禁、Git 权限与文档同步规则。
- [产品企划概要 v0.1](docs/product-brief-v0.1.md)：定位、用户、价值、市场参照与长期方向。
- [产品设计基线 v0.1](docs/product-design-v0.1.md)：对象规则、房间权限、体验流程与首版边界。
- [服务端设计基线 v0.1](docs/server-design-v0.1.md)：200 人在线容量、实时同步、部署与扩容。
- [开发工作流 v0.1](docs/development-workflow-v0.1.md)：任务、分支、Worktree、提交和验收流程。
- [分阶段开发计划 v0.1](docs/development-plan-v0.1.md)：Phase 0–5 的范围、交付物与阶段门禁。
- [Phase 1 桌面生命感原型详细设计](docs/phase-1-desktop-life-prototype-design.md)：已批准的桌面宿主对比、场景与资产边界、性能门槛和分层验收。
- [Phase 1 实施计划](docs/phase-1-implementation-plan.md)：已批准的 P0-001 工作区脚手架与 P1-001–P1-007 顺序任务、失败停止点、分支策略和阶段检查点。
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
