# 团子步态小样实施计划

日期：2026-09-22。状态：**用户以“继续”授权实施；G0–G3 自动验证完成，G4 真实启动/模式往返/退出已核对，步态人工观感待定。**

用户回复：“我也无法模拟出效果，先试试吧，按上述两点继续制定实施计划”。据此确认[设计](2026-09-22-gait-sample-design.md)中的两项首轮取舍：普通单团子同时覆盖自主散步与键盘横移；先试贴地、无整体起伏、最大新增位移 2 DIP 的轻微形变与起停回正。其余数值作为首轮试验候选，实际观感由可运行小样确认，不要求用户凭文字想象后再逐项定参数。

本计划只实施上述两个表现。下文 G0–G4 是这一小批的工程步骤，不是五批新功能；不启动外观、联网、岛、真实体型/柔软度、完整动作系统或日常更新。

## 1. 起点与已核对的前置（计划编写时快照）

- 当前分支 `codex/gait-sample-design`，起点 `246d567`，工作区干净；主线物理/宿主代码仍为 `e8938d3`，包含 `8329397` 闪回修复。路线已通过本地 `032809d` 纳入，源分支与其他工作树不动。
- SDK 按 [global.json](../../global.json) 的 10.0.300/latestPatch；[Desktop 测试项目](../../tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj) 为 net10.0-windows/MSTest，真实 WPF 测试沿用 `[STATestMethod]`。本轮只核对文件，没有执行 SDK、构建或测试命令。
- 本工作树缺少预编译统一模块的历史夹具路径。但已只读确认：路线记录的日常版本目录存在统一模块，SHA-256 为 `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`；可在实施前将它作为明确来源，重新核对后只复制到本任务缺失的构建/夹具输出。此刻未复制、加载或修改该文件。
- 受保护的旧 `dango_box2d.dll` 原文件存在，SHA-256 为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。不重编或替换任何生产物理 DLL，不用统一模块冒充历史旧模块。
- `DesktopPhysicsSceneTests.Module` 固定读取 `artifacts/native-spike/physics-module-unified-build/Release/dango_physics_module.dll`；runtime 则从程序输出目录加载。实施时同时满足这两个位置，优先向当前缺失的夹具路径复制已核对模块，不为路径问题改测试断言。若目标已有不同字节，停止写入并核对归属，不能覆盖。
- 另外三个历史夹具 `physics-three-preview-build/Release/dango_physics_three_preview.dll`、`physics-module-build/Release/dango_physics_module.dll`、`collision-ratio-build/Release/dango_collision_review.dll` 当前缺失（均位于 `artifacts/native-spike/`）。只在需要运行依赖它们的历史测试时查找对应已验收来源；不得自动重建、混用版本或因此把全套门禁标为通过。当前统一模块已足以准备本批直接相关的验证，不能用历史夹具缺失跳过本批必需项。

## 2. 最小实现分工

以下文件为本批实施目标，现已落地。新类型均限于 Desktop 呈现，不引入 Core 动作协议、native API、网络、数据库或依赖。

