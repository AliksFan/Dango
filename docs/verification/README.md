# Dango 本地验证

本目录保存不含隐私信息的阶段验证入口和记录。验证证据必须区分静态门禁、集成门禁、实时行为、人工验收和 Git 交付状态；构建或空壳进程启动不能替代桌面宿主验收。

## 阶段记录

- [本地提交与工作树整理](2026-09-17-worktree-checkpoints.md)：提交分组、保留实验、实际复验和 Git 交付边界。
- [眼帽机制与双宿主隔离预览](phase-1-eye-hat-motion.md)：重绘眼形、面向/跳跃眼动、帽子惯性及 Window/Wallpaper 预览已获人工认可；正式默认未切换，剩余动效和专项矩阵未完成，旧 DLL 失败保留。

- [已验收三体能力统一模块](../phase-1-physics-module-unification.md)：默认容量 3、统一 DLL/API/构建入口、1/2/3 体零误差对照及文档交付。
- [物理模块稳定性补充](phase-1-physics-module-stability.md)：当前模块人工验收确认，以及持续碰撞、偏心叠放、支撑移除、拖放/重置长测和独立三体边界。
- [Phase 1 可复用物理模块](phase-1-physics-module.md)：native/.NET 封装、逐帧等价、生命周期、旧行为回归、正式 DLL 保护和当前人工验收边界。
- [P2D-007C 新 self 团子间碰撞](phase-1-p2d-007c-inter-body-collision.md)：保留早期穿心和联合求解失败证据；后续全方向 3% 相对重叠预览获人工认可，已继续封装为统一模块，旧失败不改写为通过。

- [Phase 0 工作区与技术基线阶段记录](phase-0-stage-result.md)：自动门禁、实时空壳启动、延期项和人工验收状态。
- [P1-002 透明覆盖层宿主验证记录](phase-1-p1-002-overlay-host.md)：覆盖层输入、焦点、拖动取消、托盘、DPI 和进程回收已通过人工验收。
- [P1-002A Dango 控制中心验证记录](phase-1-p1-002a-control-center.md)：控制中心入口、图形化验收、诊断、隐私和 DPI 已通过人工验收。
- [P1-003 WorkerW/Shell 壁纸层宿主验证记录](phase-1-p1-003-workerw-host.md)：保留透明 WPF 候选失败证据；经批准的原生分层增量已完成自动门禁及 Windows 11 Build 26100 人工验收。
- [P2D-000 最新基线与动画资产集成预检](phase-1-p2d-000-integration-preflight.md)：P1-003/C3 后继基线与动画预生产历史无冲突集成，格式、构建、84 项 .NET 测试和动画门禁通过。
- [P2D-005 WPF 动态轮廓与落地预审](phase-1-p2d-005-dynamic-contour.md)：真实 Box2D 落地、WPF 纯色轮廓、默认调参预设、稳定态和人工观感已通过；WorkerW 正式动作接入留在 P2D-006。
- [P2D-006 单团子正式动作与 WorkerW 接入](phase-1-p2d-006-single-dango-motion.md)：Core 权威动作、WorkerW 动态轮廓、宿主生命周期、真实进程检查和人工观感验收均已通过。
- [P2D-007 双体接触技术证明](phase-1-p2d-007-paired-contact.md)：共享 Box2D world、真实 contact、共同 cue、互补挤压和有界释放已通过自动门禁与人工观感验收。
- [P2D-007A 多团子堆叠技术证明](phase-1-p2d-007a-stacked-contact.md)：恢复候选已直切正式 `Wallpaper/Window` 场景，复用 P2D-006 权威方向控制并恢复 P2D-007 完整骨架、有限重叠与帽子隔离；自动门禁通过后仍需桌面人工观感复验。
- [P1-006 壁纸层与普通窗口模式](phase-1-p1-006-window-modes.md)：运行模式、单一 Core、设置迁移、Overlay 退役、真实桌面矩阵和用户人工验收均已通过。
- [2026-09-02 P1-002 人工验收导出](evidence/p1-002-manual-acceptance-2026-09-02.md)：用户导出的 20/20 项人工通过原始记录。

## Phase 0 公共命令

从仓库根运行：

```powershell
pwsh -NoProfile -File .\eng\Validate-PlaceholderAssets.ps1
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test -c Release --no-build
git diff --check
```

测试输出必须显示两个测试程序集及非零测试数。零测试成功不视为通过。

## x64 self-contained 发布

```powershell
dotnet publish .\src\Dango.Desktop\Dango.Desktop.csproj `
  -c Release -r win-x64 --self-contained true `
  -o .\artifacts\publish\Dango.Desktop-win-x64
```

将发布目录复制到另一个干净临时目录，然后直接运行：

```powershell
.\Dango.Desktop.exe --smoke-test
```

`--smoke-test` 只初始化真实 WPF Application/Dispatcher 并正常退出，用于证明发布物可从干净目录启动。它不创建桌面宿主，不验证透明窗口、输入穿透、WorkerW、动画、视觉效果或人工体验。

## 记录约束

- 不记录用户名、真实窗口标题、桌面内容、文件列表、输入或令牌。
- 临时目录必须由当前任务创建、精确核验并在验证后删除。
- 报告 SDK、运行时、发布 RID、进程退出码、仍运行的进程和保留状态。
- 无法执行的真实 Windows 配置或人工验收必须明确标为“未验证”。
