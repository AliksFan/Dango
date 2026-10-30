using Dango.Desktop.Physics.Belt;
using Dango.Physics;
using Dango.Desktop.Physics.Tuning;

namespace Dango.Desktop.Tests.Physics.Belt;

[TestClass]
[DoNotParallelize]
public sealed class BalancedWsForceTests
{
    private static double[] Ledger(params (double Y, double Fy, int Part)[] rows)
    {
        var ledger = new double[1024];
        ledger[0] = 1; ledger[1] = 1024; ledger[4] = rows.Length; ledger[5] = rows.Length > 0 ? 2 : 0;
        ledger[6] = 6; ledger[7] = 64;
        for (var i = 0; i < rows.Length; i++)
        {
            var row = 8 + i * 6;
            ledger[row + 1] = rows[i].Y; ledger[row + 3] = rows[i].Fy;
            ledger[row + 4] = 2; ledger[row + 5] = rows[i].Part;
        }
        return ledger;
    }

    [TestMethod]
    [DataRow(1d)]
    [DataRow(-1d)]
    public void IndependentHalfBudgetsAllowUnequalBinCounts(double sign)
    {
        var ledger = Ledger((1, sign * .25, 0), (2, sign * .75, 1), (-1, -sign, 0));
        BeltNativeValidation.ValidateVerticalLedger(ledger, sign);
    }

    [TestMethod]
    public void SingleSidedOldLoadAndUnbalancedOrReversedPairsAreRejected()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => BeltNativeValidation.ValidateVerticalLedger(Ledger((1, 1, 0)), 1));
        Assert.ThrowsExactly<InvalidOperationException>(() => BeltNativeValidation.ValidateVerticalLedger(Ledger((1, 1, 0), (-1, -.8, 0)), 1));
        Assert.ThrowsExactly<InvalidOperationException>(() => BeltNativeValidation.ValidateVerticalLedger(Ledger((-1, 1, 0), (1, -1, 0)), 1));
        Assert.ThrowsExactly<InvalidOperationException>(() => BeltNativeValidation.ValidateVerticalLedger(Ledger((1, .5, 0), (1, .5, 0), (-1, -1, 0)), 1));
        var invalid = Ledger((1, 1, 0), (-1, -1, 0)); invalid[9] = double.NaN;
        Assert.ThrowsExactly<InvalidOperationException>(() => BeltNativeValidation.ValidateVerticalLedger(invalid, 1));
    }

    [TestMethod]
    public void CancellationAndJumpGateRequireNoVerticalRows()
    {
        BeltNativeValidation.ValidateVerticalLedger(Ledger(), 0);
        Assert.ThrowsExactly<InvalidOperationException>(() => BeltNativeValidation.ValidateVerticalLedger(Ledger((1, 1, 0), (-1, -1, 0)), 0));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ProductNativeLedgerAndFreeMomentumFollowBalancedContract(bool stretch)
    {
        var module = Environment.GetEnvironmentVariable("DANGO_BELT_MODULE");
        if (string.IsNullOrWhiteSpace(module) || !File.Exists(module))
            Assert.Inconclusive("Set DANGO_BELT_MODULE to the rebuilt balanced W/S product DLL.");
        var configuration = new DangoSelfPhysicsParameters
        {
            RadialElasticity = 1.82, PerimeterElasticity = 15.075, TopInwardForce = 0, GroundDeformationStrength = 0,
            Gravity = .5, DropHeight = 1.5, OverallDamping = 0
        };
        var calibration = DangoBeltCalibrationParameters.ForConfiguration(configuration) with
        { OverallDamping = 0, GroundedBrake = 0, GroundedHorizontalDamping = 0 };
        using var world = new DangoBeltWorld(module!, configuration, calibration: calibration);
        var initial = world.Read();
        var input = new DangoBeltInput(Stretch: stretch ? 1 : 0, Downward: stretch ? 0 : 1, InputActive: true);
        for (var tick = 1; tick <= 4; tick++)
        {
            world.Step(DangoBeltWorld.FixedDt, input);
            var frame = world.Read(); var ledger = world.ReadForceLedger();
            BeltNativeValidation.Validate(world, frame, tick, input, configuration.MaximumSpeed,
                world.ReadShapeGuard(), world.ReadSpeedLimit(), world.ReadRadialDrive(), world.ReadRearDrive(), ledger);
            Assert.IsFalse(frame.Grounded); Assert.AreEqual(0, frame.ActiveContacts);
            Assert.AreEqual(0d, frame.VerticalForce, 1e-9);
            var expectedMomentum = initial.Stats[44] - initial.TotalMass * (float)configuration.Gravity * (float)DangoBeltWorld.FixedDt * tick;
            Assert.AreEqual(expectedMomentum, frame.Stats[44], 2e-6, "W/S must not inject additional whole-body vertical momentum in free motion.");
        }
        world.Step(DangoBeltWorld.FixedDt, new(CancelForces: true));
        BeltNativeValidation.ValidateVerticalLedger(world.ReadForceLedger(), 0);
    }

    [TestMethod]
    public void OpposingVerticalKeysPreserveActualHorizontalNativeForces()
    {
        var module = Environment.GetEnvironmentVariable("DANGO_BELT_MODULE");
        if (string.IsNullOrWhiteSpace(module) || !File.Exists(module))
            Assert.Inconclusive("Set DANGO_BELT_MODULE to the rebuilt balanced W/S product DLL.");
        var configuration = new DangoSelfPhysicsParameters
        {
            RadialElasticity = 1.82, PerimeterElasticity = 15.075, TopInwardForce = 0, GroundDeformationStrength = 0,
            Gravity = .5, DropHeight = 1.5, OverallDamping = 0
        };
        var calibration = DangoBeltCalibrationParameters.ForConfiguration(configuration) with
        { OverallDamping = 0, GroundedBrake = 0, GroundedHorizontalDamping = 0 };
        using var world = new DangoBeltWorld(module!, configuration, calibration: calibration);
        var input = new DangoBeltInput(Horizontal: 1, Stretch: 1, Downward: 1, InputActive: true);
        for (var tick = 1; tick <= 4; tick++)
        {
            var px = world.Read().Stats[43];
            world.Step(DangoBeltWorld.FixedDt, input);
            var frame = world.Read(); var ledger = world.ReadForceLedger();
            BeltNativeValidation.Validate(world, frame, tick, input, configuration.MaximumSpeed,
                world.ReadShapeGuard(), world.ReadSpeedLimit(), world.ReadRadialDrive(), world.ReadRearDrive(), ledger);
            Assert.IsTrue(frame.HorizontalForce > .01, "D must keep a real horizontal force while W/S cancel.");
            Assert.IsTrue(frame.Stats[43] > px, "Whole-body horizontal momentum must actually increase.");
            Assert.AreEqual(0f, frame.Stats[17], "Opposing vertical keys clear the W envelope.");
            var kinds = Enumerable.Range(0, (int)ledger[4]).Select(i => ledger[8 + 6 * i + 4]).ToArray();
            Assert.AreEqual(8, kinds.Count(kind => kind == 5));
            Assert.AreEqual(0, kinds.Count(kind => kind == 2));
        }
        world.Step(DangoBeltWorld.FixedDt, input with { CancelForces = true });
        Assert.AreEqual(0d, world.Read().HorizontalForce);
        Assert.AreEqual(0d, world.ReadForceLedger()[4], "Explicit full cancellation retains its existing meaning.");
    }
}
