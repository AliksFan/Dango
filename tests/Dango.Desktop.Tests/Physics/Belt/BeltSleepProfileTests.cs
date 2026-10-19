using Dango.Desktop.Physics.Belt;
using Dango.Physics;

namespace Dango.Desktop.Tests.Physics.Belt;

[TestClass]
public sealed class BeltSleepProfileTests
{
    private static bool Sweep(BeltSleepPolicy policy, Action<int, double[], float[], float[]> update, DangoBeltInput input = default, bool healthy = true)
    {
        var asleep = false;
        for (var tick = 0; tick <= 120; tick++)
        {
            var state = new double[200]; state[0] = 1; state[1] = 200; state[79] = 10; state[82] = 1; state[125] = .45;
            var stats = new float[64]; stats[14] = 1; stats[25] = 3; var curve = new float[512];
            update(tick, state, curve, stats);
            asleep = policy.Observe(new(new float[68], curve, stats), state, input, healthy);
        }
        return asleep;
    }

    [TestMethod]
    public void DefaultIsVersionedVisibleContractAndStrictProfileRetainsOriginalNumbers()
    {
        var current = new BeltSleepPolicy(); var strict = new BeltSleepPolicy(BeltSleepProfile.StrictStateTrendV1);
        Assert.AreEqual("visible-deformation-v2", current.ContractId); Assert.AreEqual(.0005, current.MaximumCoordinateWindowDelta);
        Assert.AreEqual("strict-state-trend-v1", strict.ContractId); Assert.AreEqual(.0001, strict.MaximumCoordinateWindowDelta);
        Assert.IsTrue(Sweep(current, (i, q, _, _) => q[2] = .0003 * i / 120));
        Assert.IsFalse(Sweep(strict, (i, q, _, _) => q[2] = .0003 * i / 120));
    }

    [TestMethod]
    public void VisibleEnvelopeUsesDiagonalAndItsInclusiveBoundary()
    {
        Assert.IsTrue(BeltSleepPolicy.WithinVisibleEnvelope(.0003, .0004));
        Assert.IsFalse(BeltSleepPolicy.WithinVisibleEnvelope(.0004, .0004));
        Assert.IsTrue(BeltSleepPolicy.WithinVisibleEnvelope(.0005, 0));
        Assert.IsFalse(BeltSleepPolicy.WithinVisibleEnvelope(double.BitIncrement(.0005), 0));
        Assert.IsTrue(Sweep(new(), (i, _, curve, _) => curve[0] = float.BitDecrement(.0005f) * i / 120));
        Assert.IsFalse(Sweep(new(), (i, _, curve, _) => curve[0] = float.BitIncrement(.0005f) * i / 120));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EachAxisBelowBoundCannotHideDiagonalMotion(bool core)
    {
        Assert.IsFalse(Sweep(new(), (i, _, curve, stats) =>
        {
            if (core) stats[0] = stats[1] = .0004f * i / 120;
            else curve[510] = curve[511] = .0004f * i / 120;
        }));
    }

    [TestMethod]
    public void CameraRelativeCurveRejectsOpposingRawCurveAndCoreMotion()
    {
        Assert.IsFalse(Sweep(new(), (i, _, curve, stats) =>
        { curve[510] = .0003f * i / 120; stats[0] = -.0003f * i / 120; }));
    }

    [TestMethod]
    public void EveryGeneralizedCoordinateAndVelocityHasBothTickAndWindowGates()
    {
        for (var index = 2; index < 74; index++)
        {
            var coordinate = index;
            Assert.IsFalse(Sweep(new(), (i, state, _, _) => state[coordinate] = .0006 * i / 120), $"window index {index}");
            Assert.IsFalse(Sweep(new(), (i, state, _, _) => state[coordinate] = i % 2 == 0 ? 0 : (index < 38 ? .000011 : .000501)), $"tick index {index}");
        }
        Assert.IsTrue(Sweep(new(), (i, state, _, _) => state[2] = .0005 * i / 120));
        Assert.IsTrue(Sweep(new(), (i, state, _, _) => state[38] = .0005 * i / 120));
    }

    [TestMethod]
    [DataRow("airborne")]
    [DataRow("contact")]
    [DataRow("native-airborne")]
    [DataRow("drag")]
    [DataRow("ramp")]
    [DataRow("cooldown")]
    [DataRow("nonfinite")]
    public void UnsupportedOrNonhealthyStateNeverSleeps(string reason)
    {
        Assert.IsFalse(Sweep(new(), (_, state, _, stats) =>
        {
            if (reason == "airborne") stats[14] = 0;
            if (reason == "contact") stats[25] = 0;
            if (reason == "native-airborne") state[82] = 0;
            if (reason == "drag") state[83] = 1;
            if (reason == "ramp") state[78] = .1;
            if (reason == "cooldown") state[79] = .1;
            if (reason == "nonfinite") state[2] = double.NaN;
        }));
        Assert.IsFalse(Sweep(new(), (_, _, _, _) => { }, healthy: false));
    }

    [TestMethod]
    public void CompleteInputOccupancyIncludingOpposingKeysNeverSleeps()
    {
        DangoBeltInput[] inputs = [new(InputActive: true), new(Horizontal: 1), new(Stretch: 1), new(Downward: 1),
            new(JumpPressed: true), new(DragActive: true), new(CancelForces: true)];
        foreach (var input in inputs) Assert.IsFalse(Sweep(new(), (_, _, _, _) => { }, input));
    }
}
