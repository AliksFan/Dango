using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class DesktopPhysicsSceneTests
{
    private static string Module => Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
    private static readonly LogicalRect Bounds = new(400, 500, 160, 120);
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1d / 60);

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void DesktopMappingMatchesRealModuleFrameForFrame(int count)
    {
        using var scene = new DesktopPhysicsScene(Module, Bounds);
        using var reference = DangoPhysicsWorld.Create(Module, configuration: DangoBodyMaterial.Configure(DangoBodyControls.Default));
        var ids = new List<DangoEntityId> { reference.Add(0, 0) };
        for (var i = 1; i < count; i++)
        {
            scene.Add(i * 2.8, 0);
            ids.Add(reference.Add(i * 2.8, 0));
        }
        for (var frame = 0; frame < 900; frame++)
        {
            var left = frame is >= 180 and < 210;
            var right = frame is >= 220 and < 250;
            var jumpHeld = frame is >= 310 and < 340;
            var down = frame is >= 400 and < 430;
            scene.SetInput(new(left, right, jumpHeld, down));
            if (frame == 500) scene.BeginDrag(scene.SelectedEntity, scene.ToHost(new(0.3, 1.5)));
            if (frame == 520) scene.MoveDrag(scene.ToHost(new(0.4, 1.7)));
            if (frame == 540) scene.CancelInput();
            if (frame == 700)
            {
                scene.Reset();
                reference.Reset();
            }
            var expected = reference.Step(Step, ids.Select((id, i) => i == 0
                ? new DangoPhysicsInput(id, left ? -1 : right ? 1 : 0, down ? 1 : 0,
                    frame == 310, frame is >= 500 and < 540,
                    frame < 520 ? 0.3 : 0.4, frame < 520 ? 1.5 : 1.7)
                : new DangoPhysicsInput(id)).ToArray());
            var actual = scene.Step(Step);
            Assert.AreEqual(0, actual.Physics.ErrorCode, $"count={count}, frame={frame}");
            AssertPhysicsEqual(expected, actual.Physics);
            foreach (var body in actual.Bodies)
            {
                var state = actual.Physics.Bodies.Single(value => value.EntityId == body.EntityId);
                Assert.AreEqual(Bounds.Center.X + state.CoreX * 72d, body.Bounds.Center.X, 1e-10);
                Assert.AreEqual(Bounds.Center.Y - state.CoreY * 72d, body.Bounds.Center.Y, 1e-10);
                Assert.AreEqual(16, body.Presentation.Contour.Count);
            }
        }
    }

    [TestMethod]
    public void PopulationUsesStableIdsAndRejectsStaleTargets()
    {
        using var scene = new DesktopPhysicsScene(Module, Bounds);
        var first = scene.SelectedEntity;
        var second = scene.Add(2.8, 0);
        var third = scene.Add(5.6, 0);
        Assert.AreEqual(DangoPhysicsFailureKind.CapacityExceeded,
            Assert.ThrowsExactly<DangoPhysicsException>(() => scene.Add(8.4, 0)).Kind);
        scene.Select(second);
        scene.SetInput(new(false, true, true, false));
        Assert.IsTrue(scene.Remove(second));
        Assert.AreEqual(first, scene.SelectedEntity);
        Assert.AreEqual(DangoPhysicsFailureKind.InvalidEntity,
            Assert.ThrowsExactly<DangoPhysicsException>(() => scene.Select(second)).Kind);
        var added = scene.Add(2.8, 0);
        Assert.IsTrue(added.Value > third.Value);
        scene.Reset();
        CollectionAssert.AreEquivalent(new[] { first, third, added }, scene.EntityIds.ToArray());
        scene.Remove(third);
        scene.Remove(added);
        Assert.IsFalse(scene.Remove(first));
    }

    [TestMethod]
    public void RemoveDoesNotResetSurvivorsAndPauseDoesNotAdvance()
    {
        using var scene = new DesktopPhysicsScene(Module, Bounds);
        using var reference = DangoPhysicsWorld.Create(Module, configuration: DangoBodyMaterial.Configure(DangoBodyControls.Default));
        var first = reference.Add(0, 0);
        var extra = scene.Add(2.8, 0);
        var referenceExtra = reference.Add(2.8, 0);
        for (var i = 0; i < 90; i++)
        {
            scene.Step(Step);
            reference.Step(Step, [new(first), new(referenceExtra)]);
        }
        scene.Select(extra);
        scene.BeginDrag(extra, new(700, 450));
        scene.Remove(extra);
        reference.Remove(referenceExtra);
        scene.SetPaused(true);
        Assert.ThrowsExactly<InvalidOperationException>(() => scene.Step(Step));
        Assert.ThrowsExactly<InvalidOperationException>(() => scene.SetInput(new(false, true, false, false)));
        scene.SetPaused(false);
        AssertPhysicsEqual(reference.Step(Step, [new(first)]), scene.Step(Step).Physics);
    }

    [TestMethod]
    public void RehostChangesOnlyDisplayAndCancelsInput()
    {
        using var scene = new DesktopPhysicsScene(Module, Bounds);
        using var reference = DangoPhysicsWorld.Create(Module, configuration: DangoBodyMaterial.Configure(DangoBodyControls.Default));
        var first = reference.Add(0, 0);
        scene.Step(Step);
        reference.Step(Step, [new(first)]);
        scene.BeginDrag(scene.SelectedEntity, new(420, 450));
        scene.Rehost(new(180, 250));
        var point = new LogicalPoint(-0.6, 1.2);
        var roundTrip = scene.ToWorld(scene.ToHost(point));
        Assert.AreEqual(point.X, roundTrip.X, 1e-12);
        Assert.AreEqual(point.Y, roundTrip.Y, 1e-12);
        AssertPhysicsEqual(reference.Step(Step, [new(first)]), scene.Step(Step).Physics);
        Assert.ThrowsExactly<InvalidOperationException>(() => scene.MoveDrag(new(200, 300)));
    }

    [TestMethod]
    public void LoadingFailureDoesNotFallBackAndDisposedSceneRejectsAccess()
    {
        Assert.AreEqual(DangoPhysicsFailureKind.MissingLibrary,
            Assert.ThrowsExactly<DangoPhysicsException>(() => new DesktopPhysicsScene(
                Path.Combine(TestRepository.Root, "artifacts", "nonexistent-module-for-test.dll"), Bounds)).Kind);
        var scene = new DesktopPhysicsScene(Module, Bounds);
        scene.Dispose();
        scene.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => scene.Step(Step));
        Assert.ThrowsExactly<ObjectDisposedException>(() => scene.Add(0, 0));
    }

    [TestMethod]
    public void WrongThreadAndInvalidInputDoNotAdvanceWorld()
    {
        using var scene = new DesktopPhysicsScene(Module, Bounds);
        Exception? observed = null;
        var thread = new Thread(() =>
        {
            try { scene.Step(Step); }
            catch (Exception exception) { observed = exception; }
        });
        thread.Start();
        thread.Join();
        Assert.IsInstanceOfType<InvalidOperationException>(observed);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => scene.Step(TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => scene.ToWorld(new(double.NaN, 0)));
        Assert.IsNull(scene.LastFrame);
    }

    private static void AssertPhysicsEqual(DangoPhysicsFrame expected, DangoPhysicsFrame actual)
    {
        Assert.AreEqual(expected.ErrorCode, actual.ErrorCode);
        Assert.AreEqual(expected.ContactStepCount, actual.ContactStepCount);
        Assert.AreEqual(expected.Bodies.Count, actual.Bodies.Count);
        foreach (var a in expected.Bodies)
        {
            var b = actual.Bodies.Single(body => body.EntityId == a.EntityId);
            Assert.AreEqual(a with { Contour = b.Contour }, b);
            CollectionAssert.AreEqual(a.Contour.ToArray(), b.Contour.ToArray());
        }
    }
}
