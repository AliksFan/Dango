using System.Windows.Threading;
using Dango.Desktop.DesktopHost.Win32;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace Dango.Desktop.AppShell.Rooms;

internal readonly record struct DesktopPetPointerSample(Point ScreenPoint, bool Shift,
    bool HasForbiddenModifier, uint Timestamp);

// A Dispatcher-produced snapshot of the same body, projection and pointer clamp
// used by the room driver. The hook performs only comparisons against this value.
internal readonly record struct DesktopPetPointerGestureGuard(Rect ScreenBounds,
    double MaximumVerticalDistance, Rect? PointerClampBounds = null)
{
    internal bool IsValid => Finite(ScreenBounds) && ScreenBounds.Width > 0 && ScreenBounds.Height > 0 &&
        double.IsFinite(MaximumVerticalDistance) && MaximumVerticalDistance > 0 &&
        (PointerClampBounds is null || Finite(PointerClampBounds.Value));

    internal Point ClampInput(Point point) => PointerClampBounds is { } clamp
        ? new(Math.Clamp(point.X, clamp.Left, clamp.Right), Math.Clamp(point.Y, clamp.Top, clamp.Bottom)) : point;

    internal bool Allows(Point origin, Point point)
    {
        if (!IsValid || !double.IsFinite(origin.X) || !double.IsFinite(origin.Y) ||
            !double.IsFinite(point.X) || !double.IsFinite(point.Y)) return false;
        var qualified = ClampInput(point);
        return ScreenBounds.Contains(qualified) && Math.Abs(qualified.Y - origin.Y) <= MaximumVerticalDistance;
    }

    private static bool Finite(Rect rectangle) => !rectangle.IsEmpty &&
        double.IsFinite(rectangle.X) && double.IsFinite(rectangle.Y) &&
        double.IsFinite(rectangle.Right) && double.IsFinite(rectangle.Bottom);
}

internal interface IDesktopPetPointerGesturePlatform
{
    nint Install(NativeMethods.LowLevelMouseProcedure callback);
    bool Uninstall(nint hook);
    nint Next(nint hook, int code, nint message, nint data);
    DesktopPetPointerSample Read(nint data);
}

// Supplements background-window capture for one gesture that began on our painted body.
// The hook observes only move/release; physics and layout stay on the room Dispatcher.
internal sealed class DesktopPetPointerGestureBridge : IDisposable
{
    private static readonly object releasePendingLock = new();
    private static readonly HashSet<DesktopPetPointerGestureBridge> releasePending = [];
    private readonly Dispatcher dispatcher;
    private readonly Func<bool> canContinue;
    private readonly Action<Point, bool> move;
    private readonly Action<Point, bool> release;
    private readonly Action cancel;
    private readonly IDesktopPetPointerGesturePlatform platform;
    private readonly NativeMethods.LowLevelMouseProcedure callback;
    private readonly DispatcherTimer releaseRetry;
    private nint hook;
    private long generation;
    private bool active, disposed, queued, releaseQueued, cancelQueued, petBranch;
    private Sample? pendingMove, farthestMove, verticalMove, pendingRelease;
    private Point origin;
    private double greatestDistanceSquared, greatestVerticalDistance;
    private long sampleSequence;
    private uint? firstMoveTimestamp, lastMoveTimestamp, releaseTimestamp;
    private DesktopPetPointerGestureGuard? gestureGuard;
    internal bool IsListening => active && !disposed && hook != 0;
    internal bool IsHookReleasePending => !active && hook != 0;

    internal DesktopPetPointerGestureBridge(Dispatcher dispatcher, Func<bool> canContinue,
        Action<Point, bool> move, Action<Point, bool> release, Action cancel,
        IDesktopPetPointerGesturePlatform? platform = null)
    {
        this.dispatcher = dispatcher;
        this.canContinue = canContinue;
        this.move = move;
        this.release = release;
        this.cancel = cancel;
        this.platform = platform ?? new NativeDesktopPetPointerGesturePlatform();
        dispatcher.VerifyAccess();
        callback = OnMouse;
        releaseRetry = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        { Interval = TimeSpan.FromMilliseconds(100) };
        releaseRetry.Tick += RetryRelease;
        dispatcher.ShutdownStarted += OnShutdown;
    }

