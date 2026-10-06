namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class DesktopPetRoomReviewEntryTests
{
    [TestMethod]
    public void DesktopPetReviewUsesAnIndependentInstanceAndRejectsMixedReviewModes()
    {
        var review = App.ResolveSingleInstanceName(["--desktop-pet-room-review"]);
        Assert.AreNotEqual(App.ResolveSingleInstanceName([]), review);
        Assert.AreNotEqual(App.ResolveSingleInstanceName(["--local-room-review"]), review);
        Assert.AreNotEqual(App.ResolveSingleInstanceName(["--face-review"]), review);
        Assert.ThrowsExactly<ArgumentException>(() => App.ValidateIsolatedReviewSelection(
            ["--desktop-pet-room-review", "--local-room-review"]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ValidateIsolatedReviewSelection(
            ["--desktop-pet-room-review", "--face-review"]));
        App.ValidateIsolatedReviewSelection(["--desktop-pet-room-review"]);
    }
}
