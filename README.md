# Dango

`Dango` 是一个多人联机桌面宠物项目的开发代号。

2026-10-07 普通房窗口：用户通过现有窗口体验，已按授权将 `codex/local-room-implementation@4bbd2bb` 快进合入本地 main，包含两层初始场景、玩法／抚摸、滚轮视距、三种镜头模式和纵向中线修订、Esc 菜单及首页往返。沿用最终受影响测试69/69和隔离 Release 构建证据，15项真实前台右键自动门禁仍未验证；详见[验收与整合记录](docs/verification/2026-10-06-window-camera-modes.md#2026-10-07-用户人工通过与本地-main-整合)。壁纸代码和入口保留，壁纸层继续搁置、未验收，待桌宠房间充分测试后再回来排查。未推送、更新日常安装或部署；完整 Windows／正式房间／多人阶段仍开放。下方状态保留各自历史范围。

2026-10-06 捏脸独立本地整合：按用户选择剥离普通房依赖，已将验证后的捏脸／首页成果快进合入本地 main。Release 构建 0 警告／0 错误，受影响测试 370/370、零跳过，普通房代码与历史未带入；具体范围、失败修复及原人工验收边界见[整合记录](docs/verification/2026-10-06-face-only-local-main-integration.md)。原候选现场和受保护 DLL 保留，未推送、更新日常安装或部署；完整 Windows／多人阶段与其它 DPI 专项仍开放。

2026-10-06 原候选人工确认（合并前快照）：身体／眼睛 RGB、四个眼睛滑块与整套显式保存，以及首页三侧按键入口、镜像面板、贴地阴影和纵向保存位置获用户观感确认。此前对话设计已归档为[首页与自身外观设计](docs/planning/2026-10-06-home-and-appearance-design.md)，历史证据见[捏脸实现](docs/verification/2026-10-05-face-implementation.md)和[原布局验收](docs/verification/2026-10-05-home-icons-and-ground-shadow.md)。用户随后选择只合捏脸，拆分结果见[独立整合记录](docs/verification/2026-10-06-face-only-local-main-integration.md)。实际 100%／125% 缩放及其它显示器未验证，不代表整个 Windows 或多人阶段完成。

2026-10-04：在独立任务分支实施 E1 二维跟随门廊与本地导航，见[专项验证](docs/verification/2026-10-04-room-porch-e1.md)和[实施计划](docs/planning/2026-10-04-room-entry-implementation-plan.md)。虚构门只在隔离场景预览中展示；正式房间业务、服务器、数据库、合并、推送和日常安装更新不在本轮范围，候选待人工验收。

2026-10-03：按用户单独授权，已将团队整合与 Windows 本地收口成果经 `codex/team-integration` 快进合入本地 `main`；见[本地整合记录](docs/verification/2026-10-03-windows-local-main-integration.md)。日常安装沿用已验收新版，不重新发布或迁移；未推送远端。已知自动差异、专项未验证项及 Phase 2A/M7 未验收边界保留，本次不是完整阶段验收或正式发布。

2026-10-01：首页 UI 按已认可的简约横向原型重新实施，本轮仅包含“我的团子”与捏脸、换装展开交互；用户已确认本批人工验收通过。范围和边界见[首页实施约定](docs/planning/2026-10-01-home-ui-fidelity.md)，自动检查及遗留限制见[验证记录](docs/verification/2026-10-01-home-ui-fidelity.md)。

2026-09-22 规划：已确认[步态与外观分批路线](docs/planning/2026-09-22-gait-and-customization-roadmap.md)和[行走自然感设计](docs/planning/2026-09-22-gait-sample-design.md)的首轮取舍，现已制定[小样实施计划](docs/planning/2026-09-22-gait-sample-plan.md)并获用户授权实施。单团子自主/键盘轻微步态及隔离项目入口已完成，Desktop 373/373、Core 37/37 通过；用户已确认当前小样整体观感并保留此版。见[本批验证](docs/verification/2026-09-22-gait-sample.md)。

2026-09-22：已验收性能改动已按授权经阶段分支合入本地 main，并更新日常安装；真实启动、Window/Wallpaper 切换、设置保留和正常退出检查通过，旧版本可回退。见[本地交付记录](docs/verification/2026-09-22-performance-local-delivery.md)。原自动失败和专项未验证项继续保留，未推送或部署。

2026-09-18：本分支已加入轻点眨眼、Shift＋左键横向轻抚眯眼，并获用户整体人工验收通过；普通移动达到 3 DIP 后沿用原拖动。见[点击与抚摸验证记录](docs/verification/2026-09-17-light-interaction.md)。本批 32 项用例通过，完整回归无新增失败；历史门禁问题和未逐项执行的专项矩阵仍保留。

本分支已实现单人正式雏形的第一批入口整合：普通无参数启动接入一只团子及新眼帽，提供既有拖放/方向控制、三种帽型、基础偏好保存、隐藏/恢复和故障重试。多体、骨架、物理参数和验收工具只在显式评审模式展示。用户于 2026-09-17 确认本批人工验收通过；完整互动、自主生活和真实上下岛继续后置，专项矩阵及整个 Phase 1 尚未完成。见[第一批设计](docs/planning/2026-09-17-desktop-entry-integration-design.md)、[实施计划](docs/planning/2026-09-17-desktop-entry-integration-plan.md)和[实施验证](docs/verification/2026-09-17-desktop-entry-integration.md)。下文原有验收说明保留各自历史范围。

日常验收默认直接从项目启动对应窗口，不再默认提供整个压缩包。需要分发时再构建普通便携候选，将已有验收模块的本机路径作为显式输入：

```powershell
.\eng\publish-desktop-entry.ps1 -RunName <新的输出名称> -PhysicsModulePath <已验收模块DLL路径>
```

脚本不会重建 native、启动程序或覆盖旧输出。输出为 `artifacts/desktop-entry/<RunName>/package/`，普通启动使用其中的 `Dango.Desktop.exe`；设置位于当前 Windows 用户的本地应用数据目录，人工检查优先使用隔离测试用户。旧 DLL 导出失败和全仓格式基线问题仍保留，该包仅为内部候选。

固定本地安装、版本切换和回退已实现，桌面快捷方式已通过真实冷启动及重复打开检查；脚本与入口回归、设置/单实例测试及真实包切换通过，详见[本批验证记录](docs/verification/2026-09-20-local-launch-update.md)。方案见[本地启动、更新与回退设计](docs/planning/2026-09-20-local-launch-update-design.md)、[实施计划](docs/planning/2026-09-20-local-launch-update-plan.md)及[本地桌面安装说明](docs/local-desktop-install.md)。候选不代表已发布版本。

项目希望让每位用户拥有一只代表自己的“团子”。团子先在产品首页出现；进入桌宠房间后才在本机桌面显示，进入共同房间后则在独立场景中与其他团子相遇。

Phase 0 工作区与技术基线已通过自动门禁并由用户验收。Phase 1 正在实施：P1-001 场景与资产契约、P1-002A 控制中心、P1-003 WorkerW 壁纸层、P2D-006 单团子动态动作、P2D-007 双体接触以及 P1-006 `Wallpaper/Window` 正式显示模式均已完成对应验收。后续统一物理模块与眼帽隔离预览的最新状态见下文；旧 P2D-007A 技术证明的待验收记录按原路线保留。Phase 2A 已有隔离同机多人同步小样，正式多人及房间功能尚未实现。阶段证据见 [本地验证入口](docs/verification/README.md)。

已验收的单/双/三体模块已接入 Window/Wallpaper 隔离候选；用户已确认本轮宿主接入人工验收通过，旧 DLL 保留，其历史导出门禁失败未消除，尚未发布。初始一只，控制中心可增至三只；方向键或 WASD 控制当前选中团子，轻拖选择目标，“移除团子”移除选中实体。参数只读，骨架可独立开关。推荐使用独立设置和单实例标识的人工入口：

```powershell
.\eng\run-physics-host-review.ps1 -Build
```

## 构建与验证

开发机要求和 SDK 锁定策略见 [开发环境](docs/development-environment.md)。从仓库根运行：

```powershell
pwsh -NoProfile -File .\eng\Validate-PlaceholderAssets.ps1
dotnet format --verify-no-changes
$module = (Resolve-Path .\artifacts\native-spike\physics-module-unified-build\Release\dango_physics_module.dll).Path
dotnet build -c Release "-p:DangoPhysicsModulePath=$module"
dotnet test -c Release --no-build
```

P1-004 固定的 Box2D native 依赖、来源/许可证/哈希和显式重建方式见 [Box2D native 依赖](eng/native/box2d/README.md)。普通 .NET 构建与应用运行不会联网下载 native 文件。

新模块构建依据见[模块说明](docs/phase-1-physics-module.md)。当前完整 Desktop 回归仍有一项历史旧 DLL 导出失败，未替换 DLL 来消除红灯；实际结果与限制见[接入验证记录](docs/verification/phase-1-physics-desktop-integration.md)。

Windows x64 self-contained 发布和干净目录启动方式见 [本地验证](docs/verification/README.md)。当前 WPF 程序默认使用 WorkerW/Shell 承载的 `Wallpaper`，也可在控制中心显式切换到标准、可缩放、有任务栏入口的 `Window`。两种宿主共享同一 Core 团子实体；旧透明覆盖层只保留历史设计和验收记录，不再参与运行。

运行当前 Phase 1 原型：

```powershell
.\eng\run-physics-host-review.ps1
```

通过托盘菜单或双击托盘图标可以打开“Dango 控制中心”，其中提供显示模式、显示/隐藏、交互与键盘控制、壁纸层对象层级、图形化人工清单、本地草稿、隐私安全诊断和 Markdown 导出；关闭控制中心后 Dango 继续在托盘运行。当前 P1-006 证据见 [壁纸层与普通窗口验证记录](docs/verification/phase-1-p1-006-window-modes.md)。

## 当前文档

- [AI Agent 工作区规则](AGENTS.md)：项目红线、验证门禁、Git 权限与文档同步规则。
- [开发环境](docs/development-environment.md)：.NET SDK 锁定、Windows/PowerShell 工具要求与环境核验命令。
- [产品企划概要 v0.1](docs/product-brief-v0.1.md)：定位、用户、价值、市场参照与长期方向。
- [产品设计基线 v0.1](docs/product-design-v0.1.md)：对象规则、房间权限、体验流程与首版边界。
- [服务端设计基线 v0.1](docs/server-design-v0.1.md)：200 人在线容量、实时同步、部署与扩容。
- [邮箱后端首期设计](docs/planning/2026-10-08-email-backend-design-proposal.md)、[实施计划](docs/planning/2026-10-08-email-backend-implementation-plan.md)、[ADR 0006](docs/adr/0006-email-accounts-and-persistent-room-state.md)与[B0 验证](docs/verification/2026-10-08-email-backend-foundation.md)：用户同意开始本地账号、团子与房间首期，五项质量要求已入规则；B0 真实 PostgreSQL 基础通过，正式账号、邮件及 Windows 接入后续实施。
- [房间机制设计](docs/planning/2026-10-02-room-mechanism-design-proposal.md)及[二维门廊、返回与无房间修订](docs/planning/2026-10-04-room-entry-and-no-room-design.md)：容量／权限机制与已通过的入口、宿主和离房规则；正式业务未实现。
- [房间入口最小闭环实施计划](docs/planning/2026-10-04-room-entry-implementation-plan.md)：待确认的门廊 UI、真实领域前置、入房显示、回门廊和分层验收顺序；不代表编码或数据库操作已授权。
- [本地普通房实施计划](docs/planning/2026-10-05-local-single-room-plan.md)、[产品实现验证](docs/verification/2026-10-05-local-room-product.md)及[人工评审修订](docs/verification/2026-10-05-local-room-review-revision.md)：窗口／壁纸两种呈现、Esc 中央菜单、外出控制中心与完整壁纸玩法的候选；自动结果、真实前台失败及待人工验收项分别记录，未合并 main 或更新日常安装。
- [桌宠房间分层设计](docs/planning/2026-10-07-desktop-pet-room-layered-design.md)、[实施计划](docs/planning/2026-10-07-desktop-pet-room-layered-implementation-plan.md)及[体验修订](docs/planning/2026-10-07-desktop-pet-room-feedback-revision.md)：真实壁纸房间与独立上层团子，当前修订大小、屏幕边界与拖动故障；人工验收未完成。
- [设置页范围与验收设计](docs/planning/2026-10-01-settings-ui.md)及[专项验证记录](docs/verification/2026-10-01-settings-ui.md)：产品首页内设置页的范围、失败型验收与独立 Product 宿主人工确认；日常安装和完整自动门禁待补。
- [统一实时首页与固定尺寸验证记录](docs/verification/2026-10-03-unified-live-home.md)：首页真实团子、门廊交互及三档物理目标尺寸的当前证据和未完成边界。
- [Phase 2A 最小多人同步详细设计](docs/planning/2026-09-24-minimal-multiplayer-sync-design.md)、[实施计划](docs/planning/2026-09-30-minimal-multiplayer-sync-plan.md)、[实施验证记录](docs/verification/2026-09-30-minimal-multiplayer-sync.md)及[UI 前置排期记录](docs/planning/2026-09-30-ui-before-dual-machine-note.md)：真实 SignalR 与内存权威临时房间的同机 M0–M6 小样已获人工确认；双 Windows M7、Phase 2A 整体及正式房间均未验收，日常入口未接入多人。
- [团队整合候选验证记录](docs/verification/2026-10-03-team-integration.md)：首页、Phase 2A 与已批准房间规则的合并来源、自动/同机证据及未完成门禁。
- [Windows 收口与团队基线本地整合记录](docs/verification/2026-10-03-windows-local-main-integration.md)：共享分支到 main 的快进、已验收源码一致性、交接边界和仍待授权的推送。
- [开发工作流 v0.1](docs/development-workflow-v0.1.md)：任务、分支、Worktree、提交和验收流程。
- [分阶段开发计划 v0.1](docs/development-plan-v0.1.md)：Phase 0–5 的范围、交付物与阶段门禁。
- [本地桌面安装说明](docs/local-desktop-install.md)：固定目录安装、启动、更新、状态检查与回退的日常操作。
- [Windows 本地收口设计与实施计划](docs/planning/2026-10-03-windows-local-closeout.md)及[验证记录](docs/verification/2026-10-03-windows-local-closeout.md)：同屏两缩放单机候选已获人工确认，后续分别授权的日常更新和本地整合已完成；自动差异与未验证项保留，推送另行批准。
- [帽子轨道分配优化](docs/phase-1-hat-track-allocation-design.md)及[实施计划](docs/phase-1-hat-track-allocation-plan.md)：已批准的局部优化与等价、分配、真实宿主性能验收顺序。
- [可复用团子物理模块](docs/phase-1-physics-module.md)：项目内 native/.NET 模块、固定配置、API、构建、显示适配与当前验收边界。
- [Phase 1 桌面生命感原型详细设计](docs/phase-1-desktop-life-prototype-design.md)：已批准的桌面宿主对比、场景与资产边界、性能门槛和分层验收。
- [Phase 1 实施计划](docs/phase-1-implementation-plan.md)：已批准的 P0-000 环境准备、P0-001 工作区脚手架与 P1 系列顺序任务；P1-002A 在 P1-003 前实现控制中心和图形化验收入口。
- [Dango 控制中心 UI 设计](docs/phase-1-control-center-ui-design.md)：已批准、完成实现并通过人工验收的正式控制中心、Phase 1 图形化验收入口和未来扩展边界。
- [架构决策记录](docs/adr/README.md)：已批准的重要技术决策、后果与重新评估条件。

- [B5B Windows 房间目录与管理计划](docs/planning/2026-10-09-backend-b5b-room-execution.md)、[候选验证](docs/verification/2026-10-09-email-backend-b5b.md)与[精确数据计划](docs/planning/2026-10-09-backend-b5b-local-data-plan.md)：正式账号会话绑定的目录与 CRUD 已接入，静态／离屏门禁通过；按新精确授权执行的真实 HTTPS／数据库首轮 4／4 通过，完整 B5 与人工未验收。

- [登录持续捏脸同步、昵称与门廊设计](docs/planning/2026-10-10-account-sync-nickname-and-porch-design.md)、[联合验证](docs/verification/2026-10-10-account-sync-nickname-and-porch.md)和[门廊实窗证据](docs/verification/2026-10-10-porch-account-scenes.md)：按用户确认实现登录差异选择、会话自动同步、首次昵称及场景直接展开房间功能。核心和受影响检查通过，新真实数据库与主观验收仍待完成，配饰同步随后接入。

## 当前调研

- [B5C 正式连接／租约增量](docs/planning/2026-10-09-backend-b5c-session-execution.md)、[候选验证](docs/verification/2026-10-09-email-backend-b5c.md)与[精确数据计划](docs/planning/2026-10-09-backend-b5c-local-data-plan.md)：用户明确批准后，真实HTTPS／LongPolling／PostgreSQL最终6／6通过，Client静态364／364；三轮测试资源均退出、数据保留。Windows人工、正式移动／动作与多人呈现仍未接入，完整 B5 未验收。
- [B5C Windows 验收记录](docs/verification/2026-10-10-email-backend-b5c-windows-manual.md)：R2 曾获批启动并完成注册、登录、建房和入房，目前已收尾、数据保留；该旧候选不包含后续昵称与持续同步修订，完整阶段尚未验收。

- [Windows 客户端技术栈专项调研](docs/research/2026-08-31-windows-client-stack-comparison.md)：比较 .NET/WPF、Godot、Avalonia、Tauri、Electron、Unity 和 WinUI 3，推荐结论已经批准并记录为 [ADR 0001](docs/adr/0001-windows-client-foundation.md)。
- [服务端技术栈专项调研](docs/research/2026-08-31-server-stack-comparison.md)：比较 ASP.NET Core、Go、Node.js 和 Rust；推荐结论与扩展边界已经批准并记录为 [ADR 0002](docs/adr/0002-server-foundation-and-scaling.md)。
- [动画与渲染管线专项调研](docs/research/2026-09-01-animation-rendering-pipeline-comparison.md)：比较逐帧、分层位图、骨骼网格、矢量和程序化动画，以及 WPF、SkiaSharp、Direct2D 和 Godot 渲染路径；推荐结论已经批准并记录为 [ADR 0003](docs/adr/0003-animation-assets-and-rendering-validation.md)。

## 当前边界

眼帽双宿主隔离预览已于 2026-09-17 获用户人工认可：重绘眼形、130 ms 眨眼、7/5.25 DIP 非对称面向、跳跃眼动、1.35 倍帽子和急停惯性。见[验证与预览入口](docs/verification/phase-1-eye-hat-motion.md)和[眼帽设计](docs/phase-1-eye-hat-motion-design.md)。本分支普通入口候选已启用新机制，仍需普通入口的新组合人工检查；独立 BounceHat、完整组合预算及降低额外动效控制仍待完成，第二种 DPI、长测和完整故障矩阵未验证。[单人完整动作草案](docs/phase-1-single-player-interaction-design.md)继续搁置，保留[类似项目参考](docs/research/2026-09-12-single-player-desktop-pet-reference.md)。本地提交与保留实验见[工作树整理记录](docs/verification/2026-09-17-worktree-checkpoints.md)。

- 首发平台：Windows
- 项目性质：个人作品，暂不以盈利为目标
- 首版核心：桌面常驻、多人房间、好友权限、严格同步、轻量互动
- 房间基线：默认桌宠房间不占额度；每人最多另建 3 个共同房间
- 服务端基线：大陆单节点、2 核 4 GB 起步、峰值 200 人在线
- 暂不包含：移动端、公开广场、随机匹配、自由绘画上传、复杂养成与 AI 对话
