using System.Text.Json;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class DangoPhysicsThreePreviewTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);
    public TestContext TestContext { get; set; } = null!;
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dango.slnx"))) directory = directory.Parent;
            return directory!.FullName;
        }
    }
    private static string Preview => PhysicsTestLibraries.Unified;

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void ThreeEntitiesRemoveAndReaddAnySlot(int removed)
    {
        using var world = DangoPhysicsWorld.Create(Preview, capacity: 3);
        var ids = Enumerable.Range(0, 3).Select(i => world.Add((i - 1) * 2.8, 0)).ToArray();
        var overflow = Assert.ThrowsExactly<DangoPhysicsException>(() => world.Add(0, 4));
        Assert.AreEqual(DangoPhysicsFailureKind.CapacityExceeded, overflow.Kind);
        var f = world.Step(Dt, ids.Select(id => new DangoPhysicsInput(id)).ToArray()); Check(f, 3);
        world.Remove(ids[removed]);
        var survivors = ids.Where((_, i) => i != removed).ToArray();
        f = world.Step(Dt, survivors.Select(id => new DangoPhysicsInput(id)).ToArray()); Check(f, 2);
        CollectionAssert.AreEquivalent(survivors, f.Bodies.Select(b => b.EntityId).ToArray());
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.Remove(ids[removed]));
        var replacement = world.Add((removed - 1) * 2.8, 0); Assert.IsFalse(ids.Contains(replacement)); ids[removed] = replacement;
        world.Reset();
        for (int i = 0; i < 120; i++) Check(world.Step(Dt, ids.Select(id => new DangoPhysicsInput(id)).ToArray()), 3);
        foreach (var id in ids) world.Remove(id);
        Check(world.Step(Dt, []), 0); world.Reset(); world.Dispose(); world.Dispose();
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(0, 2)]
    [DataRow(1, 2)]
    public void EveryPairHasRealContact(int left, int right)
    {
        using var world = DangoPhysicsWorld.Create(Preview, capacity: 3);
        var ids = Enumerable.Range(0, 3).Select(i => world.Add(i == left ? 0 : i == right ? 2.8 : -4, 0)).ToArray();
        DangoPhysicsFrame? frame = null;
        for (int i = 0; i < 600; i++)
        {
            frame = world.Step(Dt, ids.Select((id, index) => new DangoPhysicsInput(id, Horizontal: index == left && i >= 120 ? 1 : 0)).ToArray());
            Check(frame, 3);
        }
        Assert.IsTrue(frame!.ContactStepCount > 100, "Each slot pair must really collide.");
        var l = frame.Bodies.Single(b => b.EntityId == ids[left]); var r = frame.Bodies.Single(b => b.EntityId == ids[right]);
        Assert.IsTrue(l.CoreX < r.CoreX && r.CoreX - l.CoreX > .5, "Active body must not pass through its partner.");
        TestContext.WriteLine($"Pair={left},{right}; contacts={frame.ContactStepCount}");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void ThreeSceneSmoke(int layout)
    {
        using var world = DangoPhysicsWorld.Create(Preview, capacity: 3);
        var ids = Enumerable.Range(0, 3).Select(i => world.Add(
            layout == 1 ? 0 : layout == 2 ? (i == 2 ? 0 : (i == 0 ? -1 : 1)) : (i - 1) * 2.8,
            layout == 1 ? i * 1.65 : layout == 2 && i == 2 ? 1.65 : 0)).ToArray();
        DangoPhysicsFrame? frame = null;
        for (int i = 0; i < 600; i++)
        {
            frame = world.Step(Dt, ids.Select((id, index) => new DangoPhysicsInput(id,
                JumpPressed: index == 2 && i == 300,
                DragActive: index == 2 && i is >= 360 and < 420, DragTargetX: .5, DragTargetY: 2)).ToArray());
            if (i % 120 == 0 || !frame.Succeeded) TestContext.WriteLine($"layout={layout}; frame={i}; error={frame.ErrorCode}; contacts={frame.ContactStepCount}");
            Check(frame, 3);
        }
        if (layout != 0) Assert.IsTrue(frame!.ContactStepCount > 0);
        world.Reset(); Check(world.Step(Dt, ids.Select(id => new DangoPhysicsInput(id)).ToArray()), 3);
    }

    [TestMethod]
    [DataRow(1, 0)]
    [DataRow(2, 0)]
    [DataRow(2, 1)]
    public void PreservesAcceptedOneAndTwoBodyTrajectory(int count, int layout)
    {
        using var baseline = DangoPhysicsWorld.Create(Path.Combine(Root, "artifacts/native-spike/physics-module-build/Release/dango_physics_module.dll"), capacity: 2);
        using var preview = DangoPhysicsWorld.Create(Preview, capacity: 2);
        var oldIds = Enumerable.Range(0, count).Select(i => baseline.Add(layout == 1 ? 0 : i * 2.8, layout == 1 ? i * 1.65 : 0)).ToArray();
        var newIds = Enumerable.Range(0, count).Select(i => preview.Add(layout == 1 ? 0 : i * 2.8, layout == 1 ? i * 1.65 : 0)).ToArray();
        for (int i = 0; i < 900; i++)
        {
            if (i == 600) { baseline.Reset(); preview.Reset(); }
            var inputs = oldIds.Select((id, index) => new DangoPhysicsInput(id,
                Horizontal: i is >= 120 and < 200 && index == 0 ? 1 : 0,
                JumpPressed: i == 240, DragActive: i is >= 300 and < 360,
                DragTargetX: index * 2.8, DragTargetY: 1.5)).ToArray();
            var a = baseline.Step(Dt, inputs);
            var b = preview.Step(Dt, inputs.Select((input, index) => input with { EntityId = newIds[index] }).ToArray());
            Check(a, count); Check(b, count);
            Assert.AreEqual(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b), $"Trajectory mismatch at frame {i}.");
        }
    }

    private static void Check(DangoPhysicsFrame f, int count)
    {
        Assert.AreEqual(0, f.ErrorCode); Assert.AreEqual(count, f.Bodies.Count);
        foreach (var body in f.Bodies)
        {
            Assert.AreEqual(16, body.PointCount);
            Assert.IsTrue(body.Contour.All(float.IsFinite) && float.IsFinite(body.CoreX) && float.IsFinite(body.CoreY));
            Assert.IsTrue(body.SignedArea > 0 && body.MaximumSpeed <= 12.00001);
        }
    }
}
