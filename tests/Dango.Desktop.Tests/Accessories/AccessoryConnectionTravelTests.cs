using System.Windows;
using Dango.Desktop.Accessories;
using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryConnectionTravelTests
{
    [TestMethod]
    [DataRow(0d)]
    [DataRow(5d)]
    [DataRow(25d)]
    [DataRow(75d)]
    [DataRow(100d)]
    [DataRow(200d)]
    public void TravelCapsActualRadialDistanceWithoutChangingInternalShapeOrRotation(double percent)
    {
        var item = new AccessoryItem("travel-test", "Fictional connection travel", "builtin-sprout", 64, 64)
        {
            ConnectionStrength = 0,
            MaxConnectionTravelPercent = percent,
            Softness = 100,
            AllowFolding = true,
            MaxDeformationPercent = 200,
            LockRotation = false,
            LimitSwing = false,
            ReturnStrength = 0,
            RotationDamping = 0,
            GravityInfluence = 100,
        };
        var limited = new AccessoryMotionSolver(); var exact = new AccessoryMotionSolver();
        var limit = 16 * Math.Sqrt(2) * percent / 100;
        var largest = 0d;
        for (var step = 0; step < 300; step++)
        {
            // Sustained diagonal acceleration reaches the radial boundary. Alternating
            // teleports mostly cancel through a genuinely slow, weak connection.
            var time = step * .1;
            var anchor = new Point(80 - 1200 * time * time, 80 - 1200 * time * time);
            var frame = limited.Update(step, .1, anchor, 16, 16, item);
            var reference = exact.Update(step, .1, anchor, 16, 16, item with { ConnectionStrength = 100 });
            Assert.IsTrue(frame.ConnectionOffset.Length <= limit + 1e-9);
            Assert.IsTrue((frame.ConnectionOffset - (frame.ImagePivot - anchor)).Length < 1e-9);
            Assert.AreEqual(reference.RotationDegrees, frame.RotationDegrees);
            for (var vertex = 0; vertex < frame.Vertices.Count; vertex++)
                Assert.IsTrue(((frame.Vertices[vertex] - frame.ImagePivot) -
                    (reference.Vertices[vertex] - reference.ImagePivot)).Length < 1e-8,
                    "The connection limit must not substitute for material shape or angular controls.");
            largest = Math.Max(largest, frame.ConnectionOffset.Length);
        }
        if (percent == 0) Assert.AreEqual(0d, largest);
        else Assert.IsTrue(largest >= limit * .95,
            $"Pressure must really reach the requested travel, including values beyond the old hidden cap: {largest}/{limit}.");
        var refreshed = limited.Refresh(new(80, 80), 16, 16, item with { MaxConnectionTravelPercent = 1 });
        Assert.IsTrue(refreshed.ConnectionOffset.Length <= 16 * Math.Sqrt(2) / 100 + 1e-9);
        Assert.AreEqual(299, refreshed.StepIndex);
    }

    [TestMethod]
    public void VeryLooseConnectionStillReturnsAfterForceStops()
    {
        var item = new AccessoryItem("travel-recovery", "Fictional loose recovery", "builtin-sprout", 64, 64)
        { ConnectionStrength = 0, MaxConnectionTravelPercent = 200, GravityInfluence = 100 };
        var solver = new AccessoryMotionSolver(); AccessoryMeshFrame? frame = null;
        for (var step = 0; step < 1800; step++) frame = solver.Update(step, 1d / 60, new(80, 80), 64, 64,
            step < 600 ? item : item with { GravityInfluence = 0 });
        Assert.IsTrue(frame!.ConnectionOffset.Length < .001);
    }

    [TestMethod]
    public void AtExactRadialBoundaryOnlyOutwardSpeedIsRemoved()
    {
        var solver = new AccessoryMotionSolver();
        var item = new AccessoryItem("travel-boundary", "Fictional exact boundary", "builtin-sprout", 64, 64)
        { ConnectionStrength = 0, MaxConnectionTravelPercent = 20 };
        var x = typeof(AccessoryMotionSolver).GetField("connectionX", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var y = typeof(AccessoryMotionSolver).GetField("connectionY", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        x.SetValue(solver, new SpringState(1, 2)); y.SetValue(solver, new SpringState(0, 3));
        var frame = solver.Refresh(new(20, 20), 3, 4, item);
        Assert.AreEqual(new Vector(1, 0), frame.ConnectionOffset);
        Assert.AreEqual(new SpringState(1, 0), (SpringState)x.GetValue(solver)!);
        Assert.AreEqual(new SpringState(0, 3), (SpringState)y.GetValue(solver)!);
        x.SetValue(solver, new SpringState(1, -2));
        solver.Refresh(new(20, 20), 3, 4, item);
        Assert.AreEqual(new SpringState(1, -2), (SpringState)x.GetValue(solver)!);
        Assert.AreEqual(new SpringState(0, 3), (SpringState)y.GetValue(solver)!);
    }
}
