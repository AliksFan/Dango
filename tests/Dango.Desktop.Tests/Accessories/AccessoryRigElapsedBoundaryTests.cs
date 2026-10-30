using System.Windows;
using Dango.Desktop.Accessories.Rig;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryRigElapsedBoundaryTests
{
    [TestMethod]
    public void SubnormalElapsedCannotPoisonTheFollowingSuccessfulFrame()
    {
        var definition = new AccessoryRigDefinition([new(.2, .2), new(.4, .2), new(.55, .3)]);
        var solver = new AccessoryRigSolver(definition);
        solver.Advance(0, 1d / 60, new(new Point(.2, .2)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => solver.Advance(1, 1e-320, new(new Point(.21, .2))));
        Assert.AreEqual(0L, solver.Refresh().Tick);
        solver.Advance(2, 1d / 60, new(new Point(.21, .2)));
        Assert.IsTrue(solver.Refresh().Nodes.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)));
    }
}
