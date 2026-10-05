using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class LocalRoomHandoffTests
{
    [TestMethod]
    public void OnlyCurrentHealthyTargetCanComplete()
    {
        var gate = new LocalRoomHandoffGate();
        var first = gate.Begin(LocalRoomPresentation.Window);
        Assert.IsFalse(gate.TryComplete(first, LocalRoomPresentation.Window, false));
        Assert.IsFalse(gate.TryComplete(first, LocalRoomPresentation.Wallpaper, true));
        Assert.AreEqual(LocalRoomPresentation.Porch, gate.Current);
        Assert.IsTrue(gate.Fail(first));
        var second = gate.Begin(LocalRoomPresentation.Wallpaper);
        Assert.IsFalse(gate.TryComplete(first, LocalRoomPresentation.Window, true));
        Assert.IsFalse(gate.Fail(first));
        Assert.IsTrue(gate.TryComplete(second, LocalRoomPresentation.Wallpaper, true));
        Assert.IsFalse(gate.TryComplete(second, LocalRoomPresentation.Wallpaper, true));
        Assert.AreEqual(LocalRoomPresentation.Wallpaper, gate.Current);
    }

    [TestMethod]
    public void CancelAndHideInvalidateEveryOldCallback()
    {
        var gate = new LocalRoomHandoffGate();
        var token = gate.Begin(LocalRoomPresentation.Window);
        Assert.ThrowsExactly<InvalidOperationException>(() => gate.Begin(LocalRoomPresentation.Window));
        gate.Hide();
        Assert.IsFalse(gate.TryComplete(token, LocalRoomPresentation.Window, true));
        Assert.IsFalse(gate.Pending);
        Assert.AreEqual(LocalRoomPresentation.Hidden, gate.Current);
    }
}