    internal bool Start(bool shift = false, Point origin = default, DesktopPetPointerGestureGuard? guard = null)
    {
        dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsListening) return true;
        if (dispatcher.HasShutdownStarted || !TryReleaseHook() || !canContinue() ||
            shift && guard is { IsValid: false }) return false;
        lock (releasePendingLock) if (releasePending.Count != 0) return false;
        ++generation;
        ClearSamples();
        queued = releaseQueued = cancelQueued = false;
        petBranch = shift;
        this.origin = origin;
        gestureGuard = guard;
        hook = platform.Install(callback);
        active = hook != 0;
        return active;
    }

    internal void Stop()
    {
        dispatcher.VerifyAccess();
        active = false;
        ++generation;
        ClearSamples();
        queued = releaseQueued = cancelQueued = false;
        TryReleaseHook();
    }

    internal void UpdateGuard(DesktopPetPointerGestureGuard guard)
    {
        dispatcher.VerifyAccess();
        if (!IsListening || !petBranch) return;
        gestureGuard = guard;
        if (!guard.IsValid) { cancelQueued = true; QueueDrain(); }
    }

    // Suppress already-observed physical WM events even when a newer hook sample
    // overtook them or this moving HWND changed its origin. The interval is local
    // to this gesture; signed uint differences also handle the system-clock wrap.
    internal bool MatchesNativeMessage(int message, uint timestamp) => IsListening &&
        (message == 0x0200 && firstMoveTimestamp is { } first && lastMoveTimestamp is { } last &&
            unchecked((int)(timestamp - first)) >= 0 && unchecked((int)(last - timestamp)) >= 0 ||
         message == 0x0202 && releaseTimestamp == timestamp);

    private nint OnMouse(int code, nint message, nint data)
    {
        var currentHook = hook;
        try
        {
            if (code >= 0 && IsListening && !releaseQueued && message is 0x0200 or 0x0202)
            {
                var input = platform.Read(data);
                cancelQueued |= input.HasForbiddenModifier || petBranch && !input.Shift;
                // An invalid stroke is final. Later valid samples in this coalesced
                // batch cannot revive it or play a pet reaction before cancellation.
                if (petBranch && gestureGuard is { } guard)
                    cancelQueued |= !guard.Allows(origin, input.ScreenPoint);
                var sample = new Sample((int)message, input, ++sampleSequence);
                if (message == 0x0200)
                {
                    firstMoveTimestamp ??= input.Timestamp;
                    lastMoveTimestamp = input.Timestamp;
                    pendingMove = sample;
                    var dx = input.ScreenPoint.X - origin.X;
                    var dy = input.ScreenPoint.Y - origin.Y;
                    var distance = dx * dx + dy * dy;
                    if (!double.IsFinite(distance)) cancelQueued = true;
                    else
                    {
                        if (distance > greatestDistanceSquared)
                        { greatestDistanceSquared = distance; farthestMove = sample; }
                        if (Math.Abs(dy) > greatestVerticalDistance)
                        { greatestVerticalDistance = Math.Abs(dy); verticalMove = sample; }
                    }
                }
                else
                {
                    pendingRelease = sample;
                    releaseTimestamp = input.Timestamp;
                }
                releaseQueued = message == 0x0202;
                QueueDrain();
            }
        }
        catch
        {
            // Never let managed failures escape into the native hook chain.
            cancelQueued = true;
            QueueDrain();
        }
        return platform.Next(currentHook, code, message, data);
    }

    private void QueueDrain()
    {
        if (queued || dispatcher.HasShutdownStarted) return;
        queued = true;
        var version = generation;
        dispatcher.BeginInvoke(() => Drain(version), DispatcherPriority.Normal);
    }

    private void Drain(long version)
    {
        if (version != generation || !IsListening) return;
        queued = false;
        if (cancelQueued || !canContinue())
        {
            Stop();
            cancel();
            return;
        }
        // Keep only bounded eligibility extrema, the latest move and release.
        // A return to the down point cannot erase a real drag or invalid pet stroke.
        var moves = new[] { farthestMove, verticalMove, pendingMove }.Where(sample => sample is not null)
            .Select(sample => sample!.Value).DistinctBy(sample => sample.Sequence).OrderBy(sample => sample.Sequence).ToArray();
        var up = pendingRelease;
        farthestMove = verticalMove = pendingMove = pendingRelease = null;
        foreach (var value in moves)
        {
            if (version != generation || !IsListening) return;
            if (!canContinue()) { Stop(); cancel(); return; }
            move(value.Input.ScreenPoint, value.Input.Shift);
        }
        if (up is { } ended && version == generation && IsListening)
        {
            if (!canContinue()) { Stop(); cancel(); return; }
            // Stop before releasing capture so a queued WM_UP cannot end it twice.
            Stop();
            release(ended.Input.ScreenPoint, ended.Input.Shift);
        }
    }

    private void ClearSamples()
    {
        pendingMove = farthestMove = verticalMove = pendingRelease = null;
        firstMoveTimestamp = lastMoveTimestamp = releaseTimestamp = null;
        greatestDistanceSquared = greatestVerticalDistance = 0;
        sampleSequence = 0;
        origin = default;
        gestureGuard = null;
    }

    private bool TryReleaseHook()
    {
        if (hook != 0 && platform.Uninstall(hook)) hook = 0;
        if (hook != 0)
        {
            lock (releasePendingLock) releasePending.Add(this);
            releaseRetry.Start();
        }
        else
        {
            lock (releasePendingLock) releasePending.Remove(this);
            releaseRetry.Stop();
            if (disposed)
            {
                releaseRetry.Tick -= RetryRelease;
                dispatcher.ShutdownStarted -= OnShutdown;
            }
        }
        return hook == 0;
    }

    private void RetryRelease(object? sender, EventArgs args) => TryReleaseHook();
    private void OnShutdown(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (disposed) return;
        disposed = true;
        Stop();
        // A failed unhook retains the callback and retry timer after the Window closes.
        // NativeMethods also roots the delegate until a successful UnhookWindowsHookEx.
    }

    private readonly record struct Sample(int Message, DesktopPetPointerSample Input, long Sequence);
}

internal sealed class NativeDesktopPetPointerGesturePlatform : IDesktopPetPointerGesturePlatform
{
    public nint Install(NativeMethods.LowLevelMouseProcedure callback) => NativeMethods.InstallLowLevelMouseHook(callback);
    public bool Uninstall(nint hook) => NativeMethods.UninstallMouseHook(hook);
    public nint Next(nint hook, int code, nint message, nint data) => NativeMethods.CallNextKeyboardHook(hook, code, message, data);
    public DesktopPetPointerSample Read(nint data)
    {
        var sample = NativeMethods.ReadDesktopPetGestureInput(data);
        return new(new(sample.Point.X, sample.Point.Y), sample.Shift,
            sample.HasForbiddenModifier, sample.Timestamp);
    }
}
