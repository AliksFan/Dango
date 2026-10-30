using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Tests.Rendering;

[TestClass, DoNotParallelize]
public sealed class BeltRoomSleepTests
{
    private static string Module => Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE")
        ?? throw new InvalidOperationException("An explicit independent room module is required.");

    [TestMethod, TestCategory("NativeIntegration")]
    public void UncalibratedRoomAlwaysRunsAndCalibratedSleepRetainsExactStateUntilInput()
    {
        using var room = new BeltRoomSceneAdapter(Module, new(400, 500, 160, 120), DangoBodyControls.Default);
        for (var i = 0; i < 240; i++) room.Step(default);
        Assert.AreEqual(0, room.LastErrorCode);
        Assert.AreEqual(240L, room.NativeStepCalls);
        Assert.IsFalse(room.IsSleeping);
        room.ConfigureSleepPresentationScale(1.4);
        for (var i = 0; i < 3600 && !room.IsSleeping && room.LastErrorCode == 0; i++) room.Step(default);
        Assert.AreEqual(0, room.LastErrorCode);
        Assert.IsTrue(room.IsSleeping, $"No sleep at native tick {room.Frame.Tick}.");
        Console.WriteLine($"Room natural sleep tick={room.Frame.Tick}, native calls={room.NativeStepCalls}");
        var safe = room.Frame;
        var calls = room.NativeStepCalls;
        for (var i = 0; i < 600; i++) room.Step(default);
        Assert.AreSame(safe, room.Frame);
        Assert.AreEqual(calls, room.NativeStepCalls);
        room.Step(new(InputActive: true)); // Opposing keys still occupy input even when net axis is zero.
        Assert.IsFalse(room.IsSleeping);
        Assert.AreEqual(calls + 1, room.NativeStepCalls);
        Assert.AreEqual(safe.Tick + 1, room.Frame.Tick);
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void ScaleChangesAndLifecycleWakeWithoutFabricatingTicksOrCatchup()
    {
        using var room = new BeltRoomSceneAdapter(Module, new(400, 500, 160, 120), DangoBodyControls.Default);
        room.ConfigureSleepPresentationScale(1);
        for (var i = 0; i < 3600 && !room.IsSleeping && room.LastErrorCode == 0; i++) room.Step(default);
        Assert.IsTrue(room.IsSleeping);
        var before = room.Frame;
        room.ConfigureSleepPresentationScale(BeltRoomSceneAdapter.MaximumSleepMetresToDip / 72 + .01);
        Assert.IsFalse(room.IsSleeping);
        Assert.AreEqual(before.Tick, room.Frame.Tick);
        for (var i = 0; i < 240; i++) room.Step(default);
        Assert.IsFalse(room.IsSleeping);
        Assert.AreEqual(before.Tick + 240, room.Frame.Tick);
        room.ConfigureSleepPresentationScale(1);
        room.Rehost(new(300, 400));
        Assert.IsFalse(room.IsSleeping);
        var calls = room.NativeStepCalls;
        room.Step(default);
        Assert.AreEqual(calls + 1, room.NativeStepCalls);
        Assert.AreEqual(0, room.LastErrorCode);
    }
}
