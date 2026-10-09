using System.Windows;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryExtendedRangeTests
{
    private static AccessoryItem Item() => new("extended-range", "Fictional extended material", "builtin-sprout", 64, 64)
    {
        ImagePivotX = .5,
        ImagePivotY = 0,
        LockRotation = true,
    };

    [TestMethod]
    public void MaximumSoftnessHasAtLeastFiveTimesThePreviousUnsaturatedResponse()
    {
        var frame = Settle(Item() with { Softness = 100, GravityInfluence = 100 }, 256);
        var (bend, strain) = VerticalResponse(frame, 256);
        var force = 300 / (256 * Math.Sqrt(2));
        Assert.IsTrue(bend > force * .018 * 3 * 5.5, $"Bend {bend} did not exceed the previous maximum response.");
        Assert.IsTrue(strain > force * .009 * 3 * 5.5, $"Strain {strain} did not exceed the previous maximum response.");
    }

    [TestMethod]
    public void SmallImagesHaveAnExpandedShapeEnvelopeRatherThanOnlyReachingTheOldCapSooner()
    {
        var frame = Settle(Item() with { Softness = 100, GravityInfluence = 100 }, 16);
        var (bend, strain) = VerticalResponse(frame, 16);
        Assert.IsTrue(bend > .24 * 2.5, $"Bend {bend} is still constrained by the previous envelope.");
        Assert.IsTrue(strain > .18 * 2.5, $"Strain {strain} is still constrained by the previous envelope.");
    }

    [TestMethod]
    [DataRow(256d)]
    [DataRow(64d)]
    public void WeakestConnectionHasMuchMoreTravelWhileTheImageItselfStaysRigid(double size)
    {
        var frame = Settle(Item() with { ConnectionStrength = 0, GravityInfluence = 100 }, size);
        var oldEquilibrium = Math.Min(300 / Math.Pow(2 * Math.PI * .35, 2), size * Math.Sqrt(2) * .18);
        Assert.IsTrue(frame.ConnectionOffset.Y > oldEquilibrium * 3.5,
            $"Connection travel {frame.ConnectionOffset.Y} is still close to the previous {oldEquilibrium}.");
        Assert.AreEqual(size * Math.Sqrt(2),
            (frame.Vertices[FindUv(frame, 0, 0)] - frame.Vertices[FindUv(frame, 1, 1)]).Length, 1e-8);
    }

    private static AccessoryMeshFrame Settle(AccessoryItem item, double size)
    {
        var solver = new AccessoryMotionSolver();
        AccessoryMeshFrame? frame = null;
        for (var step = 0; step < 1200; step++) frame = solver.Update(step, 1d / 60, new(300, 200), size, size, item);
        return frame!;
    }

    private static (double Bend, double Strain) VerticalResponse(AccessoryMeshFrame frame, double size)
    {
        var middle = frame.Vertices[FindUv(frame, .5, .5)].Y - frame.ImagePivot.Y;
        var bottom = frame.Vertices[FindUv(frame, .5, 1)].Y - frame.ImagePivot.Y;
        var bend = 2 * (bottom - 2 * middle) * size * Math.Sqrt(2) / (size * size);
        return (bend, (bottom - bend * size / Math.Sqrt(2)) / size - 1);
    }

    private static int FindUv(AccessoryMeshFrame frame, double x, double y) =>
        Enumerable.Range(0, frame.TextureCoordinates.Count).Single(index => frame.TextureCoordinates[index] == new Point(x, y));
}
