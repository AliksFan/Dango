using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class NetworkPresentationProbeTests
{
    [TestMethod]
    public void IndependentNeutralWorldsKeepFiniteLocalContoursAndNoCrossContact()
    {
        using var first = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified, capacity: 1);
        using var second = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified, capacity: 1);
        var a = first.Add(0, 0);
        var b = second.Add(3, 0);
        DangoBodyState? lastA = null, lastB = null;
        for (var frame = 0; frame < 600; frame++)
        {
            var fa = first.Step(TimeSpan.FromSeconds(1d / 60), [new(a)]);
            var fb = second.Step(TimeSpan.FromSeconds(1d / 60), [new(b)]);
            Assert.IsTrue(fa.Succeeded && fb.Succeeded);
            Assert.AreEqual(0, fa.ContactStepCount);
            Assert.AreEqual(0, fb.ContactStepCount);
            lastA = fa.Bodies.Single(); lastB = fb.Bodies.Single();
            Assert.IsTrue(lastA.Contour.All(float.IsFinite) && lastB.Contour.All(float.IsFinite));
            foreach (var body in new[] { lastA, lastB })
            {
                var xs = body.Contour.Where((_, i) => i % 2 == 0).ToArray();
                Assert.IsTrue(xs.Min() < 0 && xs.Max() > 0, "Contour is centered around the local core, not the world position.");
                var config = DangoPhysicsDefaults.Current;
                var localLimit = config.RadialRestLength * config.RadialMaximumRatio + config.NodeRadius + .001;
                Assert.IsTrue(body.Contour.All(v => Math.Abs(v) <= localLimit), "Core-local contour stays within the configured radial envelope.");
            }
        }
        Assert.IsNotNull(lastA); Assert.IsNotNull(lastB);
        Assert.AreEqual(3, lastB.CoreX - lastA.CoreX, .01, "Native core positions remain separate from host placement.");
        // Translation in a floating-point solver need not preserve every vertex over time.
        // M5 separately mutates CoreX/CoreY of the same body to prove host placement isolation.
    }
}
