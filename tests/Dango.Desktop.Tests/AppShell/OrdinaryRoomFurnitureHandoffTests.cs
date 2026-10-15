using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Dango.Core.Furniture;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Furniture;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class OrdinaryRoomFurnitureHandoffTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void GroundedSupportWithoutFloorContactPreservesThePreviousGroundCheckpoint()
    {
        using var rig = new ProductRig(); rig.EnterRoom();
        var driver = Field<LocalRoomSceneDriver>(rig.Host, "localRoomDriver");
        typeof(LocalRoomSceneDriver).GetField("safeCheckpoint", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(driver, new RoomSafePosition(500, 0));
        var original = rig.Scene.LastFrame!;
        // The native adapter marks platform support grounded while floor contacts remain zero.
        // Feed that adapter snapshot to the real checkpoint path without changing native state.
        var supported = original.Physics.Bodies.Single() with { IsGrounded = true, GroundContactCount = 0 };
        typeof(DesktopPhysicsScene).GetProperty(nameof(DesktopPhysicsScene.LastFrame))!
            .SetValue(rig.Scene, original with { Physics = original.Physics with { Bodies = [supported] } });
        try { Assert.AreEqual(500, driver.CaptureCheckpoint()!.Value.Position!.X); }
        finally { typeof(DesktopPhysicsScene).GetProperty(nameof(DesktopPhysicsScene.LastFrame))!.SetValue(rig.Scene, original); }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void DissolutionBeforeHealthyFrameConfirmationClearsThePendingRoom()
    {
        using var rig = new ProductRig();
        var dissolved = false;
        var window = rig.Host.LocalWindow!;
        Action? dissolve = null;
        dissolve = () =>
        {
            if (dissolved || !rig.Host.LocalHandoffPending || rig.Furniture.NativeFrame?.Items.Count != 1) return;
            window.Scene.FrameApplied -= dissolve;
            Assert.IsTrue(new LocalRoomCatalogStore(rig.Root).Dissolve(Guid.NewGuid(), rig.RoomId).Success);
            dissolved = true;
        };
        window.Scene.FrameApplied += dissolve;
        try
        {
            rig.Host.EnterCatalogRoom(rig.RoomId, false);
            Until(() => dissolved && !rig.Host.LocalHandoffPending && rig.Home.OwnsLiveFrameDriver,
                "A target dissolved after attach must return ownership to the porch.");
            Pump(150);
            Assert.AreEqual(LocalRoomPresentation.Porch, rig.Host.LocalPresentation);
            Assert.IsFalse(window.IsVisible);
            Assert.IsFalse(rig.Home.IsDangoAway);
            Assert.HasCount(0, rig.Scene.ReadFurnitureCollectionFrame().Items);
        }
        finally { window.Scene.FrameApplied -= dissolve; }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void UnknownCheckpointResultRemainsVisibleAfterReturningToPorch()
    {
        using var rig = new ProductRig();
        typeof(DangoApplicationHost).GetField("roomResumeStore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(rig.Host, new RoomResumeStore(rig.Catalog, new UnknownCommitWriter()));
        rig.EnterRoom();
        rig.Host.ReturnToPorch();
        Until(() => !rig.Host.LocalHandoffPending && rig.Home.OwnsLiveFrameDriver, "Return must retain the porch.");
        var notice = (System.Windows.Controls.Button)rig.Home.FindName("NoticeButton");
        Until(() => Equals(notice.Content, "位置／视角未保存 · 查看"), "The failed source-room checkpoint must remain visible outside the hidden room window.");
        rig.Home.SetNotice(null, null); // The normal home status refresh cannot erase a room failure.
        Assert.AreEqual("位置／视角未保存 · 查看", notice.Content);
        Assert.AreEqual(Visibility.Visible, notice.Visibility);
        StringAssert.Contains((string)notice.ToolTip, "普通房");
        Assert.IsFalse(new RoomResumeStore(rig.Catalog).Load(rig.RoomId).Valid);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void DissolvedTargetCannotEnterFromAnAlreadyPreparedCallback(bool furniture)
    {
        using var rig = new ProductRig();
        rig.Host.EnterCatalogRoom(rig.RoomId, furniture);
        // Keep this Dispatcher paused after IO prepares the target, before its callback binds it.
        var preparation = Field<Task>(rig.Host, "roomPreparationTask");
        Assert.IsTrue(preparation.Wait(TimeSpan.FromSeconds(10)), "Target preparation did not finish.");
        Assert.IsTrue(new LocalRoomCatalogStore(rig.Root).Dissolve(Guid.NewGuid(), rig.RoomId).Success);
        Until(() => !rig.Host.LocalHandoffPending, "The stale prepared callback must resolve.");
        Assert.AreEqual(LocalRoomPresentation.Porch, rig.Host.LocalPresentation);
        Assert.IsFalse(rig.Host.LocalWindow!.IsVisible);
        Assert.IsFalse(rig.Home.IsDangoAway);
        Assert.HasCount(0, rig.Scene.ReadFurnitureCollectionFrame().Items);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void CancellationAfterFurnitureAttachBeforeFirstFrameConfirmationCannotPollutePorch(bool hide)
    {
        using var rig = new ProductRig();
        var bytes = File.ReadAllBytes(rig.Path);
        var cancelled = false;
        ulong world = 0;
        DangoEntityId? entity = null;
        rig.Host.EnterLocalRoomFurniture();
        var window = rig.Host.LocalWindow!;
        Action? cancel = null;
        cancel = () =>
        {
            if (cancelled || !rig.Host.LocalHandoffPending || rig.Furniture.NativeFrame?.Items.Count != 1) return;
            cancelled = true;
            world = rig.Furniture.WorldGeneration;
            entity = rig.Runtime.EntityIds.Single();
            window.Scene.FrameApplied -= cancel;
            if (hide) rig.Host.HideLocalRoom(); else rig.Host.ReturnToPorch();
        };
        window.Scene.FrameApplied += cancel;
        try
        {
            Until(() => cancelled, "The test must intercept the real attached first frame before confirmation.");
            Until(() => !rig.Host.LocalHandoffPending && rig.Home.OwnsLiveFrameDriver && rig.Home.PorchScene.LiveBody.IsVisible,
                "Cancellation must restore a real porch owner and body frame.");
            Pump(150); // Any already-queued room confirmation must remain harmless.
            Assert.AreEqual(LocalRoomPresentation.Porch, rig.Host.LocalPresentation);
            Assert.IsFalse(window.IsVisible);
            Assert.IsFalse(window.IsFurnitureEditing, "An old E request must not open an editor after cancellation.");
            Assert.IsFalse(rig.Furniture.IsAttached);
            var actual = rig.Scene.ReadFurnitureCollectionFrame();
            Assert.HasCount(0, actual.Items, "The shared world must not carry a room collider into the porch.");
            Assert.AreEqual(world, actual.WorldGeneration);
            Assert.AreEqual(entity!.Value, rig.Runtime.EntityIds.Single());
            Assert.IsNull(rig.Runtime.LastError);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(rig.Path), "Runtime cancellation must not save a Delete operation.");
        }
        finally { window.Scene.FrameApplied -= cancel; }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void EditorCannotOpenBetweenBodyApplyAndHealthyFirstFrameConfirmation()
    {
        using var rig = new ProductRig();
        rig.Host.EnterLocalRoom();
        var window = rig.Host.LocalWindow!;
        bool? prematureAdmission = null;
        Action? observe = null;
        observe = () =>
        {
            if (prematureAdmission.HasValue || !rig.Host.LocalHandoffPending || rig.Furniture.NativeFrame?.Items.Count != 1) return;
            prematureAdmission = window.BeginFurnitureEditing();
            window.Scene.FrameApplied -= observe;
        };
        window.Scene.FrameApplied += observe;
        try
        {
            Until(() => prematureAdmission.HasValue, "A real first frame must reach the admission check.");
            Assert.AreEqual(false, prematureAdmission, "Scene.HasHealthyFrame alone cannot bypass the pending handoff.");
            rig.WaitForRoom();
            Assert.IsTrue(window.BeginFurnitureEditing(), rig.Furniture.Status);
        }
        finally { window.Scene.FrameApplied -= observe; }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void StaleFailureCannotDetachTheCurrentRoomOrChangeItsNativeReceipt()
    {
        using var rig = new ProductRig();
        rig.EnterRoom();
        var gate = Field<LocalRoomHandoffGate>(rig.Host, "roomHandoff");
        var before = JsonSerializer.Serialize(rig.Scene.ReadFurnitureCollectionFrame());
        var bytes = File.ReadAllBytes(rig.Path);
        var owner = rig.Runtime.FrameDriver;
        typeof(DangoApplicationHost).GetMethod("FailLocalHandoff", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(rig.Host, [gate.Generation - 1, "synthetic stale first-frame failure"]);
        Assert.AreEqual(before, JsonSerializer.Serialize(rig.Scene.ReadFurnitureCollectionFrame()), "A stale callback must be rejected before native mutation.");
        Assert.AreSame(owner, rig.Runtime.FrameDriver);
        Assert.IsTrue(rig.Furniture.IsAttached);
        Assert.AreEqual(LocalRoomPresentation.Window, rig.Host.LocalPresentation);
        Assert.IsFalse(rig.Host.LocalHandoffPending);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(rig.Path));
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void RuntimeUnknownStopsTheRoomOwnerWithoutRehostingOrErasingTheLayout()
    {
        using var rig = new ProductRig();
        rig.EnterRoom();
        var old = rig.Scene.ReadFurnitureCollectionFrame();
        var bytes = File.ReadAllBytes(rig.Path);
        rig.Furniture.FailRuntime("synthetic unconfirmable furniture runtime");
        Until(() => rig.Runtime.LastError is not null && rig.Runtime.FrameDriver is null,
            "An unconfirmable world must stop its room owner instead of repeatedly reporting failure.");
        Assert.IsFalse(rig.Host.LocalHandoffPending);
        Assert.IsFalse(rig.Runtime.IsRunning);
        Assert.IsTrue(rig.Host.LocalWindow!.IsVisible, "Keep a visible fault surface; never hand the unknown world to the porch.");
        Assert.AreEqual(LocalRoomPresentation.Window, rig.Host.LocalPresentation);
        Assert.AreEqual(OrdinaryFurnitureSceneState.RuntimeUnknown, rig.Furniture.State);
        var current = rig.Scene.ReadFurnitureCollectionFrame();
        Assert.AreEqual(old.WorldGeneration, current.WorldGeneration);
        Assert.AreEqual(old.Physics.Bodies.Single().EntityId, current.Physics.Bodies.Single().EntityId);
        Assert.AreEqual(old.Items.Single().InstanceId, current.Items.Single().InstanceId);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(rig.Path));
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void BusyMinimizeAndRestoreKeepTheOwnerUsableUntilTheSingleCommitFinishes()
    {
        using var rig = new ProductRig();
        rig.EnterRoom();
        var writer = new BlockingWriter();
        rig.ReplaceCoordinator(new FurnitureLayoutStore(writer, FurnitureLayoutContract.OrdinaryRoom, rig.RoomId, () => rig.Catalog.AcquireLayoutGuard(rig.RoomId)));
        var old = rig.Scene.ReadFurnitureCollectionFrame();
        var bytes = File.ReadAllBytes(rig.Path);
        var owner = rig.Runtime.FrameDriver;
        Assert.IsTrue(rig.Host.LocalWindow!.BeginFurnitureEditing(), rig.Furniture.Status);
        rig.Furniture.PreviewPlatform(1600, 120);
        var pending = rig.Furniture.ConfirmAsync();
        try
        {
            Assert.IsTrue(writer.Entered.Task.Wait(TimeSpan.FromSeconds(5)), "The real staged file must reach the blocked writer.");
            Assert.IsTrue(rig.Furniture.Busy);
            PressEscape();
            Assert.IsTrue(rig.Host.LocalWindow!.IsFurnitureEditing, "An in-flight save must finish before exiting editing.");
            Assert.IsFalse(rig.Host.LocalWindow.IsMenuOpen, "Esc while saving cannot open another overlay.");
            rig.Host.LocalWindow!.WindowState = WindowState.Minimized;
            Assert.AreEqual(WindowState.Normal, rig.Host.LocalWindow.WindowState,
                "Refusing a busy hide must restore the rendering surface so the existing owner can finish.");
            rig.Host.RestoreLocalRoom();
            Assert.AreEqual(WindowState.Normal, rig.Host.LocalWindow.WindowState);
            Assert.IsTrue(rig.Host.LocalWindow.IsVisible);
            Assert.AreSame(owner, rig.Runtime.FrameDriver);
            Assert.AreEqual(LocalRoomPresentation.Window, rig.Host.LocalPresentation);
            Assert.IsFalse(rig.Host.LocalHandoffPending);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(rig.Path));
            writer.Release.TrySetResult();
            Until(() => pending.IsCompleted, "The same real owner must process the completed staging task.");
            pending.GetAwaiter().GetResult();
            Assert.AreEqual(OrdinaryFurnitureSceneState.Synced, rig.Furniture.State, rig.Furniture.Status);
            Assert.AreEqual(2L, rig.Furniture.Layout.LayoutVersion);
            Assert.AreEqual(1, writer.CommitCalls);
            PressEscape();
            Assert.IsFalse(rig.Host.LocalWindow.IsFurnitureEditing, "A fresh Esc can exit after the real commit finishes.");
            Assert.IsFalse(rig.Host.LocalWindow.IsMenuOpen);
            var saved = File.ReadAllBytes(rig.Path);
            rig.Host.HideLocalRoom();
            Assert.AreEqual(LocalRoomPresentation.Hidden, rig.Host.LocalPresentation);
            Assert.IsNull(rig.Runtime.FrameDriver);
            Assert.IsFalse(rig.Runtime.IsRunning);
            var hiddenStep = rig.Scene.ReadFurnitureCollectionFrame().PhysicsStep;
            Pump(150);
            Assert.AreEqual(hiddenStep, rig.Scene.ReadFurnitureCollectionFrame().PhysicsStep, "Hidden time must not advance the world.");
            rig.Host.RestoreLocalRoom(); rig.WaitForRoom();
            Assert.AreEqual(old.WorldGeneration, rig.Furniture.WorldGeneration);
            Assert.AreEqual(old.Physics.Bodies.Single().EntityId, rig.Runtime.EntityIds.Single());
            Assert.AreEqual(new FurnitureAnchor(1600, 120), rig.Furniture.Layout.Platform!.Anchor);
            Assert.AreEqual(2L, rig.Furniture.Layout.LayoutVersion);
            Assert.AreEqual(1, writer.CommitCalls, "Restoration must not resave or replay a commit.");
            CollectionAssert.AreEqual(saved, File.ReadAllBytes(rig.Path));
        }
        finally { writer.Release.TrySetResult(); }

        void PressEscape()
        {
            var window = rig.Host.LocalWindow!;
            window.FocusScene();
            var args = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(window)!, Environment.TickCount, System.Windows.Input.Key.Escape)
            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            window.Scene.RaiseEvent(args);
            Assert.IsTrue(args.Handled);
        }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void SavedButUnappliedLayoutBlocksDepartureAndCanRecoverAfterHideWithoutResaving()
    {
        using var rig = new ProductRig();
        rig.EnterRoom();
        var old = rig.Scene.ReadFurnitureCollectionFrame();
        var owner = rig.Runtime.FrameDriver;
        rig.Furniture.BeforeNativeApply = () => throw new IOException("synthetic saved-before-apply failure");
        rig.Furniture.PreviewPlatform(1600, 120);
        var pending = rig.Furniture.ConfirmAsync();
        Until(() => pending.IsCompleted, "The real save must finish with its native apply failure confirmed.");
        pending.GetAwaiter().GetResult();
        Assert.AreEqual(OrdinaryFurnitureSceneState.PersistedNotApplied, rig.Furniture.State);
        Assert.AreEqual(1L, rig.Furniture.Layout.LayoutVersion);
        Assert.AreEqual(2L, rig.Furniture.PersistedLayout.LayoutVersion);
        var saved = File.ReadAllBytes(rig.Path);
        rig.Host.ReturnToPorch();
        Assert.AreEqual(LocalRoomPresentation.Window, rig.Host.LocalPresentation);
        Assert.IsFalse(rig.Host.LocalHandoffPending);
        Assert.AreSame(owner, rig.Runtime.FrameDriver, "Refused departure must retain the owner needed to recover the pending apply.");
        Assert.AreEqual(old.Items.Single().InstanceId, rig.Scene.ReadFurnitureCollectionFrame().Items.Single().InstanceId);
        rig.Host.HideLocalRoom();
        Assert.IsNull(rig.Runtime.FrameDriver);
        rig.Host.RestoreLocalRoom(); rig.WaitForRoom();
        Assert.IsTrue(rig.Host.LocalWindow!.BeginFurnitureEditing(), "The saved-but-unapplied state must expose its actual recovery action.");
        Assert.AreEqual(OrdinaryFurnitureSceneState.PersistedNotApplied, rig.Furniture.State);
        Assert.AreEqual(old.WorldGeneration, rig.Furniture.WorldGeneration);
        Assert.AreEqual(old.Items.Single().InstanceId, rig.Scene.ReadFurnitureCollectionFrame().Items.Single().InstanceId);
        rig.Furniture.BeforeNativeApply = null;
        var recovered = rig.Furniture.RecoverAsync();
        Until(() => recovered.IsCompleted, "Recovery must run at the restored owner's real boundary.");
        recovered.GetAwaiter().GetResult();
        Assert.AreEqual(OrdinaryFurnitureSceneState.Synced, rig.Furniture.State, rig.Furniture.Status);
        Assert.AreEqual(2L, rig.Furniture.Layout.LayoutVersion);
        Assert.AreEqual(new FurnitureAnchor(1600, 120), rig.Furniture.Layout.Platform!.Anchor);
        Assert.AreEqual(old.WorldGeneration, rig.Furniture.WorldGeneration);
        Assert.AreEqual(old.Physics.Bodies.Single().EntityId, rig.Runtime.EntityIds.Single());
        CollectionAssert.AreEqual(saved, File.ReadAllBytes(rig.Path), "Applying an already-saved layout must not add a new file version.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void UnknownCommitCannotLeaveAndKeepsTheOldColliderAcrossHideAndRecovery()
    {
        using var rig = new ProductRig();
        rig.EnterRoom();
        var original = File.ReadAllBytes(rig.Path);
        var writer = new UnknownCommitWriter();
        rig.ReplaceCoordinator(new FurnitureLayoutStore(writer, FurnitureLayoutContract.OrdinaryRoom, rig.RoomId, () => rig.Catalog.AcquireLayoutGuard(rig.RoomId)));
        var old = rig.Scene.ReadFurnitureCollectionFrame();
        var owner = rig.Runtime.FrameDriver;
        rig.Furniture.PreviewPlatform(1600, 120);
        var pending = rig.Furniture.ConfirmAsync();
        Until(() => pending.IsCompleted, "The actual third-content commit must be classified before departure.");
        pending.GetAwaiter().GetResult();
        Assert.AreEqual(OrdinaryFurnitureSceneState.CommitUnknown, rig.Furniture.State, rig.Furniture.Status);
        Assert.AreEqual(FurnitureLayoutLoadState.Protected,
            new FurnitureLayoutStore(contract: FurnitureLayoutContract.OrdinaryRoom).Load(rig.Path).State);
        var uncertainBytes = File.ReadAllBytes(rig.Path);
        rig.Host.ReturnToPorch();
        Assert.AreEqual(LocalRoomPresentation.Window, rig.Host.LocalPresentation);
        Assert.IsFalse(rig.Host.LocalHandoffPending);
        Assert.AreSame(owner, rig.Runtime.FrameDriver);
        Assert.AreEqual(old.Items.Single().InstanceId, rig.Scene.ReadFurnitureCollectionFrame().Items.Single().InstanceId);
        CollectionAssert.AreEqual(uncertainBytes, File.ReadAllBytes(rig.Path), "Refused departure must preserve the uncertain target.");
        rig.Host.HideLocalRoom();
        Assert.IsNull(rig.Runtime.FrameDriver);
        rig.Host.RestoreLocalRoom(); rig.WaitForRoom();
        Assert.AreEqual(OrdinaryFurnitureSceneState.CommitUnknown, rig.Furniture.State);
        Assert.IsTrue(rig.Host.LocalWindow!.BeginFurnitureEditing(), "An unresolved safe old world must retain its recovery entry.");
        Assert.AreEqual(old.WorldGeneration, rig.Furniture.WorldGeneration);
        Assert.AreEqual(old.Items.Single().InstanceId, rig.Scene.ReadFurnitureCollectionFrame().Items.Single().InstanceId);
        // Restore only this fixture's synthetic original bytes. Recovery must
        // classify them as not committed, without a second writer invocation.
        File.WriteAllBytes(rig.Path, original);
        var recovered = rig.Furniture.RecoverAsync();
        Until(() => recovered.IsCompleted, "The restored owner must confirm the original byte snapshot.");
        recovered.GetAwaiter().GetResult();
        Assert.AreEqual(OrdinaryFurnitureSceneState.Synced, rig.Furniture.State, rig.Furniture.Status);
        Assert.AreEqual(1L, rig.Furniture.Layout.LayoutVersion);
        Assert.AreEqual(old.Items.Single().InstanceId, rig.Scene.ReadFurnitureCollectionFrame().Items.Single().InstanceId);
        Assert.AreEqual(old.Physics.Bodies.Single().EntityId, rig.Runtime.EntityIds.Single());
        Assert.AreEqual(1, writer.CommitCalls);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(rig.Path));
    }

    private sealed class ProductRig : IDisposable
    {
        internal string Root { get; }
        internal string Path { get; }
        internal LocalRoomCatalogStore Catalog { get; }
        internal Guid RoomId { get; }
        internal DangoApplicationHost Host { get; }
        internal StackedDangoSceneRuntime Runtime { get; }
        internal ControlCenterWindow HomeWindow { get; }
        internal ProductHomeView Home => HomeWindow.ProductHome;
        internal DesktopPhysicsScene Scene => Field<DesktopPhysicsScene>(Runtime, "scene");
        internal OrdinaryRoomFurnitureSession Furniture => Runtime.Furniture!;

        internal ProductRig()
        {
            Root = System.IO.Path.Combine(TestRepository.Root, "artifacts", "furniture-room", "handoff-tests", Guid.NewGuid().ToString("N"));
            var legacyPath = FurnitureLayoutStore.GetLayoutPath(Root, FurnitureLayoutContract.OrdinaryRoom);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(legacyPath)!);
            var initial = new FurnitureLayout(1, Guid.NewGuid(), new(Guid.NewGuid(), FurnitureLayoutContract.OrdinaryRoom.DefaultAnchor));
            File.WriteAllBytes(legacyPath, FurnitureLayoutSerializer.Serialize(initial, FurnitureLayoutContract.OrdinaryRoom));
            Catalog = new(Root); var imported = Catalog.Refresh(); Assert.IsTrue(imported.Success, imported.Warning);
            RoomId = imported.Catalog!.Rooms.Single().RoomId; Path = Catalog.LayoutPath(RoomId);
            Host = new(settingsFile: System.IO.Path.Combine(Root, "settings.json"),
                nativeLibraryPath: Environment.GetEnvironmentVariable("DANGO_ORDINARY_FURNITURE_TEST_MODULE") ??
                    throw new InvalidOperationException("Use the explicit isolated furniture candidate for these product handoff gates."),
                isolatedLocalRoomReview: true);
            Runtime = Field<StackedDangoSceneRuntime>(Host, "stackedRuntime");
            try
            {
                Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
                Host.ShowControlCenter(); Pump(250);
                HomeWindow = Field<ControlCenterWindow>(Host, "controlCenterWindow");
                Home.ResumePorch(); Pump(250);
            }
            catch { Host.Dispose(); throw; }
        }

        internal void EnterRoom() { Host.EnterLocalRoom(); WaitForRoom(); }
        internal void WaitForRoom() => Until(() => !Host.LocalHandoffPending && Host.LocalPresentation == LocalRoomPresentation.Window &&
            Host.LocalWindow!.Scene.HasHealthyFrame, "The product room must produce its real healthy first frame.");

        internal void ReplaceCoordinator(FurnitureLayoutStore store)
        {
            Assert.IsTrue(Runtime.TryDetachOrdinaryFurniture(), Furniture.Status);
            Furniture.Dispose();
            var driver = Field<LocalRoomSceneDriver>(Host, "localRoomDriver");
            var replacement = new OrdinaryRoomFurnitureSession(Scene, Root, store,
                ownsActiveRoom: () => driver.OwnsRuntime && Runtime.IsRunning && Runtime.LastError is null, layoutPath: Path);
            typeof(StackedDangoSceneRuntime).GetProperty("Furniture", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Runtime, replacement);
            Assert.IsTrue(Runtime.AttachOrdinaryFurniture(), replacement.Status);
        }

        public void Dispose() => Host.Dispose();
    }

    private sealed class BlockingWriter : IFurnitureFileWriter
    {
        private readonly FurnitureAtomicFileWriter writer = new();
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int CommitCalls { get; private set; }
        public void WriteStaged(string stagedPath, byte[] bytes)
        {
            writer.WriteStaged(stagedPath, bytes);
            Entered.TrySetResult();
            if (!Release.Task.Wait(TimeSpan.FromSeconds(15))) throw new IOException("synthetic blocked-stage deadline");
        }
        public void Commit(string stagedPath, string targetPath, bool targetExisted)
        { ++CommitCalls; writer.Commit(stagedPath, targetPath, targetExisted); }
    }

    private sealed class UnknownCommitWriter : IFurnitureFileWriter
    {
        private readonly FurnitureAtomicFileWriter writer = new();
        internal int CommitCalls { get; private set; }
        public void WriteStaged(string stagedPath, byte[] bytes) => writer.WriteStaged(stagedPath, bytes);
        public void Commit(string stagedPath, string targetPath, bool targetExisted)
        {
            ++CommitCalls;
            File.WriteAllText(targetPath, "synthetic-unconfirmable-target");
            throw new IOException("synthetic third-content commit interruption");
        }
    }

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static void Until(Func<bool> condition, string message, int timeout = 5000)
    {
        var watch = Stopwatch.StartNew();
        do { Pump(25); } while (!condition() && watch.ElapsedMilliseconds < timeout);
        Assert.IsTrue(condition(), message);
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
