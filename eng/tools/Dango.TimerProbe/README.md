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

## 原处理器包装边界

工具使用 `MethodInfo.CreateDelegate<EventHandler>(workerWWindow)` 绑定原 `OnMotionTimerTick`，从此测试进程的 timer 移除同一方法/目标的原订阅，再添加包装器。包装器保持原 `sender` 与 `EventArgs`，每次只调用原处理器一次。处理器入口、出口各取 `Stopwatch` 时间戳，随后向预分配列表记录结构体。原处理器异常仍进入本工具的异常处理；不会吞掉并继续计作成功帧。退出时恢复原订阅并 Dispose 自己的宿主。

产品源码、程序集、物理 DLL、计时器间隔/优先级、物理算法和渲染后端均不变。包装器增加的两个时间戳、调用间接层以及回调后的缓冲写入会产生很小但非零的观测开销；后两项主要包含在“出口到下一入口”中。合成空段也包括观测开销。工具不逐帧写盘、不订阅 `CompositionTarget.Rendering`、不注入输入、不采集窗口标题或内容。

## 产物与解释

- `environment.json`：操作系统、显示配置、WPF render tier、运行时、DPI、产品/物理/探针/WPF 程序集 SHA-256，及诊断限制。
- `timings.json`：每段实际时长、回调频率、均值/P50/P95/最大值，以及全部原始入口/出口 timestamp（相对进程 origin，单位为 `Stopwatch.Frequency` ticks）。原始数据全程在内存缓冲，结束后统一写入。
- `disposal.json`：本宿主 runtime/scene/tray、timer、剩余窗口、附件内部条目数量与路由器 disposed 状态。
- `complete.json`：四段完成且清理未抛异常；进程退出码仍须由调用方核实。
- `failure.json` 或 `watchdog-failure.json`：错误类型或 15 秒无回调心跳。看门狗只退出本工具；硬退出时没有完整缓冲数据，不能判通过。系统睡眠/全局调度停顿造成的缺口不能直接归因于产品崩溃。

连续两个回调之间：`入口到入口 = 前一回调处理器耗时 + 前一出口到本次入口`。同一组相邻配对的平均值可相加：使用 `pairedPreviousHandlerElapsedMs` 的前 N−1 个处理器样本，与 N−1 个 `exitToNextEntryMs` 配对；`handlerElapsedMs` 则保留全部 N 个处理器样本。独立 P95 不能相加。处理器耗时是墙钟耗时，包含抢占和同步原生调用，并不等于 CPU 时间；渲染线程及延迟布局/呈现不全在该区间。出口到下一入口还包含探针记账、WPF 重排 timer、延迟工作、计时等待和 dispatcher 队列等待，最多支持队列层面的归因推断，不是纯 OS 调度延迟。回调频率不是 GPU 呈现 FPS。

前两段没有产品窗口/渲染工作，是调度机制对照；不同段的桌面并存负载可能不同。若空 timer 已有相近低频，支持继续调查调度；若产品处理器耗时明显增大，支持再调查具体调用栈。单次结果不能证明某一层是唯一根因。
