using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class DampedSpringTests
{
    [TestMethod]
    public void CriticalAndUnderdampedMatchIndependentSolutions()
    {
        var critical = DampedSpring.Advance(new(1, 0), 0, 1 / (2 * Math.PI), 1, 0.1);
        Assert.AreEqual(1.1 * Math.Exp(-0.1), critical.Position, 1e-12);
        Assert.AreEqual(-0.1 * Math.Exp(-0.1), critical.Velocity, 1e-12);
        // Unit-frequency oscillator: x = exp(-t/2)*(cos(sqrt(3)t/2)+sin(sqrt(3)t/2)/sqrt(3)).
        var under = DampedSpring.Advance(new(1, 0), 0, 1 / (2 * Math.PI), 0.5, 0.1);
        var phase = Math.Sqrt(3) * 0.05;
        Assert.AreEqual(Math.Exp(-0.05) * (Math.Cos(phase) + Math.Sin(phase) / Math.Sqrt(3)), under.Position, 1e-12);
        Assert.AreEqual(-2 / Math.Sqrt(3) * Math.Exp(-0.05) * Math.Sin(phase), under.Velocity, 1e-12);
    }

    [TestMethod]
    [DataRow(0.00001)]
    [DataRow(0.75)]
    [DataRow(0.999999999999)]
    [DataRow(1.0)]
    public void ConstantTargetHasSemigroupAndMirrorSymmetry(double damping)
    {
        var initial = new SpringState(0.07, -0.13);
        var once = DampedSpring.Advance(initial, -0.03, 5, damping, 0.1);
        var split = initial;
        for (var i = 0; i < 24; i++) split = DampedSpring.Advance(split, -0.03, 5, damping, 1.0 / 240);
        Assert.AreEqual(once.Position, split.Position, 1e-10);
        Assert.AreEqual(once.Velocity, split.Velocity, 1e-10);
        var mirror = DampedSpring.Advance(new(-initial.Position, -initial.Velocity), 0.03, 5, damping, 0.1);
        Assert.AreEqual(-once.Position, mirror.Position, 1e-12);
        Assert.AreEqual(-once.Velocity, mirror.Velocity, 1e-12);
    }

    [TestMethod]
    public void BoundsRemoveOutwardSpeedAndContinueAfterFirstImpact()
    {
        var result = DampedSpring.AdvanceBounded(new(0.079, 2), 0, 3.5, 0.8, 0.1, -0.08, 0.08);
        Assert.IsTrue(result.Position is > 0 and < 0.075, "The remaining time after impact must return inward, not clamp at the end.");
        Assert.IsTrue(result.Velocity < 0);
        var edge = DampedSpring.AdvanceBounded(new(0.08, 2), 0.08, 3.5, 0.8, 0.1, -0.08, 0.08);
        Assert.AreEqual(new SpringState(0.08, 0), edge);
        var collapsed = DampedSpring.AdvanceBounded(new(0.02, -2), 0, 3.5, 0.8, 0.1, 0, 0);
        Assert.AreEqual(new SpringState(0, 0), collapsed);
    }

    [TestMethod]
    public void InteriorExcursionIsDetectedEvenWhenEndpointIsInside()
    {
        var initial = new SpringState(0.0799999, 0.02);
        const double duration = 1.0 / 240;
        var free = DampedSpring.Advance(initial, -0.08, 10, 1, duration);
        Assert.IsTrue(free.Position < 0.08);
        var bounded = DampedSpring.AdvanceBounded(initial, -0.08, 10, 1, duration, -0.08, 0.08);
        // Independent small-step RK4 plus projection converges to the first-impact solution.
        var reference = initial;
        var dt = duration / 100000;
        double Acceleration(double x, double v) => -2 * 20 * Math.PI * v - Math.Pow(20 * Math.PI, 2) * (x + 0.08);
        for (var i = 0; i < 100000; i++)
        {
            var x = reference.Position; var v = reference.Velocity;
            var a = Acceleration(x, v);
            var v2 = v + dt * a / 2; var a2 = Acceleration(x + dt * v / 2, v2);
            var v3 = v + dt * a2 / 2; var a3 = Acceleration(x + dt * v2 / 2, v3);
            var v4 = v + dt * a3; var a4 = Acceleration(x + dt * v3, v4);
            x += dt * (v + 2 * v2 + 2 * v3 + v4) / 6;
            v += dt * (a + 2 * a2 + 2 * a3 + a4) / 6;
            if (x >= 0.08) { x = 0.08; v = Math.Min(0, v); }
            reference = new(x, v);
        }
        Assert.AreEqual(reference.Position, bounded.Position, 1e-7);
        Assert.AreEqual(reference.Velocity, bounded.Velocity, 1e-5);
        Assert.IsTrue(Math.Abs(free.Position - bounded.Position) > 1e-6, "Endpoint-only clamping must fail.");
    }

    [TestMethod]
    public void RepeatedImpulseIsBoundedAndLongRestDecays()
    {
        var state = new SpringState(0, 0);
        for (var i = 0; i < 1000; i++)
        {
            state = DampedSpring.Impulse(state, i % 2 == 0 ? 0.3 : -0.3, 0.3);
            Assert.IsTrue(Math.Abs(state.Velocity) <= 0.3);
            state = DampedSpring.AdvanceBounded(state, 0, 3.5, 0.8, 1.0 / 240, -0.08, 0.08);
            Assert.IsTrue(Math.Abs(state.Position) <= 0.08);
        }
        Assert.IsTrue(Math.Abs(state.Position) > 1e-6, "A no-op implementation must fail.");
        for (var i = 0; i < 10000; i++) state = DampedSpring.AdvanceBounded(state, 0, 3.5, 0.8, 0.1, -0.08, 0.08);
        Assert.IsTrue(Math.Abs(state.Position) < 1e-10 && Math.Abs(state.Velocity) < 1e-10);
    }

    [TestMethod]
    public void InvalidParametersAreRejectedAndZeroQueryPreservesState()
    {
        var state = new SpringState(0.02, 0.03);
        Assert.AreEqual(state, DampedSpring.Advance(state, 0, 5, 1, 0));
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, -0.1, 0.101 })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DampedSpring.Advance(state, 0, 5, 1, bad));
        foreach (var bad in new[] { 0, -1, double.NaN, 1.01 })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DampedSpring.Advance(state, 0, 5, bad, 0.01));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DampedSpring.Advance(state, 0, 11, 1, 0.01));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DampedSpring.Advance(new(double.NaN, 0), 0, 5, 1, 0.01));
    }
}
