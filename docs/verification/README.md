# Dango 本地验证

本目录保存不含隐私信息的阶段验证入口和记录。验证证据必须区分静态门禁、集成门禁、实时行为、人工验收和 Git 交付状态；构建或空壳进程启动不能替代桌面宿主验收。

## 阶段记录

- [Phase 0 工作区与技术基线阶段记录](phase-0-stage-result.md)：自动门禁、实时空壳启动、延期项和人工验收状态。
- [P1-002 透明覆盖层宿主验证记录](phase-1-p1-002-overlay-host.md)：覆盖层输入、焦点、拖动取消、托盘、DPI 和进程回收已通过人工验收。
- [P1-002A Dango 控制中心验证记录](phase-1-p1-002a-control-center.md)：控制中心入口、图形化验收、诊断、隐私和 DPI 已通过人工验收。
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
