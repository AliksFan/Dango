using System.Windows.Controls;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class DesktopPetRoomSettingsTests
{
    [TestMethod]
    public void RoomModeLockRejectsWindowBeforeTouchingEitherHostAndReleasesAfterReturn()
    {
        var wallpaper = new FakeHost("Wallpaper");
        var window = new FakeHost("Window");
        var coordinator = new DesktopHostCoordinator(wallpaper, window);
        coordinator.SetRoomDisplayModeLock(DesktopDisplayMode.Wallpaper);
        var commands = new ApplicationCommandController(coordinator, ApplicationUiMode.Product);
        var result = commands.SetHostMode(DesktopDisplayMode.Window);
        Assert.AreEqual(CommandApplicationState.Failed, result.State);
        Assert.AreEqual("Wallpaper", coordinator.GetSnapshot().Mode);
        Assert.AreEqual(0, wallpaper.Changes);
        Assert.AreEqual(0, window.Changes);
        coordinator.SetRoomDisplayModeLock(null);
        coordinator.SetMode(DesktopDisplayMode.Window);
        Assert.AreEqual("Window", coordinator.GetSnapshot().Mode);
    }

    [STATestMethod]
    public void SettingsKeepVisibilityAndInteractionButDoNotOfferWindowInsideDesktopPetRoom()
    {
        var home = new ProductHomeView();
        var state = new ProductSettingsState(true, true, true, "Wallpaper",
            KeyboardControlSettings.CreateDefault(), DesktopObjectLayer.BelowDesktopIcons, null, false, false, "");
        home.UpdateSettings(state);
        home.SetDesktopPetRoomContext(true);
        Assert.IsFalse(((Button)home.FindName("WindowModeButton")).IsEnabled);
        Assert.IsTrue(((Button)home.FindName("DisplayToggleButton")).IsEnabled);
        Assert.IsTrue(((Button)home.FindName("InteractionToggleButton")).IsEnabled);
        home.UpdateSettings(state);
        Assert.IsFalse(((Button)home.FindName("WindowModeButton")).IsEnabled, "A diagnostics refresh must preserve the room restriction.");
        home.SetDesktopPetRoomContext(false);
        Assert.IsTrue(((Button)home.FindName("WindowModeButton")).IsEnabled);
    }

    [STATestMethod]
    public void DesktopPetLayerControlsShowActualBelowIconsWithoutOverwritingSavedPreference()
    {
        var home = new ProductHomeView();
        var state = new ProductSettingsState(true, true, true, "Wallpaper",
            KeyboardControlSettings.CreateDefault(), DesktopObjectLayer.AboveDesktopIcons, null, false, false, "");
        home.UpdateSettings(state);
        var below = (Button)home.FindName("BelowIconsButton");
        var above = (Button)home.FindName("AboveIconsButton");
        Assert.IsTrue(above.IsEnabled);
        Assert.AreEqual("Selected", above.Tag);
        home.SetDesktopPetRoomContext(true);
        Assert.IsFalse(above.IsEnabled, "This room cannot offer an unsupported above-icons presentation.");
        Assert.IsFalse(below.IsEnabled);
        Assert.AreEqual("Selected", below.Tag);
        Assert.IsNull(above.Tag);
        home.UpdateSettings(state);
        Assert.IsFalse(above.IsEnabled);
        Assert.AreEqual("Selected", below.Tag);
        home.SetDesktopPetRoomContext(false);
        Assert.IsTrue(above.IsEnabled);
        Assert.AreEqual("Selected", above.Tag, "The original preference still belongs to the other host contexts.");
        Assert.AreEqual(DesktopObjectLayer.AboveDesktopIcons, state.DangoLayer);
    }

    private sealed class FakeHost(string mode) : IDesktopHostControl
    {
        private DesktopHostSnapshot snapshot = new(mode, false, true, "fixture", null);
        internal int Changes { get; private set; }
        public DesktopHostSnapshot GetSnapshot() => snapshot;
        public void SetVisibility(bool visible) { Changes++; snapshot = snapshot with { IsVisible = visible }; }
        public void SetInteractionEnabled(bool enabled) { Changes++; snapshot = snapshot with { IsInteractionEnabled = enabled }; }
        public string CreateDiagnosticSummary() => mode;
    }
}
