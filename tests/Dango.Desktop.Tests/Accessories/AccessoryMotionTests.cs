using System.Windows;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

// Owner tick, refresh, rebase and chain deformation contracts live in
// AccessoryRigRuntimeContractTests; retain shared connection and angular controls here.
[TestClass]
public sealed class AccessoryMotionTests
{
    private static AccessoryItem Item() => new("motion-test", "Synthetic material", "builtin-sprout", 64, 64)
    {
        ImagePivotX = 0.5,
        ImagePivotY = 0.2,
        ConnectionStrength = 100,
        LockRotation = true,
    };

    [TestMethod]
    public void RigidConnectionGravityHasAnEquilibrium()
    {
        var fixedSolver = new AccessoryMotionSolver(); var elastic = new AccessoryMotionSolver();
        var fixedItem = Item(); var elasticItem = fixedItem with { ConnectionStrength = 0, GravityInfluence = 100 };
        AccessoryMeshFrame? fixedFrame = null, elasticFrame = null;
        for (var i = 0; i < 600; i++)
        {
            fixedFrame = fixedSolver.Update(i, 1d / 60, new(80, 80), 64, 64, fixedItem);
            elasticFrame = elastic.Update(i, 1d / 60, new(80, 80), 64, 64, elasticItem);
        }
        Assert.AreEqual(new Vector(), fixedFrame!.ConnectionOffset);
        Assert.IsTrue(elasticFrame!.ConnectionOffset.Y > 50 && elasticFrame.ConnectionOffset.Length < 70);
        Assert.AreEqual(64 * Math.Sqrt(2), Distance(elasticFrame, 0, elasticFrame.Vertices.Count - 1), 1e-8);
    }

    [TestMethod]
    public void RotationLockAndSwingBoundRemainAvailableForSingleChain()
    {
        var locked = new AccessoryMotionSolver(); var swinging = new AccessoryMotionSolver();
        var a = Chain(Item() with { GravityInfluence = 100, BaseAngleDegrees = 20, ImagePivotX = 0.1 });
        var b = a with { LockRotation = false, MaxSwingDegrees = 12, ReturnStrength = 0, RotationDamping = 80 };
        AccessoryMeshFrame? fixedFrame = null, swingFrame = null;
        for (var i = 0; i < 180; i++)
        {
            fixedFrame = locked.Update(i, 1d / 60, new(80, 80), 64, 64, a);
            swingFrame = swinging.Update(i, 1d / 60, new(80, 80), 64, 64, b);
            Assert.AreEqual(20, fixedFrame.RotationDegrees, 1e-10);
            Assert.IsTrue(Math.Abs(swingFrame.RotationDegrees - 20) <= 12.000001);
        }
        Assert.IsTrue(Math.Abs(swingFrame!.RotationDegrees - 20) > 1);
    }

    [TestMethod]
    public void AngularDampingStopsMotionWithoutPretendingToReturnToTheBaseAngle()
    {
        var slow = new AccessoryMotionSolver(); var fast = new AccessoryMotionSolver();
        var a = Chain(Item() with
        {
            LockRotation = false,
            ReturnStrength = 0,
            RotationDamping = 5,
            GravityInfluence = 0,
            LimitSwing = false,
            ImagePivotX = 0.1,
            ImagePivotY = 0.1
        });
        var b = a with { RotationDamping = 100 };
        AccessoryMeshFrame? low = null, high = null;
        double lowStart = 0, highStart = 0;
        for (var i = 0; i < 150; i++)
        {
            // Continue at constant velocity so stopping does not inject an opposite angular impulse.
            var anchor = new Point(i < 30 ? 80 + .015 * i * i : 80 + .015 * 29 * 29 + .015 * 59 * (i - 29), 80);
            low = slow.Update(i, 1d / 60, anchor, 64, 64, a);
            high = fast.Update(i, 1d / 60, anchor, 64, 64, b);
            if (i == 120) { lowStart = low.RotationDegrees; highStart = high.RotationDegrees; }
        }
        Assert.IsTrue(Math.Abs(high!.RotationDegrees - highStart) < 0.01);
        Assert.IsTrue(Math.Abs(low!.RotationDegrees - lowStart) > 0.1);
        Assert.IsTrue(Math.Abs(high.RotationDegrees) > 0.1, "Damping alone must not silently restore base orientation.");
    }

    [TestMethod]
    public void ReturnSpringRecoversTheBaseAngleAfterRootAccelerationStops()
    {
        var solver = new AccessoryMotionSolver(); var noSpring = new AccessoryMotionSolver();
        var item = Chain(Item() with { LockRotation = false, LimitSwing = false,
            GravityInfluence = 0, ReturnStrength = 100, RotationDamping = 60 });
        var unreturnedItem = item with { ReturnStrength = 0 };
        AccessoryMeshFrame? frame = null, unreturned = null; var peak = 0d;
        for (var step = 0; step < 600; step++)
        {
            var anchor = new Point(80 + (step < 30 ? .15 * step * step : .15 * 29 * 29 + .15 * 59 * (step - 29)), 80);
            frame = solver.Update(step, 1d / 60, anchor, 64, 64, item);
            unreturned = noSpring.Update(step, 1d / 60, anchor, 64, 64, unreturnedItem);
            peak = Math.Max(peak, Math.Abs(frame.RotationDegrees));
        }
        Assert.IsTrue(peak > 1, "An unchanged configuration must first excite the return spring.");
        Assert.AreEqual(item.BaseAngleDegrees, frame!.RotationDegrees, .01);
        Assert.IsTrue(Math.Abs(unreturned!.RotationDegrees - item.BaseAngleDegrees) > 1,
            "The identical root trajectory without restoration must retain its displaced angle.");
    }

    private static AccessoryItem Chain(AccessoryItem item) => item with
    {
        DeformationMode = AccessoryDeformationMode.SingleChain,
        Rig = new([new(item.ImagePivotX, item.ImagePivotY), new(.7, .5), new(.8, .8)], .8, 3, 2),
    };

    private static double Distance(AccessoryMeshFrame frame, int a, int b) => (frame.Vertices[a] - frame.Vertices[b]).Length;
}
