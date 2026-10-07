using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Forms = System.Windows.Forms;
using Shape = System.Windows.Shapes.Shape;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
[DoNotParallelize]
public sealed class DesktopPetRoomProductTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(true)]
    [DataRow(false)]
    public void EachUnpresentedSurfaceKeepsTheRecoverablePorchAndRetryRequiresBoth(bool blockEnvironment)
    {
        using var rig = new DesktopPetRoomFixture();
        rig.Host.EnterDesktopPetRoom();
        var unreadyWindow = rig.Window;
        var missing = blockEnvironment ? (UIElement)rig.Pet.Scene : unreadyWindow;
        missing.Visibility = Visibility.Hidden;
        DesktopPetRoomFixture.Pump(250);
        Assert.AreNotEqual(LocalRoomPresentation.DesktopPet, rig.Host.LocalPresentation,
            "A displayed background or a displayed body alone cannot commit entry.");
        Assert.IsTrue(rig.HomeWindow.IsVisible, "The only recoverable source must remain visible until both surfaces present.");
        Assert.IsFalse(rig.Pet.IsReady);
        Assert.IsFalse(unreadyWindow.InteractionEnabled);
        DesktopPetRoomFixture.WaitUntil(() => !rig.Host.LocalHandoffPending, rig.Runtime);
        Assert.IsFalse(rig.Host.LocalHandoffPending);
        Assert.IsNotNull(rig.Host.LocalRoomFailure);
        Assert.IsTrue(rig.HomeWindow.IsVisible);
        Assert.IsTrue(rig.Home.IsInPorch);
        Assert.IsTrue(rig.Home.OwnsLiveFrameDriver);
        Assert.IsFalse(rig.Driver.OwnsRuntime);
        Assert.IsFalse(rig.Pet.IsVisible);
        if (blockEnvironment) rig.Pet.Scene.Visibility = Visibility.Visible;
        rig.Enter();
        Assert.AreSame(rig.Runtime.LastFrame, rig.Pet.Scene.LastAppliedFrame);
        Assert.AreSame(rig.Pet.Scene.LastAppliedFrame, rig.Window.LastAppliedFrame);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void TwoRealSurfacesRenderOneEntityAndControlCenterCannotAddAnOwner()
    {
        using var rig = new DesktopPetRoomFixture();
        var entity = rig.Runtime.EntityIds.Single();
        rig.Enter();
        var owner = rig.Runtime.FrameDriver;
        Assert.IsNotNull(owner);
        Assert.IsTrue(rig.Driver.OwnsRuntime);
        Assert.IsFalse(rig.Home.OwnsLiveFrameDriver);
        Assert.IsFalse(rig.HomeWindow.IsVisible);
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        var wallpaperSource = rig.Pet.WallpaperSource ?? throw new AssertFailedException("The wallpaper source must exist after entry.");
        Assert.IsNotNull(PresentationSource.FromVisual(rig.Window));
        Assert.AreNotEqual(wallpaperSource.Handle, rig.Window.Handle);
        Assert.AreEqual(0, DesktopPetRoomFixture.Descendants<DeformableDangoVisual>(rig.Pet.Scene).Count(body => body.IsVisible),
            "The wallpaper environment must not render another body.");
        Assert.AreEqual(1, DesktopPetRoomFixture.Descendants<DeformableDangoVisual>(rig.Window).Count(body => body.IsVisible));
        Assert.AreSame(rig.Runtime.LastFrame, rig.Pet.Scene.LastAppliedFrame);
        Assert.AreSame(rig.Pet.Scene.LastAppliedFrame, rig.Window.LastAppliedFrame);
        DesktopPetRoomFixture.Capture(rig.Pet.Scene, "desktop-pet-environment.png");
        DesktopPetRoomFixture.Capture((FrameworkElement)rig.Window.Content, "desktop-pet-transparent-pet.png");
        rig.Host.ShowControlCenter();
        DesktopPetRoomFixture.Pump(100);
        Assert.IsTrue(rig.HomeWindow.IsVisible);
        Assert.IsTrue(rig.Home.IsDangoAway);
        Assert.IsFalse(rig.Home.OwnsLiveFrameDriver);
        Assert.AreSame(owner, rig.Runtime.FrameDriver);
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        Assert.IsNull(rig.Home.RoomNavigation.Authority.RoomId, "Local single-player entry cannot fabricate online admission.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void PhysicalDragReleasesToTheSameNativeFloorWithoutReplacingEntityOrOwner()
    {
        using var rig = new DesktopPetRoomFixture();
        rig.Enter();
        var entity = rig.Runtime.EntityIds.Single();
        var owner = rig.Runtime.FrameDriver;
        var floor = rig.Runtime.LocalGroundY;
        // Entry readiness proves presentation health, not that the native soft body has finished falling.
        // Ground contact plus max node speed below .05 native units/s gives this comparison a quiet ground baseline.
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.LastPhysicsFrame?.Bodies.Single() is
        { IsGrounded: true, MaximumSpeed: < .05f }, rig.Runtime, timeoutMilliseconds: 4500,
            diagnostic: () => $"Ground baseline not ready; FloorAtEntry={floor}; FloorCurrent={rig.Runtime.LocalGroundY}; " +
                $"Current={rig.Runtime.CurrentSelectedCenter}; Native=[{BodyDiagnostic(rig.Runtime.LastPhysicsFrame?.Bodies.Single())}]; " +
                $"Scheduler={rig.Scheduler.Snapshot}; Driver={rig.Driver.DesktopPetPointerDiagnostics}; Host={rig.Pet.LastSurfaceFailure}");
        var initial = rig.Runtime.CurrentSelectedCenter!.Value;
        var initialPhysics = rig.Runtime.LastPhysicsFrame!;
        var initialBody = initialPhysics.Bodies.Single();
        LogicalPoint? releasedCenter = null;
        DangoBodyState? releasedBody = null;
        string? firstContactFrame = null, firstSettledFrame = null;
        var lifted = new LogicalPoint(initial.X + 100, initial.Y - 180);
        var point = rig.ToScene(new(initial.X, initial.Y + rig.Runtime.NeutralBodyGroundOffset));
        var target = rig.ToScene(new(lifted.X, lifted.Y + rig.Runtime.NeutralBodyGroundOffset));
        static string BodyDiagnostic(DangoBodyState? body)
        {
            if (body is null) return "none";
            var nodeY = Enumerable.Range(0, body.PointCount).Select(index => body.Contour[index * 2 + 1]).ToArray();
            return $"Grounded={body.IsGrounded},GroundContacts={body.GroundContactCount},Nodes={body.PointCount}," +
                $"Core=({body.CoreX},{body.CoreY}),Centroid=({body.CentroidX},{body.CentroidY}),MaxSpeed={body.MaximumSpeed}," +
                $"NativeNodeY=[{nodeY.Min()},{nodeY.Max()}]";
        }
        string DragDiagnostic() => $"Initial={initial}; Target={lifted}; Current={rig.Runtime.CurrentSelectedCenter}; " +
            $"ReleasedCenter={releasedCenter}; FloorInitial={floor}; FloorCurrent={rig.Runtime.LocalGroundY}; " +
            $"InitialBody=[{BodyDiagnostic(initialBody)}]; ReleasedBody=[{BodyDiagnostic(releasedBody)}]; " +
            $"CurrentBody=[{BodyDiagnostic(rig.Runtime.LastPhysicsFrame?.Bodies.Single())}]; " +
            $"InitialContactSteps={initialPhysics.ContactStepCount}; CurrentContactSteps={rig.Runtime.LastPhysicsFrame?.ContactStepCount}; " +
            $"FirstContact=[{firstContactFrame}]; FirstGroundedWithinTen=[{firstSettledFrame}]; " +
            $"SettledErrorY={Math.Abs((rig.Runtime.CurrentSelectedCenter?.Y ?? double.NaN) - initial.Y)}; " +
            $"Dragging={rig.Runtime.IsDragging}; PointerActive={rig.Driver.IsPointerActive}; " +
            $"SceneHealthy={rig.Pet.Scene.HasHealthyFrame}; PetHealthy={rig.Pet.PetWindow?.HasHealthyFrame}; " +
            $"Owner={rig.Driver.OwnsRuntime}; FrameSteps={rig.Scheduler.Snapshot.SuccessfulSteps}; " +
            $"LocalFailure={rig.Host.LocalRoomFailure}; NativeError={rig.Runtime.LastError}; " +
            $"DriverDiagnostic={rig.Driver.DesktopPetPointerDiagnostics}; HostFailure={rig.Pet.LastSurfaceFailure}";
        Assert.IsTrue(rig.Driver.PointerDown(point, nowMilliseconds: 0));
        Assert.IsTrue(rig.Driver.PointerMove(target, nowMilliseconds: 20));
        Assert.IsTrue(rig.Runtime.IsDragging, "Desktop-pet plain movement must convert to a real native physical drag.");
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.CurrentSelectedCenter!.Value.Y < initial.Y - 65, rig.Runtime,
            diagnostic: DragDiagnostic);
        var releasedAt = rig.Runtime.CurrentSelectedCenter!.Value;
        releasedCenter = releasedAt;
        releasedBody = rig.Runtime.LastPhysicsFrame!.Bodies.Single();
        Assert.IsTrue(rig.Driver.PointerUp(target, nowMilliseconds: 500));
        Assert.IsFalse(rig.Runtime.IsDragging);
        Assert.IsFalse(rig.Driver.IsPointerActive);
        void ObservePostReleaseFrame()
        {
            if (firstContactFrame is not null && firstSettledFrame is not null) return;
            if (rig.Runtime.LastPhysicsFrame?.Bodies.Single() is not { IsGrounded: true } body ||
                rig.Runtime.CurrentSelectedCenter is not { } center) return;
            var error = Math.Abs(center.Y - initial.Y);
            var frame = $"Step={rig.Scheduler.Snapshot.SuccessfulSteps},Center={center},ErrorY={error}," +
                $"Floor={rig.Runtime.LocalGroundY},Native=[{BodyDiagnostic(body)}]";
            firstContactFrame ??= frame;
            if (error < 10) firstSettledFrame ??= frame;
        }
        // Lock only two real applied frames; a Background pump may return long after first native contact.
        rig.Pet.Scene.FrameApplied += ObservePostReleaseFrame;
        try
        {
            DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.CurrentSelectedCenter!.Value.Y > releasedAt.Y + 35, rig.Runtime,
                diagnostic: () => $"ReleasedAt={releasedAt}; {DragDiagnostic()}");
            DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded &&
                Math.Abs(rig.Runtime.CurrentSelectedCenter!.Value.Y - initial.Y) < 10, rig.Runtime,
                timeoutMilliseconds: 4500, diagnostic: DragDiagnostic);
            Assert.AreEqual(floor, rig.Runtime.LocalGroundY, "The application-facing surface must not create a second screen-edge floor.");
            Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
            Assert.AreSame(owner, rig.Runtime.FrameDriver);
            Assert.IsTrue(Math.Abs(rig.Runtime.CurrentSelectedCenter!.Value.Y - initial.Y) < 10,
                $"After release, the same entity must settle back onto its original room floor. {DragDiagnostic()}");
        }
        finally { rig.Pet.Scene.FrameApplied -= ObservePostReleaseFrame; }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void WholeRoomHidePausesFramesAndRestoreKeepsPositionAppearanceAndEntity()
    {
        var appearance = new DangoAppearanceConfiguration(new(59, 91, 131), new(119, 47, 79), new(104, 12, 41, 22));
        using var rig = new DesktopPetRoomFixture(appearance);
        rig.Enter();
        var entity = rig.Runtime.EntityIds.Single();
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        var point = rig.ToScene(new(center.X, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        var target = rig.ToScene(new(center.X + 145, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        Assert.IsTrue(rig.Driver.PointerDown(point, nowMilliseconds: 0));
        Assert.IsTrue(rig.Driver.PointerMove(target, nowMilliseconds: 20));
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.CurrentSelectedCenter!.Value.X > center.X + 80, rig.Runtime);
        var visibleWindow = rig.Window;
        rig.Host.HideLocalRoom();
        var hiddenPosition = rig.Runtime.CurrentSelectedCenter!.Value;
        var hiddenFrame = rig.Runtime.LastFrame;
        var hiddenSteps = rig.Scheduler.Snapshot.SuccessfulSteps;
        Assert.IsFalse(rig.Pet.IsVisible);
        Assert.IsFalse(visibleWindow.IsVisible);
        Assert.IsFalse(visibleWindow.InteractionEnabled);
        Assert.IsFalse(visibleWindow.IsPointerCaptured);
        Assert.IsNull(rig.Pet.PetWindow);
        Assert.IsFalse(rig.Driver.IsPointerActive);
        Assert.IsFalse(rig.Runtime.IsDragging);
        Assert.IsFalse(rig.Runtime.IsRunning);
        Assert.IsNull(rig.Runtime.FrameDriver);
        DesktopPetRoomFixture.Pump(400);
        Assert.AreEqual(hiddenSteps, rig.Scheduler.Snapshot.SuccessfulSteps);
        Assert.AreSame(hiddenFrame, rig.Runtime.LastFrame);
        LogicalPoint? firstRestoredPosition = null;
        void Observe() => firstRestoredPosition ??= rig.Runtime.CurrentSelectedCenter;
        rig.Pet.Scene.FrameApplied += Observe;
        try
        {
            rig.Host.RestoreLocalRoom();
            Assert.IsTrue(rig.Host.LocalHandoffPending, "Restore cannot reuse cached dual-surface readiness.");
            Assert.IsFalse(rig.Window.InteractionEnabled);
            rig.WaitForEntry();
        }
        finally { rig.Pet.Scene.FrameApplied -= Observe; }
        var restoredPosition = firstRestoredPosition ?? throw new AssertFailedException("Restore must apply a fresh native frame.");
        Assert.IsTrue(Math.Abs(restoredPosition.X - hiddenPosition.X) < 3,
            "Restore must resume the current entity position, rather than re-enter at the default centre.");
        Assert.IsTrue(Math.Abs(restoredPosition.Y - hiddenPosition.Y) < 3);
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        Assert.AreEqual(appearance, rig.Runtime.EffectiveAppearance);
        var body = DesktopPetRoomFixture.Descendants<DeformableDangoVisual>(rig.Window).Single(value => value.IsVisible);
        Assert.AreEqual(Color.FromRgb(59, 91, 131), ((SolidColorBrush)body.BodyFill).Color);
        Assert.AreSame(rig.Pet.Scene.LastAppliedFrame, rig.Window.LastAppliedFrame);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void CancelledEntryCannotReopenLayersOrReplayTheOldHealthyCallback()
    {
        using var rig = new DesktopPetRoomFixture();
        rig.Host.EnterDesktopPetRoom();
        var attempt = rig.Pet.Generation;
        var unreadyWindow = rig.Window;
        rig.Host.HideLocalRoom();
        rig.Pet.ConfirmReady(attempt);
        DesktopPetRoomFixture.Pump(250);
        Assert.IsFalse(rig.Host.LocalHandoffPending);
        Assert.IsFalse(rig.Pet.IsVisible);
        Assert.IsFalse(unreadyWindow.InteractionEnabled);
        Assert.IsNull(rig.Pet.PetWindow);
        Assert.IsFalse(rig.Driver.OwnsRuntime);
        Assert.IsTrue(rig.HomeWindow.IsVisible);
        Assert.IsTrue(rig.Home.OwnsLiveFrameDriver);
        rig.Enter();
        Assert.AreNotEqual(attempt, rig.Pet.Generation);
        Assert.IsTrue(rig.Driver.OwnsRuntime);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void CachedPorchFrameAndPrematureConfirmationCannotCommitTheNewDualSurfaceEntry()
    {
        using var rig = new DesktopPetRoomFixture();
        var oldFrame = rig.Runtime.LastFrame!;
        rig.Host.EnterDesktopPetRoom();
        var attempt = rig.Pet.Generation;
        _ = rig.Pet.Scene.ApplyFrame(oldFrame, rig.Runtime);
        rig.Pet.ConfirmReady(attempt);
        Assert.IsTrue(rig.Host.LocalHandoffPending);
        Assert.IsTrue(rig.HomeWindow.IsVisible);
        Assert.IsFalse(rig.Pet.IsReady);
        Assert.IsFalse(rig.Window.InteractionEnabled);
        rig.WaitForEntry();
        Assert.AreNotSame(oldFrame, rig.Pet.Scene.LastAppliedFrame,
            "Entry requires a newly advanced native frame from the new driver generation.");
        Assert.AreSame(rig.Pet.Scene.LastAppliedFrame, rig.Window.LastAppliedFrame);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void FailedHiddenRestorePreservesSelectionAndRetryKeepsEntityPosition()
    {
        using var rig = new DesktopPetRoomFixture();
        rig.Enter();
        var entity = rig.Runtime.EntityIds.Single();
        var initial = rig.Runtime.CurrentSelectedCenter!.Value;
        var point = rig.ToScene(new(initial.X, initial.Y + rig.Runtime.NeutralBodyGroundOffset));
        var moved = rig.ToScene(new(initial.X + 145, initial.Y + rig.Runtime.NeutralBodyGroundOffset));
        Assert.IsTrue(rig.Driver.PointerDown(point, nowMilliseconds: 0));
        Assert.IsTrue(rig.Driver.PointerMove(moved, nowMilliseconds: 20));
        DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.CurrentSelectedCenter!.Value.X > initial.X + 80, rig.Runtime);
        Assert.IsTrue(rig.Driver.PointerUp(moved, nowMilliseconds: 500));
        DesktopPetRoomFixture.Pump(400);
        rig.Host.HideLocalRoom();
        var hiddenPosition = rig.Runtime.CurrentSelectedCenter!.Value;
        rig.Host.RestoreLocalRoom();
        var failedWindow = rig.Window;
        failedWindow.Visibility = Visibility.Hidden;
        DesktopPetRoomFixture.WaitUntil(() => !rig.Host.LocalHandoffPending, rig.Runtime);
        Assert.AreEqual(LocalRoomPresentation.Hidden, rig.Host.LocalPresentation);
        Assert.IsTrue(DesktopPetRoomFixture.Field<bool>(rig.Host, "localRoomSelected"),
            "A failed hidden-room restore must retain the current desktop-pet room for retry.");
        Assert.IsNotNull(rig.Host.LocalRoomFailure);
        Assert.IsTrue(rig.Home.IsDangoAway);
        Assert.IsFalse(rig.Home.OwnsLiveFrameDriver, "Recovery UI cannot take the failed hidden room's runtime owner.");
        Assert.IsFalse(rig.Runtime.IsRunning);
        Assert.IsNull(rig.Runtime.FrameDriver);
        Assert.IsFalse(rig.Pet.IsVisible);
        Assert.IsNull(rig.Pet.PetWindow);
        Assert.IsFalse(failedWindow.IsVisible);
        Assert.IsFalse(failedWindow.InteractionEnabled);
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        var retryPosition = rig.Runtime.CurrentSelectedCenter!.Value;
        Assert.IsTrue(Math.Abs(retryPosition.X - hiddenPosition.X) < 3,
            "A partial failed restore cannot reset the selected entity to its entry position.");
        Assert.IsTrue(Math.Abs(retryPosition.Y - hiddenPosition.Y) < 3);
        rig.Host.RestoreLocalRoom();
        Assert.IsTrue(rig.Host.LocalHandoffPending);
        Assert.IsFalse(rig.Window.InteractionEnabled);
        rig.WaitForEntry();
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        Assert.IsTrue(rig.Runtime.CurrentSelectedCenter!.Value.X > initial.X + 60,
            "Retry must keep the deliberately moved entity instead of creating the default centre placement.");
        Assert.IsTrue(rig.Driver.OwnsRuntime);
        Assert.IsFalse(rig.Home.OwnsLiveFrameDriver);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow("top")]
    [DataRow("left")]
    [DataRow("right")]
    [DataRow("bottom")]
    public void NativeExtremeDragKeepsActuallyDrawnBodyAndHatInsideIndependentPrimaryWorkArea(string edge)
    {
        using var rig = new DesktopPetRoomFixture();
        rig.Enter();
        var entity = rig.Runtime.EntityIds.Single();
        var owner = rig.Runtime.FrameDriver;
        var safe = IndependentPrimarySafePixels(rig.Window);
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        var start = rig.ToScene(new(center.X, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        var target = edge switch
        {
            "top" => new LogicalPoint(center.X, -10000),
            "left" => new LogicalPoint(-10000, center.Y - 100),
            "right" => new LogicalPoint(10000, center.Y - 100),
            _ => new LogicalPoint(center.X, 10000),
        };
        Assert.IsTrue(rig.Driver.PointerDown(start, nowMilliseconds: 0));
        Assert.IsTrue(rig.Driver.PointerMove(rig.ToScene(new(target.X, target.Y + rig.Runtime.NeutralBodyGroundOffset)), nowMilliseconds: 20));
        Assert.IsTrue(rig.Runtime.IsDragging);
        rig.Scheduler.DetailedTimingEnabled = true;
        var firstScheduler = rig.Scheduler.Snapshot;
        var firstSteps = firstScheduler.SuccessfulSteps;
        var lastFrame = rig.Pet.Scene.LastAppliedFrame;
        var renderedSamples = 0;
        var reachedEdge = false;
        Exception? observationFailure = null;
        void ObserveFrame()
        {
            if (observationFailure is not null) return;
            try
            {
                Assert.IsTrue(rig.Pet.IsVisible && rig.Pet.IsReady && rig.Driver.OwnsRuntime,
                    $"The real room must stay healthy during {edge} drag. Host={rig.Pet.LastSurfaceFailure}; Driver={rig.Driver.DesktopPetPointerDiagnostics}");
                if (ReferenceEquals(lastFrame, rig.Pet.Scene.LastAppliedFrame)) return;
                lastFrame = rig.Pet.Scene.LastAppliedFrame;
                Assert.IsTrue(rig.Window.HasHealthyFrame);
                Assert.AreSame(lastFrame, rig.Window.LastAppliedFrame);
                var paint = DrawnBodyAndHatPixels(rig.Window);
                Assert.IsFalse(paint.IsEmpty, "A healthy current native frame must have a real rendered body/hat footprint.");
                Assert.IsTrue(paint.Left >= safe.Left - 1 && paint.Top >= safe.Top - 1 &&
                    paint.Right <= safe.Right + 1 && paint.Bottom <= safe.Bottom + 1,
                    $"Actual {edge} body/hat footprint {paint} crosses independent work safety {safe}. " +
                    $"Driver={rig.Driver.DesktopPetPointerDiagnostics}; Host={rig.Pet.LastSurfaceFailure}");
                renderedSamples++;
                var dpi = VisualTreeHelper.GetDpi(rig.Window);
                reachedEdge |= edge switch
                {
                    "top" => paint.Top - safe.Top < 40 * dpi.DpiScaleY,
                    "left" => paint.Left - safe.Left < 40 * dpi.DpiScaleX,
                    "right" => safe.Right - paint.Right < 40 * dpi.DpiScaleX,
                    _ => rig.Runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded,
                };
            }
            catch (Exception error) { observationFailure = error; }
        }
        // The host subscribed first and has applied this frame to the real pet visual before this observer runs.
        // A Background-priority dispatcher pump can return after many render callbacks, so sample each applied frame.
        rig.Pet.Scene.FrameApplied += ObserveFrame;
        try
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 2500)
            {
                DesktopPetRoomFixture.Pump(15);
                Assert.IsTrue(rig.Pet.IsVisible && rig.Pet.IsReady && rig.Driver.OwnsRuntime,
                    $"The real room must stay healthy during {edge} drag. Host={rig.Pet.LastSurfaceFailure}; Driver={rig.Driver.DesktopPetPointerDiagnostics}");
            }
        }
        finally { rig.Pet.Scene.FrameApplied -= ObserveFrame; }
        // An assertion escaping FrameApplied would be caught by the production scheduler instead of failing this test.
        if (observationFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(observationFailure).Throw();
        string ExtremeDiagnostic() => $"Edge={edge}; RenderedSamples={renderedSamples}; " +
            $"FirstSteps={firstSteps}; CurrentSteps={rig.Scheduler.Snapshot.SuccessfulSteps}; " +
            $"FirstScheduler={firstScheduler}; Scheduler={rig.Scheduler.Snapshot}; LastStageTiming={rig.Scheduler.LastStageTiming}; " +
            $"LastSampleIsRuntime={ReferenceEquals(lastFrame, rig.Runtime.LastFrame)}; " +
            $"RuntimeIsScene={ReferenceEquals(rig.Runtime.LastFrame, rig.Pet.Scene.LastAppliedFrame)}; " +
            $"SceneIsPet={ReferenceEquals(rig.Pet.Scene.LastAppliedFrame, rig.Pet.PetWindow?.LastAppliedFrame)}; " +
            $"Owner={rig.Driver.OwnsRuntime}; Running={rig.Runtime.IsRunning}; Dragging={rig.Runtime.IsDragging}; " +
            $"HostFailure={rig.Pet.LastSurfaceFailure}; Driver={rig.Driver.DesktopPetPointerDiagnostics}";
        Assert.IsTrue(renderedSamples > 5 && rig.Scheduler.Snapshot.SuccessfulSteps > firstSteps + 5,
            $"Safety must be observed across newly advanced native frames, not one frozen appearance. {ExtremeDiagnostic()}");
        Assert.IsTrue(reachedEdge,
            $"The actual {edge} footprint must approach the requested boundary rather than silently rejecting the extreme drag. {ExtremeDiagnostic()}");
        rig.Driver.CancelPointer();
        Assert.IsFalse(rig.Runtime.IsDragging);
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        Assert.AreSame(owner, rig.Runtime.FrameDriver);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void OwnDisplayChangeMessageReleasesCaptureOwnerAndBothSurfacesThenAllowsFreshRecovery()
    {
        using var rig = new DesktopPetRoomFixture();
        rig.Enter();
        var entity = rig.Runtime.EntityIds.Single();
        var window = rig.Window;
        var petHandle = window.Handle;
        var wallpaperHandle = rig.Pet.WallpaperSource!.Handle;
        var center = rig.Runtime.CurrentSelectedCenter!.Value;
        var start = rig.ToScene(new(center.X, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        var move = rig.ToScene(new(center.X + 9, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        nint ClientParameter(Point scenePoint)
        {
            var screen = rig.Pet.Scene.PointToScreen(scenePoint);
            var origin = window.PointToScreen(default(Point));
            return (nint)((uint)(ushort)(short)Math.Round(screen.X - origin.X) |
                ((uint)(ushort)(short)Math.Round(screen.Y - origin.Y) << 16));
        }
        _ = SendOwnMessage(petHandle, 0x0201, 1, ClientParameter(start));
        Assert.IsTrue(window.IsPointerCaptured, "The targeted own pet HWND must first establish a real native capture.");
        _ = SendOwnMessage(petHandle, 0x0200, 1, ClientParameter(move));
        Assert.IsTrue(rig.Driver.IsPointerActive);
        Assert.IsTrue(rig.Runtime.IsDragging);
        // Invalidate only this owned surface; do not change the machine's display configuration.
        _ = SendOwnMessage(petHandle, 0x007E, 0, 0);
        DesktopPetRoomFixture.WaitUntil(() => !rig.Pet.IsVisible && rig.Runtime.FrameDriver is null, rig.Runtime);
        Assert.AreEqual(LocalRoomPresentation.Hidden, rig.Host.LocalPresentation);
        Assert.IsFalse(window.IsPointerCaptured);
        Assert.IsFalse(window.InteractionEnabled);
        Assert.IsFalse(rig.Driver.IsPointerActive);
        Assert.IsFalse(rig.Runtime.IsDragging);
        Assert.IsFalse(rig.Runtime.IsRunning);
        Assert.IsFalse(IsWindow(petHandle));
        Assert.IsFalse(IsWindow(wallpaperHandle));
        Assert.IsTrue(DesktopPetRoomFixture.Field<bool>(rig.Host, "localRoomSelected"));
        rig.Host.RestoreLocalRoom();
        Assert.IsTrue(rig.Host.LocalHandoffPending);
        Assert.IsFalse(rig.Window.InteractionEnabled);
        rig.WaitForEntry();
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        Assert.IsTrue(rig.Driver.OwnsRuntime);
        Assert.IsFalse(rig.Home.OwnsLiveFrameDriver);
    }

    private static Rect IndependentPrimarySafePixels(Window window)
    {
        var work = (Forms.Screen.PrimaryScreen ?? throw new AssertFailedException("The test needs its primary display geometry.")).WorkingArea;
        return new(work.Left, work.Top, work.Width, work.Height);
    }

    private static Rect DrawnBodyAndHatPixels(Window window)
    {
        var result = Rect.Empty;
        foreach (var shape in DesktopPetRoomFixture.Descendants<Shape>(window))
        {
            if (!shape.IsVisible || shape.Opacity <= 0) continue;
            static bool Painted(Brush? brush) => brush is not null && brush.Opacity > 0 &&
                brush is not SolidColorBrush { Color.A: 0 };
            var fill = Painted(shape.Fill);
            var stroke = Painted(shape.Stroke) && shape.StrokeThickness > 0;
            if (!fill && !stroke) continue;
            var local = stroke ? shape.RenderedGeometry.GetRenderBounds(new Pen(shape.Stroke, shape.StrokeThickness))
                : shape.RenderedGeometry.Bounds;
            if (local.IsEmpty) continue;
            var inWindow = shape.TransformToAncestor(window).TransformBounds(local);
            var first = window.PointToScreen(inWindow.TopLeft);
            var last = window.PointToScreen(inWindow.BottomRight);
            result.Union(new Rect(first, last));
        }
        return result;
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ReturningToPorchRetainsSourceUntilHealthyTargetThenDestroysBothRoomSurfaces()
    {
        using var rig = new DesktopPetRoomFixture();
        rig.Enter();
        var entity = rig.Runtime.EntityIds.Single();
        var wallpaper = rig.Pet.WallpaperSource!.Handle;
        var petWindow = rig.Window;
        var pet = petWindow.Handle;
        var porch = (PorchSceneView)rig.Home.FindName("PorchScene");
        porch.Visibility = Visibility.Hidden;
        rig.Host.ReturnToPorch();
        DesktopPetRoomFixture.Pump(150);
        Assert.IsTrue(rig.Host.LocalHandoffPending);
        Assert.IsTrue(rig.Pet.IsVisible, "The frozen room source remains until the target porch has really presented.");
        Assert.IsFalse(petWindow.InteractionEnabled);
        porch.Visibility = Visibility.Visible;
        DesktopPetRoomFixture.WaitUntil(() => !rig.Host.LocalHandoffPending &&
            rig.Host.LocalPresentation == LocalRoomPresentation.Porch, rig.Runtime);
        Assert.IsFalse(rig.Pet.IsVisible);
        Assert.IsFalse(IsWindow(wallpaper), "Return must release the wallpaper source HWND.");
        Assert.IsFalse(IsWindow(pet), "Return must release the transparent interactive HWND.");
        Assert.IsFalse(rig.Driver.OwnsRuntime);
        Assert.IsTrue(rig.Home.IsInPorch);
        Assert.IsTrue(rig.Home.OwnsLiveFrameDriver);
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        Assert.IsFalse(rig.Home.IsDangoAway);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendOwnMessage(nint window, uint message, nint wParam, nint lParam);
}

internal sealed class DesktopPetRoomFixture : IDisposable
{
    internal DangoApplicationHost Host { get; }
    internal StackedDangoSceneRuntime Runtime { get; }
    internal ControlCenterWindow HomeWindow { get; }
    internal ProductHomeView Home => HomeWindow.ProductHome;
    internal DesktopPetRoomHost Pet => Host.DesktopPetHost!;
    internal DesktopPetDangoWindow Window => Pet.PetWindow ?? throw new AssertFailedException("The active desktop-pet surface was not created.");
    internal LocalRoomSceneDriver Driver => Field<LocalRoomSceneDriver>(Host, "localRoomDriver");
    internal HostFrameScheduler Scheduler => Field<HostFrameScheduler>(Driver, "scheduler");

    internal DesktopPetRoomFixture(DangoAppearanceConfiguration? savedAppearance = null)
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "desktop-pet-layered", "tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (savedAppearance is { } choice)
        {
            var seed = new AppearanceSettingsSession(Path.Combine(root, "appearance.json"));
            seed.StagePreview(choice);
            Assert.IsTrue(seed.TrySave(), "The fixture must save its fictional appearance before the product host loads it.");
        }
        Host = new(settingsFile: Path.Combine(root, "settings.json"), isolatedDesktopPetRoomReview: true);
        try
        {
            Runtime = Field<StackedDangoSceneRuntime>(Host, "stackedRuntime");
            Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
            if (savedAppearance is { } expected)
                Assert.AreEqual(expected, Runtime.EffectiveAppearance, "The actual host must load the saved fixture appearance.");
            Host.ShowControlCenter();
            HomeWindow = Field<ControlCenterWindow>(Host, "controlCenterWindow");
            Home.ReduceMotion = true;
            Home.ResumePorch();
            WaitUntil(() => Home.IsInPorch && Home.OwnsLiveFrameDriver && Runtime.LastFrame is not null &&
                ((PorchSceneView)Home.FindName("PorchScene")).LiveBody.IsVisible, Runtime);
        }
        catch { Host.Dispose(); throw; }
    }

    internal void Enter()
    {
        Host.EnterDesktopPetRoom();
        Assert.IsTrue(Host.LocalHandoffPending);
        Assert.IsTrue(HomeWindow.IsVisible, "Show alone must leave the source recoverable.");
        Assert.IsFalse(Window.InteractionEnabled);
        WaitForEntry();
    }

    internal Point ToScene(LogicalPoint world)
    {
        var point = Pet.Scene.RoomToScene(world);
        return new(point.X, point.Y);
    }

    internal void WaitForEntry()
    {
        WaitUntil(() => !Host.LocalHandoffPending && Host.LocalPresentation == LocalRoomPresentation.DesktopPet, Runtime);
        Assert.IsTrue(Pet.IsVisible);
        Assert.IsTrue(Pet.IsReady);
        Assert.IsTrue(Pet.Scene.HasHealthyFrame);
        Assert.IsTrue(Window.HasHealthyFrame);
        Assert.IsTrue(Window.InteractionEnabled);
    }

    internal static T Field<T>(object value, string name) =>
        (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;

    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T typed) yield return typed;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }

    internal static void WaitUntil(Func<bool> predicate, StackedDangoSceneRuntime runtime, int timeoutMilliseconds = 4500,
        Func<string>? diagnostic = null)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate() && runtime.LastError is null && watch.ElapsedMilliseconds < timeoutMilliseconds) Pump(15);
        Assert.IsTrue(predicate(), $"Expected actual presentation/physics did not arrive. NativeError={runtime.LastError}. {diagnostic?.Invoke()}");
    }

    internal static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    internal static void Capture(FrameworkElement ownVisual, string fileName)
    {
        Assert.IsTrue(ownVisual.ActualWidth > 0 && ownVisual.ActualHeight > 0);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ownVisual.ActualWidth),
            (int)Math.Ceiling(ownVisual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(ownVisual), null, new Rect(0, 0, ownVisual.ActualWidth, ownVisual.ActualHeight));
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(TestRepository.Root, "artifacts", "desktop-pet-layered", "visual");
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, fileName));
        encoder.Save(output);
    }

    public void Dispose() => Host.Dispose();
}
