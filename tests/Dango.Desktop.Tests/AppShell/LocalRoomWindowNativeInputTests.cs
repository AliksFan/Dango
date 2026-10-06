using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.AppShell;

// These tests briefly take foreground/cursor ownership. They inject Win32 input;
// they are neither physical-mouse evidence nor human acceptance.
[TestClass]
[DoNotParallelize]
public sealed class LocalRoomWindowNativeInputTests
{
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("NativeWindowInput")]
    public void NativeWheelAndShiftStrokeApplyNewSoftFeedbackWithTheSameEntityAndOwner()
    {
        using var rig = NativeWindowFixture.Create();
        rig.WaitForNeutral();
        var scale = rig.Scene.Camera.Scale;
        rig.Wheel(-60);
        Assert.AreEqual(scale, rig.Scene.Camera.Scale, 1e-8,
            "A native half-notch must remain in the window wheel accumulator.");
        rig.Wheel(-60);
        Assert.AreEqual(scale - rig.Scene.Camera.StandardScale * .1, rig.Scene.Camera.Scale, 1e-8,
            "Two timely native half-notches must produce one real window zoom step.");
        rig.Wheel(120);
        Assert.AreEqual(scale, rig.Scene.Camera.Scale, 1e-8);
        rig.AssertSameEntityAndOwner();

        var baseline = rig.Runtime.LastFrame!.Bodies[0].Attachments!;
        var feedbackObserved = false;
        var newlyAppliedSoftFrame = false;
        void Observe()
        {
            feedbackObserved |= rig.Runtime.HasLightFeedback;
            var attachment = rig.Runtime.LastFrame?.Bodies[0].Attachments;
            newlyAppliedSoftFrame |= rig.Runtime.HasLightFeedback && attachment is not null &&
                attachment.Generation == baseline.Generation && attachment.StepIndex > baseline.StepIndex &&
                attachment.Face.Left.Shape == EyeShape.Soft && attachment.Face.Right.Shape == EyeShape.Soft &&
                attachment.Face.Left.Openness < .4 && attachment.Face.Right.Openness < .4;
        }

        rig.Scene.FrameApplied += Observe;
        try
        {
            var center = rig.BodyBounds.Center;
            rig.KeyDown(VkLeftShift);
            Assert.AreEqual(ModifierKeys.Shift, Keyboard.Modifiers);
            rig.MoveTo(rig.ToScene(center));
            rig.MouseDown();
            Assert.IsTrue(rig.Driver.IsPointerActive, "The actual window Down route must start the gesture.");
            Assert.AreSame(rig.Scene, Mouse.Captured, "The actual room scene must own WPF mouse capture.");
            rig.MoveTo(rig.ToScene(new(center.X + 20, center.Y)));
            feedbackObserved |= rig.Runtime.HasLightFeedback;
            Assert.IsTrue(feedbackObserved, "Native Shift horizontal movement must create new runtime feedback.");
            Assert.IsFalse(rig.Runtime.IsDragging);
            rig.MouseUp();
            Assert.IsFalse(rig.Driver.IsPointerActive);
            Assert.AreNotSame(rig.Scene, Mouse.Captured);
            Assert.IsTrue(rig.Runtime.HasLightFeedback, "Normal window Up/capture release must preserve pet feedback.");
            rig.KeyUp(VkLeftShift);
            WaitUntil(() => newlyAppliedSoftFrame, 1000,
                "A newly advanced real Scene.FrameApplied must contain both Soft eyes with openness below .4.");
            rig.AssertSameEntityAndOwner();
        }
        finally { rig.Scene.FrameApplied -= Observe; }
        TestContext.WriteLine("Win32 automatic injection: actual AppHost → LocalRoomWindow wheel and Shift stroke; new real Soft feedback frame. Physical input/human acceptance not established.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("NativeWindowInput")]
    public void NativePlainLeftMovementCannotDragOrReplayClickAfterReturningToTheBody()
    {
        using var rig = NativeWindowFixture.Create();
        rig.WaitForNeutral();
        var center = rig.BodyBounds.Center;
        var draggingObserved = false;
        var feedbackObserved = false;
        void Observe()
        {
            draggingObserved |= rig.Runtime.IsDragging;
            feedbackObserved |= rig.Runtime.HasLightFeedback;
        }
        rig.Scene.FrameApplied += Observe;
        try
        {
            rig.MoveTo(rig.ToScene(center));
            rig.MouseDown();
            Assert.IsTrue(rig.Driver.IsPointerActive);
            Assert.AreSame(rig.Scene, Mouse.Captured);
            rig.MoveTo(rig.ToScene(new(center.X + 10, center.Y)));
            Assert.IsFalse(rig.Driver.IsPointerActive, "Ordinary-room movement must reject drag conversion.");
            Assert.IsFalse(rig.Runtime.IsDragging);
            Assert.AreNotSame(rig.Scene, Mouse.Captured);
            rig.MoveTo(rig.ToScene(center));
            rig.MouseUp();
            Pump(250);
            Assert.IsFalse(rig.Driver.IsPointerActive);
            Assert.IsFalse(draggingObserved);
            Assert.IsFalse(feedbackObserved || rig.Runtime.HasLightFeedback,
                "Returning to the body and a late native Up must not replay a click.");
            rig.AssertSameEntityAndOwner();
        }
        finally { rig.Scene.FrameApplied -= Observe; }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("NativeWindowInput")]
    public void NativeEscapeMenuBlocksWheelAndControlCenterReturnRestoresNativeMovement()
    {
        using var rig = NativeWindowFixture.Create();
        rig.KeyDown(VkEscape);
        Assert.IsTrue(rig.Window.IsMenuOpen);
        Assert.IsTrue(rig.Scene.InputBlocked);
        rig.RepeatKeyDown(VkEscape);
        Assert.IsTrue(rig.Window.IsMenuOpen, "A native repeated Esc Down must not close the menu.");
        rig.KeyUp(VkEscape);
        var scale = rig.Scene.Camera.Scale;
        rig.Wheel(-120, allowMenu: true);
        Assert.AreEqual(scale, rig.Scene.Camera.Scale, 1e-8, "The actual menu route must block window zoom.");
        rig.Tap(VkEscape);
        Assert.IsFalse(rig.Window.IsMenuOpen);
        Assert.IsFalse(rig.Scene.InputBlocked);
        Assert.IsTrue(rig.Scene.IsKeyboardFocused);

        rig.Tap(VkEscape);
        var menu = Field<LocalRoomMenuView>(rig.Window, "menu");
        var button = (Button)menu.FindName("RoomControlCenterButton");
        rig.ClickElement(button);
        var controlCenter = Field<ControlCenterWindow>(rig.Host, "controlCenterWindow");
        WaitUntil(() => controlCenter.IsVisible && controlCenter.IsActive, 1000,
            "The native room menu click must open and activate the real control center.");
        Assert.AreEqual(new WindowInteropHelper(controlCenter).Handle, GetForegroundWindow());
        Assert.IsFalse(rig.Window.IsMenuOpen);
        Assert.IsFalse(controlCenter.ProductHome.OwnsLiveFrameDriver);
        Assert.AreEqual(DirectionalControlIntent.None, Field<DirectionalControlIntent>(rig.Driver, "intent"));
        rig.AssertSameEntityAndOwner();

        // Uses the real host's existing restore/focus command, then tests OS input again.
        rig.Host.RestoreLocalRoom();
        Pump(100);
        rig.RequireRoomForeground();
        Assert.IsTrue(rig.Scene.IsKeyboardFocused);
        var beforeX = rig.Runtime.CurrentSelectedCenter!.Value.X;
        rig.KeyDown(VkD);
        Assert.IsTrue(Field<DirectionalControlIntent>(rig.Driver, "intent").Right);
        WaitUntil(() =>
        {
            rig.RequireRoomForeground();
            return rig.Runtime.CurrentSelectedCenter!.Value.X > beforeX + 2;
        }, 1000,
            "Native D after control-center return must move the same real physical entity right.");
        rig.KeyUp(VkD);
        Assert.IsFalse(Field<DirectionalControlIntent>(rig.Driver, "intent").Right);
        rig.AssertSameEntityAndOwner();
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("NativeWindowInput")]
    public void IdleWindowReportsRealSchedulerAndAppliedFrameAggregatesWithoutAnFpsGate()
    {
        using var rig = NativeWindowFixture.Create();
        var scheduler = Field<HostFrameScheduler>(rig.Driver, "scheduler");
        var before = scheduler.Snapshot;
        var previousDetailed = scheduler.DetailedTimingEnabled;
        var callbackMilliseconds = new List<double>();
        var appliedIntervals = new List<double>();
        long? previousApplied = null;
        void OnApplied()
        {
            var now = Stopwatch.GetTimestamp();
            if (previousApplied is { } previous) appliedIntervals.Add((now - previous) * 1000d / Stopwatch.Frequency);
            previousApplied = now;
        }
        void OnCallback(object? sender, EventArgs args)
        {
            if (scheduler.LastStageTiming is { } timing)
                callbackMilliseconds.Add((timing.FinishedTimestamp - timing.StartedTimestamp) * 1000d / Stopwatch.Frequency);
        }
        scheduler.DetailedTimingEnabled = true;
        scheduler.CallbackCompleted += OnCallback;
        rig.Scene.FrameApplied += OnApplied;
        var watch = Stopwatch.StartNew();
        try { Pump(3000); }
        finally
        {
            rig.Scene.FrameApplied -= OnApplied;
            scheduler.CallbackCompleted -= OnCallback;
            scheduler.DetailedTimingEnabled = previousDetailed;
        }
        var after = scheduler.Snapshot;
        rig.RequireRoomForeground();
        rig.AssertSameEntityAndOwner();
        Assert.IsTrue(after.AppliedFrames > before.AppliedFrames, "The real foreground idle window must continue applying frames.");
        Assert.AreEqual(before.Faults, after.Faults);
        TestContext.WriteLine($"Idle observation only: {watch.Elapsed.TotalSeconds:F2}s; callbacks={after.Callbacks - before.Callbacks}; " +
            $"applied={after.AppliedFrames - before.AppliedFrames}; steps={after.SuccessfulSteps - before.SuccessfulSteps}; " +
            $"overloads={after.Overloads - before.Overloads}; pauses={after.LongPauses - before.LongPauses}; " +
            $"callback duration ms p95={Percentile95(callbackMilliseconds):F3}, max={Maximum(callbackMilliseconds):F3}; " +
            $"FrameApplied interval ms p95={Percentile95(appliedIntervals):F3}, max={Maximum(appliedIntervals):F3}. " +
            "Applied frames are not visible presentation FPS; this short idle sample has no FPS gate and does not establish sustained responsiveness or human acceptance.");
    }

    private static double Percentile95(List<double> samples)
    {
        if (samples.Count == 0) return double.NaN;
        var sorted = samples.Order().ToArray();
        return sorted[(int)Math.Ceiling(sorted.Length * .95) - 1];
    }
    private static double Maximum(List<double> samples) => samples.Count == 0 ? double.NaN : samples.Max();

    private sealed class NativeWindowFixture : IDisposable
    {
        private readonly HashSet<ushort> ownedKeys = [];
        private readonly NativePoint previousCursor;
        private readonly nint previousForeground;
        private DangoApplicationHost? host;
        private bool ownedLeftButton;
        private bool disposed;
        private object? originalOwner;
        private DangoEntityId originalEntity;
        internal DangoApplicationHost Host => host!;
        internal LocalRoomWindow Window => Host.LocalWindow!;
        internal LocalRoomSceneView Scene => Window.Scene;
        internal LocalRoomSceneDriver Driver => Field<LocalRoomSceneDriver>(Host, "localRoomDriver");
        internal StackedDangoSceneRuntime Runtime => Field<StackedDangoSceneRuntime>(Host, "stackedRuntime");
        internal LogicalRect BodyBounds => DangoHostFrameBinding.Bounds(Runtime.LastFrame!, 0)
            .Translate(new LogicalVector(0, Runtime.NeutralBodyGroundOffset));
        private nint Handle => new WindowInteropHelper(Window).Handle;

        private NativeWindowFixture()
        {
            Assert.IsTrue(GetCursorPos(out previousCursor), "Cannot preserve the user's cursor for native window input.");
            previousForeground = GetForegroundWindow();
        }

        internal static NativeWindowFixture Create()
        {
            var rig = new NativeWindowFixture();
            try { rig.Initialize(); return rig; }
            catch { rig.Dispose(); throw; }
        }

        private void Initialize()
        {
            RequireNoForeignInput();
            var root = Path.Combine(TestRepository.Root, "artifacts", "local-room", "tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            host = new(settingsFile: Path.Combine(root, "settings.json"), isolatedLocalRoomReview: true);
            Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
            Host.ShowControlCenter();
            Pump(350);
            var area = SystemParameters.WorkArea;
            if (area.Width < Window.MinWidth + 40 || area.Height < Window.MinHeight + 40)
                Assert.Inconclusive("The display work area cannot safely contain the room's minimum window.");
            Window.WindowStartupLocation = WindowStartupLocation.Manual;
            Window.Width = Math.Min(960, area.Width - 40);
            Window.Height = Math.Min(600, area.Height - 40);
            Window.Left = area.Left + (area.Width - Window.Width) / 2;
            Window.Top = area.Top + (area.Height - Window.Height) / 2;
            Host.EnterLocalRoom();
            WaitUntil(() => !Host.LocalHandoffPending && Host.LocalPresentation == LocalRoomPresentation.Window && Scene.HasHealthyFrame,
                2500, "Actual AppHost → window handoff must acknowledge a new healthy room frame.");
            Host.RestoreLocalRoom();
            Pump(100);
            RequireRoomForeground();
            Assert.IsTrue(Scene.IsKeyboardFocused);
            Assert.IsTrue(Driver.OwnsRuntime);
            originalOwner = Runtime.FrameDriver;
            Assert.IsNotNull(originalOwner);
            originalEntity = Runtime.EntityIds.Single();
            MoveTo(new(Scene.ActualWidth / 2, Scene.ActualHeight / 2));
        }

        internal void AssertSameEntityAndOwner()
        {
            Assert.AreSame(originalOwner, Runtime.FrameDriver);
            Assert.AreEqual(originalEntity, Runtime.EntityIds.Single());
            Assert.AreEqual(LocalRoomPresentation.Window, Host.LocalPresentation);
            Assert.IsTrue(Driver.OwnsRuntime);
            Assert.IsNull(Runtime.LastError);
            Assert.IsTrue(Scene.HasHealthyFrame);
        }

        internal void WaitForNeutral() => WaitUntil(() => !Runtime.HasLightFeedback &&
            Runtime.LastFrame?.Bodies[0].Attachments is { } attachment &&
            attachment.Face.Left.Openness >= .98 && attachment.Face.Right.Openness >= .98,
            1500, "Each native gesture must start with open eyes and no residual feedback.");

        internal Point ToScene(LogicalPoint point)
        {
            var projected = Scene.Camera.Projection.WorldToScreen(point);
            return new(projected.X + Scene.SafeInsets.Left, projected.Y + Scene.SafeInsets.Top);
        }

        internal void RequireRoomForeground()
        {
            if (!Window.IsVisible || !Window.IsActive || Handle == 0 || GetForegroundWindow() != Handle)
                Assert.Inconclusive("Refusing native injection: the exact ordinary-room window is not foreground.");
            if (!Window.IsKeyboardFocusWithin)
                Assert.Inconclusive("Refusing native injection: keyboard focus is outside the ordinary-room window.");
            RequireNoForeignInput();
        }

        private void RequireNoForeignInput()
        {
            foreach (var key in ObservedInputKeys)
            {
                var owned = key == VkLeftButton ? ownedLeftButton : ownedKeys.Contains(key);
                if (!owned && (GetAsyncKeyState(key) & 0x8000) != 0)
                    Assert.Inconclusive("Refusing native injection while an unowned mouse button/control/modifier key is held.");
            }
        }

        internal void MoveTo(Point scenePoint, bool allowMenu = false)
        {
            RequireRoomForeground();
            if (!allowMenu && (Scene.InputBlocked || !Scene.IsKeyboardFocusWithin))
                Assert.Inconclusive("Refusing gameplay injection while the room scene is blocked or unfocused.");
            Assert.IsTrue(Scene.HasHealthyFrame);
            Assert.IsTrue(scenePoint.X > 2 && scenePoint.Y > 38 && scenePoint.X < Scene.ActualWidth - 2 && scenePoint.Y < Scene.ActualHeight - 2,
                "The native pointer target must be inside the room scene and below its window chrome.");
            var screenPoint = Scene.PointToScreen(scenePoint);
            var target = new NativePoint(checked((int)Math.Round(screenPoint.X)), checked((int)Math.Round(screenPoint.Y)));
            RequireUnobstructed(target);
            Assert.IsTrue(SetCursorPos(target.X, target.Y));
            Pump(20);
            RequireCurrentPointer();
        }

        private void RequireUnobstructed(NativePoint target)
        {
            var hit = WindowFromPoint(target);
            if (hit == 0 || GetAncestor(hit, 2) != Handle)
                Assert.Inconclusive("Refusing native injection: another native window obstructs the room target.");
        }

        private void RequireCurrentPointer()
        {
            RequireRoomForeground();
            Assert.IsTrue(GetCursorPos(out var cursor));
            RequireUnobstructed(cursor);
            var expectedScenePoint = Scene.PointFromScreen(new(cursor.X, cursor.Y));
            if (expectedScenePoint.X <= 2 || expectedScenePoint.Y <= 38 ||
                expectedScenePoint.X >= Scene.ActualWidth - 2 || expectedScenePoint.Y >= Scene.ActualHeight - 2)
                Assert.Inconclusive("Refusing native injection: cursor left the safe ordinary-room scene bounds.");
        }

        internal void Wheel(int delta, bool allowMenu = false)
        {
            MoveTo(new(Scene.ActualWidth / 2, Scene.ActualHeight / 2), allowMenu);
            RequireCurrentPointer();
            SendOne(new() { Type = 0, Data = new() { Mouse = new() { Flags = 0x0800, MouseData = unchecked((uint)delta) } } });
            Pump(20);
            RequireRoomForeground();
        }

        internal void KeyDown(ushort key)
        {
            RequireCurrentPointer();
            Assert.IsFalse(ownedKeys.Contains(key));
            var sent = SendInput(1, [KeyboardInput(key, up: false)], Marshal.SizeOf<NativeInput>());
            if (sent == 1) ownedKeys.Add(key);
            Assert.AreEqual(1u, sent, "Win32 did not inject the owned key Down.");
            Pump(20);
            RequireRoomForeground();
        }

        internal void RepeatKeyDown(ushort key)
        {
            RequireCurrentPointer();
            Assert.IsTrue(ownedKeys.Contains(key));
            SendOne(KeyboardInput(key, up: false));
            Pump(20);
            RequireRoomForeground();
        }

        internal void KeyUp(ushort key)
        {
            RequireCurrentPointer();
            Assert.IsTrue(ownedKeys.Contains(key));
            SendOne(KeyboardInput(key, up: true));
            ownedKeys.Remove(key);
            Pump(20);
            RequireRoomForeground();
        }

        internal void Tap(ushort key) { KeyDown(key); KeyUp(key); }

        internal void MouseDown()
        {
            RequireCurrentPointer();
            Assert.IsFalse(ownedLeftButton);
            var sent = SendInput(1, [MouseInput(up: false)], Marshal.SizeOf<NativeInput>());
            if (sent == 1) ownedLeftButton = true;
            Assert.AreEqual(1u, sent, "Win32 did not inject the owned left-button Down.");
            Pump(20);
            RequireRoomForeground();
        }

        internal void MouseUp()
        {
            RequireCurrentPointer();
            Assert.IsTrue(ownedLeftButton);
            SendOne(MouseInput(up: true));
            ownedLeftButton = false;
            Pump(20);
        }

        internal void ClickElement(FrameworkElement element)
        {
            element.UpdateLayout();
            Assert.IsTrue(element.ActualWidth > 4 && element.ActualHeight > 4);
            MoveTo(element.TranslatePoint(new(element.ActualWidth / 2, element.ActualHeight / 2), Scene), allowMenu: true);
            MouseDown();
            MouseUp(); // The real button Up is allowed to activate the control-center HWND.
            Pump(100);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var foreground = GetForegroundWindow();
            var room = host?.LocalWindow;
            var controlCenter = host is null ? null : typeof(DangoApplicationHost)
                .GetField("controlCenterWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(host) as ControlCenterWindow;
            var ownsForeground = (room is not null && foreground == new WindowInteropHelper(room).Handle) ||
                (controlCenter is not null && foreground == new WindowInteropHelper(controlCenter).Handle);
            try
            {
                // Never release a key/button not injected and still owned by this fixture.
                var releases = ownedKeys.Select(key => KeyboardInput(key, up: true)).ToList();
                if (ownedLeftButton) releases.Add(MouseInput(up: true));
                if (releases.Count > 0)
                    _ = SendInput((uint)releases.Count, releases.ToArray(), Marshal.SizeOf<NativeInput>());
            }
            finally
            {
                ownedKeys.Clear();
                ownedLeftButton = false;
                try { host?.Dispose(); }
                finally
                {
                    _ = SetCursorPos(previousCursor.X, previousCursor.Y);
                    if (ownsForeground && previousForeground != 0 && IsWindow(previousForeground))
                        _ = SetForegroundWindow(previousForeground);
                }
            }
        }
    }

    private static void WaitUntil(Func<bool> condition, int milliseconds, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < milliseconds) Pump(10);
        Assert.IsTrue(condition(), message);
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;

    private const ushort VkLeftButton = 0x01;
    private const ushort VkEscape = 0x1B;
    private const ushort VkD = 0x44;
    private const ushort VkLeftShift = 0xA0;
    private static readonly ushort[] ObservedInputKeys =
        [VkLeftButton, 0x02, 0x04, 0x05, 0x06, VkEscape, 0x20, 0x41, VkD, 0x53, VkLeftShift, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C];

    private static NativeInput MouseInput(bool up) => new()
    { Type = 0, Data = new() { Mouse = new() { Flags = up ? 0x0004u : 0x0002u } } };
    private static NativeInput KeyboardInput(ushort key, bool up) => new()
    { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Flags = up ? 0x0002u : 0 } } };
    private static void SendOne(NativeInput input) =>
        Assert.AreEqual(1u, SendInput(1, [input], Marshal.SizeOf<NativeInput>()), "Win32 did not inject the owned input event.");

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    { public int X = x; public int Y = y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    { public uint Type; public NativeInputUnion Data; }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)] public NativeMouseInput Mouse;
        [FieldOffset(0)] public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public nuint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    { public ushort VirtualKey; public ushort ScanCode; public uint Flags; public uint Time; public nuint ExtraInfo; }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsWindow(nint window);
}
