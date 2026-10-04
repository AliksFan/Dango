using System.Text.Json;
using System.Reflection;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class PorchPhysicsSceneContractTests
{
    private static string Module => Environment.GetEnvironmentVariable("DANGO_PHYSICS_MODULE_PATH") ??
        Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
    private static string Baseline => Environment.GetEnvironmentVariable("DANGO_PHYSICS_BASELINE_PATH") ??
        throw new InvalidOperationException("Set DANGO_PHYSICS_BASELINE_PATH to the unchanged accepted module.");
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1d / 60);
    private static readonly LogicalRect Bounds = new(400, 300, 160, 120);

    [TestMethod]
    public void RehostRequiresAPublishedFrameInsteadOfGuessingCurrentCenter()
    {
        using var scene = new DesktopPhysicsScene(Module, Bounds);
        Assert.IsFalse(scene.RehostSelectedEntityAt(new(400, 280)));
        Assert.IsFalse(scene.TryGetHorizontalPlacement(new(400, 280), 24, 2136, out _));
        using var runtime = new StackedDangoSceneRuntime(Module);
        Assert.IsFalse(runtime.RehostSelectedEntityAt(new(400, 280)));
        Assert.IsFalse(runtime.TryGetHorizontalPlacement(new(400, 280), 24, 2136, out _));
        Assert.IsNull(runtime.LocalGroundY);
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.SetHorizontalBounds(0, 2160));
    }

    [TestMethod]
    public void RehostUsesActualEntityCenterClearsIntentAndPreservesPhysicsShapeAndId()
    {
        using var scene = new DesktopPhysicsScene(Module, Bounds);
        for (var i = 0; i < 300; i++) scene.Step(Step);
        scene.SetInput(new(false, true, false, false));
        for (var i = 0; i < 100; i++) scene.Step(Step);
        var before = scene.LastFrame!;
        var original = before.Physics.Bodies.Single();
        Assert.IsTrue(Math.Abs(original.CoreX) > .5);
        Assert.IsTrue(scene.RehostSelectedEntityAt(new(400, 280)));
        var after = scene.LastFrame!;
        Assert.AreEqual(original.EntityId, scene.SelectedEntity);
        Assert.AreEqual(400, after.Bodies.Single().Bounds.Center.X, 1e-8);
        Assert.AreEqual(280, after.Bodies.Single().Bounds.Center.Y, 1e-8);
        Assert.AreEqual(0, after.Physics.Bodies.Single().CoreX, 1e-6);
        Assert.AreEqual(original.CoreY, after.Physics.Bodies.Single().CoreY);
        CollectionAssert.AreEqual(original.Contour.ToArray(), after.Physics.Bodies.Single().Contour.ToArray());
        Assert.AreEqual(original.MaximumSpeed, after.Physics.Bodies.Single().MaximumSpeed);
        Assert.IsFalse(scene.LastFrame == before);
        Assert.IsTrue(scene.Step(Step).Physics.Succeeded);
    }

    [TestMethod]
    public void BoundsAndGroundUseTheSameHostToWorldProjection()
    {
        using var scene = new DesktopPhysicsScene(Module, Bounds);
        for (var i = 0; i < 300; i++) scene.Step(Step);
        scene.RehostSelectedEntityAt(new(400, 280));
        Assert.AreEqual(scene.ToHost(new(0, DangoPhysicsDefaults.Current.GroundHeight)).Y, scene.GroundY, 1e-10);
        scene.SetHorizontalBounds(0, 2160);
        scene.SetInput(new(false, true, false, false));
        for (var i = 0; i < 2600; i++)
        {
            var frame = scene.Step(Step);
            Assert.IsTrue(frame.Physics.Succeeded, $"frame={i}");
            Assert.IsTrue(frame.Bodies.Single().Bounds.Center.X <= 2164, $"right edge frame={i}");
            Assert.IsTrue(frame.Bodies.Single().Bounds.Center.Y < scene.GroundY + 4, $"fell below floor frame={i}");
        }
        Assert.IsTrue(scene.LastFrame!.Bodies.Single().Bounds.Center.X > 1700);
        scene.RehostSelectedEntityAt(new(480, 360));
        scene.ClearHorizontalBounds();
        for (var i = 0; i < 300; i++) Assert.IsTrue(scene.Step(Step).Physics.Succeeded);
        Assert.IsTrue(scene.LastFrame!.Bodies.Single().Bounds.Center.Y < scene.GroundY + 4);
    }

    [TestMethod]
    public void RuntimeHandoffDoesNotReapplyCoreOffsetOnTheNextTickOrRepeatedSwitch()
    {
        using var runtime = new StackedDangoSceneRuntime(Module);
        runtime.Start();
        var home = Primary(Bounds);
        for (var i = 0; i < 300; i++) runtime.Tick(home, Step);
        runtime.SetDirectionalControl(new(false, true, false, false));
        for (var i = 0; i < 100; i++) runtime.Tick(home, Step);
        var id = runtime.SelectedEntity;
        foreach (var center in new[] { new LogicalPoint(400, 280), new LogicalPoint(480, 360), new LogicalPoint(400, 280) })
        {
            Assert.IsTrue(runtime.RehostSelectedEntityAt(center));
            Assert.AreEqual(center.X, runtime.CurrentSelectedCenter!.Value.X, 1e-8);
            Assert.AreEqual(center.Y, runtime.CurrentSelectedCenter.Value.Y, 1e-8);
            Assert.AreEqual(id, runtime.SelectedEntity);
            runtime.SetHorizontalBounds(0, 2160);
            var next = Primary(new(center.X - 80, center.Y - 60, 160, 120));
            var frame = runtime.Tick(next, Step);
            var bounds = DangoHostFrameBinding.Bounds(frame, 0);
            Assert.IsTrue(Math.Abs(bounds.Center.X - center.X) < 8, "The old native CoreX must not be added again.");
            Assert.IsNotNull(runtime.LocalGroundY);
            Assert.AreEqual(DirectionalControlIntent.None, typeof(StackedDangoSceneRuntime)
                .GetField("directionalIntent", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime));
        }
        runtime.ClearHorizontalBounds();
    }

    [TestMethod]
    public void OldModuleRehostReportsUnsupportedInsteadOfMovingOnlyThePicture()
    {
        using var scene = new DesktopPhysicsScene(Baseline, Bounds);
        scene.Step(Step);
        var before = JsonSerializer.Serialize(scene.LastFrame);
        Assert.AreEqual(DangoPhysicsFailureKind.MissingExport,
            Assert.ThrowsExactly<DangoPhysicsException>(() => scene.RehostSelectedEntityAt(new(400, 280))).Kind);
        Assert.AreEqual(before, JsonSerializer.Serialize(scene.LastFrame));
    }

    [TestMethod]
    public void BoundedViewportChangesNeverFitTheWorldOrShiftItsPhysicalWalls()
    {
        using var runtime = new StackedDangoSceneRuntime(Module);
        runtime.Start();
        var input = Primary(Bounds);
        for (var i = 0; i < 300; i++) runtime.Tick(input, Step);
        var center = new LogicalPoint(400, 280);
        runtime.RehostSelectedEntityAt(center);
        runtime.SetHorizontalBounds(0, 2160);
        var porch = Primary(new(center.X - 80, center.Y - 60, 160, 120));
        var floor = runtime.LocalGroundY!.Value;
        runtime.SetViewport(new(0, 0, 220, 180));
        runtime.Tick(porch, Step);
        Assert.AreEqual(center.X, runtime.CurrentSelectedCenter!.Value.X, 1,
            "A narrow screen is a camera viewport, not permission to fit/translate the 2160-wide world.");
        Assert.AreEqual(floor, runtime.LocalGroundY!.Value, 1e-9);
        runtime.SetDirectionalControl(new(false, true, false, false));
        for (var i = 0; i < 2600; i++) runtime.Tick(porch, Step);
        var edge = runtime.CurrentSelectedCenter!.Value;
        Assert.IsTrue(edge.X > 1700 && edge.X <= 2164);
        runtime.SetViewport(new(0, 0, 1280, 720));
        runtime.Tick(porch, Step);
        Assert.AreEqual(edge.X, runtime.CurrentSelectedCenter!.Value.X, 4);
        Assert.AreEqual(floor, runtime.LocalGroundY!.Value, 1e-9);
        runtime.SetDirectionalControl(new(true, false, false, false));
        for (var i = 0; i < 3000; i++) runtime.Tick(porch, Step);
        Assert.IsTrue(runtime.CurrentSelectedCenter!.Value.X >= -4 && runtime.CurrentSelectedCenter.Value.X < 200);
        Assert.IsNull(runtime.LastError);
    }

    [TestMethod]
    public void ContactingWallPoseCanRecenterAndRestoreTheSameHostBoundsWithoutTeleport()
    {
        using var scene = new DesktopPhysicsScene(Module, Bounds);
        for (var i = 0; i < 300; i++) scene.Step(Step);
        scene.RehostSelectedEntityAt(new(560, 300));
        scene.SetHorizontalBounds(24, 2136);
        scene.SetInput(new(false, true, false, false));
        for (var i = 0; i < 2000; i++) Assert.IsTrue(scene.Step(Step).Physics.Succeeded);
        var original = scene.LastFrame!.Bodies.Single().Bounds.Center;
        Assert.IsTrue(original.X > 2000 && original.X < 2136);
        var state = scene.LastFrame.Physics.Bodies.Single();
        var radius = DangoPhysicsDefaults.Current.NodeRadius;
        var maximumX = Enumerable.Range(0, 16).Max(n =>
        {
            var x = state.Contour[2 * n];
            var y = state.Contour[2 * n + 1];
            return original.X + (x * (1 - radius / Math.Sqrt(x * x + y * y)) + radius) * 80;
        });
        for (var i = 0; i < 3; i++)
        {
            scene.RehostSelectedEntityAt(original);
            try { scene.SetHorizontalBounds(24, 2136); }
            catch (DangoPhysicsException error)
            {
                Assert.Fail($"An already-contacting legal pose must restore unchanged walls; right penetration={(maximumX - 2136) / 80:R} world, center={original}, error={error.Message}");
            }
            Assert.AreEqual(original.X, scene.LastFrame!.Bodies.Single().Bounds.Center.X, 1e-8);
            scene.Step(Step);
        }
        Assert.ThrowsExactly<DangoPhysicsException>(() => scene.SetHorizontalBounds(24, 2134),
            "Shrinking the wall by 2 logical pixels is beyond Box2D contact slop and must still fail without teleport.");
        scene.SetInput(new(true, false, false, false));
        for (var i = 0; i < 2600; i++) Assert.IsTrue(scene.Step(Step).Physics.Succeeded);
        var left = scene.LastFrame!.Bodies.Single().Bounds.Center;
        Assert.IsTrue(left.X > 24 && left.X < 230);
        scene.RehostSelectedEntityAt(left);
        scene.SetHorizontalBounds(24, 2136);
        Assert.IsTrue(scene.Step(Step).Physics.Succeeded);
    }

    [TestMethod]
    public void ContactingWallPoseCanRestoreAfterNinetyHomeFramesUsingMinimalPhysicalPlacement()
    {
        using var scene = new DesktopPhysicsScene(Module, new(80, 240, 160, 120));
        for (var i = 0; i < 90; i++) scene.Step(Step);
        scene.RehostSelectedEntityAt(new(560, 300));
        scene.SetHorizontalBounds(24, 2136);
        for (var i = 0; i < 60; i++) scene.Step(Step);
        scene.SetInput(new(false, true, false, false));
        for (var i = 0; i < 1600; i++) scene.Step(Step);
        scene.CancelInput();
        for (var i = 0; i < 90; i++) scene.Step(Step);
        var id = scene.SelectedEntity;
        RestoreAfterHome(true);
        scene.SetInput(new(true, false, false, false));
        for (var i = 0; i < 2300; i++) Assert.IsTrue(scene.Step(Step).Physics.Succeeded);
        scene.CancelInput();
        for (var i = 0; i < 90; i++) scene.Step(Step);
        RestoreAfterHome(false);

        void RestoreAfterHome(bool right)
        {
            for (var roundTrip = 0; roundTrip < 3; roundTrip++)
            {
                var original = scene.LastFrame!.Bodies.Single().Bounds.Center;
                Assert.IsTrue(right ? original.X > 2000 && original.X < 2136 : original.X > 24 && original.X < 230);
                scene.RehostSelectedEntityAt(new(160, 300));
                scene.ClearHorizontalBounds();
                for (var i = 0; i < 90; i++) Assert.IsTrue(scene.Step(Step).Physics.Succeeded);
                var homeFrame = scene.LastFrame!;
                var shape = homeFrame.Physics.Bodies.Single();
                var requested = new LogicalPoint(original.X, 300);
                Assert.IsTrue(scene.TryGetHorizontalPlacement(requested, 24, 2136, out var safe));
                Assert.AreSame(homeFrame, scene.LastFrame, "Query must not publish or advance a frame.");
                Assert.IsTrue(Math.Abs(safe.X - requested.X) < 12, "Keep the original UI return-position gate.");
                if (roundTrip == 0)
                {
                    Assert.IsTrue(right ? safe.X < requested.X : safe.X > requested.X,
                        "An expanded shape must move inward once; returning the stored core unchanged reproduces native -410.");
                    var extents = NodeHorizontalExtent(shape);
                    var expected = right ? 2136 - extents.Right * 80 : 24 - extents.Left * 80;
                    Assert.AreEqual(expected, safe.X, 1e-8, "Placement must be the minimal physical correction.");
                }
                Assert.IsTrue(scene.RehostSelectedEntityAt(safe));
                scene.SetHorizontalBounds(24, 2136);
                var restored = scene.LastFrame!.Physics.Bodies.Single();
                Assert.AreEqual(id, restored.EntityId);
                CollectionAssert.AreEqual(shape.Contour.ToArray(), restored.Contour.ToArray());
                Assert.AreEqual(shape.MaximumSpeed, restored.MaximumSpeed);
                Assert.AreEqual(shape.CoreY, restored.CoreY);
                Assert.AreEqual(safe, scene.LastFrame.Bodies.Single().Bounds.Center);
                for (var i = 0; i < 90; i++) Assert.IsTrue(scene.Step(Step).Physics.Succeeded);
                Assert.AreEqual(original.X, scene.LastFrame.Bodies.Single().Bounds.Center.X, 12);
            }
        }
    }

    [TestMethod]
    public void HorizontalPlacementQueryIsPureIncludesAllEntitiesAndRejectsImpossibleSpans()
    {
        using var scene = new DesktopPhysicsScene(Module, Bounds);
        using var reference = new DesktopPhysicsScene(Module, Bounds);
        scene.Add(4, 0);
        reference.Add(4, 0);
        for (var i = 0; i < 300; i++)
        {
            scene.Step(Step);
            reference.Step(Step);
        }
        var original = scene.LastFrame!;
        var serialized = JsonSerializer.Serialize(original);
        var requested = new LogicalPoint(2050, 300);
        Assert.IsTrue(scene.TryGetHorizontalPlacement(requested, 24, 2136, out var safe));
        Assert.IsTrue(safe.X < 1800, "The second physical entity, not just the selected body, must fit.");
        Assert.AreEqual(requested.Y, safe.Y);
        var inside = new LogicalPoint(560, 300);
        Assert.IsTrue(scene.TryGetHorizontalPlacement(inside, 24, 2136, out safe));
        Assert.AreEqual(inside, safe, "An already-contained placement must stay exact.");
        Assert.IsFalse(scene.TryGetHorizontalPlacement(inside, 0, 100, out _));
        Assert.IsFalse(scene.TryGetHorizontalPlacement(inside, 0, 300, out _),
            "Each body fits individually, but the complete relative arrangement does not.");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => scene.TryGetHorizontalPlacement(inside, 100, 0, out _));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => scene.TryGetHorizontalPlacement(inside, double.NaN, 2136, out _));
        Assert.AreSame(original, scene.LastFrame);
        Assert.AreEqual(serialized, JsonSerializer.Serialize(scene.LastFrame), "No ID, shape, velocity proxy or frame may change.");
        Assert.AreEqual(JsonSerializer.Serialize(reference.Step(Step)), JsonSerializer.Serialize(scene.Step(Step)),
            "The next native frame must remain byte-identical, catching hidden steps or velocity mutation.");
    }

    [TestMethod]
    public void RuntimeHorizontalPlacementQueryPreservesPhysicalAndPresentationSnapshots()
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct(Module);
        Assert.IsTrue(runtime.TryInitialize());
        runtime.Start();
        runtime.SetViewport(new(0, 0, 480, 360));
        var primary = Primary(new(80, 240, 160, 120));
        for (var i = 0; i < 90; i++) runtime.Tick(primary, Step);
        var physical = runtime.LastPhysicsFrame;
        var presentation = runtime.LastFrame;
        var center = runtime.CurrentSelectedCenter;
        Assert.IsTrue(runtime.TryGetHorizontalPlacement(new(560, 300), 24, 2136, out var safe));
        Assert.AreEqual(new LogicalPoint(560, 300), safe);
        Assert.AreSame(physical, runtime.LastPhysicsFrame);
        Assert.AreSame(presentation, runtime.LastFrame);
        Assert.AreEqual(center, runtime.CurrentSelectedCenter);
    }

    private static (double Left, double Right) NodeHorizontalExtent(DangoBodyState state)
    {
        var radius = DangoPhysicsDefaults.Current.NodeRadius;
        var xCoordinates = Enumerable.Range(0, state.PointCount).Select(n =>
        {
            var x = (double)state.Contour[2 * n];
            var y = (double)state.Contour[2 * n + 1];
            return x * (1 - radius / Math.Sqrt(x * x + y * y));
        }).ToArray();
        return (xCoordinates.Min() - radius, xCoordinates.Max() + radius);
    }

    private static PhysicsPresentationInput Primary(LogicalRect bounds) => new("fictional-porch", bounds,
        DangoActionState.Idle, 1000, 17, "fictional-ground", new(0, -1), 0, 1, false, true, []);
}