| 文件/模块 | 具体职责与禁止越界 |
|---|---|
| 新增 `src/Dango.Desktop/Rendering/Dango/GaitController.cs` | 一个普通单实体控制器：成功步采样、资格/归属、相位、实际速度幅度、起停、取消与步态专属错误锁存。不持有物理 world、宿主窗口、计时器或随机源。 |
| 新增 `src/Dango.Desktop/Physics/Presentation/GaitPresentationFrame.cs` | 不可变描述符：实体 ID、随附原呈现帧的附件 Generation/StepIndex、Canvas DIP 锚点与正尺度 sx/sy；可选只读原因码供测试。缺省/空值表示单位变换，拒绝非有限值；不存 WPF 可变对象。控制器采样代际另行管理，不作为宿主拒绝旧安全帧的依据。 |
| 新增 `src/Dango.Desktop/Rendering/Dango/GaitGeometry.cs` | 将原可见曲线、眼睛和帽子几何投影到同一 DIP 空间，计算底部锚、形状资格与共享幅度上限；不改原始几何或物理边界。 |
| `Physics/Presentation/Box2DStackedContactPresentationWorld.cs` 内的 `StackedPhysicsPresentationFrame` 声明 | 仅增加默认空值的只读单实体 Gait 描述符，不修改同文件的旧物理 world 类。原 Bodies、AuthoritativeBounds、RenderOffsets、EntityIds 与颜色数据保持原义。 |
| `Rendering/Dango/StackedDangoSceneRuntime.cs` | Product 工厂启用能力，成功 Tick 只推进一次，附加描述符后完整发布；复用已有取消/实体/恢复接线。原物理、自主和附件算法不变。 |
| `Physics/Presentation/Attachments/AttachmentMotionController.cs`、`AttachmentPresentationFrame.cs` | 仅在需要避免重复采样时把当步已算出的不可变 `AttachmentShapeGeometry` 作为内部只读来源带出；不改眼帽参数、积分、轨道或表情仲裁。几何来源必须与当前附件成功步匹配，冻结的旧附件几何不可冒充当步身体。 |
| `Rendering/Dango/DangoHostFrameBinding.cs` | Apply 校验描述符属于随附帧及实体，把相同变换传给身体与独立帽；Bounds/HitTest 保持原实现。不与当前 runtime 采样代际/新宿主代际比较。未匹配实体/源帧、空描述符和原本没有步态的旧回退路径使用单位变换；合法旧安全帧保留自身描述符。 |
| `DeformableDangoVisual.xaml/.cs`、`DangoHatVisual.xaml/.cs` | 新增内部呈现组，按“原眼帽局部变换 → 步态变换”应用一次。透明根控件、Canvas 定位、调试骨架和 zzz 不变；ApplyFrame 的旧调用默认单位变换。隐藏或回退不得遗留前一实体的矩阵。 |
| 新增 `eng/tools/Dango.GaitSample/Dango.GaitSample.csproj`、`Program.cs` | 最小可见 Product 小样宿主，独立虚构设置，引用本任务项目；详见第 5 节。只用于打开和退出候选，不发展成通用评审面板。 |

`ShortWanderController`、`WanderVisibleBounds` 的驱动/安全算法、`DesktopPhysicsScene.Step`、`HostFrameClock/Loop/Scheduler`、原生代码和 `App.xaml.cs` 预期无需修改。若必须改这些模块，先解释无法从现有接线完成的具体原因，重新检查是否改变了真实移动或故障策略。

### 2.1 固定成功步内的顺序

1. 保留本步实际要消费的键盘意图和 `autonomousHorizontal`，仍调用原 `scene.Step` 一次；不从 Core 初始位置、节点 MaximumSpeed 或宿主 Canvas 差分采样。
2. 沿原次序生成身体及眼帽，发布原语义的 Bodies，并按原逻辑完成 `AdvanceWander`；其读取的轮廓不含步态，保持随机次数、方向选择和撤驱步号不变。
3. 步态结合“本步实际消费的输入”和“本步后是否仍有驱动归属”采样成功物理位置。自主刚进入 Driving 的一帧尚未消费自主输入，不能预演；当步决定撤驱则从当前显示形状开始收回，不额外推进一轮。
4. 计算候选形状与共享安全限幅，将 Gait 随该完整帧一次发布。每个宿主只应用结果；零步、重复回调和 Prepare 不推进步态。

键盘归属看 `Left != Right`，自主归属看实际消费且未被抢占的自主输入；左右同时按沿用原自主回填规则。`SetDirectionalControl` 会在每次宿主准备时重复调用，**不能把 gait.Cancel 塞进 CancelQuietIdle**，也不能每次 setter 都重建相位。普通同值输入是无动作观察，松键/反向只触发一次状态转换。

