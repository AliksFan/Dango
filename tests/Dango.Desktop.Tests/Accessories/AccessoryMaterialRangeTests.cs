using System.Windows;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryMaterialRangeTests
{
    private const double Seconds = 1d / 60;
    public TestContext TestContext { get; set; } = null!;

    private static AccessoryItem Item() => new("range-test", "Synthetic material range", "synthetic", 64, 64)
    {
        ImagePivotX = .5,
        ImagePivotY = 0,
        ConnectionStrength = 100,
        LockRotation = true,
    };

    [TestMethod]
    public void MaximumSoftnessBendsAndStretchesAtLeastTwiceAsMuchAsTheOldMaximum()
    {
        const double width = 256, height = 256;
        var anchor = new Point(300, 200);
        var item = Item() with { Softness = 100, GravityInfluence = 100 };
        var frame = Settle(item, anchor, width, height);
        var (bend, strain) = VerticalMaterialResponse(frame, width, height);
        // The prior maximum under this unsaturated, stationary gravity load was gain=1.
        var force = 300 / Math.Sqrt(width * width + height * height);
        var oldBend = force * .018;
        var oldStrain = force * .009;
        TestContext.WriteLine($"256 DIP: bend={bend:F8} (old={oldBend:F8}), strain={strain:F8} (old={oldStrain:F8}).");
        Assert.IsTrue(bend > oldBend * 2.8, $"Maximum bend {bend} has not expanded beyond {oldBend}.");
        Assert.IsTrue(strain > oldStrain * 2.8, $"Maximum stretch {strain} has not expanded beyond {oldStrain}.");
        Assert.AreEqual(anchor, frame.ImagePivot);
        AssertExactPivot(frame, item);
    }

    [TestMethod]
    public void MinimumConnectionStrengthIsMoreElasticThanTheOldMinimumWithoutMaterialStrain()
    {
        var item = Item() with { ConnectionStrength = 0, GravityInfluence = 100 };
        var frame = Settle(item, new(300, 200), 256, 256);
        // The old 0.75 Hz spring's equilibrium remains below its existing travel bound.
        var oldOffset = 300 / Math.Pow(2 * Math.PI * .75, 2);
        TestContext.WriteLine($"256 DIP: minimum-strength gravity offset={frame.ConnectionOffset.Y:F6} DIP (old={oldOffset:F6}).");
        Assert.IsTrue(frame.ConnectionOffset.Y > oldOffset * 4,
            $"Minimum-strength equilibrium {frame.ConnectionOffset.Y} has not expanded beyond {oldOffset}.");
        Assert.AreEqual(256 * Math.Sqrt(2), CornerDistance(frame), 1e-8,
            "A loose connection must not invent material softness.");
        Assert.IsTrue(frame.ConnectionOffset.Length < Math.Sqrt(2) * 256 * .75);

        var solver = new AccessoryMotionSolver();
        for (var step = 0; step < 600; step++) solver.Update(step, Seconds, new(300, 200), 256, 256, item);
        for (var step = 600; step < 1800; step++)
            frame = solver.Update(step, Seconds, new(300, 200), 256, 256, item with { GravityInfluence = 0 });
        Assert.IsTrue(frame.ConnectionOffset.Length < .001, "The weaker spring must still recover after force stops.");
    }

    [TestMethod]
    [DataRow(0d, 100d)]
    [DataRow(30d, 30d)]
    [DataRow(60d, 60d)]
    public void MiddleRangesAndRigidLockedDefaultsKeepTheirPriorEquilibrium(double softness, double strength)
    {
        const double width = 256, height = 256;
        var item = Item() with { Softness = softness, ConnectionStrength = strength, GravityInfluence = 100 };
        var frame = Settle(item, new(300, 200), width, height);
        var (bend, strain) = VerticalMaterialResponse(frame, width, height);
        var force = 300 / Math.Sqrt(width * width + height * height);
        Assert.AreEqual(force * .018 * softness / 100, bend, 1e-8);
        Assert.AreEqual(force * .009 * softness / 100, strain, 1e-8);
        var oldOffset = strength == 100 ? 0 : 300 / Math.Pow(2 * Math.PI * (.75 + strength / 100 * 4.25), 2);
        Assert.AreEqual(oldOffset, frame.ConnectionOffset.Y, 1e-8);
        Assert.AreEqual(0, frame.ConnectionOffset.X, 1e-8);
        Assert.AreEqual(0, frame.RotationDegrees);
        AssertExactPivot(frame, item);
    }

    [TestMethod]
    public void SoftnessExtensionIsProgressiveAcrossTheUpperRange()
    {
        var responses = new[] { 60d, 70, 80, 90, 100 }.Select(softness =>
        {
            var frame = Settle(Item() with { Softness = softness, GravityInfluence = 100 }, new(300, 200), 256, 256);
            return VerticalMaterialResponse(frame, 256, 256);
        }).ToArray();
        for (var index = 1; index < responses.Length; index++)
        {
            Assert.IsTrue(responses[index].Bend > responses[index - 1].Bend);
            Assert.IsTrue(responses[index].Strain > responses[index - 1].Strain);
        }
    }

    [TestMethod]
    public void ConnectionAndMaterialResponsesRemainIndependentDuringMotionAndRotation()
    {
        var material = new AccessoryMotionSolver();
        var looseMaterial = new AccessoryMotionSolver();
        var looseRigid = new AccessoryMotionSolver();
        var item = Item() with
        {
            ImagePivotX = .37,
            ImagePivotY = .41,
            Softness = 100,
            GravityInfluence = 100,
            LockRotation = false,
            BaseAngleDegrees = 73,
            LimitSwing = false,
            RotationDamping = 0,
            ReturnStrength = 0,
        };
        var loose = item with { ConnectionStrength = 0 };
        var rigid = loose with { Softness = 0 };
        var sawElasticity = false;
        var sawMaterialChange = false;
        for (var step = 0; step < 180; step++)
        {
            var anchor = new Point(300 + 30 * Math.Sin(step * .09), 200 + 20 * Math.Cos(step * .07));
            var a = material.Update(step, Seconds, anchor, 128, 64, item);
            var b = looseMaterial.Update(step, Seconds, anchor, 128, 64, loose);
            var c = looseRigid.Update(step, Seconds, anchor, 128, 64, rigid);
            Assert.AreEqual(new Vector(), a.ConnectionOffset, "Strength=100 must remain an exact connection.");
            Assert.AreEqual(b.ConnectionOffset, c.ConnectionOffset, "Material softness must not change the connection spring.");
            Assert.AreEqual(a.RotationDegrees, b.RotationDegrees);
            Assert.AreEqual(b.RotationDegrees, c.RotationDegrees);
            for (var vertex = 0; vertex < a.Vertices.Count; vertex++)
                Assert.IsTrue(((a.Vertices[vertex] - a.ImagePivot) - (b.Vertices[vertex] - b.ImagePivot)).Length < 1e-8,
                    "Connection elasticity must only translate the independently deformed material.");
            Assert.AreEqual(Math.Sqrt(128 * 128 + 64 * 64), CornerDistance(c), 1e-8,
                "Softness=0 must remain rigid even during elastic movement and free rotation.");
            AssertExactPivot(a, item); AssertExactPivot(b, loose); AssertExactPivot(c, rigid);
            sawElasticity |= b.ConnectionOffset.Length > 1;
            sawMaterialChange |= Math.Abs(CornerDistance(b) - CornerDistance(c)) > 1;
        }
        Assert.IsTrue(sawElasticity && sawMaterialChange, "The independence gate must exercise both live responses.");
    }

    [TestMethod]
    [DataRow(6.4, .003125, 0d, 1d, -180d, 100d, false)]
    [DataRow(.003125, 6.4, 1d, 0d, 180d, 0d, true)]
    [DataRow(2048d, 2048d, 0d, 0d, 180d, 100d, false)]
    [DataRow(1d, 2048d, .37, .41, -180d, 100d, true)]
    [DataRow(2048d, 1d, 1d, 1d, 179d, 0d, false)]
    [DataRow(256d, 256d, .37, .41, 73d, 100d, false)]
    [DataRow(256d, 32d, 1d, 1d, 90d, 100d, false)]
    [DataRow(32d, 256d, 0d, 0d, -90d, 100d, false)]
    public void ExpandedExtremesStayFiniteBoundedAndUnfoldedUnderRapidSignals(
        double width, double height, double pivotX, double pivotY, double baseAngle, double gravity, bool limitSwing)
    {
        var solver = new AccessoryMotionSolver();
        var item = Item() with
        {
            ImagePivotX = pivotX,
            ImagePivotY = pivotY,
            BaseAngleDegrees = baseAngle,
            Softness = 100,
            ConnectionStrength = 0,
            GravityInfluence = gravity,
            LockRotation = false,
            RotationDamping = 0,
            ReturnStrength = 0,
            LimitSwing = limitSwing,
            MaxSwingDegrees = 7,
        };
        var diagonal = Math.Sqrt(width * width + height * height);
        for (var step = 0; step < 300; step++)
        {
            var anchor = new Point(step % 2 == 0 ? 1e7 : -1e7, step % 3 == 0 ? 1e7 : -1e7);
            var seconds = step % 2 == 0 ? .1 : 1d / 240;
            var frame = solver.Update(step, seconds, anchor, width, height, item);
            Assert.IsTrue(double.IsFinite(frame.RotationDegrees));
            Assert.IsTrue(frame.ConnectionOffset.Length <= .75 * Math.Sqrt(2) * diagonal + 1e-8);
            if (limitSwing) Assert.IsTrue(Math.Abs(frame.RotationDegrees - baseAngle) <= 7.000001);
            Assert.IsTrue(double.IsFinite(frame.Bounds.X) && double.IsFinite(frame.Bounds.Y));
            Assert.IsTrue(double.IsFinite(frame.Bounds.Width) && double.IsFinite(frame.Bounds.Height));
            AssertExactPivot(frame, item);
            foreach (var vertex in frame.Vertices)
            {
                Assert.IsTrue(double.IsFinite(vertex.X) && double.IsFinite(vertex.Y));
                Assert.IsTrue((vertex - frame.ImagePivot).Length <= 2 * diagonal + 1e-8);
            }
            for (var triangle = 0; triangle < frame.TriangleIndices.Count; triangle += 3)
            {
                var a = frame.TriangleIndices[triangle]; var b = frame.TriangleIndices[triangle + 1]; var c = frame.TriangleIndices[triangle + 2];
                var actualArea = Vector.CrossProduct(frame.Vertices[b] - frame.Vertices[a], frame.Vertices[c] - frame.Vertices[a]);
                var uvArea = Vector.CrossProduct(frame.TextureCoordinates[b] - frame.TextureCoordinates[a], frame.TextureCoordinates[c] - frame.TextureCoordinates[a]);
                Assert.IsTrue(actualArea / (uvArea * width * height) > .04,
                    "Even the most flexible material must retain positive, non-singular triangles.");
            }
        }
    }

    private static AccessoryMeshFrame Settle(AccessoryItem item, Point anchor, double width, double height)
    {
        var solver = new AccessoryMotionSolver();
        AccessoryMeshFrame? frame = null;
        for (var step = 0; step < 600; step++) frame = solver.Update(step, Seconds, anchor, width, height, item);
        return frame!;
    }

    private static (double Bend, double Strain) VerticalMaterialResponse(AccessoryMeshFrame frame, double width, double height)
    {
        var middle = frame.Vertices[FindUv(frame, .5, .5)].Y - frame.ImagePivot.Y;
        var bottom = frame.Vertices[FindUv(frame, .5, 1)].Y - frame.ImagePivot.Y;
        var diagonal = Math.Sqrt(width * width + height * height);
        var bend = 2 * (bottom - 2 * middle) * diagonal / (height * height);
        return (bend, (bottom - bend * height * height / diagonal) / height - 1);
    }

    private static double CornerDistance(AccessoryMeshFrame frame) =>
        (frame.Vertices[FindUv(frame, 0, 0)] - frame.Vertices[FindUv(frame, 1, 1)]).Length;

    private static void AssertExactPivot(AccessoryMeshFrame frame, AccessoryItem item) =>
        Assert.AreEqual(frame.ImagePivot, frame.Vertices[FindUv(frame, item.ImagePivotX, item.ImagePivotY)]);

    private static int FindUv(AccessoryMeshFrame frame, double x, double y) =>
        Enumerable.Range(0, frame.TextureCoordinates.Count).Single(index => frame.TextureCoordinates[index] == new Point(x, y));
}
