# Dango TimerProbe

用于区分当前低回调频率中的处理器耗时和回调之间的等待。它是一次性诊断工具，不是性能门禁或长期稳定性基线。

## 构建与运行

先完成现有长测，再运行此工具；合成忙工作与额外的可见 Product 宿主会改变并存负载。构建只引用已有发布包，不重建或修改产品代码。`DesktopPackagePath` 由调用方传入；保持输入包和本工具的 WPF/.NET 版本一致。当前固定 `RuntimeFrameworkVersion=10.0.11`。

```powershell
dotnet publish eng/tools/Dango.TimerProbe/Dango.TimerProbe.csproj -c Release -r win-x64 --self-contained true -p:DesktopPackagePath="$packagePath" -o artifacts/performance/timer-probe-package

$timerProbe = (Resolve-Path artifacts/performance/timer-probe-package/Dango.TimerProbe.exe).Path
$timerOutput = [IO.Path]::GetFullPath('artifacts/performance/timer-diagnostic-run')
$timerProcess = Start-Process -FilePath $timerProbe -ArgumentList ('"' + $timerOutput + '"') -PassThru -WindowStyle Hidden
```

输出目录必须不存在。工具拒绝向日常 `%LOCALAPPDATA%/Programs/Dango` 安装目录或 `%LOCALAPPDATA%/Dango` 设置目录写入。不要把 `-WindowStyle Hidden` 误解为 Product 宿主不可见：前两段没有产品窗口；后两段会显示独立测试团子约两分钟。工具只释放自己创建的宿主，不激活日常单实例入口，也不关闭其他进程。

顺序与真实时间：

1. 无工作体的 `DispatcherTimer(Render, 1/60 秒)`，至少 60 秒。
2. 相同计时器，每次回调使用 `Stopwatch` 忙工作至少 5 ms，至少 60 秒。该段为明确的合成调度对照，不能代表产品行为或容量。
3. 已发布 Product Wallpaper 宿主，虚构设置、单团子、默认交互、键盘控制关闭；至少 60 秒预热。
4. 同一产品宿主连续测量至少 60 秒。阶段之间不重设产品 timer 或物理步长。

正常运行约四分钟加构造/清理时间。每段按单调墙钟计时；延迟只会延长。没有缩短时长的参数。运行完成退出后再读取产物；`complete.json` 仅表示工具四段运行并正常清理，**不代表达到 60 FPS、CPU 门槛或长期稳定性通过**。

产品测量结束后，工具仅一次通过 `Dispatcher.BeginInvoke(Send)` 排队执行自有宿主清理和退出，使清理在当前产品回调返回后运行，并避免连续 Rendering 工作饿死 Background 收尾。该优先级只用于测量窗口结束后的清理；采样、心跳门槛、产品调度及帧统计保持原样。成功保存诊断文件不表示产品性能通过。

## 调度入口与原处理器边界

旧包（schema 1）使用 `MethodInfo.CreateDelegate<EventHandler>(workerWWindow)` 绑定原 `OnMotionTimerTick`，仅在测试进程中移除原订阅并添加调用它一次的包装器。原 `sender` / `EventArgs` 保留；入口出口取 `Stopwatch` 时间戳。退出恢复订阅并释放自己的宿主。

候选包（schema 2）反射 `motionScheduler`，只订阅其 `CallbackCompleted` 事件并读取 `Snapshot`。工具不替换产品处理器，也不订阅 WPF 静态 Rendering 事件。处理器耗时采用产品记录的 `LastStartedTimestamp` / `LastFinishedTimestamp`；每次原始回调记录成功步、实际帧应用、重复回调、刷新、故障和丢弃时间的计数差。`LifecycleDroppedTicks` 单列正常 Stop/宿主切换取消的未推进时间，与过载、长停顿、故障丢时分开；正常隐藏或退出产生该计数不表示计时失败。只有 `AppliedFrames` 增加 1 的回调进入应用帧间隔；多个物理步不会冒充多帧，故障回调不会冒充成功。未知或缺失候选契约直接失败。

产品源码、程序集、物理 DLL、计时参数与渲染后端均不被工具修改。旧包装器的间接调用与所有版本的反射、快照转换、列表写入产生观测开销；完成通知后的开销主要进入出口到下一入口。工具不逐帧写盘、不注入输入、不采集窗口标题或内容。新旧测量分别保存，不改写旧记录。

## 产物与解释

