using Dango.Desktop.DesktopHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost;

[TestClass]
public sealed class FullscreenGeometryClassifierTests
{
    [TestMethod]
    public void FullMonitorCoverageIsFullscreenButWorkAreaMaximizeIsNot()
    {
        var monitor = new DesktopPixelRect(0, 0, 1920, 1080);

        Assert.IsTrue(FullscreenGeometryClassifier.IsFullscreen(
            new DesktopPixelRect(0, 0, 1920, 1080),
            monitor));
        Assert.IsFalse(FullscreenGeometryClassifier.IsFullscreen(
            new DesktopPixelRect(0, 0, 1920, 1040),
            monitor));
    }

    [TestMethod]
    public void OnePixelCompositionVarianceStillCountsAsFullscreen()
    {
        Assert.IsTrue(FullscreenGeometryClassifier.IsFullscreen(
            new DesktopPixelRect(-1, -1, 1921, 1081),
            new DesktopPixelRect(0, 0, 1920, 1080)));
    }
}
