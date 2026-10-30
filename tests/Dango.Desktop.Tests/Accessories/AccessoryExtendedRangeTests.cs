using System.Windows;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

// Only the shared root-connection range survives. Old material bend/strain gains are retired;
// single-chain fixed-length bending and extreme inputs are covered by AccessoryRigMechanismTests.
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

    private static int FindUv(AccessoryMeshFrame frame, double x, double y) =>
        Enumerable.Range(0, frame.TextureCoordinates.Count).Single(index => frame.TextureCoordinates[index] == new Point(x, y));
}
