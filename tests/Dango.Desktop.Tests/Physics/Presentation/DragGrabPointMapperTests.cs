using Dango.Desktop.Physics.Presentation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.Physics.Presentation;

[TestClass]
public sealed class DragGrabPointMapperTests
{
    [TestMethod]
    public void VisualClickMapsBackToTheRenderedLocalPhysicsPoint()
    {
        var center = DragGrabPointMapper.FromVisualPoint(80, 66, 160, 120);
        var upperLeft = DragGrabPointMapper.FromVisualPoint(44, 18, 160, 120);

        Assert.AreEqual(0, center.X, 0.0001);
        Assert.AreEqual(0, center.Y, 0.0001);
        Assert.AreEqual(-0.5, upperLeft.X, 0.0001);
        Assert.AreEqual(0.827586, upperLeft.Y, 0.0001);
    }
}
