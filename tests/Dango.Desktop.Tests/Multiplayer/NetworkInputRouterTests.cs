using Dango.Desktop.Multiplayer;

namespace Dango.Desktop.Tests.Multiplayer;

[TestClass]
public sealed class NetworkInputRouterTests
{
    [TestMethod]
    public void LocalFocusCoalescesKeyboardRepeatsLimitsRenewalsAndStopSupersedesMove()
    {
        var sent = new List<string>();
        var router = new NetworkInputRouter(sent.Add, () => sent.Add("wave"), () => true);
        router.MoveLeft(); router.Wave(); Assert.AreEqual(0, sent.Count);
        router.SetActive(true); router.MoveLeft(); router.MoveLeft();
        router.Tick(TimeSpan.FromMilliseconds(249));
        CollectionAssert.AreEqual(new[] { "left" }, sent);
        router.Tick(TimeSpan.FromMilliseconds(250)); router.MoveRight(); router.Stop();
        router.Tick(TimeSpan.FromMilliseconds(500)); router.Wave();
        CollectionAssert.AreEqual(new[] { "left", "left", "right", "stopped", "wave" }, sent);
        Assert.IsNull(router.HeldDirection);
    }

    [TestMethod]
    public void FocusLossHiddenDisconnectAndResumeNeverReplayOldIntent()
    {
        var sent = new List<string>(); var connected = true;
        var router = new NetworkInputRouter(sent.Add, () => sent.Add("wave"), () => connected);
        router.SetActive(true); router.MoveLeft(); router.SetActive(false);
        router.Tick(TimeSpan.FromMilliseconds(250)); router.MoveRight();
        CollectionAssert.AreEqual(new[] { "left", "stopped" }, sent);
        router.SetActive(true); router.Tick(TimeSpan.FromMilliseconds(500));
        Assert.AreEqual(2, sent.Count);
        router.MoveRight(); connected = false; router.Tick(TimeSpan.FromMilliseconds(750));
        connected = true; router.Tick(TimeSpan.FromMilliseconds(1000));
        Assert.AreEqual(3, sent.Count); Assert.IsNull(router.HeldDirection);
        router.MoveLeft(); router.Tick(TimeSpan.FromSeconds(5));
        Assert.AreEqual("stopped", sent[^1]); Assert.IsNull(router.HeldDirection);
        router.Tick(TimeSpan.FromSeconds(5.5)); Assert.AreEqual(5, sent.Count);
    }

    [TestMethod]
    public void StopTransportFailureStillClearsFocusAndNeverRestoresHeldInput()
    {
        var sent = new List<string>();
        var router = new NetworkInputRouter(direction =>
        {
            if (direction == "stopped") throw new InvalidOperationException("Connection closed.");
            sent.Add(direction);
        }, () => sent.Add("wave"), () => true);
        router.SetActive(true); router.MoveLeft(); router.SetActive(false);
        Assert.IsFalse(router.IsActive); Assert.IsNull(router.HeldDirection);
        router.MoveRight(); router.Tick(TimeSpan.FromMilliseconds(500));
        CollectionAssert.AreEqual(new[] { "left" }, sent);
    }
}
