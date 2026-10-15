using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class ShortWanderScaleTests
{
    [TestMethod]
    [DataRow(72d)]
    [DataRow(80d)]
    public void ScreenSpeedAndDistanceGatesAreIndependentOfNativeScale(double pixelsPerUnit)
    {
        var controller = new ShortWanderController { Next = (min, _) => min };
        WanderSample Sample(double screenX) => new(screenX / pixelsPerUnit, 0, true, 400, 400, 1200, pixelsPerUnit);

        // 11.8 DIP/s is below the unchanged 12 DIP/s stability limit at both scales.
        var start = 0d;
        for (var step = 0; step < 426; step++)
        {
            start = step * .02 * 11.8;
            controller.Advance(Sample(start), .02, true, false);
        }
        Assert.IsTrue(controller.Driving);
        controller.Advance(Sample(start - 127), .02, true, false);
        Assert.IsTrue(controller.Driving, "The sampled 128 DIP target has not yet been reached.");
        controller.Advance(Sample(start - 128), .02, true, false);
        Assert.IsFalse(controller.Driving);

        // The unit correction must not admit support moving above the same limit.
        controller.Cancel();
        for (var step = 0; step < 426; step++)
            controller.Advance(Sample(step * .02 * 12.2), .02, true, false);
        Assert.IsFalse(controller.Active);
    }
}
