using System.Reflection;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics.Configuration;

namespace Dango.Desktop.Tests.DesktopHost.WorkerW;

[TestClass]
public sealed class DesktopPetRoomPhysicsTests
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1d / 60);

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ActualBodyHitAcceptsCoreButLeavesItsBlankReferenceCornersToDesktop()
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        Seed(runtime);
        var frame = runtime.LastFrame!;
        var bounds = DangoHostFrameBinding.Bounds(frame, 0);
        Assert.IsTrue(WorkerWWindow.DesktopPetBodyContains(frame, bounds.Center));
        Assert.IsFalse(WorkerWWindow.DesktopPetBodyContains(frame, new(bounds.X + 1, bounds.Y + 1)),
            "A rectangular proxy corner must not intercept the desktop's own right-click.");
        Assert.IsFalse(WorkerWWindow.DesktopPetBodyContains(frame, new(bounds.Right - 1, bounds.Y + 1)));
        Assert.IsFalse(WorkerWWindow.DesktopPetBodyContains(frame, new(bounds.Right + 1, bounds.Center.Y)));
        Assert.IsFalse(WorkerWWindow.DesktopPetBodyContains(frame, new(double.NaN, bounds.Center.Y)));
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void RehostKeepsEntityAndReplacesRoomSidesWithActualDesktopSafeGroundAndFootprint()
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var session = new DesktopSceneSession();
        session.AttachSurface(new(0, 0, 1920, 900), 0);
        session.Controller!.MoveTo(new(1000, 780));
        runtime.SetViewport(new(0, 0, 1920, 900));
        runtime.Start();
        var input = Input(session);
        for (var i = 0; i < 300; i++) runtime.Tick(input, Step);
        runtime.SetHorizontalBounds(1000, 1900);
        var entity = runtime.EntityIds.Single();
        var desktop = new LogicalRect(16, 16, 900, 668);
        session.AttachSurface(desktop, 0);
        runtime.SetViewport(desktop);
        Assert.IsTrue(WorkerWWindow.TryPlaceDesktopPetRoom(runtime, session, desktop));
        input = Input(session);
        Assert.AreEqual(desktop.Bottom, runtime.LocalGroundY!.Value, 1e-6);
        Assert.AreEqual(runtime.CurrentSelectedCenter, session.Controller!.Snapshot.Dango.VisualBounds.Center,
            "The controller's blank reference margin must not clamp the actual physical core away from the floor.");
        runtime.Tick(input, Step);
        Assert.AreEqual(desktop.Bottom, runtime.LocalGroundY.Value, 1e-6,
            "The first new fixed step must not trigger an unintended automatic rehost.");
        runtime.SetDirectionalControl(new(true, false, false, false));
        for (var i = 0; i < 1800; i++) runtime.Tick(input, Step);
        Assert.IsTrue(runtime.CurrentSelectedCenter!.Value.X < desktop.X + 180,
            $"The previous room's 1000-DIP left wall must not survive the desktop handoff. core={runtime.CurrentSelectedCenter}, ground={runtime.LocalGroundY}, error={runtime.LastError}");
        AssertFootprint(runtime, desktop);
        runtime.SetDirectionalControl(new(false, true, false, false));
        for (var i = 0; i < 2200; i++) runtime.Tick(input, Step);
        Assert.IsTrue(runtime.CurrentSelectedCenter!.Value.X > desktop.Right - 180);
        AssertFootprint(runtime, desktop);
        Assert.AreEqual(desktop.Bottom, runtime.LocalGroundY.Value, 1e-6);
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.IsNull(runtime.LastError);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void SuspendFreezesVisiblePictureButReleasesOwnerAndHideInvalidatesReadiness()
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var session = Seed(runtime);
        var host = new WorkerWWindow(session, runtime);
        try
        {
            var scheduler = Field<HostFrameScheduler>(host, "motionScheduler");
            Set(host, "desktopPetRoomMode", true);
            Set(host, "isVisible", true);
            host.Opacity = 1;
            scheduler.Start();
            Field<DispatcherTimer>(host, "shellTimer").Start();
            runtime.BeginPhysicalDrag(runtime.CurrentSelectedCenter!.Value);
            Assert.IsTrue(runtime.IsDragging);
            Assert.IsTrue(host.OwnsDesktopPetRuntime);
            host.SuspendDesktopPetRoom();
            Assert.IsFalse(host.OwnsDesktopPetRuntime);
            Assert.IsNull(runtime.FrameDriver);
            Assert.IsFalse(runtime.IsRunning);
            Assert.IsFalse(runtime.IsDragging);
            Assert.IsFalse(Field<DispatcherTimer>(host, "shellTimer").IsEnabled,
                "A frozen source must not inspect or recover Shell while another owner enters the porch.");
            Assert.IsFalse(host.DesktopPetRoomReady);
            Assert.AreEqual(1, host.Opacity, "Keep the last rendered picture until the porch is ready.");
            host.SetVisibility(false);
            Assert.AreEqual(0, host.Opacity);
            Assert.IsFalse(host.GetSnapshot().IsVisible);
            Assert.IsFalse(host.ConfirmDesktopPetRoom(1));
        }
        finally { host.Close(); }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void FailureClearsDragAndOwnerBeforeDeferredRecoveryNotification()
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var session = Seed(runtime);
        var host = new WorkerWWindow(session, runtime);
        try
        {
            Set(host, "desktopPetRoomMode", true);
            Set(host, "desktopPetAttempt", 41L);
            Field<HostFrameScheduler>(host, "motionScheduler").Start();
            runtime.BeginPhysicalDrag(runtime.CurrentSelectedCenter!.Value);
            var failed = 0;
            host.DesktopPetRoomFailed += (attempt, message) =>
            {
                Assert.AreEqual(41L, attempt);
                Assert.AreEqual("fixture fault", message);
                Assert.IsFalse(host.OwnsDesktopPetRuntime);
                Assert.IsNull(runtime.FrameDriver);
                Assert.IsFalse(runtime.IsDragging);
                failed++;
            };
            Method(host, "ReportDesktopPetFailure").Invoke(host, ["fixture fault"]);
            Assert.AreEqual(0, failed, "A failure inside a frame must not start another owner reentrantly.");
            Assert.IsFalse(runtime.IsRunning);
            Assert.IsFalse(runtime.IsDragging);
            Assert.IsFalse(host.OwnsDesktopPetRuntime);
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.AreEqual(1, failed);
            Method(host, "ReportDesktopPetFailure").Invoke(host, ["fixture fault"]);
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.AreEqual(1, failed);
        }
        finally { host.Close(); }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void FailedHostNeverRestartsAttachmentFromAnOldTimerOrBeginCallback()
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var host = new WorkerWWindow(Seed(runtime), runtime);
        try
        {
            Set(host, "desktopPetRoomMode", true);
            Set(host, "desktopPetAttempt", 41L);
            var discoveries = 0;
            host.DesktopPetTargetFinder = () => { discoveries++; return null; };
            Field<WorkerWRecoveryStateMachine>(host, "recoveryState").BeginAttach(DateTimeOffset.Now);
            Method(host, "ReportDesktopPetFailure").Invoke(host, ["fixture fault"]);
            var timer = Field<DispatcherTimer>(host, "shellTimer");
            Assert.IsFalse(timer.IsEnabled);
            Method(host, "BeginAttachment").Invoke(host, []);
            Assert.IsFalse(timer.IsEnabled, "A failure must not be followed by BeginAttachment's unconditional timer Start.");
            timer.Start(); // Simulate a previously queued Shell tick, without touching the real Explorer.
            Method(host, "OnShellTimerTick").Invoke(host, [null, EventArgs.Empty]);
            Assert.IsFalse(timer.IsEnabled);
            Assert.AreEqual(0, discoveries, "No implicit Shell rediscovery or owner reacquisition after failure.");
            Assert.IsFalse(host.OwnsDesktopPetRuntime);
            Assert.IsFalse(host.DesktopPetRoomReady);
        }
        finally { host.Close(); }
    }

    [TestMethod]
    public void UnsafeOrUnknownDesktopAreaRejectsPlacement()
    {
        Assert.IsFalse(WorkerWWindow.ValidDesktopPetArea(new(0, 0, 200, 200)));
        Assert.IsFalse(WorkerWWindow.ValidDesktopPetArea(default));
        Assert.IsFalse(WorkerWWindow.ValidDesktopPetArea(new(double.MaxValue, 0, double.MaxValue, 668)));
        Assert.IsTrue(WorkerWWindow.ValidDesktopPetArea(new(-900, -100, 900, 668)));
    }

    private static DesktopSceneSession Seed(StackedDangoSceneRuntime runtime)
    {
        var session = new DesktopSceneSession();
        session.AttachSurface(new(0, 0, 900, 668), 0);
        runtime.Start();
        for (var i = 0; i < 300; i++) runtime.Tick(Input(session), Step);
        return session;
    }

    private static PhysicsPresentationInput Input(DesktopSceneSession session)
    {
        var controller = session.Controller!;
        return PhysicsPresentationInput.FromSnapshot(controller.Snapshot, controller.SquashAmount,
            controller.GroundFrictionCue, controller.FacingDirection, null);
    }

    private static void AssertFootprint(StackedDangoSceneRuntime runtime, LogicalRect safe)
    {
        var body = runtime.LastPhysicsFrame!.Bodies.Single();
        var center = runtime.CurrentSelectedCenter!.Value;
        var radius = DangoPhysicsDefaults.Current.NodeRadius;
        for (var n = 0; n < body.PointCount; n++)
        {
            var x = body.Contour[2 * n]; var y = body.Contour[2 * n + 1];
            var length = Math.Sqrt(x * x + y * y);
            var nodeX = length > radius ? x * (1 - radius / length) : x;
            Assert.IsTrue(center.X + (nodeX - radius) * 80 >= safe.X - .25);
            Assert.IsTrue(center.X + (nodeX + radius) * 80 <= safe.Right + .25);
        }
    }

    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Set(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, field);
    private static MethodInfo Method(object value, string name) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
}
