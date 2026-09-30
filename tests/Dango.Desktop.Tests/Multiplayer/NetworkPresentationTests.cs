using System.Windows;
using System.Windows.Media;
using Dango.Core.Coordinates;
using Dango.Desktop.Multiplayer;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Dango.Protocol;

namespace Dango.Desktop.Tests.Multiplayer;

[TestClass]
public sealed class NetworkPresentationTests
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1d / 60);
    private static readonly LogicalRect Viewport = new(0, 0, 710, 380);
    private static string NativePath => Environment.GetEnvironmentVariable("DANGO_PHYSICS_MODULE_PATH") ??
        System.IO.Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll");
    internal static PetState Pet(string id = "pet-a", double x = .3) => new(id, "owner-" + id,
        new(x, .12), "right", null, null, PresetRef.Default, PresetRef.Default);
    private static ActionInstance Wave => new("wave-a", "wave", ["pet-a"], 100, 1300, 3, 1, "wave-nod");
    private static DangoBodyState ActualBody()
    {
        using var world = DangoPhysicsWorld.Create(NativePath, capacity: 1);
        var id = world.Add(0, 0);
        DangoBodyState? body = null;
        for (var i = 0; i < 120; i++)
        {
            var frame = world.Step(Step, [new(id)]);
            Assert.IsTrue(frame.Succeeded); body = frame.Bodies.Single();
        }
        return body!;
    }

    [TestMethod]
    public void RealNativeBodyWithOnlyCorePositionChangedCannotMoveNetworkAnchor()
    {
        var body = ActualBody();
        using var first = new NetworkPetPresentation("pet-a", () => new FixedWorld(body with { CoreX = 7, CoreY = 12 }));
        using var second = new NetworkPetPresentation("pet-a", () => new FixedWorld(body with { CoreX = -19, CoreY = -41 }));
        var a = first.Sample(Pet(), 500, Viewport, Step);
        var b = second.Sample(Pet(), 500, Viewport, Step);
        Assert.AreEqual(new RoomPoint(.3, .12), a.AnchorPosition);
        Assert.AreEqual(a.Bounds, b.Bounds, "Local CoreX/CoreY must never place a shared pet.");
        Assert.AreEqual(165, a.Bounds.X, 1e-10, "Independent normalized-anchor mapping.");
        CollectionAssert.AreEqual(a.Presentation.Contour.ToArray(), b.Presentation.Contour.ToArray());
    }

    [TestMethod]
    public void NativeWorldsHaveIndependentAttachmentsAndOneFailureDoesNotFreezeOtherOrSharedTime()
    {
        var body = ActualBody();
        using var a = new NetworkPetPresentation("pet-a", () => new FixedWorld(body, failAfter: 1));
        using var b = new NetworkPetPresentation("pet-b", NativePath);
        var pet = Pet() with { Action = Wave };
        var firstA = a.Sample(pet, 100, Viewport, Step);
        var firstB = b.Sample(Pet("pet-b", .7), 100, Viewport, Step);
        Assert.IsFalse(firstA.IsFallback); Assert.IsFalse(firstB.IsFallback);
        Assert.IsNotNull(firstA.Presentation.Attachments); Assert.IsNotNull(firstB.Presentation.Attachments);
        Assert.AreNotSame(firstA.Presentation.Attachments, firstB.Presentation.Attachments);
        var failed = a.Sample(pet, 700, Viewport, Step);
        var healthy = b.Sample(Pet("pet-b", .7), 700, Viewport, Step);
        Assert.IsTrue(failed.IsFallback); Assert.IsFalse(healthy.IsFallback);
        Assert.AreEqual(.5, failed.Wave.Progress, 1e-12); Assert.AreEqual(600, failed.Wave.RemainingMilliseconds);
        Assert.AreEqual(.3, failed.AnchorPosition.X, 1e-12);
        Assert.AreEqual(2, healthy.FrameNumber);
        Assert.IsFalse(a.Sample(pet, 1300, Viewport, Step).Wave.IsActive);
    }

    [TestMethod]
    public void AuthoritativeWaveLateJoinAndFallbackKeepRemainingTimeAndReturnToIdle()
    {
        using var native = new NetworkPetPresentation("pet-a", NativePath);
        using var fallback = new NetworkPetPresentation("pet-a", "missing-network-native.dll");
        var pet = Pet() with { Action = Wave };
        foreach (var now in new long[] { 100, 400, 700, 1000, 1299, 1300, 2500 })
        {
            var normal = native.Sample(pet, now, Viewport, Step);
            var safe = fallback.Sample(pet, now, Viewport, Step);
            Assert.IsFalse(normal.IsFallback); Assert.IsTrue(safe.IsFallback);
            Assert.AreEqual(normal.Wave, safe.Wave);
            Assert.AreEqual(normal.AnchorPosition, safe.AnchorPosition);
            if (now < 1300)
            {
                Assert.AreEqual((now - 100) / 1200d, safe.Wave.Progress, 1e-12);
                Assert.AreEqual(1300 - now, safe.Wave.RemainingMilliseconds);
            }
            else { Assert.IsFalse(safe.Wave.IsActive); Assert.AreEqual(1, safe.ScaleX); Assert.AreEqual(1, safe.ScaleY); }
        }
        var reduced = native.Sample(pet, 700, Viewport, Step, reducedMotion: true);
        Assert.IsTrue(reduced.IsFallback); Assert.AreEqual(.5, reduced.Wave.Progress, 1e-12);
        Assert.AreEqual(600, reduced.Wave.RemainingMilliseconds);
    }

    [TestMethod]
    public void SceneSortsStableIdsResetsOnlyForNewEpochAndNeverPushesOverlappingPetsApart()
    {
        using var scene = new NetworkScenePresenter(NativePath);
        var snapshot = new RoomSnapshot(RoomProtocol.Version, 1, RoomProtocol.RoomId, "epoch-one", 2, 100,
            "pet-a", CoordinateSpace.Default, [Pet("pet-b", .5), Pet("pet-a", .5)]);
        var first = scene.Sample(snapshot, 100, Viewport, Step);
        CollectionAssert.AreEqual(new[] { "pet-a", "pet-b" }, first.Select(p => p.PetId).ToArray());
        Assert.AreEqual(first[0].Bounds.X, first[1].Bounds.X, 1e-10);
        var next = scene.Sample(snapshot, 200, Viewport, Step);
        Assert.AreEqual(2, next[0].FrameNumber);
        scene.SetPaused(true);
        Assert.ThrowsExactly<InvalidOperationException>(() => scene.Sample(snapshot, 10000, Viewport, Step));
        scene.SetPaused(false);
        Assert.AreEqual(3, scene.Sample(snapshot, 10000, Viewport, Step)[0].FrameNumber, "No hidden frame catch-up.");
        Assert.AreEqual(1, scene.Sample(snapshot with { RoomEpoch = "epoch-two" }, 100, Viewport, Step)[0].FrameNumber);
        Assert.AreEqual(2, snapshot.LastSequence); Assert.AreEqual(.5, snapshot.Pets[0].AnchorPosition.X);
    }

    [STATestMethod]
    public void RealWpfBodyEyesHatShareGroundPivotIncludingNativeFallbackAndStayWithinTwoDip()
    {
        foreach (var path in new[] { NativePath, "missing-network-native.dll" })
        {
            using var presentation = new NetworkPetPresentation("pet-a", path);
            var visual = new NetworkPetVisual();
            for (var now = 100; now <= 1300; now += 20)
            {
                var frame = presentation.Sample(Pet() with { Action = Wave }, now, Viewport, Step);
                visual.ApplyFrame(frame);
                visual.Measure(new Size(160, 120)); visual.Arrange(new Rect(0, 0, 160, 120));
                var body = (DeformableDangoVisual)visual.BodyVisual;
                var transform = (ScaleTransform)body.RenderTransform;
                var pivot = new Point(80, frame.BottomY);
                Assert.AreEqual(pivot, transform.Transform(pivot), "No movement of ground anchor.");
                foreach (var point in new[] { new Point(0, -16), new Point(160, -16), new Point(0, 120), new Point(160, 120) })
                    Assert.IsTrue((transform.Transform(point) - point).Length <= 2, "Bound includes hat, eyes and body.");
                Assert.IsNotNull(body.FindName("GaitBodyLayer"));
                Assert.IsNotNull(body.FindName("GaitAttachmentLayer"));
                var geometry = DangoStreamGeometryFactory.TryCreate(frame.IsFallback
                    ? NetworkPetPresentation.FallbackContour : frame.Presentation.Contour, 160, 120).Geometry!;
                Assert.AreEqual(frame.BottomY, geometry.Bounds.Bottom, .01);
                Assert.AreEqual(380 - 8 - .12 * (380 - 144), frame.Bounds.Y + frame.BottomY, 1e-10);
                Assert.AreSame(frame, visual.CurrentFrame);
            }
            Assert.AreEqual(61, visual.RenderedFrameCount);
        }
    }

    [TestMethod]
    public void ConfirmedMotionAloneDrivesPositionAndFiniteEndNotNativeVelocity()
    {
        var pet = Pet() with { Motion = new("motion-a", new(.3, .12), new(.396, .12), 100, 900, "right", 1) };
        using var presentation = new NetworkPetPresentation("pet-a", "missing-network-native.dll");
        var middle = presentation.Sample(pet, 500, Viewport, Step);
        Assert.AreEqual(.348, middle.AnchorPosition.X, 1e-12);
        var end = presentation.Sample(pet, 900, Viewport, Step);
        Assert.AreEqual(.396, end.AnchorPosition.X, 1e-12);
        Assert.AreEqual(1, end.ScaleX); Assert.AreEqual(1, end.ScaleY);
        var later = presentation.Sample(pet, 1900, Viewport, Step);
        Assert.AreEqual(end.AnchorPosition, later.AnchorPosition);
        Assert.AreEqual(.3, pet.AnchorPosition.X, "Immutable Replica input must not be mutated.");
    }

    [TestMethod]
    public void InvalidNativeContourDegradesOnlyPresentationAndDoesNotLoseActionTimeline()
    {
        var body = ActualBody() with { Contour = new float[32] };
        using var presentation = new NetworkPetPresentation("pet-a", () => new FixedWorld(body));
        var frame = presentation.Sample(Pet() with { Action = Wave }, 1000, Viewport, Step);
        Assert.IsTrue(frame.IsFallback);
        Assert.AreEqual(PhysicsPresentationFallbackReason.SimulationUnstable, frame.Presentation.FallbackReason);
        Assert.AreEqual(.75, frame.Wave.Progress, 1e-12);
        Assert.AreEqual(300, frame.Wave.RemainingMilliseconds);
        Assert.AreEqual(165, frame.Bounds.X, 1e-10);
    }

    private sealed class FixedWorld(DangoBodyState body, int failAfter = int.MaxValue) : INetworkPhysicsWorld
    {
        private int count;
        public DangoBodyState Step(TimeSpan _) => ++count > failAfter
            ? throw new DangoPhysicsException(DangoPhysicsFailureKind.NativeFailure, "Expected test failure.") : body;
        public void Dispose() { }
    }
}
