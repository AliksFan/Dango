using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Rendering.Visuals;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class DesktopPetRoomFrameHealthTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void WakingSleepingSingleEntityWaitsForNextCompositionBeforeJudgingNewestRuntimeFrame()
    {
        using var rig = new DesktopPetRoomFixture();
        WakeSleepingEntityBeforeNextApply(rig);
        var owner = rig.Runtime.FrameDriver;
        var entity = rig.Runtime.EntityIds.Single();

        // Exercise the exact timer path inside the valid interval before the next render.
        rig.Pet.CheckSurfaceHealth();
        Assert.IsTrue(rig.Pet.IsReady, rig.Pet.LastSurfaceFailure);
        Assert.AreEqual(string.Empty, rig.Pet.LastFailureReason);
        DesktopPetRoomFixture.WaitUntil(() =>
            ReferenceEquals(rig.Runtime.LastFrame, rig.Pet.Scene.LastAppliedFrame) &&
            ReferenceEquals(rig.Runtime.LastFrame, rig.Window.LastAppliedFrame), rig.Runtime);
        Assert.IsTrue(rig.Pet.IsReady, rig.Pet.LastSurfaceFailure);
        Assert.IsTrue(rig.Window.InteractionEnabled);
        Assert.AreSame(owner, rig.Runtime.FrameDriver);
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        rig.Driver.CancelPointer();
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void UnappliedWakeFrameFailsTheSecondRunningHealthCheck()
    {
        using var rig = new DesktopPetRoomFixture();
        WakeSleepingEntityBeforeNextApply(rig);
        var window = rig.Window;
        rig.Pet.CheckSurfaceHealth();
        Assert.IsTrue(rig.Pet.IsReady, rig.Pet.LastSurfaceFailure);
        // Do not pump: no new scene frame is applied between these timer-path checks.
        rig.Pet.CheckSurfaceHealth();
        Assert.IsFalse(rig.Pet.IsReady, "A runtime record that never reaches the scene and pet on their shared surface must not get unlimited grace.");
        StringAssert.Contains(rig.Pet.LastFailureReason, "not reconciled");
        Assert.IsFalse(window.InteractionEnabled);
        Assert.IsFalse(rig.Driver.IsPointerActive);
        DesktopPetRoomFixture.WaitUntil(() => !rig.Pet.IsVisible && rig.Runtime.FrameDriver is null, rig.Runtime);
        Assert.IsFalse(rig.Runtime.IsRunning);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void WindowGestureGuardRefreshesFromTheActuallyAppliedPhysicalFrame()
    {
        using var rig = new DesktopPetRoomFixture();
        rig.Enter();
        var before = AssertCurrentGuardProjection(rig);
        var frame = rig.Window.LastAppliedFrame;
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        var down = rig.ToDisplayedScene(new(center.X, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        var lifted = rig.ToDisplayedScene(new(center.X + 80, center.Y - 130 + rig.Runtime.NeutralBodyGroundOffset));
        Assert.IsTrue(rig.Driver.PointerDown(down));
        Assert.IsTrue(rig.Driver.PointerMove(lifted));
        Assert.IsTrue(rig.Runtime.IsDragging);
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.CurrentSelectedCenter!.Value.Y < center.Y - 25 &&
            !ReferenceEquals(frame, rig.Window.LastAppliedFrame), rig.Runtime);
        var current = AssertCurrentGuardProjection(rig);
        Assert.AreNotEqual(before.ScreenBounds, current.ScreenBounds,
            "The product's guard must follow the moved physical body instead of retaining its down bounds.");
        Assert.IsTrue(current.ScreenBounds.Y < before.ScreenBounds.Y);
        Assert.IsTrue(rig.Driver.PointerUp(lifted));
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded, rig.Runtime);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(-10000d, -10000d)]
    [DataRow(10000d, 10000d)]
    public void WindowGestureGuardKeepsRawContactForPettingInsteadOfUsingPhysicalDragAnchor(double x, double y)
    {
        using var rig = new DesktopPetRoomFixture();
        rig.Enter();
        var guard = AssertCurrentGuardProjection(rig);
        Assert.IsNull(guard.PointerClampBounds, "Petting qualification must use actual contact, not the native top-node drag target.");
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        Assert.IsTrue(rig.Driver.PointerDown(rig.ToDisplayedScene(new(center.X, center.Y + rig.Runtime.NeutralBodyGroundOffset)), true));
        var rawScreen = new Point(x, y);
        Assert.IsTrue(rig.Driver.PointerMove(rig.Pet.Scene.PointFromScreen(rawScreen), true));
        var actual = DesktopPetRoomFixture.Field<LogicalPoint>(rig.Driver, "lastClampedPointerWorld");
        var actualScreen = PhysicalPointToScreen(rig, actual);
        var guarded = guard.ClampInput(rawScreen);
        Assert.AreEqual(actualScreen.X, guarded.X, .01,
            "Hook qualification and driver petting must preserve the raw contact through the shared projection.");
        Assert.AreEqual(actualScreen.Y, guarded.Y, .01);
        rig.Driver.CancelPointer();
    }

    private static DesktopPetPointerGestureGuard AssertCurrentGuardProjection(DesktopPetRoomFixture rig)
    {
        var guard = rig.Window.CurrentPointerGuard ?? throw new AssertFailedException("The healthy product frame has no pointer guard.");
        Assert.IsTrue(guard.IsValid);
        var bounds = DangoHostFrameBinding.Bounds(rig.Window.LastAppliedFrame!, 0);
        var expected = new Rect(PhysicalPointToScreen(rig, new(bounds.X, bounds.Y)),
            PhysicalPointToScreen(rig, new(bounds.Right, bounds.Bottom)));
        Assert.AreEqual(expected.X, guard.ScreenBounds.X, .01);
        Assert.AreEqual(expected.Y, guard.ScreenBounds.Y, .01);
        Assert.AreEqual(expected.Width, guard.ScreenBounds.Width, .01);
        Assert.AreEqual(expected.Height, guard.ScreenBounds.Height, .01);
        var vertical = PhysicalPointToScreen(rig, new(bounds.X, bounds.Y + 8)).Y -
            PhysicalPointToScreen(rig, new(bounds.X, bounds.Y)).Y;
        Assert.AreEqual(vertical, guard.MaximumVerticalDistance, .01, "The existing eight-world petting threshold is unchanged.");
        return guard;
    }

    private static Point PhysicalPointToScreen(DesktopPetRoomFixture rig, LogicalPoint point)
    {
        var scenePoint = rig.ToScene(new(point.X + rig.Pet.Scene.PresentationOffset.X,
            point.Y + rig.Runtime.NeutralBodyGroundOffset + rig.Pet.Scene.PresentationOffset.Y));
        var source = System.Windows.PresentationSource.FromVisual(rig.Window) as System.Windows.Interop.HwndSource
            ?? throw new AssertFailedException("The owned pet HWND is unavailable.");
        Assert.AreSame(source, System.Windows.PresentationSource.FromVisual(rig.Pet.Scene));
        Assert.AreEqual(rig.Window.Handle, source.Handle);
        // Visual.PointToScreen rounds its native POINT to integer pixels. Keep the
        // independent HWND-origin/DPI oracle continuous, matching gesture precision.
        var pixels = source.CompositionTarget.TransformToDevice.Transform(scenePoint);
        var origin = rig.Pet.SceneOriginPixels;
        return new(origin.X + pixels.X, origin.Y + pixels.Y);
    }

    private static void WakeSleepingEntityBeforeNextApply(DesktopPetRoomFixture rig)
    {
        rig.Enter();
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded, rig.Runtime);
        var physics = rig.Runtime.LastPhysicsFrame!.Bodies.Single();
        Assert.IsFalse(rig.Runtime.Doze.Active, "The deterministic prime starts from the fixture's awake entity.");
        rig.Runtime.Doze.Reset();
        rig.Runtime.Doze.Next = (minimum, _) => minimum;
        for (var index = 0; index < 3000; index++)
            rig.Runtime.Doze.Advance(new(true, physics.CoreX, physics.CoreY), .1, true, true, _ => true);
        Assert.IsTrue(rig.Runtime.Doze.Due);
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.Doze.Sleeping &&
            rig.Runtime.LastFrame?.Bodies.Single().DozeSeconds is not null &&
            ReferenceEquals(rig.Runtime.LastFrame, rig.Pet.Scene.LastAppliedFrame) &&
            ReferenceEquals(rig.Runtime.LastFrame, rig.Window.LastAppliedFrame), rig.Runtime);
        var presented = rig.Pet.Scene.LastAppliedFrame;
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        var point = rig.ToDisplayedScene(new LogicalPoint(center.X, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        Assert.IsTrue(rig.Driver.PointerDown(point, false));
        Assert.IsFalse(rig.Runtime.Doze.Active);
        Assert.IsNull(rig.Runtime.LastFrame!.Bodies.Single().DozeSeconds);
        Assert.AreNotSame(presented, rig.Runtime.LastFrame,
            "The real wake controller synchronously changes the runtime frame before the next render.");
        Assert.AreSame(presented, rig.Pet.Scene.LastAppliedFrame);
        Assert.AreSame(presented, rig.Window.LastAppliedFrame);
    }
}
