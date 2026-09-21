# 宿主时钟与帧调度修复实施计划

日期：2026-09-21。状态：用户已确认设计并明确“开始实施”。H0–H3 实现与自动回归已完成；H4 真实短测未通过，按停止点保留候选和证据，H5 未启动。实际完成和未验证项见[验证报告](verification/2026-09-21-host-clock-scheduling-repair.md)。

依据：[已批准设计](phase-1-host-clock-scheduling-repair-design.md)、[原始验证报告](verification/2026-09-20-performance-stability.md)。起点为 `codex/performance-stability` / `584549b`；当前唯一既有未提交文件是本任务的设计文档。

## 1. 执行范围与交付

在当前隔离 Worktree 顺序推进，主代理拥有产品、测试和探针文件；审查代理只读。若后续需要并行改文件，先划定所有权并使用独立 Worktree，不同时改两个宿主及共享 runtime。没有数据库、服务、端口、云资源或新依赖需求。

预计交付：单调固定步调度器、两个宿主的唯一活动驱动、版本明确的诊断适配、可失败测试、隔离候选及分层验证报告。产品范围保持批准设计不变；本计划不包含后端替换、广泛内存优化、日常安装更新、合并 main、推送或部署。

实现顺序为 H0 → H1 → H2 → H3 → H4 → H5。每步先确定能失败的断言，再实现使其通过；只在新的修改、失败或未解决风险需要时重跑验证。

## 2. 分步实施与停止点

### H0：固定对照和保护输入

- 重新核对分支、HEAD、工作区和 Worktree，保留已有文档与后续用户修改。把当前设计批准日期写入交付记录，不改旧报告中的历史结论。
- 从原报告和本地证据定位基线包，核对原包清单、三个产品程序集、两个原生 DLL 和 .NET/WPF 指纹。日常进程身份及真实设置只读核对；不沿用旧 PID 作为当前事实。
- 原样统一模块 SHA-256 必须为 `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`；保护旧 DLL 为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。产品代码更新后程序集哈希可以变化，原生库不得变化。
- 对本轮涉及的测试取得修复前结果，记录旧 DLL 失败的具体测试、错误和二进制身份；不把历史“440/441”等计数硬编码为本轮结果。
- 本轮所有输出使用新目录，原包和 `artifacts/performance/wallpaper-60m`、`timer-diagnostic-run` 保持原样。中间候选记录 `sourceDirty`；最终长测候选应有可追溯的干净本地提交及清单。

停止点：保护输入指纹不符、工作区出现无法归属的修改、原生模块未正确复制或依赖环境缺失时先定位；不得重新编译/替换受保护库来让测试通过。

### H1：纯时间调度与行为时长

拟新增 `src/Dango.Desktop/DesktopHost/HostFrameClock.cs` 与 `tests/Dango.Desktop.Tests/DesktopHost/HostFrameClockTests.cs`；名称可随现有命名约定微调，职责不变。

- 提供可注入单调时间源，返回本次固定步预算、剩余余量、丢弃时间及停顿原因；不包含 WPF 静态事件和 native 调用。
- 固定步沿用 `TimeSpan.FromSeconds(1d / 60d)` 的实际表示，最多 6 步；不足 1 秒的过载丢整步债务、留不足一步余量；达到 1 秒时整段丢弃并发出重置请求。重复时间戳不吞余量，非单调输入重建基准。
- 区分预算步数、实际成功步数和故障后弃置时间，不能预先把预算记为成功执行。正常无丢时窗口用“实际步长总和 + 余量”对照单调 elapsed，容差仅来自一个固定步及明确的量化误差。
- 扩展 `IdleDozeTests` 或新增窄化的调度集成测试，以同一时间序列经新时钟再推进真实控制器，覆盖候睡/闭眼/睡眠端点和忙动作等待；不靠直接给 Doze 喂入大 elapsed 代替时钟路径。

对应 T1、T2、T4。使用 10 秒/600 秒、30/60/120 Hz、正常抖动、99/100/101/999/1000 ms、10 秒停顿和已有余量的边界组合。先证明“一回调一步”、每回调丢余量、无限补步等错误策略会失败。

停止点：回调频率改变模拟速度、普通卡顿重置候睡、时间记账不守恒或需要改物理步长才能通过。H1 完成只证明时间模型，不能发布为帧率修复。

### H2：两个宿主接入有界补步

目标文件：`WorkerWWindow.xaml.cs`、`DangoSceneWindow.xaml.cs`；必要时新增窄化的 `HostFrameLoop.cs`，共享回调编排，避免复制两套累加规则。`StackedDangoSceneRuntime.cs` 仅增加本循环所需的状态/诊断边界，不重构行为系统。

