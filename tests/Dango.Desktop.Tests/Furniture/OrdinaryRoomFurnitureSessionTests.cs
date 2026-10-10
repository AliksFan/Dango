using System.Text.Json;
using Dango.Core.Coordinates;
using Dango.Core.Furniture;
using Dango.Desktop.Furniture;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.Furniture;

[TestClass]
public sealed class OrdinaryRoomFurnitureSessionTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);
    private static string Module => Environment.GetEnvironmentVariable("DANGO_ORDINARY_FURNITURE_TEST_MODULE") ??
        Environment.GetEnvironmentVariable("DANGO_PHYSICS_MODULE_PATH") ??
        throw new InvalidOperationException("Use the explicit isolated furniture candidate for these native integration gates.");
    private static FurnitureLayoutContract Contract => FurnitureLayoutContract.OrdinaryRoom;

    [TestMethod, TestCategory("NativeIntegration")]
    public void BorrowedWorldPreviewAndCommitDoNotAdvanceOrRecreateThePlayer()
    {
        using var fixture = new Fixture();
        var before = fixture.Scene.ReadFurnitureCollectionFrame();
        fixture.Session.PreviewPlatform(1440, 120);
        Assert.IsTrue(fixture.Session.CanConfirm, fixture.Session.Status);
        Assert.AreEqual(Json(before), Json(fixture.Scene.ReadFurnitureCollectionFrame()), "Preview must leave the real world unchanged.");
        CompleteAtBoundary(fixture.Session, fixture.Session.ConfirmAsync());
        var after = fixture.Scene.ReadFurnitureCollectionFrame();
        Assert.AreEqual(before.WorldGeneration, after.WorldGeneration);
        Assert.AreEqual(before.PhysicsStep, after.PhysicsStep, "The coordinator must not perform a hidden Step.");
        Assert.AreEqual(before.SceneRevision + 1, after.SceneRevision);
        Assert.AreEqual(Json(before.BodyDiagnostics.Single().Nodes), Json(after.BodyDiagnostics.Single().Nodes));
        Assert.AreEqual(before.Physics.Bodies.Single().EntityId, after.Physics.Bodies.Single().EntityId);
        Assert.AreEqual(1L, fixture.Session.Layout.LayoutVersion);
        Assert.AreEqual(fixture.Session.Layout, fixture.Store.Load(fixture.Path).Layout);
        Assert.IsFalse(fixture.Session.HasPreview);
        Assert.AreEqual(OrdinaryFurnitureSceneState.Synced, fixture.Session.State);
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void DetachAndReenterKeepSavedVersionAndAnchorAcrossAnElevatedOrigin()
    {
        using var fixture = new Fixture();
        fixture.Session.PreviewPlatform(1440, 120);
        CompleteAtBoundary(fixture.Session, fixture.Session.ConfirmAsync());
        var bytes = File.ReadAllBytes(fixture.Path);
        var saved = fixture.Session.Layout;
        var old = fixture.Scene.ReadFurnitureCollectionFrame();
        Assert.IsTrue(fixture.Session.TryDetach(), fixture.Session.Status);
        Assert.AreEqual(0, fixture.Scene.ReadFurnitureCollectionFrame().Items.Count);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(fixture.Path));
        Assert.IsTrue(fixture.Scene.RehostSelectedEntityAt(new(1080, 700)));
        fixture.Scene.SetHorizontalBounds(Contract.Left, Contract.Right);
        Assert.IsTrue(fixture.Session.Attach(), fixture.Session.Status);
        var current = fixture.Scene.ReadFurnitureCollectionFrame();
        Assert.AreEqual(old.WorldGeneration, current.WorldGeneration);
        Assert.AreEqual(old.PhysicsStep, current.PhysicsStep);
        Assert.AreEqual(old.Physics.Bodies.Single().EntityId, current.Physics.Bodies.Single().EntityId);
        Assert.AreEqual(saved, fixture.Session.Layout);
        var platform = current.Items.Single().Definition.Platform!;
        var actualTop = fixture.Scene.ToHost(new(platform.CenterX, platform.Top));
        Assert.AreEqual(1440d, actualTop.X, 1e-4);
        Assert.AreEqual(fixture.Scene.GroundY - 120, actualTop.Y, 1e-4);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(fixture.Path));
        Assert.IsTrue(fixture.Session.Attach(), "Repeated Attach must be idempotent.");
        Assert.AreEqual(Json(current), Json(fixture.Scene.ReadFurnitureCollectionFrame()));
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void FailedStagingPreservesWorldAndPersistedFile()
    {
        using var fixture = new Fixture(new ThrowOnStage());
        var before = fixture.Scene.ReadFurnitureCollectionFrame();
        fixture.Session.PreviewPlatform(1440, 120);
        CompleteAtBoundary(fixture.Session, fixture.Session.ConfirmAsync());
        Assert.IsFalse(File.Exists(fixture.Path));
        Assert.AreEqual(Json(before), Json(fixture.Scene.ReadFurnitureCollectionFrame()));
        Assert.AreEqual(0L, fixture.Session.Layout.LayoutVersion);
        Assert.IsTrue(fixture.Session.HasPreview);
        Assert.IsTrue(fixture.Session.CanEdit);
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void SavedBeforeApplyFailureBlocksDepartureAndRecoveryDoesNotResave()
    {
        using var fixture = new Fixture();
        fixture.Session.BeforeNativeApply = () => throw new IOException("synthetic pre-apply interruption");
        fixture.Session.PreviewPlatform(1440, 120);
        CompleteAtBoundary(fixture.Session, fixture.Session.ConfirmAsync());
        Assert.AreEqual(OrdinaryFurnitureSceneState.PersistedNotApplied, fixture.Session.State);
        Assert.IsTrue(fixture.Session.CanStep);
        Assert.IsTrue(fixture.Session.CanRecover);
        Assert.IsFalse(fixture.Session.TryDetach());
        Assert.AreEqual(0L, fixture.Session.Layout.LayoutVersion);
        Assert.AreEqual(1L, fixture.Session.PersistedLayout.LayoutVersion);
        var bytes = File.ReadAllBytes(fixture.Path);
        fixture.Session.BeforeNativeApply = null;
        CompleteAtBoundary(fixture.Session, fixture.Session.RecoverAsync());
        Assert.AreEqual(OrdinaryFurnitureSceneState.Synced, fixture.Session.State);
        Assert.AreEqual(1L, fixture.Session.Layout.LayoutVersion);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(fixture.Path));
        Assert.IsTrue(fixture.Session.TryDetach());
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void ApplyReturnFailureUsesTheRealReceiptAndDoesNotApplyTwice()
    {
        using var fixture = new Fixture();
        fixture.Session.AfterNativeApply = () => throw new IOException("synthetic post-apply interruption");
        var before = fixture.Scene.ReadFurnitureCollectionFrame();
        fixture.Session.PreviewPlatform(1440, 120);
        CompleteAtBoundary(fixture.Session, fixture.Session.ConfirmAsync());
        var current = fixture.Scene.ReadFurnitureCollectionFrame();
        Assert.AreEqual(OrdinaryFurnitureSceneState.Synced, fixture.Session.State);
        Assert.AreEqual(before.SceneRevision + 1, current.SceneRevision);
        Assert.AreEqual(1L, fixture.Session.Layout.LayoutVersion);
        Assert.IsFalse(fixture.Session.CanRecover);
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void SameRequestDeduplicatesOnlyItsCompleteSuccessfulReceipt()
    {
        using var fixture = new Fixture();
        var item = new FurniturePlatformInstance(Guid.NewGuid(), Contract.DefaultAnchor);
        var operation = new FurnitureEditOperation(Guid.NewGuid(), 0, FurnitureEditKind.Insert, null, item);
        CompleteAtBoundary(fixture.Session, fixture.Session.SubmitAsync(operation));
        var current = fixture.Scene.ReadFurnitureCollectionFrame();
        var bytes = File.ReadAllBytes(fixture.Path);
        CompleteAtBoundary(fixture.Session, fixture.Session.SubmitAsync(operation));
        Assert.AreEqual(Json(current), Json(fixture.Scene.ReadFurnitureCollectionFrame()));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(fixture.Path));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Session.SubmitAsync(operation with
        { Candidate = item with { Anchor = new(1500, 120) } }));
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void IllegalOriginChangeStopsTheCoordinatorBeforeAnotherStep()
    {
        using var fixture = new Fixture();
        var before = fixture.Scene.ReadFurnitureCollectionFrame();
        fixture.Scene.Rehost(new(100, 200));
        fixture.Session.ProcessBoundary();
        Assert.AreEqual(OrdinaryFurnitureSceneState.RuntimeUnknown, fixture.Session.State);
        Assert.IsFalse(fixture.Session.CanStep);
        Assert.IsFalse(fixture.Session.TryDetach());
        Assert.AreEqual(Json(before), Json(fixture.Scene.ReadFurnitureCollectionFrame()));
        Assert.IsFalse(File.Exists(fixture.Path));
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void MissingFurnitureAbiAllowsOnlyAVerifiedEmptyRoom()
    {
        var baselineModule = Environment.GetEnvironmentVariable("DANGO_ORDINARY_FURNITURE_BASELINE_PATH") ??
            throw new InvalidOperationException("Set the independent single-furniture module with horizontal bounds and without collection ABI for this capability gate.");
        var root = NewRoot();
        using var scene = new DesktopPhysicsScene(baselineModule, new(1000, 240, 160, 120));
        scene.SetHorizontalBounds(Contract.Left, Contract.Right);
        using (var empty = new OrdinaryRoomFurnitureSession(scene, root))
        {
            Assert.IsTrue(empty.Attach());
            Assert.IsFalse(empty.CanEdit);
            Assert.IsTrue(empty.TryDetach());
        }
        var path = FurnitureLayoutStore.GetLayoutPath(root, Contract);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var layout = new FurnitureLayout(1, Guid.NewGuid(), new(Guid.NewGuid(), Contract.DefaultAnchor));
        File.WriteAllBytes(path, FurnitureLayoutSerializer.Serialize(layout, Contract));
        using var nonempty = new OrdinaryRoomFurnitureSession(scene, root);
        Assert.IsFalse(nonempty.Attach(), "A missing native capability cannot turn a saved platform into an empty room.");
        Assert.IsFalse(nonempty.CanEdit);
        Assert.AreEqual(layout, new FurnitureLayoutStore(contract: Contract).Load(path).Layout);
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void BadOrdinaryRoomFileCannotBeLoadedAsEmptyOrOverwritten()
    {
        var root = NewRoot();
        var path = FurnitureLayoutStore.GetLayoutPath(root, Contract);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"schemaVersion\":999}");
        var bytes = File.ReadAllBytes(path);
        using var scene = new DesktopPhysicsScene(Module, new(1000, 240, 160, 120));
        scene.SetHorizontalBounds(Contract.Left, Contract.Right);
        using var session = new OrdinaryRoomFurnitureSession(scene, root);
        Assert.IsFalse(session.Attach());
        Assert.IsFalse(session.CanEdit);
        Assert.AreEqual(OrdinaryFurnitureSceneState.Protected, session.State);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        Assert.AreEqual(0, scene.ReadFurnitureCollectionFrame().Items.Count);
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void BodyEnteringThePreviewWhileStagingCausesLatestBoundaryRejection()
    {
        using var writer = new BlockingStage();
        using var fixture = new Fixture(writer);
        fixture.Session.PreviewPlatform(1440, 120);
        Assert.IsTrue(fixture.Session.CanConfirm);
        var operation = fixture.Session.ConfirmAsync();
        try
        {
            Assert.IsTrue(writer.Entered.Wait(2000), "The real temporary writer must be in progress.");
            Assert.IsTrue(fixture.Session.Busy);
            Assert.IsFalse(fixture.Session.TryDetach(), "An in-flight commit cannot be abandoned by a host handoff.");
            fixture.Scene.BeginDrag(fixture.Scene.SelectedEntity, new(1440, fixture.Scene.GroundY - 110));
            var definition = fixture.Scene.OrdinaryFurnitureDefinition(fixture.Session.Preview!);
            var penetrated = false;
            for (var i = 0; i < 900 && !penetrated; i++)
            {
                fixture.Scene.Step(Dt); fixture.Session.OnFrame();
                var frame = fixture.Scene.ReadFurnitureCollectionFrame();
                penetrated = fixture.Scene.ProbeFurnitureChange(DangoFurnitureChangeKind.Insert, 0,
                    definition, frame.SceneRevision).Rejection == DangoFurnitureChangeRejection.Penetration;
            }
            Assert.IsTrue(penetrated, "The test must actually move the native body into the frozen preview.");
            var beforeCommit = fixture.Scene.ReadFurnitureCollectionFrame();
            writer.Release.Set();
            CompleteAtBoundary(fixture.Session, operation);
            Assert.AreEqual(Json(beforeCommit), Json(fixture.Scene.ReadFurnitureCollectionFrame()));
            Assert.IsFalse(File.Exists(fixture.Path));
            Assert.AreEqual(0L, fixture.Session.Layout.LayoutVersion);
            Assert.IsTrue(fixture.Session.HasPreview);
            Assert.AreEqual(OrdinaryFurnitureSceneState.Synced, fixture.Session.State);
        }
        finally { writer.Release.Set(); }
    }

    [STATestMethod, TestCategory("NativeIntegration")]
    public void ProductRuntimeKeepsItsRoomMappingAndAttachmentsWhileWalkingAndUsingTheGlove()
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct(Module);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var desktop = new DesktopSceneSession();
        desktop.AttachSurface(new(0, 0, Contract.SurfaceWidth, Contract.SurfaceHeight), 0);
        var controller = desktop.Controller!;
        controller.MoveTo(new(1000, 240));
        var input = PhysicsPresentationInput.FromSnapshot(controller.Snapshot, controller.SquashAmount,
            controller.GroundFrictionCue, controller.FacingDirection, null);
        runtime.SetViewport(new(0, 0, Contract.SurfaceWidth, Contract.SurfaceHeight));
        runtime.Start();
        for (var i = 0; i < 240; i++) runtime.Tick(input, Dt);
        Assert.IsTrue(runtime.RehostSelectedEntityAt(controller.Snapshot.Dango.VisualBounds.Center));
        runtime.SetHorizontalBounds(Contract.Left, Contract.Right);
        // Direct runtime fixture owns one driver token; actual WPF scheduler
        // ownership and entry are exercised separately by the product test.
        runtime.FrameDriver = new object();
        runtime.ConfigureOrdinaryFurniture(NewRoot());
        Assert.IsTrue(runtime.AttachOrdinaryFurniture(), runtime.Furniture?.Status);
        var old = runtime.Furniture!.NativeFrame!;
        var ground = runtime.LocalGroundY!.Value;
        runtime.SetDirectionalControl(new(false, true, false, false));
        for (var i = 0; i < 180; i++)
        {
            runtime.Tick(input, Dt);
            Assert.IsNull(runtime.LastError);
            Assert.AreEqual(ground, runtime.LocalGroundY.Value, 1e-9);
        }
        runtime.SetDirectionalControl(default);
        Assert.IsTrue(runtime.BeginOrdinaryFurnitureGlove(1900, 280));
        for (var i = 0; i < 90; i++)
        {
            runtime.UpdateOrdinaryFurnitureGlove(2000, 290);
            var frame = runtime.Tick(input, Dt);
            Assert.IsNull(runtime.LastError);
            Assert.AreEqual(PhysicsPresentationFallbackReason.None, frame.Bodies.Single().FallbackReason);
            Assert.IsTrue(runtime.LastPhysicsFrame!.Bodies.Single().MaximumSpeed <= 12.001);
        }
        runtime.EndOrdinaryFurnitureGlove();
        Assert.IsFalse(runtime.IsDragging);
        Assert.IsTrue(runtime.AttachmentsEnabled);
        var current = runtime.Furniture.NativeFrame!;
        Assert.AreEqual(old.WorldGeneration, current.WorldGeneration);
        Assert.AreEqual(old.Physics.Bodies.Single().EntityId, current.Physics.Bodies.Single().EntityId);
        Assert.IsTrue(current.PhysicsStep > old.PhysicsStep);
        Assert.IsTrue(runtime.TryDetachOrdinaryFurniture());
        runtime.FrameDriver = null;
    }

    private static void CompleteAtBoundary(OrdinaryRoomFurnitureSession session, Task operation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!operation.IsCompleted && DateTime.UtcNow < deadline)
        { session.ProcessBoundary(); Thread.Sleep(1); }
        Assert.IsTrue(operation.IsCompleted, session.Status);
        operation.GetAwaiter().GetResult();
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static string NewRoot()
    {
        var path = System.IO.Path.Combine(TestRepository.Root, "artifacts", "test-runs", "ordinary-room-furniture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class Fixture : IDisposable
    {
        internal DesktopPhysicsScene Scene { get; }
        internal OrdinaryRoomFurnitureSession Session { get; }
        internal FurnitureLayoutStore Store { get; }
        internal string Path { get; }
        internal Fixture(IFurnitureFileWriter? writer = null)
        {
            var root = NewRoot();
            Path = FurnitureLayoutStore.GetLayoutPath(root, Contract);
            Store = new(writer, Contract);
            Scene = new(Module, new(1000, 240, 160, 120));
            for (var i = 0; i < 180; i++) Scene.Step(Dt);
            Scene.SetHorizontalBounds(Contract.Left, Contract.Right);
            Session = new(Scene, root, Store);
            Assert.IsTrue(Session.Attach(), Session.Status);
        }
        public void Dispose() { Session.Dispose(); Scene.Dispose(); }
    }

    private sealed class ThrowOnStage : IFurnitureFileWriter
    {
        public void WriteStaged(string stagedPath, byte[] bytes) => throw new IOException("synthetic staging failure");
        public void Commit(string stagedPath, string targetPath, bool targetExisted) => throw new AssertFailedException("A failed stage cannot commit.");
    }

    private sealed class BlockingStage : IFurnitureFileWriter, IDisposable
    {
        private readonly FurnitureAtomicFileWriter writer = new();
        internal ManualResetEventSlim Entered { get; } = new();
        internal ManualResetEventSlim Release { get; } = new();
        public void WriteStaged(string stagedPath, byte[] bytes)
        {
            writer.WriteStaged(stagedPath, bytes);
            Entered.Set();
            if (!Release.Wait(10000)) throw new IOException("synthetic writer deadline");
        }
        public void Commit(string stagedPath, string targetPath, bool targetExisted) => writer.Commit(stagedPath, targetPath, targetExisted);
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }
}
