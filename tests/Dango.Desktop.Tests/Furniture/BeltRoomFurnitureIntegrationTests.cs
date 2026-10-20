using System.Text.Json;
using Dango.Core.Animation;
using Dango.Core.Furniture;
using Dango.Desktop.Furniture;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Tests.Furniture;

[TestClass, DoNotParallelize]
public sealed class BeltRoomFurnitureIntegrationTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);
    private static readonly FurnitureLayoutContract Contract = FurnitureLayoutContract.OrdinaryRoom;
    private static FurnitureItemInstance Item(int id, FurnitureItemKind kind, double x, double y) =>
        new(Guid.Parse($"30000000-0000-4000-8000-{id:000000000000}"), kind, new(x, y));
    private static FurnitureLayout Layout(params FurnitureItemInstance[] items) => FurnitureLayout.ForItems(1, Guid.NewGuid(), items);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    [TestMethod, TestCategory("NativeIntegration")]
    public void DrivingTheRealBodyPushesTheRoomBallWithBilateralContact()
    {
        using var fixture = new Fixture(Layout(Item(1, FurnitureItemKind.RoundBall, 1360, 66)));
        var initial = fixture.Session.RenderItems.Single().Anchor;
        var contacts = 0;
        double impulse = 0;
        fixture.Scene.SetInput(new(false, true, false, false));
        for (var i = 0; i < 360; i++)
        {
            fixture.Step();
            var contact = fixture.Session.NativeFrame!.Items.Single().DangoContact!;
            if (contact.Touching) { contacts++; impulse = Math.Max(impulse, contact.NormalImpulse); }
        }
        Assert.IsTrue(contacts > 0);
        Assert.IsTrue(impulse > 1e-5);
        Assert.IsTrue(fixture.Session.RenderItems.Single().Anchor.X > initial.X + 50);
        Assert.AreEqual(initial, fixture.Session.Layout.Items.Single().Anchor);
        Console.WriteLine($"Actual body/ball driven contact frames={contacts}; max impulse={impulse:G9}");
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void AddingABallWakesTheSleepingBodyAndAllBallFramesKeepAdvancing()
    {
        using var fixture = new Fixture(Layout());
        fixture.Scene.ConfigureBeltSleepPresentationScale(1);
        for (var i = 0; i < 3600 && !fixture.Scene.BeltRoomSleeping; i++) fixture.Step();
        Assert.IsTrue(fixture.Scene.BeltRoomSleeping);
        var tick = fixture.Scene.BeltRoomFrame!.Tick;
        var calls = fixture.Scene.BeltRoomNativeStepCalls;
        fixture.Session.PreviewNewFurniture(FurnitureItemKind.RoundBall, new(1700, 240));
        Assert.IsTrue(fixture.Session.CanConfirm, fixture.Session.Status);
        Complete(fixture.Session, fixture.Session.ConfirmAsync());
        Assert.IsFalse(fixture.Scene.BeltRoomSleeping);
        Assert.AreEqual(tick, fixture.Scene.BeltRoomFrame!.Tick);
        for (var i = 0; i < 600; i++) fixture.Step();
        Assert.IsFalse(fixture.Scene.BeltRoomSleeping);
        Assert.AreEqual(tick + 600, fixture.Scene.BeltRoomFrame!.Tick);
        Assert.AreEqual(calls + 600, fixture.Scene.BeltRoomNativeStepCalls);
        Assert.AreEqual(2400UL, fixture.Session.NativeFrame!.Items.Single().BallState!.IntegrationCount);
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void ThirtyActualItemsUseUnchangedRoomGeometryAndAtomicCapacity()
    {
        var items = Enumerable.Range(0, 20).Select(i => Item(i + 1, FurnitureItemKind.StaticPlatform,
            new[] { 160d, 432, 704 }[i % 3], 21 * (i / 3 + 1)))
            .Concat(Enumerable.Range(0, 10).Select(i => Item(i + 21, FurnitureItemKind.RoundBall,
                1326 + 132 * (i % 6), 66 + 132 * (i / 6)))).ToArray();
        using var fixture = new Fixture(Layout(items));
        var before = fixture.Scene.ReadFurnitureCollectionFrame();
        Assert.AreEqual(30, before.Items.Count);
        Assert.AreEqual(DangoFurnitureCoordinateSpace.BeltWorldMeters, before.CoordinateSpace);
        foreach (var item in before.Items)
        {
            Assert.AreEqual(DangoFurnitureCoordinateSpace.BeltWorldMeters, item.Definition.CoordinateSpace);
            if (item.Definition.Platform is { } platform)
            {
                Assert.AreEqual(272d / 72, platform.Width, 1e-12);
                Assert.AreEqual(21d / 72, platform.Height, 1e-12);
            }
            else Assert.AreEqual(66d / 72, item.Definition.Ball!.RadiusQ, 1e-12);
        }
        var bytes = File.ReadAllBytes(fixture.Path);
        var extra = fixture.Scene.OrdinaryFurnitureDefinition(Item(31, FurnitureItemKind.StaticPlatform, 1900, 300));
        var denied = fixture.Scene.PrepareFurnitureChange(DangoFurnitureChangeKind.Insert, 0, extra, before.SceneRevision);
        Assert.AreEqual(DangoFurnitureChangeRejection.CapacityExceeded, denied.Probe.Rejection);
        Assert.IsNull(denied.Token);
        Assert.AreEqual(Json(before), Json(fixture.Scene.ReadFurnitureCollectionFrame()));
        for (var i = 0; i < 120; i++) fixture.Step();
        Assert.AreEqual(30, fixture.Session.RenderItems.Count);
        Assert.AreEqual(fixture.Scene.BeltRoomFrame!.Tick, fixture.Session.NativeFrame!.PhysicsStep);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(fixture.Path));
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void BallMotionComesFromSameNativeFrameAndReentryUsesSavedAnchor()
    {
        var item = Item(1, FurnitureItemKind.RoundBall, 1700, 240);
        using var fixture = new Fixture(Layout(item));
        var bytes = File.ReadAllBytes(fixture.Path);
        for (var i = 0; i < 60; i++) fixture.Step();
        var frame = fixture.Scene.ReadFurnitureCollectionFrame();
        var ball = frame.Items.Single().BallState!;
        var live = fixture.Session.RenderItems.Single();
        Assert.AreEqual(fixture.Scene.OrdinaryBallAnchor(ball), live.Anchor);
        Assert.IsTrue(Math.Abs(live.Anchor.Y - item.Anchor.Y) > 20);
        Assert.AreEqual(item.Anchor, fixture.Session.Layout.Items.Single().Anchor);
        Assert.AreEqual(frame.PhysicsStep, fixture.Scene.BeltRoomFrame!.Tick);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(fixture.Path));
        Assert.IsTrue(fixture.Session.TryDetach(), fixture.Session.Status);
        Assert.IsTrue(fixture.Session.Attach(), fixture.Session.Status);
        Assert.AreEqual(item.Anchor.X, fixture.Session.RenderItems.Single().Anchor.X, 1e-9);
        Assert.AreEqual(item.Anchor.Y, fixture.Session.RenderItems.Single().Anchor.Y, 1e-9);
        Assert.AreEqual(0d, fixture.Scene.ReadFurnitureCollectionFrame().Items.Single().BallState!.VelocityQY);
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void RealLockedFileCannotApplyAndPersistedApplyInterruptionCanRecover()
    {
        using var fixture = new Fixture(Layout(Item(1, FurnitureItemKind.StaticPlatform, 1440, 120)));
        var before = fixture.Scene.ReadFurnitureCollectionFrame();
        var bytes = File.ReadAllBytes(fixture.Path);
        fixture.Session.PreviewNewFurniture(FurnitureItemKind.RoundBall, new(1800, 240));
        Assert.IsTrue(fixture.Session.CanConfirm, fixture.Session.Status);
        using (var locked = new FileStream(fixture.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Complete(fixture.Session, fixture.Session.ConfirmAsync());
        Assert.AreEqual(Json(before), Json(fixture.Scene.ReadFurnitureCollectionFrame()));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(fixture.Path));
        fixture.Session.BeforeNativeApply = () => throw new IOException("Fictional pre-apply test interruption");
        fixture.Session.PreviewNewFurniture(FurnitureItemKind.RoundBall, new(1800, 240));
        Assert.IsTrue(fixture.Session.CanConfirm, fixture.Session.Status);
        Complete(fixture.Session, fixture.Session.ConfirmAsync());
        Assert.AreEqual(OrdinaryFurnitureSceneState.PersistedNotApplied, fixture.Session.State);
        Assert.AreEqual(Json(before), Json(fixture.Scene.ReadFurnitureCollectionFrame()));
        Assert.AreEqual(2, fixture.Store.Load(fixture.Path).Layout!.Items.Count);
        Assert.IsFalse(fixture.Session.TryDetach());
        fixture.Session.BeforeNativeApply = null;
        Complete(fixture.Session, fixture.Session.RecoverAsync());
        Assert.AreEqual(OrdinaryFurnitureSceneState.Synced, fixture.Session.State);
        Assert.AreEqual(fixture.Session.Layout, fixture.Store.Load(fixture.Path).Layout);
        Assert.AreEqual(2, fixture.Scene.ReadFurnitureCollectionFrame().Items.Count);
        Assert.AreEqual(before.PhysicsStep, fixture.Scene.BeltRoomFrame!.Tick);
    }

    private static void Complete(OrdinaryRoomFurnitureSession session, Task task)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < timeout) { session.ProcessBoundary(); Thread.Sleep(1); }
        Assert.IsTrue(task.IsCompleted, session.Status);
        task.GetAwaiter().GetResult();
    }
    private sealed class Fixture : IDisposable
    {
        internal DesktopPhysicsScene Scene { get; }
        internal OrdinaryRoomFurnitureSession Session { get; }
        internal FurnitureLayoutStore Store { get; } = new(contract: Contract);
        internal string Path { get; }
        internal Fixture(FurnitureLayout layout)
        {
            var root = System.IO.Path.Combine(TestRepository.Root, "artifacts", "test-runs", "belt-room-furniture", Guid.NewGuid().ToString("N"));
            Path = FurnitureLayoutStore.GetLayoutPath(root, Contract);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllBytes(Path, FurnitureLayoutSerializer.Serialize(layout, Contract));
            var module = Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE")
                ?? throw new InvalidOperationException("An explicit collection room module is required.");
            Scene = new(module, new(1000, 240, 160, 120), DangoBodyControls.Default);
            try
            {
                for (var i = 0; i < 180; i++) Assert.IsTrue(Scene.Step(Dt).Physics.Succeeded);
                Scene.SetHorizontalBounds(Contract.Left, Contract.Right);
                Session = new(Scene, root, Store);
                Assert.IsTrue(Session.Attach(), Session.Status);
            }
            catch { Scene.Dispose(); throw; }
        }
        internal void Step()
        {
            Assert.IsTrue(Scene.Step(Dt).Physics.Succeeded);
            Assert.AreEqual(0, Scene.LastErrorCode);
            Session.OnFrame();
            Assert.AreEqual(OrdinaryFurnitureSceneState.Synced, Session.State, Session.Status);
        }
        public void Dispose() { Session.Dispose(); Scene.Dispose(); }
    }
}
