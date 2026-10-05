using System.Windows.Threading;
using Dango.Desktop.DesktopHost.Win32;

namespace Dango.Desktop.DesktopHost.WorkerW;

internal readonly record struct WallpaperWheelHookData(WallpaperWheelPoint Point, uint MouseData, uint Flags, bool HasModifier = false);

internal interface IWallpaperWheelInputPlatform
{
    nint Install(NativeMethods.LowLevelMouseProcedure procedure);
    bool Uninstall(nint hook);
    nint CallNext(nint hook, int code, nint wordParameter, nint longParameter);
    WallpaperWheelHookData Read(nint longParameter);
    WallpaperWheelInputEnvironment Inspect(WallpaperWheelInputContext context, WallpaperWheelPoint point);
}

internal sealed class WallpaperWheelInputBridge : IDisposable
{
    internal const int WmMouseWheel = 0x020A;
    internal const int WmMouseHorizontalWheel = 0x020E;
    internal const int MaximumPendingPoints = 32;
    private const uint Injected = 0x01;
    private readonly Dispatcher dispatcher;
    private readonly Func<WallpaperWheelInputContext?> context;
    private readonly Action<int> applyRawDelta;
    private readonly Action clearPendingInput;
    private readonly IWallpaperWheelInputPlatform platform;
    private readonly TimeProvider timeProvider;
    private readonly NativeMethods.LowLevelMouseProcedure procedure;
    private readonly DispatcherTimer timer;
    private readonly List<PendingWheel> pending = [];
    private nint hook;
    private long revision;
    private bool rejected;
    private bool disposed;
    private bool stopping;
    private bool active;
    private RecentWheel? recent;
    private long recentTimestamp;
    private long lastInspectionTimestamp;

    internal bool IsHookReleasePending => hook != 0 && !active;

    internal WallpaperWheelInputBridge(Dispatcher dispatcher, Func<WallpaperWheelInputContext?> context,
        Action<int> applyRawDelta, Action clearPendingInput)
        : this(dispatcher, context, applyRawDelta, clearPendingInput, new NativeWallpaperWheelInputPlatform()) { }

