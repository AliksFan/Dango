using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dango.Core.Furniture;
using Dango.Desktop.Furniture;
using Dango.Desktop.Physics.Presentation;
using Dango.Physics;

namespace Dango.Desktop.Tests.Furniture;

[TestClass]
public sealed class OrdinaryRoomFurnitureCollectionSessionTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);
    private static readonly Guid PreviousOperation = Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static FurnitureLayoutContract Contract => FurnitureLayoutContract.OrdinaryRoom;
    private static string Module => Environment.GetEnvironmentVariable("DANGO_ORDINARY_FURNITURE_TEST_MODULE") ??
        throw new InvalidOperationException("Set the explicit frozen multi-furniture native candidate for session gates.");

    private static FurnitureItemInstance Item(int index, FurnitureItemKind kind, double x, double y) =>
        new(Guid.Parse($"10000000-0000-4000-8000-{index:000000000000}"), kind, new(x, y));
    private static FurnitureLayout Layout(params FurnitureItemInstance[] items) => FurnitureLayout.ForItems(1, PreviousOperation, items);
    private static FurnitureLayout MixedThirty() => FurnitureLayout.ForItems(1, PreviousOperation,
        Enumerable.Range(0, 20).Select(index => Item(index + 1, FurnitureItemKind.StaticPlatform,
            new[] { 160d, 432, 704 }[index % 3], 21 * (index / 3 + 1)))
        .Concat(Enumerable.Range(0, 10).Select(index => Item(index + 21, FurnitureItemKind.RoundBall,
            1326 + 132 * (index % 6), 66 + 132 * (index / 6)))));

    [TestMethod, TestCategory("NativeIntegration")]
    public void ThirtyMixedItemsAttachAndThirtyFirstPreviewOrDirectRequestCannotChangeAnything()
    {
        var layout = MixedThirty();
        using var fixture = new Fixture(layout);
        var before = fixture.Scene.ReadFurnitureCollectionFrame();
        var bytes = File.ReadAllBytes(fixture.Path);
        Assert.AreEqual(30, before.Items.Count);
        Assert.AreEqual(20, before.Items.Count(item => item.Definition.Kind == DangoFurnitureKind.StaticPlatform));
        Assert.AreEqual(10, before.Items.Count(item => item.Definition.Kind == DangoFurnitureKind.RoundBall));
        Assert.AreEqual(30, fixture.Session.RenderItems.Count);
        CollectionAssert.AreEquivalent(layout.Items.Select(item => item.InstanceId).ToArray(), fixture.Session.RenderItems.Select(item => item.InstanceId).ToArray());
        fixture.Session.PreviewNewFurniture(FurnitureItemKind.RoundBall, new(1800, 294));
        Assert.IsFalse(fixture.Session.HasPreview);
        Assert.IsFalse(fixture.Session.CanConfirm);
        Complete(fixture.Session, fixture.Session.ConfirmAsync());
        var direct = new RoomFurnitureEditOperation(Guid.NewGuid(), 1, FurnitureEditKind.Insert, null,
            Item(31, FurnitureItemKind.StaticPlatform, 1900, 300));
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Session.SubmitRoomOperationAsync(direct));
        Assert.AreEqual(Json(before), Json(fixture.Scene.ReadFurnitureCollectionFrame()));
        Assert.AreEqual(layout, fixture.Session.Layout);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(fixture.Path));
        Assert.AreEqual(OrdinaryFurnitureSceneState.Synced, fixture.Session.State);
        Assert.IsTrue(fixture.Session.CanEdit);
    }

    [TestMethod, TestCategory("NativeIntegration")]
    [DataRow(FurnitureItemKind.StaticPlatform)]
    [DataRow(FurnitureItemKind.RoundBall)]
    public void MoveAndDeleteOneTargetPreserveTheOtherTwentyNinePhysicalStates(FurnitureItemKind kind)
    {
        using var fixture = new Fixture(MixedThirty());
        var target = fixture.Session.Layout.Items.Last(item => item.Kind == kind);
        var before = fixture.Scene.ReadFurnitureCollectionFrame();
        var targetNative = NativeItemFor(fixture, target);
        var others = before.Items.Where(item => item.InstanceId != targetNative.InstanceId).Select(Physical).ToArray();
        var body = Json(before.BodyDiagnostics.Single().Nodes);
        var candidate = target with { Anchor = kind == FurnitureItemKind.RoundBall ? new(1800, 294) : new(432, 250) };
        fixture.Session.PreviewMoveFurniture(target.InstanceId, candidate.Anchor);
        Assert.IsTrue(fixture.Session.CanConfirm, fixture.Session.Status);
        Assert.AreEqual(Json(before), Json(fixture.Scene.ReadFurnitureCollectionFrame()));
        Complete(fixture.Session, fixture.Session.ConfirmAsync());
        var moved = fixture.Scene.ReadFurnitureCollectionFrame();
        Assert.AreEqual(30, moved.Items.Count);
        Assert.AreEqual(before.PhysicsStep, moved.PhysicsStep);
        Assert.AreEqual(before.WorldGeneration, moved.WorldGeneration);
        Assert.AreEqual(body, Json(moved.BodyDiagnostics.Single().Nodes));
        CollectionAssert.AreEqual(others, moved.Items.Where(item => item.InstanceId != targetNative.InstanceId).Select(Physical).ToArray());
        Assert.AreEqual(candidate, fixture.Session.Layout.Find(target.InstanceId));
        Assert.AreEqual(2L, fixture.Session.Layout.LayoutVersion);
        if (kind == FurnitureItemKind.RoundBall)
        {
            var ball = moved.Items.Single(item => item.InstanceId == targetNative.InstanceId).BallState!;
            Assert.AreEqual(0d, ball.VelocityQX);
            Assert.AreEqual(0d, ball.VelocityQY);
            Assert.AreEqual(0d, ball.AngularVelocity);
        }
        fixture.Session.SelectFurniture(target.InstanceId);
        Complete(fixture.Session, fixture.Session.DeleteAsync());
        var deleted = fixture.Scene.ReadFurnitureCollectionFrame();
        Assert.AreEqual(29, deleted.Items.Count);
        Assert.IsFalse(deleted.Items.Any(item => item.InstanceId == targetNative.InstanceId));
        CollectionAssert.AreEqual(others, deleted.Items.Select(Physical).ToArray());
        Assert.AreEqual(body, Json(deleted.BodyDiagnostics.Single().Nodes));
        Assert.AreEqual(before.PhysicsStep, deleted.PhysicsStep);
        Assert.AreEqual(3L, fixture.Session.Layout.LayoutVersion);
        Assert.AreEqual(fixture.Session.Layout, fixture.Store.Load(fixture.Path).Layout);
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void NativeMotionDrivesRenderWhileSavedAnchorStaysFixedAndReentryRestoresItAtRest()
    {
        var item = Item(1, FurnitureItemKind.RoundBall, 1700, 240);
        using var fixture = new Fixture(Layout(item));
        var before = fixture.Scene.ReadFurnitureCollectionFrame();
        var bytes = File.ReadAllBytes(fixture.Path);
        for (var step = 0; step < 60; step++)
        {
            fixture.Scene.Step(Dt); fixture.Session.OnFrame();
            Assert.AreEqual(OrdinaryFurnitureSceneState.Synced, fixture.Session.State, fixture.Session.Status);
        }
        var current = fixture.Scene.ReadFurnitureCollectionFrame();
        var ball = current.Items.Single().BallState!;
        var live = fixture.Scene.OrdinaryBallAnchor(ball);
        Assert.IsTrue(current.PhysicsStep > before.PhysicsStep);
        Assert.IsTrue(Math.Abs(live.Y - item.Anchor.Y) > 20, "The native ball must actually fall; a static pose cannot pass this gate.");
        Assert.AreEqual(live, fixture.Session.RenderItems.Single().Anchor);
        Assert.AreEqual(ball.AngleRadians, fixture.Session.RenderItems.Single().AngleRadians);
        Assert.AreEqual(item.Anchor, fixture.Session.Layout.Find(item.InstanceId)!.Anchor);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(fixture.Path));
        Assert.IsTrue(fixture.Session.TryDetach(), fixture.Session.Status);
        Assert.AreEqual(0, fixture.Scene.ReadFurnitureCollectionFrame().Items.Count);
        Assert.AreEqual(0, fixture.Session.RenderItems.Count);
        Assert.IsTrue(fixture.Session.Attach(), fixture.Session.Status);
        var restored = fixture.Scene.ReadFurnitureCollectionFrame();
        var restoredBall = restored.Items.Single().BallState!;
        Assert.AreEqual(item.Anchor.X, fixture.Scene.OrdinaryBallAnchor(restoredBall).X, 1e-4);
        Assert.AreEqual(item.Anchor.Y, fixture.Scene.OrdinaryBallAnchor(restoredBall).Y, 1e-4);
        Assert.AreEqual(0d, restoredBall.VelocityQX);
        Assert.AreEqual(0d, restoredBall.VelocityQY);
        Assert.AreEqual(0d, restoredBall.AngularVelocity);
        Assert.AreEqual(1L, fixture.Session.Layout.LayoutVersion);
        Assert.AreEqual(item.InstanceId, fixture.Session.RenderItems.Single().InstanceId);
        Assert.AreEqual(current.WorldGeneration, restored.WorldGeneration);
        Assert.AreEqual(current.PhysicsStep, restored.PhysicsStep);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(fixture.Path));
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void ConfirmingBallAtItsSavedAnchorStillPlacesTheMovingBallAndCommitsOneVersion()
    {
        var item = Item(1, FurnitureItemKind.RoundBall, 1700, 240);
        using var fixture = new Fixture(Layout(item));
        for (var step = 0; step < 30; step++) { fixture.Scene.Step(Dt); fixture.Session.OnFrame(); }
        var before = fixture.Scene.ReadFurnitureCollectionFrame();
        Assert.IsTrue(Math.Abs(fixture.Session.RenderItems.Single().Anchor.Y - item.Anchor.Y) > 10);
        Assert.IsTrue(Math.Abs(before.Items.Single().BallState!.VelocityQY) > .1);
        fixture.Session.PreviewMoveFurniture(item.InstanceId, item.Anchor);
        Assert.IsTrue(fixture.Session.CanConfirm, fixture.Session.Status);
        Complete(fixture.Session, fixture.Session.ConfirmAsync());
        var after = fixture.Scene.ReadFurnitureCollectionFrame();
        Assert.AreEqual(before.PhysicsStep, after.PhysicsStep);
        Assert.AreEqual(before.Items.Single().InstanceId, after.Items.Single().InstanceId);
        Assert.AreEqual(item.Anchor.X, fixture.Session.RenderItems.Single().Anchor.X, 1e-4);
        Assert.AreEqual(item.Anchor.Y, fixture.Session.RenderItems.Single().Anchor.Y, 1e-4);
        Assert.AreEqual(0d, after.Items.Single().BallState!.VelocityQY);
        Assert.AreEqual(2L, fixture.Session.Layout.LayoutVersion);
        Assert.AreEqual(fixture.Session.Layout, fixture.Store.Load(fixture.Path).Layout);
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void OverlappingSavedItemsAreProtectedBeforeAnyRuntimeBindingOrOverwrite()
    {
        var layout = Layout(Item(1, FurnitureItemKind.RoundBall, 1600, 240), Item(2, FurnitureItemKind.RoundBall, 1900, 240));
        var document = JsonNode.Parse(FurnitureLayoutSerializer.Serialize(layout, Contract))!.AsObject();
        document["items"]![1]!["anchor"] = document["items"]![0]!["anchor"]!.DeepClone();
        var bytes = Encoding.UTF8.GetBytes(document.ToJsonString());
        using var fixture = new Fixture(rawBytes: bytes, attach: false);
        var before = fixture.Scene.ReadFurnitureCollectionFrame();
        Assert.IsFalse(fixture.Session.Attach());
        Assert.AreEqual(OrdinaryFurnitureSceneState.Protected, fixture.Session.State);
        Assert.IsFalse(fixture.Session.CanEdit);
        fixture.Session.PreviewNewFurniture(FurnitureItemKind.StaticPlatform, new(1440, 120));
        Complete(fixture.Session, fixture.Session.ConfirmAsync());
        Assert.AreEqual(Json(before), Json(fixture.Scene.ReadFurnitureCollectionFrame()));
        Assert.AreEqual(0, fixture.Scene.ReadFurnitureCollectionFrame().Items.Count);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(fixture.Path));
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void LegacySinglePlatformSessionReadAndReentryPreserveBytesUntilSuccessfulBallEdit()
    {
        var layout = Layout(Item(1, FurnitureItemKind.StaticPlatform, 1440, 120));
        var document = JsonNode.Parse(FurnitureLayoutSerializer.Serialize(layout, Contract))!.AsObject();
        document["schemaVersion"] = 1;
        var bytes = Encoding.UTF8.GetBytes(" \r\n" + document.ToJsonString() + "\r\n ");
        using var fixture = new Fixture(rawBytes: bytes);
        Assert.AreEqual(layout, fixture.Session.Layout);
        Assert.IsTrue(fixture.Session.TryEnterEditing());
        Assert.IsTrue(fixture.Session.TryDetach());
        Assert.IsTrue(fixture.Session.Attach(), fixture.Session.Status);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(fixture.Path));
        fixture.Session.PreviewNewFurniture(FurnitureItemKind.RoundBall, new(1800, 240));
        Assert.IsTrue(fixture.Session.CanConfirm, fixture.Session.Status);
        Complete(fixture.Session, fixture.Session.ConfirmAsync());
        Assert.AreEqual(2, fixture.Session.Layout.Items.Count);
        Assert.AreEqual(layout.Items[0], fixture.Session.Layout.Items[0]);
        Assert.AreEqual(2L, fixture.Session.Layout.LayoutVersion);
        Assert.AreEqual(2, JsonNode.Parse(File.ReadAllBytes(fixture.Path))!["schemaVersion"]!.GetValue<int>());
        Assert.AreEqual(fixture.Session.Layout, fixture.Store.Load(fixture.Path).Layout);
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void MultiItemSaveBeforeNativeFailureRecoversWithoutResavingOrChangingOtherEntities()
    {
        var layout = Layout(Item(1, FurnitureItemKind.StaticPlatform, 1440, 120),
            Item(2, FurnitureItemKind.RoundBall, 1600, 240), Item(3, FurnitureItemKind.RoundBall, 1900, 240));
        var writer = new CountingWriter();
        using var fixture = new Fixture(layout, writer: writer);
        var before = fixture.Scene.ReadFurnitureCollectionFrame();
        var target = layout.Items[1];
        var targetNative = NativeItemFor(fixture, target);
        var others = before.Items.Where(item => item.InstanceId != targetNative.InstanceId).Select(Physical).ToArray();
        fixture.Session.BeforeNativeApply = () => throw new IOException("synthetic pre-apply multi-item interruption");
        fixture.Session.PreviewMoveFurniture(target.InstanceId, new(1700, 280));
        Assert.IsTrue(fixture.Session.CanConfirm, fixture.Session.Status);
        Complete(fixture.Session, fixture.Session.ConfirmAsync());
        Assert.AreEqual(OrdinaryFurnitureSceneState.PersistedNotApplied, fixture.Session.State);
        Assert.AreEqual(layout, fixture.Session.Layout);
        Assert.AreEqual(2L, fixture.Session.PersistedLayout.LayoutVersion);
        Assert.AreEqual(3, fixture.Session.PersistedLayout.Items.Count);
        Assert.AreEqual(Json(before), Json(fixture.Scene.ReadFurnitureCollectionFrame()));
        Assert.IsFalse(fixture.Session.TryDetach());
        Assert.IsFalse(fixture.Session.CanEdit);
        Assert.IsTrue(fixture.Session.CanRecover);
        Assert.AreEqual(1, writer.StageCalls);
        Assert.AreEqual(1, writer.CommitCalls);
        var bytes = File.ReadAllBytes(fixture.Path);
        fixture.Session.BeforeNativeApply = null;
        Complete(fixture.Session, fixture.Session.RecoverAsync());
        var after = fixture.Scene.ReadFurnitureCollectionFrame();
        Assert.AreEqual(OrdinaryFurnitureSceneState.Synced, fixture.Session.State);
        Assert.AreEqual(2L, fixture.Session.Layout.LayoutVersion);
        Assert.AreEqual(3, after.Items.Count);
        Assert.AreEqual(before.PhysicsStep, after.PhysicsStep);
        Assert.AreEqual(before.SceneRevision + 1, after.SceneRevision);
        CollectionAssert.AreEqual(others, after.Items.Where(item => item.InstanceId != targetNative.InstanceId).Select(Physical).ToArray());
        Assert.AreEqual(Json(before.BodyDiagnostics.Single().Nodes), Json(after.BodyDiagnostics.Single().Nodes));
        Assert.AreEqual(1, writer.StageCalls);
        Assert.AreEqual(1, writer.CommitCalls);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(fixture.Path));
        Assert.AreEqual(fixture.Session.Layout, fixture.Store.Load(fixture.Path).Layout);
    }

    private static DangoFurnitureCollectionItem NativeItemFor(Fixture fixture, FurnitureItemInstance item)
    {
        var definition = fixture.Scene.OrdinaryFurnitureDefinition(item);
        return fixture.Scene.ReadFurnitureCollectionFrame().Items.Single(native =>
            native.Definition.Kind == definition.Kind && (definition.Platform is { } platform
                ? native.Definition.Platform is { } actual && Math.Abs(actual.CenterX - platform.CenterX) < 1e-5 &&
                    Math.Abs(actual.CenterY - platform.CenterY) < 1e-5
                : native.Definition.Ball == definition.Ball));
    }
    private static string Physical(DangoFurnitureCollectionItem item) => Json(new { item.InstanceId, item.Definition,
        Ball = item.BallState is { } ball ? new { ball.CenterQX, ball.CenterQY, ball.VelocityQX, ball.VelocityQY,
            ball.AngleRadians, ball.AngularVelocity, ball.IsSleeping, ball.IntegrationCount } : null });
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static void Complete(OrdinaryRoomFurnitureSession session, Task operation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!operation.IsCompleted && DateTime.UtcNow < deadline) { session.ProcessBoundary(); Thread.Sleep(1); }
        Assert.IsTrue(operation.IsCompleted, session.Status);
        operation.GetAwaiter().GetResult();
    }
    private sealed class Fixture : IDisposable
    {
        internal DesktopPhysicsScene Scene { get; }
        internal OrdinaryRoomFurnitureSession Session { get; }
        internal FurnitureLayoutStore Store { get; }
        internal string Path { get; }
        internal Fixture(FurnitureLayout? layout = null, byte[]? rawBytes = null, IFurnitureFileWriter? writer = null, bool attach = true)
        {
            var root = System.IO.Path.Combine(TestRepository.Root, "artifacts", "test-runs", "ordinary-furniture-collection", Guid.NewGuid().ToString("N"));
            Path = FurnitureLayoutStore.GetLayoutPath(root, Contract);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            if (rawBytes is not null || layout is not null) File.WriteAllBytes(Path, rawBytes ?? FurnitureLayoutSerializer.Serialize(layout!, Contract));
            Store = new(writer, Contract);
            Scene = new(Module, new(1000, 240, 160, 120));
            OrdinaryRoomFurnitureSession? ownedSession = null;
            try
            {
                for (var step = 0; step < 180; step++) Assert.IsTrue(Scene.Step(Dt).Physics.Succeeded);
                Scene.SetHorizontalBounds(Contract.Left, Contract.Right);
                Assert.IsTrue(Scene.SupportsFurnitureCollection, "The frozen candidate must expose collection ABI and half ball gravity.");
                Session = ownedSession = new(Scene, root, Store);
                if (attach) Assert.IsTrue(Session.Attach(), Session.Status);
            }
            catch { ownedSession?.Dispose(); Scene.Dispose(); throw; }
        }
        public void Dispose() { Session.Dispose(); Scene.Dispose(); }
    }
    private sealed class CountingWriter : IFurnitureFileWriter
    {
        private readonly FurnitureAtomicFileWriter writer = new();
        internal int StageCalls, CommitCalls;
        public void WriteStaged(string stagedPath, byte[] bytes) { StageCalls++; writer.WriteStaged(stagedPath, bytes); }
        public void Commit(string stagedPath, string targetPath, bool targetExisted) { CommitCalls++; writer.Commit(stagedPath, targetPath, targetExisted); }
    }
}
