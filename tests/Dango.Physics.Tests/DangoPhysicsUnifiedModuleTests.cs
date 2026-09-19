using System.Text.Json;
using Dango.Physics.Configuration;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class DangoPhysicsUnifiedModuleTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);

    [TestMethod]
    public void DefaultWorldSupportsThreeAndRejectsFourth()
    {
        using var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        Assert.AreEqual(3, world.Capacity);
        Assert.AreEqual(DangoPhysicsDefaults.MaximumBodyCount, world.Capacity);
        var ids = Enumerable.Range(0, 3).Select(i => world.Add((i - 1) * 2.8, 0)).ToArray();
        var error = Assert.ThrowsExactly<DangoPhysicsException>(() => world.Add(0, 4));
        Assert.AreEqual(DangoPhysicsFailureKind.CapacityExceeded, error.Kind);
        Assert.AreEqual(-411, error.NativeErrorCode);
        var frame = world.Step(Dt, ids.Select(id => new DangoPhysicsInput(id)).ToArray());
        Assert.IsTrue(frame.Succeeded); Assert.AreEqual(3, frame.Bodies.Count);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void ExplicitCapacityIsEnforced(int capacity)
    {
        using var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified, capacity);
        for (int i = 0; i < capacity; i++) world.Add(i * 2.8, 0);
        Assert.AreEqual(capacity, world.Count);
        Assert.AreEqual(DangoPhysicsFailureKind.CapacityExceeded,
            Assert.ThrowsExactly<DangoPhysicsException>(() => world.Add(0, 4)).Kind);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified, 4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified, 0));
    }

    [TestMethod]
    [DataRow(1, 0)]
    [DataRow(2, 0)]
    [DataRow(2, 1)]
    [DataRow(3, 0)]
    [DataRow(3, 1)]
    [DataRow(3, 2)]
    public void MatchesAcceptedThreeLibraryExactly(int count, int layout)
    {
        using var baseline = DangoPhysicsWorld.Create(PhysicsTestLibraries.AcceptedThree, 3);
        using var unified = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        var oldIds = AddBodies(baseline, count, layout); var newIds = AddBodies(unified, count, layout);
        for (int frameIndex = 0; frameIndex < 900; frameIndex++)
        {
            if (frameIndex == 450)
            {
                baseline.Remove(oldIds[^1]); unified.Remove(newIds[^1]);
                oldIds.RemoveAt(oldIds.Count - 1); newIds.RemoveAt(newIds.Count - 1);
            }
            if (frameIndex == 480) { oldIds.Add(baseline.Add(3, 1)); newIds.Add(unified.Add(3, 1)); }
            if (frameIndex == 600) { baseline.Reset(); unified.Reset(); }
            var inputs = oldIds.Select((id, index) => new DangoPhysicsInput(id,
                Horizontal: layout == 0 && index == 0 && frameIndex is >= 120 and < 200 ? 1 : 0,
                Downward: index == 0 && frameIndex is >= 210 and < 230 ? .5 : 0,
                JumpPressed: index == oldIds.Count - 1 && frameIndex == 300,
                DragActive: index == oldIds.Count - 1 && frameIndex is >= 360 and < 420,
                DragTargetX: .5, DragTargetY: 2)).ToArray();
            var expected = baseline.Step(Dt, inputs);
            var actual = unified.Step(Dt, inputs.Select((input, index) => input with { EntityId = newIds[index] }).ToArray());
            Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual),
                $"count={count}, layout={layout}, frame={frameIndex}: zero-tolerance state/ID/contact/error mismatch.");
            Assert.AreEqual(0, actual.ErrorCode, $"Unexpected fault at {frameIndex}.");
        }
    }

    private static List<DangoEntityId> AddBodies(DangoPhysicsWorld world, int count, int layout) =>
        Enumerable.Range(0, count).Select(i => world.Add(
            layout == 1 ? 0 : layout == 2 ? (i == 2 ? 0 : i == 0 ? -1 : 1) : (i - 1) * 2.8,
            layout == 1 ? i * 1.65 : layout == 2 && i == 2 ? 1.65 : 0)).ToList();
}