    internal WallpaperWheelInputBridge(Dispatcher dispatcher, Func<WallpaperWheelInputContext?> context,
        Action<int> applyRawDelta, Action clearPendingInput, IWallpaperWheelInputPlatform platform,
        TimeProvider? timeProvider = null)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.applyRawDelta = applyRawDelta ?? throw new ArgumentNullException(nameof(applyRawDelta));
        this.clearPendingInput = clearPendingInput ?? throw new ArgumentNullException(nameof(clearPendingInput));
        this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        dispatcher.VerifyAccess();
        procedure = OnMouse;
        timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += OnTimerTick;
        dispatcher.ShutdownStarted += OnDispatcherShutdown;
    }

    internal void Start()
    {
        dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (stopping || active || dispatcher.HasShutdownStarted) return;
        revision++;
        try
        {
            // A failed release keeps its delegate rooted and must be resolved before another hook is installed.
            if (hook != 0)
            {
                if (!platform.Uninstall(hook)) { clearPendingInput(); return; }
                hook = 0;
            }
            hook = platform.Install(procedure);
            active = hook != 0;
        }
        catch { clearPendingInput(); return; }
        if (hook == 0) clearPendingInput();
    }

    internal void Stop()
    {
        dispatcher.VerifyAccess();
        if (stopping) return;
        stopping = true;
        active = false;
        revision++;
        timer.Stop();
        pending.Clear();
        recent = null;
        rejected = false;
        var previousHook = hook;
        try
        {
            if (previousHook != 0 && platform.Uninstall(previousHook)) hook = 0;
        }
        finally
        {
            try { clearPendingInput(); }
            finally { stopping = false; }
        }
    }

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (disposed) return;
        disposed = true;
        try { Stop(); }
        finally
        {
            timer.Tick -= OnTimerTick;
            dispatcher.ShutdownStarted -= OnDispatcherShutdown;
        }
    }

    private nint OnMouse(int code, nint wordParameter, nint longParameter)
    {
        try
        {
            if (code >= 0 && active && !disposed && !dispatcher.HasShutdownStarted &&
                wordParameter == WmMouseWheel)
            {
                var data = platform.Read(longParameter);
                if ((data.Flags & Injected) == 0)
                {
                    var captured = context();
                    var delta = WallpaperWheelInputPolicy.DecodeRawDelta(data.MouseData);
                    if (data.HasModifier || !WallpaperWheelInputPolicy.IsContextEligible(captured) ||
                        !WallpaperWheelInputPolicy.ContainsDevicePoint(captured!.ScreenBounds, data.Point))
                    {
                        RejectPending();
                    }
                    else if (delta != 0 && !rejected)
                    {
                        if (pending.Count != 0 && pending[^1] is var previous &&
                            previous.Context == captured && previous.Point == data.Point &&
                            Math.Sign(previous.Delta) == Math.Sign(delta))
                        {
                            pending[^1] = previous with { Delta = checked(previous.Delta + delta) };
                        }
                        else if (pending.Count < MaximumPendingPoints)
                        {
                            pending.Add(new PendingWheel(captured!, data.Point, delta, revision));
                        }
                        else RejectPending();
                        ScheduleCheck(recentOnly: false);
                    }
                }
            }
        }
        catch
        {
            RejectPending();
        }
        // The desktop observer never consumes input, including exceptions and rejected events.
        return platform.CallNext(hook, code, wordParameter, longParameter);
    }

    private void RejectPending()
    {
        pending.Clear();
        recent = null;
        revision++;
        rejected = true;
        if (!dispatcher.HasShutdownStarted) ScheduleCheck(recentOnly: false);
    }

    private void OnTimerTick(object? sender, EventArgs args) => FlushPendingInput();

    private void OnDispatcherShutdown(object? sender, EventArgs args) => Stop();

    // This same command boundary is used by the timer and the deterministic platform tests.
    internal void FlushPendingInput()
    {
        dispatcher.VerifyAccess();
        timer.Stop();
        if (disposed || !active || dispatcher.HasShutdownStarted)
        {
            pending.Clear();
            recent = null;
            rejected = false;
            return;
        }
        if (rejected)
        {
            rejected = false;
            ClearEligibility();
            return;
        }
        var batch = pending.ToArray();
        pending.Clear();
        try
        {
            foreach (var wheel in batch)
            {
                try
                {
                    if (!IsEligible(wheel.Context, wheel.Point, wheel.Revision))
                    {
                        ClearEligibility();
                        return;
                    }
                    applyRawDelta(wheel.Delta);
                    recent = new RecentWheel(wheel.Context, wheel.Point, wheel.Revision);
                    recentTimestamp = timeProvider.GetTimestamp();
                    lastInspectionTimestamp = recentTimestamp;
                }
                catch
                {
                    ClearEligibility();
                    return;
                }
            }
            if (batch.Length == 0 && recent is { } last)
            {
                var now = timeProvider.GetTimestamp();
                if (timeProvider.GetElapsedTime(recentTimestamp, now) >= TimeSpan.FromMilliseconds(250))
                    ClearEligibility();
                else if (timeProvider.GetElapsedTime(lastInspectionTimestamp, now) >= TimeSpan.FromMilliseconds(50))
                {
                    lastInspectionTimestamp = now;
                    try
                    {
                        if (!IsEligible(last.Context, last.Point, last.Revision)) ClearEligibility();
                    }
                    catch { ClearEligibility(); }
                }
            }
        }
        finally
        {
            if (!disposed && active && !dispatcher.HasShutdownStarted)
            {
                if (pending.Count != 0 || rejected) ScheduleCheck(recentOnly: false);
                else if (recent is not null) ScheduleCheck(recentOnly: true);
            }
        }
    }

    private bool IsEligible(WallpaperWheelInputContext captured, WallpaperWheelPoint point, long capturedRevision) =>
        capturedRevision == revision && WallpaperWheelInputPolicy.IsSamePresentation(captured, context()) &&
        WallpaperWheelInputPolicy.ShouldApply(captured, context(), point, platform.Inspect(captured, point)) &&
        WallpaperWheelInputPolicy.IsSamePresentation(captured, context()) && capturedRevision == revision &&
        !disposed && active && !dispatcher.HasShutdownStarted;

    private void ClearEligibility()
    {
        revision++;
        pending.Clear();
        recent = null;
        rejected = false;
        clearPendingInput();
    }

    private void ScheduleCheck(bool recentOnly)
    {
        var interval = TimeSpan.FromMilliseconds(recentOnly ? 50 : 20);
        if (timer.Interval != interval) timer.Interval = interval;
        timer.Start();
    }

    private sealed record PendingWheel(WallpaperWheelInputContext Context, WallpaperWheelPoint Point, int Delta, long Revision);
    private sealed record RecentWheel(WallpaperWheelInputContext Context, WallpaperWheelPoint Point, long Revision);
}

internal sealed class NativeWallpaperWheelInputPlatform : IWallpaperWheelInputPlatform
{
    public nint Install(NativeMethods.LowLevelMouseProcedure procedure) => NativeMethods.InstallRoomMouseHook(procedure);
    public bool Uninstall(nint hook) => NativeMethods.UninstallRoomMouseHook(hook);
    public nint CallNext(nint hook, int code, nint wordParameter, nint longParameter) =>
        NativeMethods.CallNextKeyboardHook(hook, code, wordParameter, longParameter);
    public WallpaperWheelHookData Read(nint longParameter) => NativeMethods.ReadRoomMouseInput(longParameter);
    public WallpaperWheelInputEnvironment Inspect(WallpaperWheelInputContext context, WallpaperWheelPoint point) =>
        NativeMethods.InspectRoomWheelInput(context, point);
}
