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
