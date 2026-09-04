using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class PresentationSquashTransformerTests
{
    [TestMethod]
    public void SquashPreservesBottomAndAppliesBoundedWidthAndHeightChange()
    {
        var frame = new PhysicsPresentationFrame(
            true,
            [new PresentationPoint(-0.5, -1), new PresentationPoint(0.5, -1), new PresentationPoint(0.5, 1)],
            new PresentationAttachment(new PresentationPoint(0.2, 0.2), 0),
            new PresentationAttachment(new PresentationPoint(0, 0.8), 0),
            PhysicsPresentationFallbackReason.None,
            new PhysicsPresentationDiagnostics(
                PhysicsPresentationBackend.DeterministicFake,
                1,
                PhysicsPresentationDiagnosticCode.FakeActive));

        var transformed = PresentationSquashTransformer.Apply(frame, 1);

        Assert.AreEqual(-1, transformed.Contour.Min(point => point.Y), 0.0001);
        Assert.AreEqual(0.84, transformed.Contour.Max(point => point.Y), 0.0001);
        Assert.AreEqual(0.525, transformed.Contour.Max(point => point.X), 0.0001);
    }

    [TestMethod]
    public void GroundFrictionKeepsBottomAnchoredAndLetsUpperBodyLag()
    {
        var frame = CreateFrame();

        var transformed = PresentationGroundFrictionTransformer.Apply(frame, -1);

        var bottom = transformed.Contour.Where(point => Math.Abs(point.Y + 1) < 0.0001).ToArray();
        Assert.AreEqual(-0.5, bottom[0].X, 0.0001);
        Assert.AreEqual(0.5, bottom[1].X, 0.0001);
        Assert.IsLessThan(0.5, transformed.Contour[2].X);
    }

    private static PhysicsPresentationFrame CreateFrame() => new(
        true,
        [new PresentationPoint(-0.5, -1), new PresentationPoint(0.5, -1), new PresentationPoint(0.5, 1)],
        new PresentationAttachment(new PresentationPoint(0.2, 0.2), 0),
        new PresentationAttachment(new PresentationPoint(0, 0.8), 0),
        PhysicsPresentationFallbackReason.None,
        new PhysicsPresentationDiagnostics(
            PhysicsPresentationBackend.DeterministicFake,
            1,
            PhysicsPresentationDiagnosticCode.FakeActive));
}
