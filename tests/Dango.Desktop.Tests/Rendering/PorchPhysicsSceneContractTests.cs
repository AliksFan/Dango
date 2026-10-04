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
        using var runtime = new StackedDangoSceneRuntime(Module);
        Assert.IsFalse(runtime.RehostSelectedEntityAt(new(400, 280)));
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

    private static PhysicsPresentationInput Primary(LogicalRect bounds) => new("fictional-porch", bounds,
        DangoActionState.Idle, 1000, 17, "fictional-ground", new(0, -1), 0, 1, false, true, []);
}