- `environment.json`：操作系统、显示配置、WPF render tier、运行时、DPI、产品/物理/探针/WPF 程序集 SHA-256，及诊断限制；顶层 `schema` / `productEntry` 明确产品入口。
- `timings.json`：每段实际时长、回调频率、均值/P50/P95/最大值，以及全部原始入口/出口 timestamp（相对进程 origin，单位为 `Stopwatch.Frequency` ticks）。候选另外保存每回调 counters、阶段计数和实际应用帧完成间隔；成功物理步、普通帧、取消刷新分别记账。原始数据全程在内存缓冲，结束后统一写入。
- `disposal.json`：本宿主 runtime/scene/tray、timer、剩余窗口、附件内部条目数量与路由器 disposed 状态；候选的 `schedulerFinalCounters` 保存 Dispose 后最终计数，包括最后 Stop 的生命周期丢时。
- `complete.json`：四段完成且清理未抛异常；进程退出码仍须由调用方核实。
- `failure.json` 或 `watchdog-failure.json`：错误类型或 15 秒无回调心跳。看门狗只退出本工具；硬退出时没有完整缓冲数据，不能判通过。系统睡眠/全局调度停顿造成的缺口不能直接归因于产品崩溃。

连续两个回调之间：`入口到入口 = 前一回调处理器耗时 + 前一出口到本次入口`。同一组相邻配对的平均值可相加：使用 `pairedPreviousHandlerElapsedMs` 的前 N−1 个处理器样本，与 N−1 个 `exitToNextEntryMs` 配对；`handlerElapsedMs` 则保留全部 N 个处理器样本。独立 P95 不能相加。处理器耗时是墙钟耗时，包含抢占和同步原生调用，并不等于 CPU 时间；渲染线程及延迟布局/呈现不全在该区间。出口到下一入口还包含探针记账、WPF 重排 timer、延迟工作、计时等待和 dispatcher 队列等待，最多支持队列层面的归因推断，不是纯 OS 调度延迟。回调频率不是 GPU 呈现 FPS。

前两段没有产品窗口/渲染工作，是调度机制对照；不同段的桌面并存负载可能不同。若空 timer 已有相近低频，支持继续调查调度；若产品处理器耗时明显增大，支持再调查具体调用栈。单次结果不能证明某一层是唯一根因。

## 可选的帧阶段诊断

对包含可选时序契约的整合候选，在原输出目录参数后添加 `--frame-stages`：

```powershell
$timerProcess = Start-Process -FilePath $timerProbe -ArgumentList ('"' + $timerOutput + '" --frame-stages') -PassThru -WindowStyle Hidden
```

此开关仅在独立诊断进程中将调度器 `DetailedTimingEnabled` 设为 true，结束订阅时恢复原值。默认关闭，PerformanceProbe 原有双参数观察器不启用细粒度记录。关闭时调度器不增加高频时间戳读取或逐帧堆分配。没有此契约的旧包显式失败，不悄悄降级成不完整阶段数据。原四阶段、预热时间、退出和文件名保持不变；`schema=2` 原字段保留，新增字段通过 `stageTimingVersion=1` 标识。

`timings.json` 每条产品 `rawTicks` 新增 `stageTiming`；合成阶段或未开启时为 null。它记录：

- `renderingTimeTicks`：WPF `RenderingEventArgs.RenderingTime.Ticks`，单位为100ns，属于WPF时间轴，不可与Stopwatch时间戳直接相减。
- `schedulerStartedTimestamp`、`startedTimestamp`、`finishedTimestamp` 及阶段边界：相对本探针origin，单位为 `stopwatchFrequency` ticks。`prepare`仅含原prepare委托；`loop`包含原input构造及整批固定步runtime推进；`apply`包含原帧应用委托，也记录取消/不健康帧刷新，但不含后续WPF布局或GPU呈现。
- `previousFinishedTimestamp`、`previousObservedExitToEntryMs`：同一运行代、连续启用观测时的前次退出及间隔；跨预热/测量分段仍连续，Start/Stop重启或重新启用观测后首条为null。该间隔包含观察器记账和调度等待。
- `requestedSteps`、`successfulSteps`、`remainderBeforeTicks`、`remainderAfterTicks`：计划步数、实际成功步数及入口/出口余量，余量以TimeSpan ticks计。`sameGenerationAtExit=false`时出口余量为null，避免把重启后新时钟余量归给旧批次。
- 未执行阶段的起止与耗时为null，不写成0；阶段抛出异常仍保留结束边界，并标 `threw=true`。`refresh=true`表示刷新路径，不能计作健康应用。原counters继续负责重复回调、故障、过载、暂停和生命周期丢时，不由新增字段改写。

`productStartEntryTimestamp` / `productStartExitTimestamp` 标出host.Start调用；其中发生的重入回调单独保存在 `startupCallbacks`。Start返回后的首批回调属于原 `product-warmup`。若Start内部没有回调，列表为空是正常事实，不能补造首帧。调度器clock.Start位置及首次推进策略均未改变。

离线分析先核对完整/错误/退出记录，再分开统计startup、预热最初数秒和稳定测量。对每个连续运行代检查0/1/多步分布、相邻RenderingTime与入口间隔、余量接近固定步边界的0步事件；用原AppliedFrames筛选应用完成间隔，不能将多物理步或零步旧帧算作多帧。对启动过载配对查看前一prepare/loop/apply耗时及出口到入口间隔；阶段耗时不含全部回调工作，三段之和不应被强制当成handler耗时。阶段观测和反射增加额外成本，必须与正式轻采样串行分开，不据此降低60FPS或资源门禁。