纯步态异常只记录步态自身安全码，不进入 `FrameFaultState/HasNewFrameFault`：这些原故障通道会让物理批次提前结束。步态失败统一输出单位描述符，物理继续按原策略；附件失败保留当步身体/冻结附件并取消步态；物理失败保留原安全帧及其步态姿态。

### 2.2 起停、几何与生命周期实现选择

- 状态采用未就绪/中性、行走、收步、步态故障这几个局部状态即可。相位每 60 DIP 一轮、上限 2 Hz，超出进度丢弃；速度迟滞/平滑、100 ms 地面资格、500 ms 中性参照及全部误差线沿用设计候选表。
- 首样、实体/宿主/坐标代际变化只建立速度基线。区分基础候选资格与完整步态资格：健康 Product 单体的成功步即允许读取当步原始身体几何，在无步态、无占用、低速稳定期间建立中性参照；不能先要求参照存在才允许采集它。压缩时不更新参照。支撑同时检查近地、竖向变化与有效底部，不把 IsGrounded 当稳定证明。
- 松键或撤驱冻结相位，将当时真实显示的尺度差向零单调收回；普通收步约 300 ms，350 ms 加一步内归零。快速再起从当前显示尺度和变化率缓接，可采用有界 Hermite 过渡；不能先清零，不能为平滑推迟真实输入。反向须出现新方向实际位移才进入。
- 身体资格几何在上述基础候选成功步读取，复用当步附件已采样曲线；完整眼帽投影与限幅只在非单位候选或收回需要时计算一次。测试另用解析 B-spline 极值作为独立真值。帽子可采用与既有帽型/最大材料变形相容的保守包络限幅，WPF 测试再核对三种帽子实际绘制几何；不每个部件重复生成曲线，也不新增无界缓存。
- 统一幅度系数约束全部可见点新增位移≤2 DIP、锚误差≤0.02 DIP、脚底增量≤0.25 DIP、脸内安全距离、既有命中矩形、视口及 Wallpaper overlay 裁切。先检验候选再整体减幅，最多进行固定次数搜索；原始几何也不安全时单位变换。帽根沿用已验收的嵌入校准，不将旧 `HatContactPose` 的表面贴合数值误当当前可见帽根必须零嵌入。
- `BeginLightPress` 请求短收步；Drag/Jump/Squash/离地/受压请求安全退出；普通 setter 只登记请求，下一完整成功步统一呈现，不在零步单改身体或帽子。跨段样本失效覆盖 `ClearPhysicalInput`、`SetViewport`、宿主中心变化、`UpdateWanderScreenWidth` 和宿主拥有者变更。
- **实际隐藏/Stop** 保留最后完整帧及其描述符，只使后续采样代际失效；恢复先完整重绑旧安全帧，再于首个成功步重建采样并收回，不因当前代际已变化而清掉旧合法姿态。**仍可见但禁用交互** 不暂停原物理，只停止新步态/相位，后续成功步收回已有形变。不得为了取消步态改变原宿主调度。
- Add 成功后步态退出；Remove 回单实体后重新取得资格；Select/拖动不得沿用旧归属。Regenerate 清故障、相位和中性参照。生命周期操作保留原 LastFrame 清除或保留语义，不为步态重置世界。

## 3. 实施顺序与逐步停止条件

执行结果以[本批验证记录](../verification/2026-09-22-gait-sample.md)为准。以下表格保留各步的完成要求。功能开关只作为内部测试注入，缺省关闭的中间实现不能用于用户观感验收；接线和安全门禁完成后才在候选 Product 启用。

