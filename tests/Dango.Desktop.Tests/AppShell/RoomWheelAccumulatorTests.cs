using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class RoomWheelAccumulatorTests
{
    [TestMethod]
    public void NativeOneHundredTwentyIsOneSignedStep()
    {
        var wheel = new RoomWheelAccumulator();
        Assert.AreEqual(1, wheel.Add(120, 0));
        Assert.AreEqual(-1, wheel.Add(-120, 1));
        Assert.AreEqual(2, wheel.Add(240, 2));
        Assert.AreEqual(0, wheel.Remainder);
    }

    [TestMethod]
    public void HighResolutionInputAccumulatesAndPreservesTheSignedRemainder()
    {
        var wheel = new RoomWheelAccumulator();
        Assert.AreEqual(0, wheel.Add(30, 0));
        Assert.AreEqual(0, wheel.Add(30, 10));
        Assert.AreEqual(1, wheel.Add(90, 20));
        Assert.AreEqual(30, wheel.Remainder);
        Assert.AreEqual(-1, wheel.Add(-150, 30));
        Assert.AreEqual(0, wheel.Remainder);
    }

    [TestMethod]
    public void DomOneHundredMustNotBeTreatedAsANativeNotch()
    {
        var wheel = new RoomWheelAccumulator();
        Assert.AreEqual(0, wheel.Add(100, 0));
        Assert.AreEqual(100, wheel.Remainder);
        Assert.AreEqual(1, wheel.Add(20, 1));
    }

    [TestMethod]
    public void ExactTimeoutDiscardsOldPartialInputBeforeTheNextEvent()
    {
        var wheel = new RoomWheelAccumulator();
        wheel.Add(60, 0);
        Assert.AreEqual(0, wheel.Add(60, 250));
        Assert.AreEqual(60, wheel.Remainder);
        Assert.AreEqual(1, wheel.Add(60, 499));
    }

    [TestMethod]
    public void ExplicitExpiryClearsAtTwoHundredFiftyMilliseconds()
    {
        var wheel = new RoomWheelAccumulator();
        wheel.Add(-60, 0);
        wheel.Expire(249);
        Assert.AreEqual(-60, wheel.Remainder);
        wheel.Expire(250);
        Assert.AreEqual(0, wheel.Remainder);
    }

    [TestMethod]
    public void ZeroDeltaCannotKeepAnOldGestureAlive()
    {
        var wheel = new RoomWheelAccumulator();
        wheel.Add(60, 0);
        Assert.AreEqual(0, wheel.Add(0, 200));
        Assert.AreEqual(0, wheel.Add(60, 250));
        Assert.AreEqual(60, wheel.Remainder);
    }

    [TestMethod]
    public void ClearDropsTheRemainderForFocusHostMenuAndDragTransitions()
    {
        var wheel = new RoomWheelAccumulator();
        foreach (var reason in new[] { "focus", "host", "menu", "drag" })
        {
            wheel.Add(60, 10);
            wheel.Clear();
            Assert.AreEqual(0, wheel.Remainder, reason);
            Assert.AreEqual(0, wheel.Add(60, 20), reason);
            wheel.Clear();
        }
    }

    [TestMethod]
    public void ClockReversalCannotCombineInputFromDifferentTimeBaselines()
    {
        var wheel = new RoomWheelAccumulator();
        wheel.Add(60, 100);
        Assert.AreEqual(0, wheel.Add(60, 99));
        Assert.AreEqual(60, wheel.Remainder);
    }

    [TestMethod]
    public void LargeSignedDeltasDoNotOverflowTheAccumulator()
    {
        var wheel = new RoomWheelAccumulator();
        Assert.AreEqual(int.MaxValue / 120, wheel.Add(int.MaxValue, 0));
        Assert.AreEqual(int.MaxValue % 120, wheel.Remainder);
        wheel.Clear();
        Assert.AreEqual(int.MinValue / 120, wheel.Add(int.MinValue, 1));
        Assert.AreEqual(int.MinValue % 120, wheel.Remainder);
    }

    [TestMethod]
    public void InvalidTimeDoesNotConsumeThePendingInput()
    {
        var wheel = new RoomWheelAccumulator();
        wheel.Add(60, 0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => wheel.Add(60, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => wheel.Expire(-1));
        Assert.AreEqual(60, wheel.Remainder);
    }
}
