using Dango.Desktop.DesktopHost.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.Overlay;

[TestClass]
public sealed class OverlayInteractionControllerTests
{
    [TestMethod]
    public void DragCanOnlyBeginFromTheDangoRegion()
    {
        var controller = new OverlayInteractionController();

        Assert.IsFalse(controller.TryBeginDrag(OverlayHitTarget.PassThrough));
        Assert.IsFalse(controller.TryBeginDrag(OverlayHitTarget.Island));
        Assert.IsTrue(controller.TryBeginDrag(OverlayHitTarget.Dango));
        Assert.IsTrue(controller.IsDragging);
    }

    [TestMethod]
    [DataRow(DragCancellationReason.Escape)]
    [DataRow(DragCancellationReason.LostCapture)]
    [DataRow(DragCancellationReason.WindowDeactivated)]
    [DataRow(DragCancellationReason.WindowClosing)]
    public void CancellationPathsAlwaysEndAnActiveDrag(DragCancellationReason reason)
    {
        var controller = new OverlayInteractionController();
        Assert.IsTrue(controller.TryBeginDrag(OverlayHitTarget.Dango));

        var captureMustBeReleased = controller.CancelDrag(reason);

        Assert.IsTrue(captureMustBeReleased);
        Assert.IsFalse(controller.IsDragging);
        Assert.AreEqual(reason, controller.LastCancellationReason);
    }

    [TestMethod]
    public void RepeatedCancellationIsIdempotent()
    {
        var controller = new OverlayInteractionController();
        Assert.IsTrue(controller.TryBeginDrag(OverlayHitTarget.Dango));

        Assert.IsTrue(controller.CancelDrag(DragCancellationReason.WindowClosing));
        Assert.IsFalse(controller.CancelDrag(DragCancellationReason.WindowClosing));
    }
}
