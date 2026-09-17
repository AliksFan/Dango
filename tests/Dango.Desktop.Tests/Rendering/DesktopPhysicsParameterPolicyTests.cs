using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class DesktopPhysicsParameterPolicyTests
{
    [TestMethod]
    public void DesktopRejectsParameterWritesEvenWithoutAWindow()
    {
        using var runtime = new StackedDangoSceneRuntime();
        var before = runtime.Parameters;
        var changed = before.WithNumericValue(nameof(before.TopInwardForce), 0.6);
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.ApplyPhysicsParameters(changed));
        Assert.AreEqual(before, runtime.Parameters);
    }
}
