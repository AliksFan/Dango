using Dango.Core.Animation;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Tests.Physics.Presentation;

[TestClass]
public sealed class DangoPhysicsPresentationAdapterTests
{
    [TestMethod]
    public void ExtractedAdapterMatchesAcceptedPresentationMappingExactly()
    {
        var parameters = DangoSelfPhysicsParameters.CreateDefault();
        var points = Enumerable.Range(0, 16).SelectMany(index =>
        {
            var angle = 2 * Math.PI * index / 16;
            return new[] { (float)(0.7 * Math.Cos(angle)), (float)(0.8 * Math.Sin(angle)) };
        }).ToArray();
        var native = new Box2DSelfSoftBodyNativeFrame(true, 0, points, 16, 3, 1.68f, 0.01f, -0.02f, 0.1f, 0.2f, 0.03f, true, -1);
        var state = new DangoBodyState(new(1), points, 0.1f, 0.2f, 1.68f, 0.01f, -0.02f, 0.03f, 3, true, -1, 16);
        using var old = new Box2DSelfPhysicsPresentationWorld("unused.dll", parameters);
        var extracted = new DangoPhysicsPresentationAdapter(parameters);
        var expected = old.PresentNativeFrame(native, TimeSpan.FromSeconds(1d / 60), DangoFacingDirection.Right);
        var actual = extracted.Present(state, TimeSpan.FromSeconds(1d / 60), DangoFacingDirection.Right);
        CollectionAssert.AreEqual(expected.Contour.ToArray(), actual.Contour.ToArray());
        Assert.AreEqual(expected.Eyes, actual.Eyes);
        Assert.AreEqual(expected.LeftEye, actual.LeftEye);
        Assert.AreEqual(expected.RightEye, actual.RightEye);
        Assert.AreEqual(expected.Hat, actual.Hat);
        Assert.AreEqual(expected.Diagnostics.SoftBody, actual.Diagnostics.SoftBody);
    }
}
