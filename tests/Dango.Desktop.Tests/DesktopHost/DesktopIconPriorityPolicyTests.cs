using Dango.Desktop.DesktopHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost;

[TestClass]
public sealed class DesktopIconPriorityPolicyTests
{
    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(true, false, false)]
    [DataRow(false, false, true)]
    public void IconHitOrUnknownResultPassesInputToDesktop(
        bool querySucceeded,
        bool iconHit,
        bool expectedDesktopPriority)
    {
        Assert.AreEqual(
            expectedDesktopPriority,
            DesktopIconPriorityPolicy.ShouldPassToDesktop(querySucceeded, iconHit));
    }

    [TestMethod]
    [DataRow(false, true, true, false, false)]
    [DataRow(true, false, false, false, true)]
    [DataRow(true, true, true, false, true)]
    [DataRow(true, true, false, false, false)]
    [DataRow(true, true, true, true, false)]
    public void InputProxyIsVisibleOnlyForKnownEmptyDesktopSpace(
        bool enabled,
        bool cursorWithinBounds,
        bool querySucceeded,
        bool iconHit,
        bool expectedVisible)
    {
        Assert.AreEqual(
            expectedVisible,
            DesktopIconPriorityPolicy.ShouldShowInputProxy(
                enabled,
                cursorWithinBounds,
                querySucceeded,
                iconHit));
    }
}
