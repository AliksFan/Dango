using System.Windows;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryMotionTests
{
    private static AccessoryItem Item() => new("motion-test", "Synthetic material", "synthetic", 64, 64)
    {
        ImagePivotX = 0.5,
        ImagePivotY = 0.2,
        ConnectionStrength = 100,
        LockRotation = true,
    };

    [TestMethod]
    public void SoftnessChangesInternalDistancesWhileRigidTextureOnlyMovesAsAWhole()
    {
        var rigid = new AccessoryMotionSolver(); var soft = new AccessoryMotionSolver();
        var a = Item(); var b = a with { Softness = 100 };
        var rigidRest = rigid.Update(0, 1d / 60, new(100, 100), 64, 64, a);
        var softRest = soft.Update(0, 1d / 60, new(100, 100), 64, 64, b);
        AccessoryMeshFrame rigidFrame = rigidRest, softFrame = softRest;
        for (var i = 1; i <= 50; i++)
        {
            var x = 100 + 0.12 * i * i;
            rigidFrame = rigid.Update(i, 1d / 60, new(x, 100), 64, 64, a);
            softFrame = soft.Update(i, 1d / 60, new(x, 100), 64, 64, b);
        }
        var top = 0; var bottom = rigidFrame.Vertices.Count - 1;
        Assert.AreEqual(Distance(rigidRest, top, bottom), Distance(rigidFrame, top, bottom), 1e-8);
        Assert.IsTrue(Math.Abs(Distance(softRest, top, bottom) - Distance(softFrame, top, bottom)) > 0.5,
            "A whole-image rotation or translation cannot satisfy internal material deformation.");
        Assert.AreEqual(new Point(400, 100), rigidFrame.ImagePivot);
        Assert.AreEqual(new Point(400, 100), softFrame.ImagePivot);
        var pivotIndex = Enumerable.Range(0, softFrame.TextureCoordinates.Count)
            .Single(i => softFrame.TextureCoordinates[i] == new Point(b.ImagePivotX, b.ImagePivotY));
        Assert.AreEqual(softFrame.ImagePivot, softFrame.Vertices[pivotIndex],
            "An arbitrary pivot must be an actual fixed texture vertex, not just a reported coordinate.");
    }

    [TestMethod]
    public void ConnectionElasticityDoesNotSubstituteForMaterialSoftnessAndGravityHasAnEquilibrium()
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
        Assert.IsTrue(elasticFrame!.ConnectionOffset.Y > 1 && elasticFrame.ConnectionOffset.Length < 30);
        Assert.AreEqual(64 * Math.Sqrt(2), Distance(elasticFrame, 0, elasticFrame.Vertices.Count - 1), 1e-8);
        var noGravity = elasticItem with { GravityInfluence = 0 };
        for (var i = 600; i < 1200; i++) elasticFrame = elastic.Update(i, 1d / 60, new(80, 80), 64, 64, noGravity);
        Assert.IsTrue(elasticFrame.ConnectionOffset.Length < 0.001);
    }

    [TestMethod]
    public void RotationLockSwingBoundAndReturnAreIndependentOfInternalGravitySag()
    {
        var locked = new AccessoryMotionSolver(); var swinging = new AccessoryMotionSolver();
        var a = Item() with { Softness = 100, GravityInfluence = 100, BaseAngleDegrees = 20, ImagePivotX = 0.1 };
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
        Assert.IsTrue(Math.Abs(Distance(fixedFrame!, 0, fixedFrame!.Vertices.Count - 1) - 64 * Math.Sqrt(2)) > 0.1);
        b = b with { GravityInfluence = 0, ReturnStrength = 100, RotationDamping = 100 };
        for (var i = 180; i < 600; i++) swingFrame = swinging.Update(i, 1d / 60, new(80, 80), 64, 64, b);
        Assert.AreEqual(20, swingFrame.RotationDegrees, 0.01);
    }

    [TestMethod]
    public void AngularDampingStopsMotionWithoutPretendingToReturnToTheBaseAngle()
    {
        var slow = new AccessoryMotionSolver(); var fast = new AccessoryMotionSolver();
        var a = Item() with
        {
            LockRotation = false,
            ReturnStrength = 0,
            RotationDamping = 5,
            GravityInfluence = 100,
            LimitSwing = false,
            ImagePivotX = 0.1,
            ImagePivotY = 0.1
        };
        var b = a with { RotationDamping = 100 };
        AccessoryMeshFrame? low = null, high = null;
        double lowStart = 0, highStart = 0;
        for (var i = 0; i < 150; i++)
        {
            if (i == 30) { a = a with { GravityInfluence = 0 }; b = b with { GravityInfluence = 0 }; }
            low = slow.Update(i, 1d / 60, new(80, 80), 64, 64, a);
            high = fast.Update(i, 1d / 60, new(80, 80), 64, 64, b);
            if (i == 120) { lowStart = low.RotationDegrees; highStart = high.RotationDegrees; }
        }
        Assert.IsTrue(Math.Abs(high!.RotationDegrees - highStart) < 0.01);
        Assert.IsTrue(Math.Abs(low!.RotationDegrees - lowStart) > 0.1);
        Assert.IsTrue(Math.Abs(high.RotationDegrees) > 0.1, "Damping alone must not silently restore base orientation.");
    }

    [TestMethod]
    public void DuplicateRenderConsumptionAndPauseResumeCannotAdvanceOrInventAnImpulse()
    {
        var solver = new AccessoryMotionSolver(); var item = Item() with { Softness = 100, ConnectionStrength = 10 };
        var first = solver.Update(4, 1d / 60, new(80, 80), 64, 64, item);
        Assert.AreSame(first, solver.Update(4, 1d / 60, new(900, 900), 64, 64, item));
        Assert.AreSame(first, solver.Update(3, 1d / 60, new(900, 900), 64, 64, item));
        solver.Paused = true;
        Assert.AreSame(first, solver.Update(5, 1d / 60, new(900, 900), 64, 64, item));
        solver.Paused = false;
        var resumed = solver.Update(6, 1d / 60, new(900, 900), 64, 64, item);
        Assert.AreEqual(new Vector(), resumed.ConnectionOffset);
        Assert.AreEqual(64 * Math.Sqrt(2), Distance(resumed, 0, resumed.Vertices.Count - 1), 1e-8);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => solver.Update(7, 0.2, new(80, 80), 64, 64, item));
        var refreshed = solver.Refresh(new(900, 900), 128, 128, item with { BaseAngleDegrees = 45 });
        Assert.AreEqual(resumed.StepIndex, refreshed.StepIndex);
        Assert.AreEqual(45, refreshed.RotationDegrees, 1e-8);
    }

    [TestMethod]
    public void ExtremeLegalInputsStayFiniteAndMeshTrianglesDoNotFlip()
    {
        var solver = new AccessoryMotionSolver();
        var item = Item() with
        {
            Softness = 100,
            ConnectionStrength = 0,
            LockRotation = false,
            RotationDamping = 0,
            ReturnStrength = 0,
            GravityInfluence = 100,
            LimitSwing = false,
            ImagePivotX = 0,
            ImagePivotY = 1
        };
        for (var i = 0; i < 600; i++)
        {
            var frame = solver.Update(i, 1d / 60, new(i % 2 * 10000, i % 3 * 10000), 256, 256, item);
            Assert.IsTrue(frame.ConnectionOffset.Length < 100);
            Assert.IsTrue(double.IsFinite(frame.RotationDegrees));
            for (var t = 0; t < frame.TriangleIndices.Count; t += 3)
            {
                var a = frame.Vertices[frame.TriangleIndices[t]];
                var b = frame.Vertices[frame.TriangleIndices[t + 1]];
                var c = frame.Vertices[frame.TriangleIndices[t + 2]];
                Assert.IsTrue(Vector.CrossProduct(b - a, c - a) > 0);
            }
        }
    }

    private static double Distance(AccessoryMeshFrame frame, int a, int b) => (frame.Vertices[a] - frame.Vertices[b]).Length;
}
