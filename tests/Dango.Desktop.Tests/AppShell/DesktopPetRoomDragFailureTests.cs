using System.Runtime.InteropServices;
using System.Windows;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Forms = System.Windows.Forms;

namespace Dango.Desktop.Tests.AppShell;

// Synthetic input enters real owned HWND capture and the production driver/overlay.
// The injected hook platform reads no hardware input and moves no system pointer.
[TestClass]
[DoNotParallelize]
public sealed class DesktopPetRoomDragFailureTests
{
    private const int Move = 0x0200;
    private const int Down = 0x0201;
    private const int Up = 0x0202;

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void HighFrequencyOwnWindowDragAndReleaseKeepBothAppliedSurfacesAndOnePhysicalOwner()
    {
        using var rig = new DragFixture();
        var initial = rig.Runtime.CurrentSelectedCenter!.Value;
        rig.SendWorld(Down, initial, 1);
        rig.AssertCaptured();
        for (var burst = 0; burst < 12; burst++)
        {
            for (var sample = 0; sample < 48; sample++)
                rig.SendWorld(Move, new(initial.X + 80 + sample % 9, initial.Y - 150 - sample % 13), 1);
            DesktopPetRoomFixture.Pump(20);
            rig.AssertHealthy();
            rig.AssertCaptured();
        }
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.CurrentSelectedCenter!.Value.Y < initial.Y - 65,
            rig.Runtime, diagnostic: rig.Diagnostic);
        var release = new LogicalPoint(initial.X + 100, initial.Y - 170);
        rig.SendWorld(Up, release, 0);
        rig.AssertReleased();
        rig.WaitForGround();
        rig.AssertHealthy(minimumObservedFrames: 6);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void FixedOwnOverlayKeepsItsNativeWorkAreaWhileTheRealPhysicalBodyMovesAndReleases()
    {
        using var rig = new DragFixture();
        var work = Forms.Screen.PrimaryScreen!.WorkingArea;
        Assert.IsTrue(GetWindowRect(rig.Window.Handle, out var before));
        Assert.AreEqual(work.Left, before.Left);
        Assert.AreEqual(work.Top, before.Top);
        Assert.AreEqual(work.Right, before.Right);
        Assert.AreEqual(work.Bottom, before.Bottom);
        var initial = rig.Runtime.CurrentSelectedCenter!.Value;
        var target = new LogicalPoint(initial.X + 100, initial.Y - 170);
        rig.SendWorld(Down, initial, 1);
        rig.SendWorld(Move, target, 1);
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.CurrentSelectedCenter!.Value.Y < initial.Y - 65,
            rig.Runtime, diagnostic: rig.Diagnostic);
        rig.AssertHealthy();
        rig.AssertCaptured();
        Assert.IsTrue(GetWindowRect(rig.Window.Handle, out var moved));
        Assert.AreEqual(before, moved, "Real body motion must update the canvas/region without synchronously moving its HWND.");
        rig.SendWorld(Up, target, 0);
        rig.WaitForGround();
        rig.AssertReleased();
        Assert.IsTrue(GetWindowRect(rig.Window.Handle, out var released));
        Assert.AreEqual(before, released);
        rig.AssertHealthy(minimumObservedFrames: 6);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void RepeatedOwnWindowDownMoveUpCannotCloseTheRoomOrLeaveQueuedCaptureFromAnOlderGesture()
    {
        using var rig = new DragFixture();
        for (var gesture = 0; gesture < 16; gesture++)
        {
            var center = rig.Runtime.CurrentSelectedCenter!.Value;
            var target = new LogicalPoint(center.X + (gesture % 2 == 0 ? 45 : -45), center.Y - 95);
            rig.SendWorld(Down, center, 1);
            rig.AssertCaptured();
            for (var sample = 0; sample < 24; sample++) rig.SendWorld(Move, target, 1);
            Assert.IsTrue(rig.Runtime.IsDragging, rig.Diagnostic());
            DesktopPetRoomFixture.Pump(20);
            rig.AssertHealthy();
            rig.SendWorld(Up, target, 0);
            rig.AssertReleased();
            // A late own-window move/up after release cannot act on the next gesture.
            rig.SendWorld(Move, target, 0);
            rig.SendWorld(Up, target, 0);
            rig.AssertReleased();
            DesktopPetRoomFixture.Pump(20);
            rig.AssertHealthy();
        }
        rig.WaitForGround();
        rig.AssertHealthy(minimumObservedFrames: 6);
        Assert.AreEqual(16, rig.Platform.InstallCount);
        Assert.AreEqual(rig.Platform.InstallCount, rig.Platform.UninstallCount);
        Assert.AreEqual(0, rig.Platform.ReadCount, "Directed own WM input must not masquerade as low-level hardware samples.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow("left")]
    [DataRow("right")]
    [DataRow("top")]
    [DataRow("bottom")]
    public void CapturedEdgeOvershootAndReturnBurstKeepNativeFramesHealthyThroughRelease(string edge)
    {
        using var rig = new DragFixture();
        var initial = rig.Runtime.CurrentSelectedCenter!.Value;
        var outside = edge switch
        {
            "left" => new LogicalPoint(-10000, initial.Y - 90),
            "right" => new LogicalPoint(10000, initial.Y - 90),
            "top" => new LogicalPoint(initial.X, -10000),
            _ => new LogicalPoint(initial.X, 10000),
        };
        var returned = new LogicalPoint(initial.X, initial.Y - 110);
        rig.SendWorld(Down, initial, 1);
        rig.AssertCaptured();
        for (var burst = 0; burst < 10; burst++)
        {
            for (var sample = 0; sample < 32; sample++)
                rig.SendWorld(Move, sample % 2 == 0 ? outside : returned, 1);
            DesktopPetRoomFixture.Pump(20);
            rig.AssertHealthy();
            rig.AssertCaptured();
        }
        Assert.IsTrue(rig.Runtime.IsDragging, rig.Diagnostic());
        rig.SendWorld(Up, returned, 0);
        rig.AssertReleased();
        rig.WaitForGround();
        rig.AssertHealthy(minimumObservedFrames: 6);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void CoalescedHookDragAndQueuedReleaseUseRealCaptureDriverAndOverlayWithoutLosingTheRoom()
    {
        using var rig = new DragFixture();
        var initial = rig.Runtime.CurrentSelectedCenter!.Value;
        rig.SendWorld(Down, initial, 1);
        rig.AssertCaptured();
        uint timestamp = 100;
        for (var burst = 0; burst < 12; burst++)
        {
            for (var sample = 0; sample < 64; sample++)
                rig.EmitWorld(Move, new(initial.X + 90 + sample % 12, initial.Y - 165 - sample % 8), timestamp++);
            Assert.IsTrue(rig.Window.IsGestureListening, rig.Diagnostic());
            DesktopPetRoomFixture.Pump(20);
            rig.AssertHealthy();
            rig.AssertCaptured();
        }
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.CurrentSelectedCenter!.Value.Y < initial.Y - 65,
            rig.Runtime, diagnostic: rig.Diagnostic);
        var target = new LogicalPoint(initial.X + 100, initial.Y - 175);
        for (var sample = 0; sample < 128; sample++) rig.EmitWorld(Move, target, timestamp++);
        rig.EmitWorld(Up, target, timestamp++);
        // Samples after the queued UP must not replace its finite pending data.
        rig.EmitWorld(Move, new(-10000, -10000), timestamp++);
        rig.EmitWorld(Up, new(-10000, -10000), timestamp);
        DesktopPetRoomFixture.WaitUntil(() => !rig.Window.IsPointerCaptured && !rig.Runtime.IsDragging,
            rig.Runtime, diagnostic: rig.Diagnostic);
        rig.AssertReleased();
        rig.WaitForGround();
        rig.AssertHealthy(minimumObservedFrames: 6);
        Assert.AreEqual(1, rig.Platform.InstallCount);
        Assert.AreEqual(1, rig.Platform.UninstallCount);
        Assert.AreEqual(12 * 64 + 128 + 1, rig.Platform.ReadCount);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void SleepingBodyWakesAtOwnDownThenDragAndReleaseReconcileThroughTheActualComposition(bool useHook)
    {
        using var rig = new DragFixture();
        rig.PrimeSleepingFrame();
        var presented = rig.Pet.Scene.LastAppliedFrame;
        var initial = rig.Runtime.CurrentSelectedCenter!.Value;
        rig.SendWorld(Down, initial, 1);
        rig.AssertCaptured();
        Assert.IsFalse(rig.Runtime.Doze.Active, rig.Diagnostic());
        Assert.AreNotSame(presented, rig.Runtime.LastFrame, "The real wake must expose the pre-apply record transition.");
        Assert.AreSame(presented, rig.Pet.Scene.LastAppliedFrame);
        Assert.AreSame(presented, rig.Window.LastAppliedFrame);
        rig.Pet.CheckSurfaceHealth();
        rig.AssertHealthy();
        var target = new LogicalPoint(initial.X + 80, initial.Y - 160);
        for (uint sample = 1; sample <= 96; sample++)
        {
            if (useHook) rig.EmitWorld(Move, target, sample);
            else rig.SendWorld(Move, target, 1);
        }
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.IsDragging &&
            rig.Runtime.CurrentSelectedCenter!.Value.Y < initial.Y - 65,
            rig.Runtime, diagnostic: rig.Diagnostic);
        rig.AssertHealthy();
        if (useHook) rig.EmitWorld(Up, target, 97);
        else rig.SendWorld(Up, target, 0);
        DesktopPetRoomFixture.WaitUntil(() => !rig.Window.IsPointerCaptured && !rig.Runtime.IsDragging,
            rig.Runtime, diagnostic: rig.Diagnostic);
        rig.AssertReleased();
        rig.WaitForGround();
        rig.AssertHealthy(minimumObservedFrames: 6);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void GroundedOwnClickUsesItsRawReleaseInsteadOfTheNativeTopNodeDragClamp(bool useHook)
    {
        using var rig = new DragFixture();
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        Assert.IsFalse(rig.Runtime.HasLightFeedback);
        rig.SendWorld(Down, center, 1);
        rig.AssertCaptured();
        if (useHook) rig.EmitWorld(Up, center, 1);
        else rig.SendWorld(Up, center, 0);
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.HasLightFeedback, rig.Runtime,
            timeoutMilliseconds: 1000, diagnostic: rig.Diagnostic);
        rig.AssertReleased();
        rig.AssertHealthy();
        Assert.IsNull(rig.Window.CurrentPointerGuard!.Value.PointerClampBounds,
            "Click/petting qualification must not apply the physical drag node clamp.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void GroundedControlledHookShiftStrokeKeepsItsRawTwentyFourWorldHorizontalPettingMotion()
    {
        using var rig = new DragFixture();
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        Assert.IsFalse(rig.Runtime.HasLightFeedback);
        rig.SendWorld(Down, center, 5);
        rig.AssertCaptured();
        for (uint sample = 1; sample <= 4; sample++)
            rig.EmitWorld(Move, new(center.X + 6 * sample, center.Y), sample, shift: true);
        rig.EmitWorld(Up, new(center.X + 24, center.Y), 5, shift: true);
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.HasLightFeedback, rig.Runtime,
            timeoutMilliseconds: 1000, diagnostic: rig.Diagnostic);
        rig.AssertReleased();
        Assert.IsFalse(rig.Runtime.IsDragging, "A genuine 24-world horizontal Shift stroke must remain petting.");
        rig.AssertHealthy();
        Assert.IsNull(rig.Window.CurrentPointerGuard!.Value.PointerClampBounds);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void OwnNativeModifierChangeDuringCapturedGestureCannotInvalidateHealthySurfaces(bool beginsWithShift)
    {
        using var rig = new DragFixture();
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        rig.SendWorld(Down, center, beginsWithShift ? 5 : 1);
        rig.AssertCaptured();
        rig.SendWorld(Move, new(center.X + 24, center.Y), beginsWithShift ? 1 : 5);
        rig.SendWorld(Up, new(center.X + 24, center.Y), beginsWithShift ? 0 : 4);
        DesktopPetRoomFixture.Pump(70);
        rig.AssertReleased();
        rig.AssertHealthy();
        rig.WaitForGround();
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void HookModifierCancellationOnlyEndsTheGestureAndAllowsTheNextRealCapture(bool forbiddenModifier)
    {
        using var rig = new DragFixture();
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        rig.SendWorld(Down, center, 5);
        rig.AssertCaptured();
        rig.EmitWorld(Move, new(center.X + 24, center.Y), 1, shift: true);
        rig.EmitWorld(Move, new(center.X + 32, center.Y), 2, shift: forbiddenModifier,
            forbiddenModifier: forbiddenModifier);
        rig.EmitWorld(Up, new(center.X + 32, center.Y), 3, shift: true);
        DesktopPetRoomFixture.Pump(70);
        rig.AssertReleased();
        rig.AssertHealthy();
        center = rig.Runtime.CurrentSelectedCenter!.Value;
        rig.SendWorld(Down, center, 1);
        rig.AssertCaptured();
        rig.SendWorld(Up, center, 0);
        rig.AssertReleased();
        rig.AssertHealthy();
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void FailedHookUninstallRejectsOnlyNewGesturesThenRecoversWithoutClosingEitherSurface()
    {
        using var rig = new DragFixture();
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        rig.SendWorld(Down, center, 1);
        rig.EmitWorld(Move, new(center.X + 75, center.Y - 150), 1);
        DesktopPetRoomFixture.Pump(40);
        Assert.IsTrue(rig.Runtime.IsDragging, rig.Diagnostic());
        rig.Platform.UninstallSucceeds = false;
        rig.EmitWorld(Up, new(center.X + 75, center.Y - 150), 2);
        DesktopPetRoomFixture.Pump(30);
        rig.AssertReleased();
        Assert.IsTrue(rig.Window.IsHookReleasePending, rig.Diagnostic());
        rig.AssertHealthy();
        center = rig.Runtime.CurrentSelectedCenter!.Value;
        rig.SendWorld(Down, center, 1);
        rig.AssertReleased();
        Assert.AreEqual(1, rig.Platform.InstallCount, "An unremoved hook must prevent installing a second hook.");
        rig.AssertHealthy();
        rig.Platform.UninstallSucceeds = true;
        DesktopPetRoomFixture.WaitUntil(() => !rig.Window.IsHookReleasePending, rig.Runtime, diagnostic: rig.Diagnostic);
        center = rig.Runtime.CurrentSelectedCenter!.Value;
        rig.SendWorld(Down, center, 1);
        rig.AssertCaptured();
        rig.SendWorld(Up, center, 0);
        rig.AssertReleased();
        rig.AssertHealthy();
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void OwnNativeDisplayFailureRetainsBoundedPreShutdownHealthAndActualMessage(bool wallpaper)
    {
        using var rig = new DragFixture();
        var owner = rig.Runtime.FrameDriver;
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        rig.SendWorld(Down, center, 1);
        rig.SendWorld(Move, new(center.X + 70, center.Y - 150), 1);
        Assert.IsTrue(rig.Runtime.IsDragging, rig.Diagnostic());
        var target = wallpaper ? rig.Pet.WallpaperSource!.Handle : rig.Window.Handle;
        _ = SendMessage(target, 0x007E, 0, 0);
        DesktopPetRoomFixture.WaitUntil(() => !rig.Pet.IsVisible && rig.Runtime.FrameDriver is null,
            rig.Runtime, diagnostic: rig.Diagnostic);
        var snapshot = rig.Pet.LastFailureSnapshot ?? throw new AssertFailedException("The failure must survive native shutdown.");
        Assert.AreEqual(0x007E, snapshot.NativeMessage);
        Assert.IsTrue(snapshot.RuntimeOwnerPresent && snapshot.RuntimeRunning && snapshot.SceneHealthy && snapshot.PetHealthy);
        Assert.IsTrue(snapshot.PetFrameMatches);
        Assert.IsTrue(snapshot.Scheduler is { IsRunning: true, AppliedFrames: > 0 });
        Assert.IsNull(snapshot.NativeErrorCode, "A native display message must not invent an exception error code.");
        StringAssert.Contains(snapshot.Reason, wallpaper ? "wallpaper native display message" : "pet native display message");
        Assert.IsNotNull(owner);
        rig.AssertReleased();
        Assert.IsFalse(rig.Runtime.IsRunning);
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
        Assert.IsTrue(json.Length < 4096, "The own-state snapshot must remain bounded to one small report.");
        foreach (var forbidden in new[] { "WorldTarget", "ClampedTarget", "ScreenPoint", "WindowTitle", "Clipboard", "ExceptionMessage" })
            Assert.IsFalse(json.Contains(forbidden, StringComparison.Ordinal), "Failure reports must not contain pointer or external content fields.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void RecordingDriverFailurePreservesTheRealOwnerCaptureAndSurfacesUntilApplicationShutdown()
    {
        using var rig = new DragFixture();
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        rig.SendWorld(Down, center, 1);
        rig.SendWorld(Move, new(center.X + 75, center.Y - 150), 1);
        DesktopPetRoomFixture.Pump(30);
        Assert.IsTrue(rig.Runtime.IsDragging, rig.Diagnostic());
        rig.Pet.RecordDriverFailure();
        var snapshot = rig.Pet.LastFailureSnapshot ?? throw new AssertFailedException("Driver failure must capture the same bounded state.");
        Assert.AreEqual("room driver failed", snapshot.Reason);
        Assert.AreEqual(0, snapshot.NativeMessage);
        Assert.IsNull(snapshot.NativeErrorCode);
        Assert.IsTrue(snapshot.PointerCaptured && snapshot.PointerActive && snapshot.PhysicalDragActive);
        Assert.IsTrue(snapshot.RuntimeOwnerPresent && snapshot.RuntimeRunning);
        rig.AssertHealthy();
        rig.AssertCaptured();
        rig.SendWorld(Up, new(center.X + 75, center.Y - 150), 0);
        rig.AssertReleased();
        rig.AssertHealthy();
    }

    private sealed class DragFixture : IDisposable
    {
        internal readonly ControlledHookPlatform Platform = new();
        internal readonly StackedDangoSceneRuntime Runtime = StackedDangoSceneRuntime.CreateProduct();
        internal readonly DesktopPetRoomHost Pet;
        internal readonly LocalRoomSceneDriver Driver;
        internal DesktopPetDangoWindow Window { get; private set; } = null!;
        private readonly HostFrameScheduler scheduler;
        private object? owner;
        private string? failure, firstUnhealthyFrame;
        private readonly DangoEntityId entity;
        private readonly double? floor;
        private int observedFrames;

        internal DragFixture()
        {
            Pet = new(Platform);
            Driver = new(new DesktopSceneSession(), Runtime);
            scheduler = DesktopPetRoomFixture.Field<HostFrameScheduler>(Driver, "scheduler");
            try
            {
                Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
                Pet.Bind(Driver);
                Pet.HealthyFrameReady += Pet.ConfirmReady;
                Pet.LostDesktop += OnSurfaceFailure;
                Driver.Failed += (_, reason) => { failure = reason; OnSurfaceFailure(); };
                Pet.Scene.FrameApplied += ObserveAppliedFrame;
                var screen = Forms.Screen.PrimaryScreen ?? throw new AssertFailedException("The own fixture requires a primary display.");
                Pet.Show(1, screen);
                Window = Pet.PetWindow ?? throw new AssertFailedException("The real overlay was not created.");
                Driver.Start(Pet.Scene, 1, enterWorld: true);
                DesktopPetRoomFixture.WaitUntil(() => Pet.IsReady, Runtime, diagnostic: Diagnostic);
                owner = Runtime.FrameDriver;
                entity = Runtime.EntityIds.Single();
                floor = Runtime.LocalGroundY;
                WaitForGround();
                AssertHealthy();
            }
            catch { Dispose(); throw; }
        }

        private void ObserveAppliedFrame()
        {
            if (Pet.PetWindow is not { } window) return;
            ++observedFrames;
            if (!window.HasHealthyFrame || !ReferenceEquals(Pet.Scene.LastAppliedFrame, window.LastAppliedFrame))
                firstUnhealthyFrame ??= Diagnostic();
        }

        private void OnSurfaceFailure()
        {
            failure ??= Pet.LastSurfaceFailure;
            Driver.Stop();
            Pet.Hide();
        }

        internal string Diagnostic() => $"Failure={failure}; FirstUnhealthyFrame={firstUnhealthyFrame}; " +
            $"Host={Pet.LastSurfaceFailure}; Driver={Driver.DesktopPetPointerDiagnostics}; " +
            $"Scheduler={scheduler.Snapshot}; NativeError={Runtime.LastError}; " +
            $"Captured={Window?.IsPointerCaptured}; HookListening={Window?.IsGestureListening}; Frames={observedFrames}";

        internal void AssertHealthy(int minimumObservedFrames = 0)
        {
            Assert.IsNull(failure, Diagnostic());
            Assert.IsNull(firstUnhealthyFrame, Diagnostic());
            Assert.IsTrue(Pet.IsReady && Pet.IsVisible && Pet.Scene.HasHealthyFrame && Window.HasHealthyFrame, Diagnostic());
            Assert.AreSame(owner, Runtime.FrameDriver, Diagnostic());
            Assert.IsTrue(Driver.OwnsRuntime, Diagnostic());
            Assert.AreEqual(entity, Runtime.EntityIds.Single(), Diagnostic());
            Assert.AreEqual(floor, Runtime.LocalGroundY, Diagnostic());
            Assert.AreSame(Pet.Scene.LastAppliedFrame, Window.LastAppliedFrame, Diagnostic());
            Assert.IsTrue(observedFrames >= minimumObservedFrames, Diagnostic());
        }

        internal void AssertCaptured()
        {
            Assert.IsTrue(Window.IsPointerCaptured && Window.IsGestureListening && Driver.IsPointerActive, Diagnostic());
        }

        internal void AssertReleased()
        {
            Assert.IsFalse(Window.IsPointerCaptured || Window.IsGestureListening || Driver.IsPointerActive || Runtime.IsDragging,
                Diagnostic());
        }

        internal void WaitForGround()
        {
            DesktopPetRoomFixture.WaitUntil(() => Runtime.LastPhysicsFrame?.Bodies.Single() is
            { IsGrounded: true, MaximumSpeed: < .05f } && ReferenceEquals(Runtime.LastFrame, Window.LastAppliedFrame),
                Runtime, timeoutMilliseconds: 4500, diagnostic: Diagnostic);
        }

        private Point ScreenPoint(LogicalPoint physical)
        {
            var dip = Pet.Scene.RoomToScene(new(physical.X, physical.Y + Runtime.NeutralBodyGroundOffset));
            return Pet.Scene.PointToScreen(new(dip.X, dip.Y));
        }

        internal void SendWorld(int message, LogicalPoint world, int modifiers)
        {
            Assert.IsTrue(IsWindow(Window.Handle), Diagnostic());
            var screen = ScreenPoint(world);
            var local = new NativePoint((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
            Assert.IsTrue(ScreenToClient(Window.Handle, ref local));
            Assert.IsTrue(local.X is >= short.MinValue and <= short.MaxValue && local.Y is >= short.MinValue and <= short.MaxValue,
                "Synthetic own WM coordinates must fit LPARAM without wrapping to another point.");
            var parameter = (nint)((uint)(ushort)(short)local.X | (uint)(ushort)(short)local.Y << 16);
            _ = SendMessage(Window.Handle, (uint)message, modifiers, parameter);
        }

        internal void EmitWorld(int message, LogicalPoint world, uint timestamp, bool shift = false, bool forbiddenModifier = false) =>
            Platform.Emit(message, ScreenPoint(world), timestamp, shift, forbiddenModifier);

        internal void PrimeSleepingFrame()
        {
            var physics = Runtime.LastPhysicsFrame!.Bodies.Single();
            Runtime.Doze.Reset();
            Runtime.Doze.Next = (minimum, _) => minimum;
            for (var step = 0; step < 3000; step++)
                Runtime.Doze.Advance(new(true, physics.CoreX, physics.CoreY), .1, true, true, _ => true);
            Assert.IsTrue(Runtime.Doze.Due);
            DesktopPetRoomFixture.WaitUntil(() => Runtime.Doze.Sleeping &&
                Runtime.LastFrame?.Bodies.Single().DozeSeconds is not null &&
                ReferenceEquals(Runtime.LastFrame, Window.LastAppliedFrame), Runtime, diagnostic: Diagnostic);
            AssertHealthy();
        }

        public void Dispose()
        {
            Platform.UninstallSucceeds = true;
            Pet.Scene.FrameApplied -= ObserveAppliedFrame;
            Driver.Dispose();
            Pet.Dispose();
            Runtime.Dispose();
        }
    }

    private sealed class ControlledHookPlatform : IDesktopPetPointerGesturePlatform
    {
        private NativeMethods.LowLevelMouseProcedure? callback;
        private DesktopPetPointerSample sample;
        internal int InstallCount, UninstallCount, ReadCount;
        internal bool UninstallSucceeds = true;
        public nint Install(NativeMethods.LowLevelMouseProcedure value) { callback = value; return ++InstallCount; }
        public bool Uninstall(nint hook) { UninstallCount++; return UninstallSucceeds; }
        public nint Next(nint hook, int code, nint message, nint data) => 0;
        public DesktopPetPointerSample Read(nint data) { ReadCount++; return sample; }
        internal void Emit(int message, Point point, uint timestamp, bool shift = false, bool forbiddenModifier = false)
        {
            sample = new(point, shift, forbiddenModifier, timestamp);
            _ = (callback ?? throw new AssertFailedException("The real overlay must install the controlled callback."))(0, message, 1);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        internal int X = x;
        internal int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref NativePoint point);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}