- 暂以现有入口验证循环集成：宿主资格与取消先处理，每回调刷新一次输入/环境；逐步调用原 runtime.Tick，最后仅一次 `ApplyPresentationFrame` 和命中区域更新。
- 一次性跳跃等边沿只消费一次，连续方向意图可供多步使用；真实物理仍在原 UI 线程、原模块和原参数下执行。
- 将初始化/取消导致的刷新与普通更新分开统计。新故障终止当次补步并清债；物理错误保持全局锁存，既有附件错误保持局部冻结，不能每次看到旧附件错误就再次中断健康对象。
- Start/Stop/长停顿接入批准的重置与取消规则，重复 Start 不重置运行中的余量。隐藏暂停保留场景，退出才释放；不补放隐藏时长。

拟新增 `HostFrameLoopTests.cs`，并扩展 `StackedDangoSceneRuntimeTests`、`IdleDozeTests`、`DesktopHostModuleIntegrationTests`、`LightInteractionHostTests`。使用真实 runtime/附件断言累计次数、末帧应用次数及局部故障边界；纯假对象只验证循环编排，不替代 native 集成。

对应 T2–T6。重点反例：30 个回调各两步仅应用 30 帧；同次六步跳跃只有一个按下边沿；第 k 步新故障后无第 k+1 步；停用代次的回调不能进入共享 runtime。

停止点：身体/眼帽参数改变、Core 事件被表现帧修改、故障范围扩大、宿主隐藏后仍步进。该中间版本仍可能只有约 35 次应用更新，不能判 60 FPS 通过。

### H3：诊断适配与 Rendering 候选

这一步先补齐测量接口，再选择调度入口。目标文件为两个宿主、拟新增的 `HostFrameScheduler.cs`，以及 `eng/tools/Dango.PerformanceProbe`、`eng/tools/Dango.TimerProbe`、`eng/performance/analyze_samples.py`、`test_analyze_samples.py`、`render_charts.py` 和必要 README。绘图器当前读取 `motionTicks`，也必须按新旧 schema 明确字段含义，不能沿用旧标签解释新计数。

1. 增加轻量、可关闭的自身诊断快照/完成通知，记录原始回调、去重、成功物理步、普通末帧应用、生命周期刷新、丢弃原因与处理器耗时。默认运行不逐帧写盘、不构建大型日志对象。
2. 探针按 schema/能力明确选择旧 timer 或新调度指标；不静默猜字段。原 schema 1 数据仍能读取且保留其旧指标含义，新 schema 不用 Rendering 回调数或物理步数伪造 FPS。释放证据直接读取附件条目/路由器状态，不再仅以控制器引用是否为 null 判定。
3. 在隔离候选实现 `CompositionTarget.Rendering` 适配器，测试同一 RenderingTime 去重、Stopwatch elapsed、重复开始、旧回调代次、停止退订。两个宿主共享 runtime 必须先停旧驱动再启新驱动。
4. 旧包与候选串行短测；基线包维持旧 timer，新候选只运行一种入口。候选可行后两个正式宿主统一采用新入口，移除旧动画 timer 的活动路径；不新增产品开关，也不保留两个并发动画循环。WorkerW Shell 探测 timer 保持原职责。

拟新增 `HostFrameSchedulerTests.cs`，扩展 `ProductApplicationHostTests`、宿主协调与窗口生命周期测试；测试真实事件订阅/解除的效果，不能只检查源码包含某个字符串。分析器增加 schema 不符、帧数混淆、缺失计数、丢时和局部失败的反例，旧失败基线仍必须失败。

对应 T3–T7。Renderer 回调收到但没有成功应用末帧，计作收到事件，不能计作成功应用更新。计数器和探针本身不得在隐藏时保留动画订阅。TimerProbe 的合成空 timer/5 ms 对照仍明确单列，新产品阶段不再绑定已移除的 `motionTimer` 字段。

停止点：透明宿主持续缺少回调、两个宿主双重推进、退出无法退订、口径无法区分步与帧，或性能恶化。失败时保留候选和证据，先定位；不自动改用忙循环、修改系统计时设置或换后端。

### H4：发布候选、短测与热点证据

