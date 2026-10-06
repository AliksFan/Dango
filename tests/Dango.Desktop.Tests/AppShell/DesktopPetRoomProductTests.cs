using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Tray;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.Interaction;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.AppShell;

/// <summary>Real WorkerW lifecycle checks. Run only in an explicitly opted-in interactive test session.</summary>
[TestClass]
[DoNotParallelize]
public sealed class DesktopPetRoomProductTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("DesktopPetRealShell")]
    public void RealWorkerWEntryHideResumeAndReturnPreserveOneEntityAndTraySelection()
    {
        RequireRealShellOptIn();
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowControlCenter(); Pump(400);
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        window.ProductHome.ResumePorch(); Pump(400);
        var entity = runtime.EntityIds.Single();
        var worker = Field<WorkerWWindow>(host, "workerWWindow");
        EnterDesktopPet(host);
        Assert.IsTrue(host.LocalHandoffPending);
        Assert.IsTrue(window.IsVisible, "Show/attachment alone cannot hide the source before a new healthy body frame.");
        Assert.IsFalse(Property<bool>(worker, "DesktopPetRoomReady"));
        Assert.IsFalse(window.ProductHome.OwnsLiveFrameDriver);
        WaitForHandoff(host);
        Assert.AreEqual(DesktopPetPresentation(), host.LocalPresentation, host.LocalRoomFailure);
        Assert.IsTrue(Property<bool>(host, "DesktopPetSelected"));
        Assert.IsTrue(Property<bool>(worker, "DesktopPetRoomReady"));
        Assert.IsTrue(Property<bool>(worker, "OwnsDesktopPetRuntime"));
        Assert.IsFalse(window.IsVisible);
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.IsNotNull(runtime.LastFrame);
        Assert.IsTrue(runtime.LastFrame.EntityIds.Contains(entity));
        Assert.IsTrue(runtime.IsRunning);
        Assert.IsNull(window.ProductHome.RoomNavigation.Authority.RoomId);
        Assert.IsFalse(window.ProductHome.RoomNavigation.CanSendRoomInput);
        Assert.AreEqual(Visibility.Collapsed, ((UIElement)worker.FindName("IslandVisual")).Visibility);
        Assert.AreEqual(Visibility.Collapsed, ((UIElement)worker.FindName("CushionVisual")).Visibility);

        // Place the real native entity well away from the entry X through the supported
        // local-surface operation, then require real scheduler steps to publish that pose.
        // No frame, entity identity, readiness event or owner is manufactured by the test.
        var scheduler = Field<HostFrameScheduler>(worker, "motionScheduler");
        var safeArea = Field<LogicalRect>(worker, "desktopPetSafeArea");
        var entryX = runtime.CurrentSelectedCenter!.Value.X;
        var requestedX = safeArea.X + safeArea.Width / 3;
        Assert.IsTrue(Math.Abs(requestedX - entryX) > 100,
            "The position-retention fixture must differ substantially from the default entry.");
        var stepsBeforePlacement = scheduler.Snapshot.SuccessfulSteps;
        Assert.IsTrue(WorkerWWindow.TryPlaceDesktopPetRoom(runtime,
            Field<DesktopSceneSession>(host, "sceneSession"), safeArea, requestedX));
        Pump(200);
        Assert.IsTrue(scheduler.Snapshot.SuccessfulSteps > stepsBeforePlacement,
            "Placement must be observed through new actual native steps.");
        Assert.IsTrue(Math.Abs(runtime.CurrentSelectedCenter!.Value.X - entryX) > 80,
            "A default-position fixture cannot detect the hidden-room teleport regression.");
        Assert.IsTrue(Property<bool>(worker, "DesktopPetRoomReady"));

        var roomOwner = runtime.FrameDriver;
        host.ShowControlCenter(); Pump(100);
        Assert.IsTrue(window.IsVisible);
        Assert.IsTrue(window.ProductHome.IsDangoAway);
        Assert.IsFalse(window.ProductHome.OwnsLiveFrameDriver);
        Assert.AreSame(roomOwner, runtime.FrameDriver, "Control-center inspection cannot acquire a second frame owner.");
        var tray = Tray(host);
        Assert.IsTrue(tray.IsSelected());
        Assert.IsTrue(tray.IsVisible());
        var xBeforeHide = runtime.CurrentSelectedCenter!.Value.X;
        var stepsBeforeHide = scheduler.Snapshot.SuccessfulSteps;
        tray.Hide();
        Assert.IsTrue(Property<bool>(host, "DesktopPetSelected"), "Hide preserves this session's selected room.");
        Assert.IsTrue(tray.IsSelected());
        Assert.IsFalse(tray.IsVisible());
        Assert.IsFalse(Property<bool>(worker, "DesktopPetRoomReady"));
        Assert.IsFalse(Property<bool>(worker, "OwnsDesktopPetRuntime"));
        Assert.IsNull(runtime.FrameDriver);
        Assert.IsFalse(runtime.IsRunning);
        Assert.IsFalse(runtime.IsDragging);
        Assert.IsFalse(Field<LightInteractionSession>(worker, "lightInteraction").IsActive);
        var hiddenFrame = runtime.LastFrame;
        Pump(150);
        Assert.AreSame(hiddenFrame, runtime.LastFrame, "A hidden room cannot continue physical stepping.");
        tray.Restore();
        Assert.IsTrue(host.LocalHandoffPending, "Restore must wait for a current frame rather than cached readiness.");
        Assert.IsFalse(Property<bool>(worker, "DesktopPetRoomReady"));
        WaitForHandoff(host);
        Assert.AreEqual(DesktopPetPresentation(), host.LocalPresentation, host.LocalRoomFailure);
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.IsTrue(Property<bool>(worker, "DesktopPetRoomReady"));
        Assert.AreNotSame(hiddenFrame, runtime.LastFrame, "Restoring requires a new real frame.");
        Assert.IsTrue(scheduler.Snapshot.SuccessfulSteps > stepsBeforeHide,
            "A cached position alone cannot satisfy the restored healthy-frame handshake.");
        Assert.AreEqual(xBeforeHide, runtime.CurrentSelectedCenter!.Value.X, 16,
            "Hide/restore must retain the current session X rather than reset to the entry X.");
        tray.Return();
        Assert.IsTrue(host.LocalHandoffPending);
        Assert.IsTrue(worker.GetSnapshot().IsVisible, "Return must keep its desktop source visible until the porch's new body frame.");
        WaitForHandoff(host);
        Assert.AreEqual(LocalRoomPresentation.Porch, host.LocalPresentation, host.LocalRoomFailure);
        Assert.IsFalse(Property<bool>(host, "DesktopPetSelected"));
        Assert.IsFalse(tray.IsSelected());
        Assert.IsFalse(worker.GetSnapshot().IsVisible);
        Assert.IsTrue(window.IsVisible);
        Assert.IsTrue(window.ProductHome.IsInPorch);
        Assert.IsTrue(window.ProductHome.OwnsLiveFrameDriver);
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.IsNull(window.ProductHome.RoomNavigation.Authority.RoomId);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("DesktopPetRealShell")]
    [DataRow(true)]
    [DataRow(false)]
    public void CancellingFirstPendingDesktopPetEntryReleasesOwnerAndModeLockBeforeRecoveringPorch(bool returnToPorch)
    {
        RequireRealShellOptIn();
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowControlCenter(); Pump(400);
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        window.ProductHome.ResumePorch(); Pump(400);
        var entity = runtime.EntityIds.Single();
        var worker = Field<WorkerWWindow>(host, "workerWWindow");
        var coordinator = Field<DesktopHostCoordinator>(host, "desktopHost");
        EnterDesktopPet(host);
        Assert.IsTrue(host.LocalHandoffPending);
        Assert.IsTrue(Property<bool>(worker, "OwnsDesktopPetRuntime"),
            "This regression needs actual WorkerW ownership before cancelling its first composition frame.");
        Assert.IsFalse(Property<bool>(worker, "DesktopPetRoomReady"));
        Assert.IsFalse(Property<bool>(host, "DesktopPetSelected"));
        Assert.IsNotNull(coordinator.GetType().GetField("roomDisplayModeLock", Instance)!.GetValue(coordinator));
        var cancelledGeneration = Field<LocalRoomHandoffGate>(host, "roomHandoff").Generation;

        // No dispatcher/composition pump occurs between entry and cancellation.
        // Real attachment has started, but entry success has not been acknowledged.
        if (returnToPorch) host.ReturnToPorch(); else host.HideLocalRoom();
        Assert.IsFalse(host.LocalHandoffPending);
        Assert.IsFalse(Property<bool>(worker, "OwnsDesktopPetRuntime"),
            "Cancelling a first entry must release the desktop scheduler before resuming the porch.");
        Assert.IsFalse(Property<bool>(worker, "DesktopPetRoomReady"));
        Assert.IsFalse(worker.GetSnapshot().IsVisible);
        Assert.IsNull(coordinator.GetType().GetField("roomDisplayModeLock", Instance)!.GetValue(coordinator),
            "A failed/cancelled unselected entry cannot retain the desktop-pet display-mode lock.");
        Assert.IsFalse(coordinator.CanShowDesktop);
        Assert.IsFalse(Field<bool>(window.ProductHome, "isDesktopPetRoomContext"));
        Assert.IsFalse(Property<bool>(host, "DesktopPetSelected"));
        Assert.IsTrue(Field<LocalRoomHandoffGate>(host, "roomHandoff").Generation > cancelledGeneration);
        Pump(450);
        Assert.IsTrue(window.IsVisible);
        Assert.IsTrue(window.ProductHome.IsInPorch);
        Assert.IsFalse(window.ProductHome.IsDangoAway);
        Assert.IsTrue(window.ProductHome.OwnsLiveFrameDriver,
            "Porch must actually regain ownership after the pending desktop-pet entry is cancelled.");
        Assert.IsFalse(Property<bool>(worker, "OwnsDesktopPetRuntime"));
        Assert.IsFalse(Property<bool>(worker, "DesktopPetRoomReady"));
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.IsNull(window.ProductHome.RoomNavigation.Authority.RoomId);
        Assert.IsFalse(window.ProductHome.RoomNavigation.CanSendRoomInput);
        Assert.IsFalse(Tray(host).IsSelected(), "Late callbacks cannot select a cancelled first entry.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("DesktopPetRealShell")]
    public void OrdinaryRoomAndDesktopPetCannotAcquireConcurrentOwners()
    {
        RequireRealShellOptIn();
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowControlCenter(); Pump(400);
        var entity = runtime.EntityIds.Single();
        host.EnterLocalRoom(); WaitForHandoff(host);
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        var ordinaryOwner = runtime.FrameDriver;
        EnterDesktopPet(host); Pump(100);
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation,
            "A direct request while the ordinary room is selected must not create another room owner.");
        Assert.AreSame(ordinaryOwner, runtime.FrameDriver);
        Assert.IsFalse(Property<bool>(host, "DesktopPetSelected"));
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        host.ReturnToPorch(); WaitForHandoff(host);
        EnterDesktopPet(host); WaitForHandoff(host);
        Assert.AreEqual(DesktopPetPresentation(), host.LocalPresentation, host.LocalRoomFailure);
        var desktopOwner = runtime.FrameDriver;
        Assert.IsNotNull(desktopOwner);
        Assert.IsFalse(host.LocalWindow!.IsVisible);
        host.EnterLocalRoom(); Pump(100);
        Assert.AreEqual(DesktopPetPresentation(), host.LocalPresentation);
        Assert.AreSame(desktopOwner, runtime.FrameDriver);
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        host.ReturnToPorch(); WaitForHandoff(host);
        host.EnterLocalRoom(); WaitForHandoff(host);
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.IsFalse(Property<bool>(host, "DesktopPetSelected"));
        Assert.IsFalse(Property<bool>(Field<WorkerWWindow>(host, "workerWWindow"), "OwnsDesktopPetRuntime"));
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("DesktopPetRealShell")]
    public void HiddenDesktopPetRestoreAttachmentTimeoutPreservesSelectionAndTrayCanRetryRealShell()
    {
        RequireRealShellOptIn();
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowControlCenter(); Pump(400);
        EnterDesktopPet(host); WaitForHandoff(host);
        Assert.AreEqual(DesktopPetPresentation(), host.LocalPresentation, host.LocalRoomFailure);
        var entity = runtime.EntityIds.Single();
        var worker = Field<WorkerWWindow>(host, "workerWWindow");
        var scheduler = Field<HostFrameScheduler>(worker, "motionScheduler");
        var tray = Tray(host);
        Assert.IsTrue(Property<bool>(worker, "DesktopPetRoomReady"));
        tray.Hide();
        Assert.AreEqual(LocalRoomPresentation.Hidden, host.LocalPresentation);
        Assert.IsTrue(Property<bool>(host, "DesktopPetSelected"));
        Assert.IsNull(runtime.FrameDriver);
        Assert.IsFalse(runtime.IsRunning);
        var hiddenFrame = runtime.LastFrame;
        var realFinder = worker.DesktopPetTargetFinder;
        var missingTargetRequests = 0;
        // Only attachment discovery for this failed attempt is replaced. Entry and
        // retry still use the actual Shell, native entity, scheduler and body frames.
        worker.DesktopPetTargetFinder = () => { missingTargetRequests++; return null; };
        try
        {
            var timeout = Stopwatch.StartNew();
            tray.Restore();
            Assert.IsTrue(host.LocalHandoffPending);
            Assert.IsFalse(Property<bool>(worker, "DesktopPetRoomReady"));
            WaitForHandoff(host);
            Assert.IsTrue(missingTargetRequests > 0, "The unavailable discovery must actually be exercised.");
            Assert.IsTrue(timeout.ElapsedMilliseconds >= 2500,
                "This case must reach the finite first-frame timeout, not a synthetic readiness/failure callback.");
            Assert.IsNotNull(host.LocalRoomFailure);
            Assert.IsTrue(Property<bool>(host, "DesktopPetSelected"),
                "A failed restore from Hidden cannot discard the previously selected desktop-pet room.");
            Assert.AreEqual(LocalRoomPresentation.Hidden, host.LocalPresentation);
            Assert.IsTrue(tray.IsSelected());
            Assert.IsFalse(tray.IsVisible());
            Assert.IsTrue(tray.IsDesktopPet?.Invoke() == true);
            Assert.IsFalse(Property<bool>(worker, "DesktopPetRoomReady"));
            Assert.IsFalse(Property<bool>(worker, "OwnsDesktopPetRuntime"));
            Assert.IsFalse(worker.GetSnapshot().IsVisible);
            Assert.IsNull(runtime.FrameDriver);
            Assert.IsFalse(runtime.IsRunning);
            Assert.AreSame(hiddenFrame, runtime.LastFrame,
                "An unavailable target cannot manufacture a healthy native frame.");
            Assert.AreEqual(entity, runtime.EntityIds.Single());

            host.ShowControlCenter(); Pump(100);
            var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
            Assert.IsTrue(window.IsVisible, "The control-center recovery entry must remain usable after timeout.");
            Assert.IsTrue(window.ProductHome.IsDangoAway);
            Assert.IsFalse(window.ProductHome.OwnsLiveFrameDriver);
            Assert.IsNull(runtime.FrameDriver, "Opening recovery UI cannot create a substitute room owner.");
            Assert.IsTrue(Property<bool>(host, "DesktopPetSelected"));
            Assert.IsNull(window.ProductHome.RoomNavigation.Authority.RoomId);

            worker.DesktopPetTargetFinder = realFinder;
            var stepsBeforeRetry = scheduler.Snapshot.SuccessfulSteps;
            tray.Restore();
            Assert.IsTrue(host.LocalHandoffPending);
            Assert.IsFalse(Property<bool>(worker, "DesktopPetRoomReady"));
            WaitForHandoff(host);
            Assert.AreEqual(DesktopPetPresentation(), host.LocalPresentation, host.LocalRoomFailure);
            Assert.IsNull(host.LocalRoomFailure);
            Assert.IsTrue(Property<bool>(host, "DesktopPetSelected"));
            Assert.IsTrue(Property<bool>(worker, "DesktopPetRoomReady"));
            Assert.IsTrue(Property<bool>(worker, "OwnsDesktopPetRuntime"));
            Assert.IsTrue(scheduler.Snapshot.SuccessfulSteps > stepsBeforeRetry,
                "A successful retry must include fresh actual native steps.");
            Assert.AreNotSame(hiddenFrame, runtime.LastFrame);
            Assert.AreEqual(entity, runtime.EntityIds.Single());
            Assert.IsTrue(tray.IsSelected());
            Assert.IsTrue(tray.IsVisible());
            Assert.IsFalse(window.ProductHome.OwnsLiveFrameDriver);
        }
        finally { worker.DesktopPetTargetFinder = realFinder; }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("DesktopPetRealShell")]
    public void UnavailablePhysicsCannotHideTheRecoverySourceOrClaimDesktopPetSuccess()
    {
        RequireRealShellOptIn();
        var directory = NewDirectory();
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(directory, "settings.json"),
            nativeLibraryPath: Path.Combine(directory, "fictional-unavailable.dll"), isolatedLocalRoomReview: true);
        host.ShowControlCenter(); Pump(150);
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        window.ProductHome.ResumePorch(); Pump(150);
        EnterDesktopPet(host);
        Assert.IsTrue(window.IsVisible, "The source must remain reachable during an unsuccessful attempt.");
        WaitForHandoff(host);
        Assert.AreNotEqual(DesktopPetPresentation(), host.LocalPresentation);
        Assert.IsFalse(Property<bool>(host, "DesktopPetSelected"));
        Assert.IsNotNull(host.LocalRoomFailure);
        Assert.IsTrue(window.IsVisible);
        Assert.IsTrue(window.ProductHome.IsInPorch);
        Assert.IsFalse(window.ProductHome.IsDangoAway);
        Assert.IsFalse(Property<bool>(Field<WorkerWWindow>(host, "workerWWindow"), "DesktopPetRoomReady"));
        Assert.IsNull(window.ProductHome.RoomNavigation.Authority.RoomId);
        Assert.IsFalse(window.ProductHome.RoomNavigation.CanSendRoomInput);
        Assert.IsNull(Field<StackedDangoSceneRuntime>(host, "stackedRuntime").FrameDriver);
    }

    private void RequireRealShellOptIn()
    {
        if (Environment.GetEnvironmentVariable("DANGO_RUN_DESKTOP_PET_REAL_SHELL") != "1")
            Assert.Inconclusive("Real desktop-pet WorkerW checks require explicit DANGO_RUN_DESKTOP_PET_REAL_SHELL=1; this is not a synthetic-shell pass.");
        Assert.IsTrue(Environment.UserInteractive, "A real interactive Windows desktop is required.");
        TestContext.WriteLine("Running explicitly opted-in real WorkerW lifecycle. Pointer/menu experience still requires its separate manual gate.");
    }

    private static DangoApplicationHost CreateHost()
    {
        var module = Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
        Assert.IsTrue(File.Exists(module), "The explicit isolated candidate native module is required.");
        Assert.AreEqual("BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module))), "The native candidate must be the approved module.");
        return new(settingsFile: Path.Combine(NewDirectory(), "settings.json"), nativeLibraryPath: module, isolatedLocalRoomReview: true);
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(TestRepository.Root, "artifacts", "desktop-pet-room", "tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void EnterDesktopPet(DangoApplicationHost host)
    {
        var method = typeof(DangoApplicationHost).GetMethod("EnterDesktopPetRoom", Instance);
        Assert.IsNotNull(method);
        method.Invoke(host, null);
    }

    private static LocalRoomPresentation DesktopPetPresentation()
    {
        Assert.IsTrue(Enum.TryParse<LocalRoomPresentation>("DesktopPet", out var value));
        return value;
    }

    private static LocalRoomTrayActions Tray(DangoApplicationHost host) => (LocalRoomTrayActions)typeof(DangoApplicationHost)
        .GetMethod("CreateLocalRoomTrayActions", Instance)!.Invoke(host, null)!;

    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, Instance)!.GetValue(source)!;
    private static T Property<T>(object source, string name)
    {
        var property = source.GetType().GetProperty(name, Instance);
        Assert.IsNotNull(property, $"Missing observable desktop-pet lifecycle property: {name}.");
        return (T)property.GetValue(source)!;
    }

    private static void WaitForHandoff(DangoApplicationHost host)
    {
        var elapsed = Stopwatch.StartNew();
        while (host.LocalHandoffPending && elapsed.ElapsedMilliseconds < 4200) Pump(40);
        Assert.IsFalse(host.LocalHandoffPending, $"The handoff must succeed or report failure within its finite timeout. {host.LocalRoomFailure}");
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