| 步骤 | 修改与验证 | 完成条件与停止条件 |
|---|---|---|
| G0 基线与可失败断言 | 核对 DLL/SDK，准备独立输出；记录当前相关回归、真实自主与键盘左右速度/稳定轮廓。新增 Gait 测试入口，以可编译的单位实现得到红例；不把编译失败或缺 DLL 算作红例。 | A1 正例在单位实现下因没有形变失败，原物理对照可重复；依赖缺失、基线出现无法解释的新错误、候选资格让正常两路径永久进不来则先定位，不调低门槛继续。 |
| G1 控制器与统一几何 | 实现局部状态、位移波形、起停和共享限幅，暂不接真实宿主；同一原始几何上的时间/距离对照及错误锚点负例。 | A1–A5 的逻辑/几何层通过，含零进度、短按、单位形状松键、反向、10/20/40 ms、高速不补播；这不等于桌面可用。 |
| G2 runtime 与内部绘制 | 按第 2 节成功步接线，保留原 Bodies 和自主驱动输入；两个内部绘制组共用描述符，同时接齐基本拖跳压抑制，Product/Review 隔离。 | A6–A7、A10–A12 通过：三帽实际绘制、双宿主命中、native 完整快照对照、边缘驱动不变、零步不闪回。任何真实轨迹变化阻断后续。 |
| G3 全部退出与失败 | 接齐隐藏/恢复、可见禁用交互、坐标/代际、多体转换、打盹/轻反馈、步态专属故障与原附件/物理故障路径；完成小样宿主隔离/退出验证。 | A8–A9、A13–A15 及全部新增测试通过，无无限残留形变，无新物理暂停/步数变化；所有必需项完成后才可打开可见候选。 |
| G4 项目小样与人工停止点 | 对最终源码构建，运行受影响回归和 DLL/文档差异检查；打开 Product 单体小样，按设计观察清单看两宿主、自主/键盘及接管。 | 自动、实时、人工分别记录；用户看过后判断“自然/太轻/太强/哪里突兀”。未收到观感结论即待人工验收，不进入下一批，也不交付日常安装。 |

新测试收敛为 `GaitControllerTests`、`GaitGeometryTests`、`GaitRuntimeTests`、`GaitRenderingTests`、`GaitSampleIsolationTests` 五组；宿主连续性扩展现有 `HostPresentationContinuityTests`，不为每个验收编号建一套框架。

测试映射：Controller 对应 A1–A4/A8；Geometry 对应 A5/A6 的安全数学；Runtime 对应 A2/A8–A11/A13–A15；Rendering 与连续性对应 A6–A7/A12–A13；SampleIsolation 覆盖候选设置、Product 模式、退出与失败前不启动的保护。

真实物理对照由两个独立 runtime 使用相同初态、输入与显式随机序列；逐帧比较送入物理的输入和完整原始快照，并记录自主抽样次数、起止步号及停止距离。直接读取实际消费输入优先复用现有测试观察点；若必须加内部只读观察钩子，默认关闭且不得改变调用次数/异常策略，不新建可替换整个物理世界的生产架构。

至少实际验证五个错误实现被抓住：始终单位变换、按回调/按键时长计相位、只缩身体、零步清描述符、将步态后的边界反馈给自主驱动。另验证控制点假锚和步态故障截断物理批次。负例修改仅在当前任务局部进行并恢复，不重置工作树或留下故障开关；保留对应红/绿结果及失败断言名。

## 4. 验证命令与证据安排

下列为实施命令模板；本轮实际执行及结果见验证记录。从仓库根执行；先将 `$gaitModulePath` 解析为本轮核对过的绝对文件路径，将 `$gaitRunRoot` 解析为 `artifacts/gait-sample/<本轮唯一标识>` 下的全新目录，再定义其 `build`、`results` 子目录。不提交开发机路径，不复用旧输出。各命令必须逐个检查退出码后再继续。