- 运行增量相关回归后，对最终候选执行一次完整 Core/Desktop 测试、Release 构建、格式检查与隔离发布；既有旧 DLL 失败保留单列，新失败必须解释并修复。完整测试不能通过筛掉失败用例冒充全绿。原生模块和契约不变，不扩大到依赖其他参考库的 Physics 长压力矩阵；若改动物理包装/原生契约，须重新核定范围。
- 复用 `eng/publish-desktop-entry.ps1` 生成内部包及 `package-manifest.json`，只引用经校验的统一模块。然后将探针构建到独立输出，并核对它加载的三个程序集、两个 DLL、CLR 和 WPF 与输入包逐一相同。
- 在同设备、显示/DPI、单团子和日常后台负载下，按 Wallpaper、Window 顺序测；每次预热至少 60 秒，每个参与资源判定的状态至少 180 秒有效样本。先按 900 秒观察准备短测，若某状态不足则标证据不足并另设足够时长的新运行，不修改自然行为凑样本。
- 维持自然 Product 动作、正常时间与虚构设置，不注入全局输入。探针的 20 次隐藏恢复和持续隐藏/恢复/释放阶段仍执行，因此“900 秒观察”不是整个工具只跑 15 分钟。
- 用独立诊断运行采集 CPU 调用栈与分配率，仅限本轮测试进程；先检查本机现有工具能力，不擅自安装依赖。资源轻采样与高开销分析分开，无法取得则记录未验证，不凭 CPU 总量猜热点。

对应 T5–T7。空闲 CPU≤1%、工作集峰值≤250 MB，普通活动 CPU≤5%、目标 60 FPS，全部沿用原口径；原始间隔、丢弃时间、混合状态和无效样本均保留。不自创 58 FPS、P95 或泄漏通过线。

停止点：短测有新场景错误、持续丢时、预算不通过或关键数据缺失，则不进入最终合格候选长测。报告时钟/生命周期的局部结论和具体热点，另提最小优化范围；不在本批顺手重写渲染/物理。

### H5：真实 60 分钟、人工验收与交付

- 对 H4 合格的同一候选执行 Wallpaper 连续观察至少 3600 秒，再 20 次隐藏/恢复（各至少 3 秒）、持续隐藏至少 180 秒、恢复至少 180 秒、释放后至少 30 秒。候选变更后必须重新判定哪些证据失效，不能沿用旧包长测。
- 仍按原计划检查采样间隔、UI 心跳、状态有效样本、场景错误、资源趋势及退出。完成文件与实际退出码分别记录；只停止本轮精确 PID，测试输出保留。
- 从项目候选启动人工验收，使用隔离设置与项目路径；直接启动常规 EXE 前确认不会通过单实例入口激活日常实例。让用户观察自然散步/打盹，并验证首次点击、拖动/方向唤醒及两个宿主的隐藏恢复；不以 ZIP 或进程响应替代观感。
- 新增 `docs/verification/2026-09-21-host-clock-scheduling-repair.md`（实际交付日期变化时相应命名），分别报告静态、集成、真实运行、人工、Git 状态、环境保护和残留资源。

对应 T8、T9。本机通过不代表其他 DPI/多屏/Windows 10/Explorer 或系统睡眠恢复通过；旧 DLL 失败仍在。只有对应证据与人工验收齐备才更新本批状态，不将整个 Phase 1 标为完成。

## 3. 验证命令与运行约定

以下是实施时使用的命令，不是本轮执行记录。均在仓库根目录运行；`$clockModulePath` 由 H0 定位并核验，`$clockResultsRoot` 指向本轮唯一测试结果目录，使用本机变量传递，不能把绝对开发机路径写进仓库。新增测试类完成后再使用相应过滤器，并核对实际发现的测试数量大于零。开发中增量命令的控制台日志一并保留，最终测试另存 TRX。

```powershell
# H1：新增时钟路径及真实打盹控制器
dotnet test tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj -c Release "-p:DangoPhysicsModulePath=$clockModulePath" --filter 'FullyQualifiedName~HostFrameClockTests|FullyQualifiedName~IdleDozeTests'

# H2/H3：补步、事件生命周期、共享宿主和互动回归
dotnet test tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj -c Release "-p:DangoPhysicsModulePath=$clockModulePath" --filter 'FullyQualifiedName~HostFrameLoopTests|FullyQualifiedName~HostFrameSchedulerTests|FullyQualifiedName~StackedDangoSceneRuntimeTests|FullyQualifiedName~DesktopHostModuleIntegrationTests|FullyQualifiedName~ProductApplicationHostTests|FullyQualifiedName~DesktopHostCoordinatorTests|FullyQualifiedName~QuietIdle|FullyQualifiedName~ShortWander|FullyQualifiedName~LightInteraction|FullyQualifiedName~LightFeedback|FullyQualifiedName~WindowHostConfigurationTests|FullyQualifiedName~WorkerWHostConfigurationTests'

# H3：新旧 schema 与错误计数反例
python -m unittest discover -s eng/performance -p 'test_*.py'

# H4：最终候选检查，保留所有失败记录
dotnet test tests/Dango.Core.Tests/Dango.Core.Tests.csproj -c Release --results-directory "$clockResultsRoot/core" --logger 'trx;LogFileName=core.trx'
dotnet test tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj -c Release "-p:DangoPhysicsModulePath=$clockModulePath" --results-directory "$clockResultsRoot/desktop" --logger 'trx;LogFileName=desktop.trx'
dotnet build Dango.slnx -c Release "-p:DangoPhysicsModulePath=$clockModulePath"
dotnet format Dango.slnx --verify-no-changes --no-restore
git diff --check
```

