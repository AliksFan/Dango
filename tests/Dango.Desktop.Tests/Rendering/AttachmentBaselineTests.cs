using Dango.Core.Animation;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class AttachmentBaselineTests
{
    [TestMethod]
    public void HistoricalMappingIsExactAcrossFramesAndDetectsACopiedContourMutation()
    {
        var parameters = DangoSelfPhysicsParameters.CreateDefault();
        using var historical = new Box2DSelfPhysicsPresentationWorld("unused.dll", parameters);
        var extracted = new DangoPhysicsPresentationAdapter(parameters);
        for (var step = 0; step < 120; step++)
        {
            var points = Enumerable.Range(0, 16).SelectMany(i =>
            {
                var angle = Math.PI * i / 8;
                return new[] { (float)(0.7 * Math.Cos(angle)), (float)((0.75 + 0.04 * Math.Sin(step / 10d)) * Math.Sin(angle)) };
            }).ToArray();
            var native = new Box2DSelfSoftBodyNativeFrame(true, 0, points, 16, 3, 1.68f, 0.01f, -0.02f, 0.1f, 0.2f, 0.03f, true, -1);
            var state = new DangoBodyState(new(1), points, 0.1f, 0.2f, 1.68f, 0.01f, -0.02f, 0.03f, 3, true, -1, 16);
            var direction = step < 60 ? DangoFacingDirection.Left : DangoFacingDirection.Right;
            var expected = historical.PresentNativeFrame(native, TimeSpan.FromSeconds(1d / 60), direction);
            var actual = extracted.Present(state, TimeSpan.FromSeconds(1d / 60), direction);
            CollectionAssert.AreEqual(expected.Contour.ToArray(), actual.Contour.ToArray());
            Assert.AreEqual(expected.LeftEye, actual.LeftEye); Assert.AreEqual(expected.RightEye, actual.RightEye);
            Assert.AreEqual(expected.Hat, actual.Hat); Assert.AreEqual(expected.Diagnostics.SoftBody, actual.Diagnostics.SoftBody);
            if (step == 119)
            {
                var mutated = actual.Contour.ToArray(); mutated[0] = new(mutated[0].X + 0.001, mutated[0].Y);
                Assert.ThrowsExactly<AssertFailedException>(() => CollectionAssert.AreEqual(expected.Contour.ToArray(), mutated));
            }
        }
    }
    [TestMethod]
    public void EvidenceCannotOverwriteAnEarlierResult()
    {
        var category = "guard-" + Guid.NewGuid().ToString("N");
        using (var first = TestEvidence.Create(category, "sentinel.txt")) first.WriteByte(42);
        Assert.ThrowsExactly<IOException>(() => { using var duplicate = TestEvidence.Create(category, "sentinel.txt"); });
    }
}