```powershell
$gaitBuildRoot = Join-Path $gaitRunRoot 'build'
$gaitResultsRoot = Join-Path $gaitRunRoot 'results'
dotnet build Dango.slnx -c Release --artifacts-path $gaitBuildRoot "-p:DangoPhysicsModulePath=$gaitModulePath"

# G0 红例和每步绿例应使用不同 results 子目录；实现后才存在 Gait 测试。
dotnet test tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj -c Release --no-build --artifacts-path $gaitBuildRoot --filter 'FullyQualifiedName~Gait|FullyQualifiedName~HostPresentationContinuityTests|FullyQualifiedName~HostFrameSchedulerTests' --results-directory "$gaitResultsRoot/gait-host" --logger 'trx;LogFileName=gait-host.trx'

# 最终受影响回归；若源码或测试改动，先重新构建再使用 --no-build。
dotnet test tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj -c Release --no-build --artifacts-path $gaitBuildRoot --filter 'FullyQualifiedName~ProductEntryRuntimeTests|FullyQualifiedName~ShortWander|FullyQualifiedName~Light|FullyQualifiedName~QuietIdle|FullyQualifiedName~IdleDoze|FullyQualifiedName~Attachment|FullyQualifiedName~Hat|FullyQualifiedName~Eye|FullyQualifiedName~FaceLayout|FullyQualifiedName~DesktopPhysicsSceneTests|FullyQualifiedName~HostFrame' --results-directory "$gaitResultsRoot/affected" --logger 'trx;LogFileName=affected.trx'
dotnet test tests/Dango.Core.Tests/Dango.Core.Tests.csproj -c Release --no-build --artifacts-path $gaitBuildRoot --results-directory "$gaitResultsRoot/core" --logger 'trx;LogFileName=core.trx'
git diff --check
```

`Dango.Desktop.csproj` 的 `DangoPhysicsModulePath` 负责复制统一模块到输出；不能以 `DANGO_PHYSICS_MODULE_PATH` 替代 Desktop 历史夹具路径。构建输出放在当前仓库内，是因为 `TestRepository` 向上寻找 `global.json` 和 `AGENTS.md`。需要图片/JSON 证据时可在对应子进程使用 `DANGO_TEST_EVIDENCE_ROOT` 指向本轮全新目录；该工具使用 CreateNew，同一个截图测试重跑也应换目录。退出后恢复本轮设置的进程环境，不改系统环境变量。

最终受影响门禁必须有实际执行清单、通过/失败/跳过数；仅有命令成功不能证明筛选覆盖了计划类别。需要时补跑完整 Desktop 及 Physics 常规（排除 `DangoPhysicsStabilityTests`），依赖第 1 节对应历史 DLL；无法运行的部分逐类登记未验证。保留旧 `dango_box2d_self_bodies_create` 导出失败，不删除或放宽它。长测、第二 DPI、多屏/兼容和 GPU 呈现仍按原安排延期，不扩大本次观察时长来冒充完成专项。

证据目录至少保留：基线版本/DLL 指纹、红例、绿例、真实输入/物理对照摘要、宿主内部矩阵/最终眼帽几何观测、最终构建指纹、人工观察结论及退出记录。只记录 Dango 自身数据和安全原因码，不采集其他应用画面、输入或用户桌面内容。新增步态的 A1–A15 必需自动门禁缺一项，不得以“继承基线失败”归类后开放候选。

## 5. 可直接观看的最小小样入口

现有普通 `App.xaml.cs` 没有独立 settings/instance 参数，固定单实例身份会唤醒日常实例；两个 review 参数则进入 DeveloperReview。不能写不存在的启动参数，也不能把 Review 当成 Product 步态验收。

因此新增的小样项目只承担启动隔离：

