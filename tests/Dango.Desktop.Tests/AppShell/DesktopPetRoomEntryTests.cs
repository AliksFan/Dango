using System.Reflection;
using Dango.Desktop.AppShell;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class DesktopPetRoomEntryTests
{
    [TestMethod]
    [DataRow("--home-ui-review")]
    [DataRow("--settings-ui-review")]
    [DataRow("--physics-host-review")]
    [DataRow("--eye-hat-review")]
    [DataRow("--unified-live-home-review")]
    [DataRow("--porch-scene-review")]
    [DataRow("--local-room-review")]
    [DataRow("--face-review")]
    public void DesktopPetReviewCannotShareAStartupWithAnotherReview(string other)
    {
        App.ValidateIsolatedReviewSelection(["--desktop-pet-room-review"]);
        Assert.ThrowsExactly<ArgumentException>(() => App.ValidateIsolatedReviewSelection(["--desktop-pet-room-review", other]));
        if (other is not ("--home-ui-review" or "--settings-ui-review"))
            Assert.AreNotEqual(App.ResolveSingleInstanceName(["--desktop-pet-room-review"]), App.ResolveSingleInstanceName([other]));
        Assert.AreNotEqual(App.ResolveSingleInstanceName(["--desktop-pet-room-review"]), App.ResolveSingleInstanceName([]));
    }

    [STATestMethod]
    public void DesktopPetReviewConstructorRejectsOtherReviewModesBeforeCreatingAHost()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new DangoApplicationHost(isolatedDesktopPetRoomReview: true, isolatedLocalRoomReview: true));
        Assert.ThrowsExactly<ArgumentException>(() => new DangoApplicationHost(isolatedDesktopPetRoomReview: true, isolatedFaceReview: true));
        Assert.ThrowsExactly<ArgumentException>(() => new DangoApplicationHost(isolatedDesktopPetRoomReview: true, isolatedPhysicsReview: true));
        Assert.ThrowsExactly<ArgumentException>(() => new DangoApplicationHost(isolatedDesktopPetRoomReview: true, isolatedEyeHatReview: true));
        Assert.ThrowsExactly<ArgumentException>(() => new DangoApplicationHost(isolatedDesktopPetRoomReview: true, isolatedUnifiedHomeReview: true));
        Assert.ThrowsExactly<ArgumentException>(() => new DangoApplicationHost(isolatedDesktopPetRoomReview: true, isolatedPorchReview: true));
    }

    [STATestMethod]
    public void DefaultReviewSettingsHaveTheirOwnDirectoryAndNeverSaveDailySettingsDuringThisCheck()
    {
        var host = new DangoApplicationHost(isolatedDesktopPetRoomReview: true);
        // The fixture must not save any default path, including if the assertion finds a bad one.
        typeof(DangoApplicationHost).GetField("exitSettingsHandled", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, true);
        try
        {
            var actual = DesktopPetRoomFixture.Field<string>(host, "settingsPath");
            Assert.AreEqual(Path.Combine(AppContext.BaseDirectory, "desktop-pet-room-review-state", "settings.json"), actual);
            var daily = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango", "settings.json");
            Assert.AreNotEqual(daily, actual);
            Assert.AreNotEqual(Path.Combine(AppContext.BaseDirectory, "local-room-review-state", "settings.json"), actual);
            Assert.AreNotEqual(Path.Combine(AppContext.BaseDirectory, "face-review-state", "settings.json"), actual);
            var runtime = DesktopPetRoomFixture.Field<Dango.Desktop.Rendering.Visuals.StackedDangoSceneRuntime>(host, "stackedRuntime");
            Assert.IsFalse(runtime.IsRunning);
            Assert.IsNull(runtime.FrameDriver);
        }
        finally { host.Dispose(); }
    }
}
