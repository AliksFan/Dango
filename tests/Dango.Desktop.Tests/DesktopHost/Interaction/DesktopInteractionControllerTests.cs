using Dango.Desktop.DesktopHost.Interaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.Overlay;

[TestClass]
public sealed class DesktopInteractionControllerTests
{
    [TestMethod]
    public void DragCanOnlyBeginFromTheDangoRegion()
    {
        var controller = new DesktopInteractionController();

        Assert.IsFalse(controller.TryBeginDrag(DesktopHitTarget.PassThrough));
        Assert.IsFalse(controller.TryBeginDrag(DesktopHitTarget.Island));
        Assert.IsTrue(controller.TryBeginDrag(DesktopHitTarget.Dango));
        Assert.IsTrue(controller.IsDragging);
    }

    [TestMethod]
    [DataRow(DragCancellationReason.Escape)]
    [DataRow(DragCancellationReason.LostCapture)]
    [DataRow(DragCancellationReason.WindowDeactivated)]
    [DataRow(DragCancellationReason.WindowClosing)]
    public void CancellationPathsAlwaysEndAnActiveDrag(DragCancellationReason reason)
    {
        var controller = new DesktopInteractionController();
        Assert.IsTrue(controller.TryBeginDrag(DesktopHitTarget.Dango));

        var captureMustBeReleased = controller.CancelDrag(reason);

        Assert.IsTrue(captureMustBeReleased);
        Assert.IsFalse(controller.IsDragging);
        Assert.AreEqual(reason, controller.LastCancellationReason);
    }

    [TestMethod]
    public void RepeatedCancellationIsIdempotent()
    {
        var controller = new DesktopInteractionController();
        Assert.IsTrue(controller.TryBeginDrag(DesktopHitTarget.Dango));

        Assert.IsTrue(controller.CancelDrag(DragCancellationReason.WindowClosing));
        Assert.IsFalse(controller.CancelDrag(DragCancellationReason.WindowClosing));
    }
}