1. 使用本任务的 `Dango.Desktop` 项目引用、同一 Product `DangoApplicationHost` 和真实 native DLL；由 `[STAThread]` 创建 WPF Application，复用现有构造注入 `settingsFile/nativeLibraryPath`，两个 review 参数均为 false。参考现有 [PerformanceProbe](../../eng/tools/Dango.PerformanceProbe/Program.cs) 的窄反射构造方式，不暴露新的产品公开 API，也不携带它的自动操作/长测逻辑。
2. 拟定唯一位置参数为**新候选会话目录**，参数已实现。程序只接受当前仓库 `artifacts/gait-sample/` 内不存在的新目录，拒绝日常设置/安装路径及已有目录；创建虚构设置，默认 Window、允许交互、启用既有键盘控制，保留真实自主等待与距离。目录校验与设置创建在宿主实例化之前完成，不读取日常设置。
3. 从自身项目输出加载已核对的统一模块，记录模块/程序集指纹与本候选 PID；缺 DLL、输出不匹配或 Product 未 Ready 时报告失败并释放自己，不转入另一后端。仅允许一个小样实例，使用独立的候选互斥身份（如 `Local\Dango.GaitSample`），重复启动直接退出且不唤醒任何实例；候选不获取日常 App 的固定身份，也不停止日常进程。互斥先于会话目录创建，避免失败启动残留空目录。
4. 启动后显示现有控制中心，标明这是步态小样，使用其中原有的 Window/Wallpaper、帽型、隐藏和退出操作；不添加调参滑条、强制散步、随机种子或循环表演按钮。用户直接操作实际团子。没有自身的步态定时器，也不自动注入输入。
5. 正常退出沿用 Product 退出语义，最终 Dispose 本候选 host，记录候选退出；异常时也只清理自身资源。证据与虚构设置保留，不删除目录；日常实例和安装保持不变。隐藏恢复/模式切换只针对本候选。

计划中的构建入口如下；G3 完成之前不会运行可见候选：

```powershell
dotnet build eng/tools/Dango.GaitSample/Dango.GaitSample.csproj -c Release --artifacts-path $gaitBuildRoot "-p:DangoPhysicsModulePath=$gaitModulePath"
```

构建后定位该项目实际产出的 `Dango.GaitSample.exe`，验证它引用的产品程序集与最终测试候选一致，再以全新会话目录作为一个参数启动并记录 PID。不要假设 `--artifacts-path` 下的输出后缀；不要运行旧 bin 中同名 EXE。这个启动方式证明的是**本任务项目代码下真实 Product 宿主的步态与交互**，不等于普通 App 单实例、安装更新或日常快捷方式已经完成验收。

独立设置并不隔离 Wallpaper 键盘：现有 [WorkerWKeyboardInputBridge](../../src/Dango.Desktop/DesktopHost/WorkerW/WorkerWKeyboardInputBridge.cs) 在桌面前台使用键盘 hook，并会消费匹配按键；日常 Wallpaper 实例可能与候选抢键。人工 Wallpaper 键盘验收前，只读核对已知 Dango 实例及其当前模式/键盘状态；发现冲突或无法确认时，先完成 Window/非键盘观察，将 Wallpaper 键盘列为待验证，待用户自行暂退或关闭冲突实例的桌面键盘控制后再检查。不自动结束日常实例，也不为此新增产品输入协调协议；自动 WPF/native 夹具结果不能顶替这一项实时验收。

人工观察按设计第 8 节执行：两宿主分别看至少三次自主散步、键盘持续/短按/反向、三种帽子、点击拖跳压、隐藏恢复与模式往返。缺少可用真实阻挡时单列未验证，不新建障碍功能凑验收；自动零进度反例仍必须通过。第一轮只请用户描述看得到的效果，若太轻/太强，先调整同一小样中的候选表现，不扩大到真实速度或下一批功能；超过 2 DIP 或修改安全失败线时须说明理由并确认修订。

## 6. 交付与权限记录

用户在计划交付后回复“继续”，据此授权本批编码、验证、独立项目小样启动和本地提交。已实现共享步态和最小 Product 小样入口；实际测试、进程及未验证边界见验证记录。日常安装未变更。

开始实施后的本批完成条件：设计 A1–A15 所需自动证据、实际双宿主观察、用户人工观感结论均具备；未通过或未观察的项目如实记录，不宣布整个 Phase 1 通过。纯小样提交不授权合并 main、推送、部署、更新安装、删除文件/工作树或开启下一批。

用户本次已确认的两个取舍不再重复询问。本次已按 G0–G4 推进常规细节和验证；无需再次逐条讨论候选参数，实际观感留在小样人工停止点确认。