完整检查若报告旧 DLL 缺失导出，应报告完整命令为失败并附失败清单，同时单列已通过的本批测试；不能把历史失败转换为跳过。当前已定位的旧失败入口是 `DangoInterBodyCollisionTests.SharedSelfWorldExportsAreAvailable`，以实际运行结果复核。原生相关测试必须检查输出目录实际加载的统一模块指纹，`-p:` 参数存在不等于加载正确。

发布和探针命令模板如下；运行名称/输出目录每次唯一且不存在。内部发布只生成候选文件，不执行安装或更新。

```powershell
# $clockRunName 为本次唯一运行名，$clockModulePath 为已核验输入
pwsh -NoProfile -File eng/publish-desktop-entry.ps1 -RunName $clockRunName -PhysicsModulePath $clockModulePath
$clockPackagePath = (Resolve-Path (Join-Path 'artifacts/desktop-entry' "$clockRunName/package")).Path
$clockEvidenceRoot = [IO.Path]::GetFullPath((Join-Path 'artifacts/performance' $clockRunName))

# H3 适配完成后才能用候选包构建和运行新探针
dotnet publish eng/tools/Dango.PerformanceProbe/Dango.PerformanceProbe.csproj -c Release -r win-x64 --self-contained true "-p:DesktopPackagePath=$clockPackagePath" -o "$clockEvidenceRoot/performance-probe"
dotnet publish eng/tools/Dango.TimerProbe/Dango.TimerProbe.csproj -c Release -r win-x64 --self-contained true "-p:DesktopPackagePath=$clockPackagePath" -o "$clockEvidenceRoot/timer-probe"

# 保持三参数契约：新输出目录、观察秒数、宿主模式
$clockOutputPath = Join-Path $clockEvidenceRoot 'wallpaper-60m'
$clockProbePath = Join-Path $clockEvidenceRoot 'performance-probe/Dango.PerformanceProbe.exe'
$clockProcess = Start-Process -FilePath $clockProbePath -ArgumentList ('"' + $clockOutputPath + '" 3600 Wallpaper') -WindowStyle Hidden -PassThru

# 等精确 PID 退出并写入真实 process-exit.json 后再分析
python eng/performance/analyze_samples.py "$clockOutputPath/samples.jsonl" "$clockOutputPath/analysis.json" --expected-observe-seconds 3600
python eng/performance/render_charts.py "$clockOutputPath/samples.jsonl" "$clockOutputPath/performance.png"
```

启动后通过短时、非阻塞轮询跟踪 `$clockProcess`，完成时记录真实退出时间/码；不得运行上面分析命令读取仍在写入的长测文件。不能仅凭 `complete.json` 写一个假的 `process-exit.json`。短测使用新目录、`900` 与所选 `Wallpaper`/`Window`，分析时明确 `--expected-observe-seconds 900`，结果仍标短测，不能用此参数降低长测标准。

两探针和高开销分析不得同时运行；合成忙工作会影响桌面采样。CLR/WPF 版本从候选包核实并保持一致，尤其 TimerProbe 当前固定 10.0.11；不为方便测试升级运行时。命令本身成功不代替二进制核对及实际结果。

## 4. 授权与完成边界

用户先确认设计，随后明确“开始实施”，H0–H5 的本批代码与隔离验证已经获得授权；日常安装更新仍不在范围内。

依据[开发工作流第 4.2 节](development-workflow-v0.1.md)，设计与实施授权分别记录。实施授权已到位，可执行 H0–H5 的代码、隔离验证及必要本地提交；每一步仍遵守停止点，人工验收等待用户实际反馈。合并、推送、部署、真实系统恢复操作及持久资源删除保持独立授权。
