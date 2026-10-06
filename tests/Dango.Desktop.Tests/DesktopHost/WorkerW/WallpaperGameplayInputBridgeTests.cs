using System.Windows.Input;
using System.Windows.Threading;
using System.Reflection;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.DesktopHost.WorkerW;

namespace Dango.Desktop.Tests.DesktopHost.WorkerW;

[TestClass]
public sealed class WallpaperGameplayInputBridgeTests
{
    private static WallpaperWheelInputContext Context => new(new WorkerWDesktopTarget(10, 11, 12, 13, true),
        new LogicalRect(-1920, 0, 1920, 1080), 1, true, false);

    [STATestMethod]
    public void KeyboardAndPointerOnlyApplyAfterCommandRequalificationAndAlwaysPassTheHook()
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        Assert.AreEqual((nint)987, platform.EmitKey(Key.A, true));
        Assert.AreEqual((nint)987, platform.EmitMouse(0x0201));
        Assert.AreEqual(0, sink.Keys.Count); Assert.AreEqual(0, sink.DownCount);
        bridge.FlushPendingInput();
        Assert.AreEqual(1, sink.Keys.Count); Assert.AreEqual(1, sink.DownCount);
        platform.EmitMouse(0x0200); platform.EmitMouse(0x0202); platform.EmitKey(Key.A, false);
        bridge.FlushPendingInput();
        Assert.AreEqual(1, sink.MoveCount); Assert.AreEqual(1, sink.UpCount);
        Assert.AreEqual(2, sink.Keys.Count);
        Assert.AreEqual(2, platform.KeyNext); Assert.AreEqual(3, platform.MouseNext);
        Assert.IsTrue(platform.GlobalQueries > 0); Assert.IsTrue(platform.PointerQueries > 0);
    }

    [STATestMethod]
    [DataRow(false, true, true, false)]
    [DataRow(true, false, true, false)]
    [DataRow(true, true, false, false)]
    [DataRow(true, true, true, true)]
    public void PointerEventGuardUnknownApplicationNonDesktopAndModifierCannotRecoverBeforeFlush(
        bool known, bool foreground, bool background, bool modifier)
    {
        var platform = new FakePlatform { EventGuard = new(known, foreground, background, modifier) };
        var sink = new Sink(); using var bridge = Create(platform, sink); bridge.Start();
        platform.EmitMouse(0x0201); platform.EventGuard = new(true, true, true, false);
        bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.DownCount); Assert.IsTrue(sink.ClearCount > 0);
        platform.EmitMouse(0x0200); platform.EmitMouse(0x0202); bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.MoveCount); Assert.AreEqual(0, sink.UpCount);
        Assert.AreEqual(0, platform.FullAtEventCalls);
        Assert.AreEqual(1, platform.GuardCalls);
        Assert.AreEqual(3, platform.MouseNext);
        platform.EmitMouse(0x0201); bridge.FlushPendingInput();
        Assert.AreEqual(1, sink.DownCount, "Recovery requires a fresh guarded physical Down.");
    }

    [STATestMethod]
    public void HookOnlyCapturesGuardAndQueuesBeforeCompleteCommandInspection()
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        Assert.AreEqual((nint)987, platform.EmitKey(Key.A, true));
        Assert.AreEqual((nint)987, platform.EmitMouse(0x0201, shift: true));

        Assert.AreEqual(0, platform.FullAtEventCalls, "Full enumeration and icon waits must not run inside either hook.");
        Assert.AreEqual(2, platform.GuardCalls);
        Assert.AreEqual(0, platform.FullCommandCalls);
        Assert.AreEqual(0, sink.Keys.Count); Assert.AreEqual(0, sink.DownCount);
        CollectionAssert.AreEqual(new[] { "Guard", "Guard" }, platform.QualificationOrder.ToArray());
        Assert.IsNull(platform.GuardPoints[0]);
        Assert.AreEqual(new WallpaperWheelPoint(-300, 300), platform.GuardPoints[1]);
        CollectionAssert.AreEqual(new[] { false, true }, platform.GuardShiftPermissions.ToArray());

        bridge.FlushPendingInput();
        Assert.AreEqual(1, sink.Keys.Count); Assert.AreEqual(1, sink.DownCount);
        Assert.AreEqual(2, platform.FullCommandCalls);
        CollectionAssert.AreEqual(new[] { "Guard", "Guard", "Command", "Command" },
            platform.QualificationOrder.ToArray());
    }

    [STATestMethod]
    [DataRow(false, true, false, true, false, false)]
    [DataRow(true, false, false, true, false, false)]
    [DataRow(true, true, true, true, false, false)]
    [DataRow(true, true, false, false, false, false)]
    [DataRow(true, true, false, true, true, false)]
    [DataRow(true, true, false, true, false, true)]
    public void CompleteCommandUnknownApplicationFullscreenNonDesktopIconOrModifierRejectsQueuedStroke(
        bool known, bool foreground, bool blocking, bool background, bool icon, bool modifier)
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        platform.EmitMouse(0x0201); platform.EmitMouse(0x0200); platform.EmitMouse(0x0202);
        Assert.AreEqual(0, sink.DownCount); Assert.AreEqual(0, sink.MoveCount); Assert.AreEqual(0, sink.UpCount);
        platform.Environment = new(known, foreground, blocking, background, icon, modifier);
        bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.DownCount); Assert.AreEqual(0, sink.MoveCount); Assert.AreEqual(0, sink.UpCount);
        Assert.IsTrue(sink.ClearCount > 0);
        platform.Environment = new(true, true, false, true, false);
        platform.EmitMouse(0x0200); platform.EmitMouse(0x0202); bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.DownCount); Assert.AreEqual(0, sink.MoveCount); Assert.AreEqual(0, sink.UpCount);
        Assert.AreEqual(5, platform.MouseNext);
    }

    [STATestMethod]
    public void GlobalKeyboardEligibilityIgnoresPointerAndPetHitButHonorsBlockingWindows()
    {
        var platform = new FakePlatform { Environment = new(true, true, false, false, true) };
        var sink = new Sink { Hit = false }; using var bridge = Create(platform, sink); bridge.Start();
        platform.EmitKey(Key.D, true); bridge.FlushPendingInput();
        Assert.AreEqual(1, sink.Keys.Count); Assert.AreEqual(0, platform.PointerQueries);
        platform.Environment = new(true, true, true, false, true);
        bridge.RevalidateActiveInput(); bridge.FlushPendingInput();
        Assert.IsTrue(sink.ClearCount > 0);
    }

    [STATestMethod]
    public void NewlyBlockingWindowBeforeApplyRejectsTheQueuedPointerAndKeyboard()
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        platform.EmitKey(Key.Space, true); platform.EmitMouse(0x0201);
        platform.Environment = new(true, true, true, true, false);
        bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.Keys.Count); Assert.AreEqual(0, sink.DownCount);
        Assert.IsTrue(sink.ClearCount > 0);
    }

    [STATestMethod]
    public void HeldKeyFromLostFocusStaysSuppressedUntilActualRelease()
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        platform.EmitKey(Key.A, true); bridge.FlushPendingInput();
        platform.Environment = new(true, false, false, true, false);
        bridge.RevalidateActiveInput(); bridge.FlushPendingInput();
        platform.Environment = new(true, true, false, true, false);
        platform.EmitKey(Key.A, true); bridge.FlushPendingInput();
        Assert.AreEqual(1, sink.Keys.Count, "Recovery cannot import an already-held/repeating key from another foreground.");
        platform.EmitKey(Key.A, false); platform.EmitKey(Key.A, true); bridge.FlushPendingInput();
        Assert.AreEqual(2, sink.Keys.Count);
    }

    [STATestMethod]
    public void AlreadyHeldKeyAtStartIsSuppressedAndShiftIsAllowedOnlyForPointer()
    {
        var platform = new FakePlatform { HeldAtStart = Key.A }; var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        platform.EmitKey(Key.A, true); platform.EmitKey(Key.D, true, modifier: true);
        bridge.FlushPendingInput(); Assert.AreEqual(0, sink.Keys.Count);
        platform.EmitMouse(0x0201, shift: true); bridge.FlushPendingInput();
        Assert.AreEqual(1, sink.DownCount); Assert.IsTrue(sink.LastShift); Assert.IsTrue(platform.PointerAllowsShift);
        platform.EmitMouse(0x0200, forbiddenModifier: true); bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.MoveCount); Assert.IsTrue(sink.ClearCount > 0);
    }

    [STATestMethod]
    public void ShiftStrokePreservesObservedCoordinatesAndNormalCompletionDoesNotClearInput()
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        var begin = new WallpaperWheelPoint(-300, 300);
        var end = new WallpaperWheelPoint(-280, 300);
        platform.EmitMouse(0x0201, shift: true, point: begin); bridge.FlushPendingInput();
        platform.EmitMouse(0x0200, shift: true, point: end); bridge.FlushPendingInput();
        // The driver may finish its gesture and cancel the pointer token inside the successful Up callback.
        // That completion must not be treated as a failed gesture that clears its accepted reaction.
        sink.OnUp = bridge.CancelPointer;
        platform.EmitMouse(0x0202, point: end); bridge.FlushPendingInput();
        CollectionAssert.AreEqual(new[]
        {
            ("Down", begin, true), ("Move", end, true), ("Up", end, false),
        }, sink.PointerEvents.ToArray());
        Assert.AreEqual(0, sink.ClearCount);
        Assert.AreEqual(3, platform.MouseNext);
        Assert.IsTrue(platform.PointerAllowsShift);
        platform.EmitMouse(0x0200, shift: true, point: begin); platform.EmitMouse(0x0202, point: begin);
        bridge.FlushPendingInput();
        Assert.AreEqual(3, sink.PointerEvents.Count, "Completed input cannot acquire another stroke without a fresh Down.");
    }

    [STATestMethod]
    public void RejectedPointerDownCannotReplayItsQueuedMoveOrUpAfterCaptureBecomesAvailable()
    {
        var platform = new FakePlatform(); var sink = new Sink { DownAccepted = false };
        using var bridge = Create(platform, sink); bridge.Start();
        platform.EmitMouse(0x0201, shift: true);
        platform.EmitMouse(0x0200, shift: true); platform.EmitMouse(0x0202);
        bridge.FlushPendingInput();
        Assert.AreEqual(1, sink.DownCount); Assert.AreEqual(0, sink.MoveCount); Assert.AreEqual(0, sink.UpCount);
        sink.DownAccepted = true;
        platform.EmitMouse(0x0200, shift: true); platform.EmitMouse(0x0202);
        bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.MoveCount); Assert.AreEqual(0, sink.UpCount);
        platform.EmitMouse(0x0201, shift: true); bridge.FlushPendingInput();
        Assert.AreEqual(2, sink.DownCount, "Recovery requires a fresh physical Down; it cannot import the rejected stroke.");
        Assert.AreEqual(6, platform.MouseNext);
    }

    [STATestMethod]
    public void BackgroundMustHitThePetToBeginButMoveAndUpMayLeaveItsBody()
    {
        var platform = new FakePlatform(); var sink = new Sink { Hit = false };
        using var bridge = Create(platform, sink); bridge.Start();
        platform.EmitMouse(0x0201); bridge.FlushPendingInput(); Assert.AreEqual(0, sink.DownCount);
        sink.Hit = true; platform.EmitMouse(0x0201); bridge.FlushPendingInput();
        sink.Hit = false; platform.EmitMouse(0x0200); platform.EmitMouse(0x0202); bridge.FlushPendingInput();
        Assert.AreEqual(1, sink.DownCount); Assert.AreEqual(1, sink.MoveCount); Assert.AreEqual(1, sink.UpCount);
    }

    [STATestMethod]
    public void PointerQualificationLossClearsControlsAndCannotReplayAfterRecovery()
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        platform.EmitMouse(0x0201); bridge.FlushPendingInput();
        platform.Environment = new(true, true, false, true, true);
        platform.EmitMouse(0x0200); bridge.FlushPendingInput();
        platform.Environment = new(true, true, false, true, false);
        platform.EmitMouse(0x0200); platform.EmitMouse(0x0202); bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.MoveCount); Assert.AreEqual(0, sink.UpCount); Assert.IsTrue(sink.ClearCount > 0);
    }

    [STATestMethod]
    public void CancelOrStopDuringPointerDownCannotRestoreOldPointerOwnership()
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        sink.OnDown = bridge.CancelPointer;
        platform.EmitMouse(0x0201); bridge.FlushPendingInput();
        platform.EmitMouse(0x0200); bridge.FlushPendingInput(); Assert.AreEqual(0, sink.MoveCount);
        sink.OnDown = () => { bridge.Stop(); bridge.Start(); };
        platform.EmitMouse(0x0201); bridge.FlushPendingInput();
        platform.EmitMouse(0x0200); platform.EmitKey(Key.D, true); bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.MoveCount); Assert.AreEqual(1, sink.Keys.Count);
    }

    [STATestMethod]
    public void StopDuringKeyCallbackCannotRestoreOldHeldStateAndLateFailuresCannotClearFreshInput()
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        sink.OnKey = () => { sink.OnKey = null; bridge.Stop(); bridge.Start(); };
        platform.EmitKey(Key.A, true); bridge.FlushPendingInput();
        var clears = sink.ClearCount;
        bridge.RevalidateActiveInput(); bridge.FlushPendingInput();
        Assert.AreEqual(clears, sink.ClearCount, "The old callback must not repopulate active keys after a new generation starts.");
        platform.EmitKey(Key.D, true); bridge.FlushPendingInput(); Assert.AreEqual(2, sink.Keys.Count);
    }

    [STATestMethod]
    public void EscapeRequestsMenuOnceWithoutConsumingShellAndInjectedInputIsIgnored()
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        platform.EmitKey(Key.Escape, true); platform.EmitKey(Key.Escape, true); platform.EmitKey(Key.Escape, false);
        bridge.FlushPendingInput(); Assert.AreEqual(1, sink.MenuCount);
        platform.EmitKey(Key.A, true, flags: 0x10); platform.EmitMouse(0x0201, flags: 1); bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.Keys.Count); Assert.AreEqual(0, sink.DownCount);
        Assert.AreEqual(4, platform.KeyNext); Assert.AreEqual(1, platform.MouseNext);
    }

    [STATestMethod]
    public void FailedHookReleaseStaysPendingAndPreventsAnotherInstallation()
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        platform.ReleaseAllowed = false; bridge.Stop();
        Assert.IsTrue(bridge.IsHookReleasePending); Assert.IsFalse(bridge.IsListening);
        bridge.Start(); Assert.AreEqual(1, platform.KeyboardInstallations); Assert.AreEqual(1, platform.MouseInstallations);
        Assert.AreEqual((nint)987, platform.EmitKey(Key.A, true)); Assert.AreEqual(0, sink.Keys.Count);
        platform.ReleaseAllowed = true; bridge.Start(); Assert.IsTrue(bridge.IsListening);
        Assert.AreEqual(2, platform.KeyboardInstallations); Assert.AreEqual(2, platform.MouseInstallations);
    }

    [STATestMethod]
    public void HostHideRetainsFailedHookReleaseAndShowCannotBypassTheOldInstance()
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        using var host = new LocalRoomWallpaperHost();
        var bridgeField = typeof(LocalRoomWallpaperHost).GetField("gameplayBridge", BindingFlags.Instance | BindingFlags.NonPublic)!;
        bridgeField.SetValue(host, bridge);
        platform.ReleaseAllowed = false;
        host.Hide();
        Assert.AreSame(bridge, bridgeField.GetValue(host));
        Assert.IsTrue(bridge.IsHookReleasePending);
        Assert.ThrowsExactly<InvalidOperationException>(() => host.Show(2, System.Windows.Forms.Screen.PrimaryScreen!));
        Assert.AreEqual(1, platform.KeyboardInstallations); Assert.AreEqual(1, platform.MouseInstallations);
        platform.ReleaseAllowed = true;
        host.Hide();
        Assert.IsNull(bridgeField.GetValue(host), "Retry must release the retained disposed instance before allowing another host.");
    }

    [STATestMethod]
    public void HostCameraInvalidationDiscardsQueuedPointerBeforeTheDriverHasAnActiveGesture()
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        using var host = new LocalRoomWallpaperHost();
        typeof(LocalRoomWallpaperHost).GetField("gameplayBridge", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, bridge);
        platform.EmitMouse(0x0201);
        host.Scene.SelectView(RoomViewPreset.Near);
        bridge.FlushPendingInput();
        platform.EmitMouse(0x0200); platform.EmitMouse(0x0202); bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.DownCount); Assert.AreEqual(0, sink.MoveCount); Assert.AreEqual(0, sink.UpCount);
    }

    [STATestMethod]
    public void CancellationDuringCommandInspectionCannotExecuteTheCapturedPointerDown()
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        platform.EmitMouse(0x0201);
        platform.OnInspect = atEvent =>
        {
            if (!atEvent) { platform.OnInspect = null; bridge.CancelPointer(); }
        };
        bridge.FlushPendingInput();
        platform.EmitMouse(0x0200); platform.EmitMouse(0x0202); bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.DownCount); Assert.AreEqual(0, sink.MoveCount); Assert.AreEqual(0, sink.UpCount);
    }

    [STATestMethod]
    public void LegalMoveDuringCommandInspectionQueuesWithoutCancellingStrokeOrRecursing()
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        platform.EmitMouse(0x0201, shift: true); bridge.FlushPendingInput();
        platform.EmitMouse(0x0200, shift: true);
        platform.OnInspect = atEvent =>
        {
            if (atEvent) return;
            platform.OnInspect = null;
            platform.EmitMouse(0x0200, shift: true, point: new(-280, 300));
            bridge.FlushPendingInput();
            bridge.RevalidateActiveInput();
        };

        bridge.FlushPendingInput();
        Assert.AreEqual(1, sink.MoveCount);
        Assert.AreEqual(0, sink.ClearCount, "A guarded event during inspection must queue, not invalidate the active stroke.");
        Assert.AreEqual(1, platform.MaxInspectDepth);
        bridge.FlushPendingInput();
        Assert.AreEqual(2, sink.MoveCount);
        platform.EmitMouse(0x0202); bridge.FlushPendingInput();
        Assert.AreEqual(1, sink.UpCount); Assert.AreEqual(0, sink.ClearCount);
    }

    [STATestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void ReentrantUnknownOrNonDesktopForegroundGuardInvalidatesTheCurrentCommandAndCannotRecover(
        bool known, bool foreground)
    {
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink); bridge.Start();
        platform.EmitMouse(0x0201, shift: true); bridge.FlushPendingInput();
        platform.EmitMouse(0x0200, shift: true);
        platform.OnInspect = atEvent =>
        {
            if (atEvent) return;
            platform.OnInspect = null;
            platform.EventGuard = new(known, foreground, true, false);
            platform.EmitMouse(0x0200, shift: true);
            platform.EventGuard = new(true, true, true, false);
        };

        bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.MoveCount); Assert.IsTrue(sink.ClearCount > 0);
        platform.EmitMouse(0x0200, shift: true); platform.EmitMouse(0x0202); bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.MoveCount); Assert.AreEqual(0, sink.UpCount);
        platform.EmitMouse(0x0201, shift: true); bridge.FlushPendingInput();
        Assert.AreEqual(2, sink.DownCount, "Only a fresh guarded physical Down can start another stroke.");
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EventGuardCannotQueueInputAcrossChangedPresentationOrBridgeRestart(bool restart)
    {
        var current = Context;
        var platform = new FakePlatform(); var sink = new Sink();
        using var bridge = Create(platform, sink, () => current); bridge.Start();
        platform.OnGuard = () =>
        {
            platform.OnGuard = null;
            if (restart) { bridge.Stop(); bridge.Start(); }
            else current = current with { Generation = current.Generation + 1 };
        };

        platform.EmitMouse(0x0201); bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.DownCount);
        platform.EmitMouse(0x0200); platform.EmitMouse(0x0202); bridge.FlushPendingInput();
        Assert.AreEqual(0, sink.MoveCount); Assert.AreEqual(0, sink.UpCount);
    }

    private static WallpaperGameplayInputBridge Create(FakePlatform platform, Sink sink,
        Func<WallpaperWheelInputContext?>? contextProvider = null) => new(Dispatcher.CurrentDispatcher,
        contextProvider ?? (() => Context), new(sink.Key, sink.Down, sink.Move, sink.Up, _ => sink.Hit,
            () => sink.MenuCount++, () => sink.ClearCount++), platform);

    private sealed class Sink
    {
        internal List<(Key, bool, bool)> Keys { get; } = [];
        internal int DownCount, MoveCount, UpCount, ClearCount, MenuCount;
        internal bool Hit = true, LastShift, DownAccepted = true;
        internal List<(string Phase, WallpaperWheelPoint Point, bool Shift)> PointerEvents { get; } = [];
        internal Action? OnDown, OnKey, OnUp;
        internal bool Key(Key key, bool down, bool repeat) { Keys.Add((key, down, repeat)); OnKey?.Invoke(); return true; }
        internal bool Down(WallpaperWheelPoint point, bool shift)
        { DownCount++; LastShift = shift; PointerEvents.Add(("Down", point, shift)); OnDown?.Invoke(); return DownAccepted; }
        internal bool Move(WallpaperWheelPoint point, bool shift)
        { MoveCount++; PointerEvents.Add(("Move", point, shift)); return true; }
        internal bool Up(WallpaperWheelPoint point, bool shift)
        { UpCount++; PointerEvents.Add(("Up", point, shift)); OnUp?.Invoke(); return true; }
    }
    private sealed class FakePlatform : IWallpaperGameplayInputPlatform
    {
        private NativeMethods.LowLevelKeyboardProcedure? keyboard;
        private NativeMethods.LowLevelMouseProcedure? mouse;
        private WallpaperGameplayKeyData keyData;
        private WallpaperGameplayMouseData mouseData;
        internal int KeyNext, MouseNext, GlobalQueries, PointerQueries, KeyboardInstallations, MouseInstallations;
        internal int FullAtEventCalls, FullCommandCalls, GuardCalls, MaxInspectDepth;
        private int inspectDepth;
        internal bool PointerAllowsShift, ReleaseAllowed = true;
        internal Key HeldAtStart = Key.None;
        internal WallpaperGameplayEventGuard EventGuard = new(true, true, true, false);
        internal WallpaperWheelInputEnvironment Environment = new(true, true, false, true, false);
        internal Action<bool>? OnInspect;
        internal Action? OnGuard;
        internal List<string> QualificationOrder { get; } = [];
        internal List<WallpaperWheelPoint?> GuardPoints { get; } = [];
        internal List<bool> GuardShiftPermissions { get; } = [];
        public nint InstallKeyboard(NativeMethods.LowLevelKeyboardProcedure callback) { keyboard = callback; KeyboardInstallations++; return 101; }
        public nint InstallMouse(NativeMethods.LowLevelMouseProcedure callback) { mouse = callback; MouseInstallations++; return 102; }
        public bool UninstallKeyboard(nint hook) => ReleaseAllowed;
        public bool UninstallMouse(nint hook) => ReleaseAllowed;
        public nint NextKeyboard(nint hook, int code, nint message, nint data) { KeyNext++; return 987; }
        public nint NextMouse(nint hook, int code, nint message, nint data) { MouseNext++; return 987; }
        public WallpaperGameplayKeyData ReadKeyboard(nint data) => keyData;
        public WallpaperGameplayMouseData ReadMouse(nint data) => mouseData;
        public bool IsKeyPressed(int virtualKey) => HeldAtStart != Key.None && KeyInterop.VirtualKeyFromKey(HeldAtStart) == virtualKey;
        public WallpaperGameplayEventGuard InspectEventGuard(WallpaperWheelInputContext context, WallpaperWheelPoint? point, bool allowShift)
        {
            GuardCalls++; QualificationOrder.Add("Guard"); GuardPoints.Add(point); GuardShiftPermissions.Add(allowShift);
            OnGuard?.Invoke();
            return EventGuard;
        }
        public WallpaperWheelInputEnvironment Inspect(WallpaperWheelInputContext context, WallpaperWheelPoint? point, bool atEvent, bool allowShift)
        {
            if (point is null) GlobalQueries++; else { PointerQueries++; PointerAllowsShift = allowShift; }
            if (atEvent) FullAtEventCalls++; else FullCommandCalls++;
            QualificationOrder.Add(atEvent ? "FullEvent" : "Command");
            inspectDepth++; MaxInspectDepth = Math.Max(MaxInspectDepth, inspectDepth);
            try { OnInspect?.Invoke(atEvent); return Environment; }
            finally { inspectDepth--; }
        }
        internal nint EmitKey(Key key, bool down, bool modifier = false, uint flags = 0)
        { keyData = new(KeyInterop.VirtualKeyFromKey(key), flags, modifier); return keyboard!(0, down ? 0x0100 : 0x0101, 0); }
        internal nint EmitMouse(int message, bool shift = false, bool forbiddenModifier = false, uint flags = 0, WallpaperWheelPoint? point = null)
        { mouseData = new(point ?? new(-300, 300), flags, shift, forbiddenModifier); return mouse!(0, message, 0); }
    }
}
