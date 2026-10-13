using Dango.Desktop.Physics.Tuning;
using Dango.Physics.Configuration;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class DangoBodyMaterialTests
{
    private const double Tolerance = 1e-12;

    [TestMethod]
    public void DefaultControlsUseTheAcceptedMaterialMidpoint()
    {
        var p = DangoBodyMaterial.Configure(DangoBodyControls.Default);
        var d = DangoPhysicsDefaults.Current;
        Assert.AreEqual(5.2 * .35, p.RadialElasticity, Tolerance);
        Assert.AreEqual(20.1 * .75, p.PerimeterElasticity, Tolerance);
        Assert.AreEqual(4.8, p.AreaPreservationStrength, Tolerance);
        Assert.AreEqual(1.22, p.RadialMaximumRatio, Tolerance);
        Assert.AreEqual(0, p.TopInwardForce);
        Assert.AreEqual(0, p.GroundDeformationStrength);
        Assert.AreEqual(1.85, p.RadialDamping, Tolerance);
        Assert.AreEqual(1.85, p.PerimeterDamping, Tolerance);
        Assert.AreEqual(d.RadialRestLength, p.RadialRestLength, Tolerance);
        Assert.AreEqual(d.NodeCount, p.NodeCount);
        Assert.AreEqual(d.SubStepCount, p.SubStepCount);
        Assert.AreEqual(d.Gravity, p.Gravity);
        Assert.AreEqual(d.GroundHeight, p.GroundHeight);
    }

    [TestMethod]
    public void LandingBuildChangesOnlyMaterialFieldsAndKeepsGeometry()
    {
        var fat = DangoBodyMaterial.Configure(new(100, 50, 0));
        var slim = DangoBodyMaterial.Configure(new(100, 50, 100));
        var mid = DangoBodyMaterial.Configure(new(100, 50, 50));
        Assert.AreEqual(1.82 * .50, fat.RadialElasticity, Tolerance);
        Assert.AreEqual(15.075 * .70, fat.PerimeterElasticity, Tolerance);
        Assert.AreEqual(4.8, fat.AreaPreservationStrength, Tolerance);
        Assert.AreEqual(1.30, fat.RadialMaximumRatio, Tolerance);
        Assert.AreEqual(1.82 * 1.60, slim.RadialElasticity, Tolerance);
        Assert.AreEqual(15.075 * 1.15, slim.PerimeterElasticity, Tolerance);
        Assert.AreEqual(4.8 * 1.15, slim.AreaPreservationStrength, Tolerance);
        Assert.AreEqual(1.22, slim.RadialMaximumRatio, Tolerance);
        foreach (var p in new[] { fat, slim })
        {
            Assert.AreEqual(mid.RadialRestLength, p.RadialRestLength);
            Assert.AreEqual(mid.NodeRadius, p.NodeRadius);
            Assert.AreEqual(mid.MinimumArea, p.MinimumArea);
            Assert.AreEqual(mid.CoreMass, p.CoreMass);
            Assert.AreEqual(mid.LocalAngleBalanceStrength, p.LocalAngleBalanceStrength);
            Assert.AreNotEqual(mid, p);
        }
    }

    [TestMethod]
    public void BuildInterpolatesAndLargeSoftFatBodiesLoseALittleRadialDamping()
    {
        var quarter = DangoBodyMaterial.Configure(new(100, 50, 25));
        Assert.AreEqual(1.82 * .75, quarter.RadialElasticity, Tolerance);
        Assert.AreEqual(1.26, quarter.RadialMaximumRatio, Tolerance);
        Assert.AreEqual(1.85, quarter.RadialDamping, Tolerance);

        var extreme = DangoBodyMaterial.Configure(new(120, 100, 0));
        Assert.AreEqual(5.2 * .25 * .50, extreme.RadialElasticity, Tolerance);
        Assert.AreEqual(20.1 * .55 * .70, extreme.PerimeterElasticity, Tolerance);
        Assert.AreEqual(1.80, extreme.RadialDamping, Tolerance);
        Assert.AreEqual(.68 * 1.2, extreme.RadialRestLength, Tolerance);
        Assert.AreEqual(.12 * 1.44, extreme.CoreMass, Tolerance);
        Assert.AreEqual(1.2, DangoBodyMaterial.SizeScale(extreme), Tolerance);
        Assert.AreEqual(1.85, DangoBodyMaterial.Configure(new(100, 100, 0)).RadialDamping, Tolerance);
    }

    [TestMethod]
    public void ControlsRejectOutOfRangeAndNonFiniteValues()
    {
        foreach (var (size, soft, build) in new[] { (79.9, 50.0, 50.0), (120.1, 50, 50), (100, -1, 50), (100, 101, 50),
                     (100, 50, -0.1), (100, 50, 100.1), (double.NaN, 50, 50), (100, double.PositiveInfinity, 50) })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DangoBodyControls(size, soft, build));
    }
}
