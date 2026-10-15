using System.Windows;
using System.Windows.Controls;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class SettingsRefreshContractTests
{
    [STATestMethod]
    public void ProductSettingsRemoveLegacyHostControlsAndKeepSceneSpecificHelp()
    {
        var home = new ProductHomeView();
        home.ShowSettingsPage();
        foreach (var name in new[] { "DisplayToggleButton", "InteractionToggleButton", "KeyboardToggleButton",
                     "WallpaperModeButton", "WindowModeButton", "BelowIconsButton", "AboveIconsButton",
                     "RetryWallpaperSettingsButton", "RestoreDangoSettingsButton" })
            Assert.IsNull(home.FindName(name), $"过时的产品设置入口仍存在：{name}");
        Assert.IsNotNull(home.FindName("HomeOperationHelp"));
        Assert.IsNotNull(home.FindName("RoomOperationHelp"));
        Assert.IsNotNull(home.FindName("DesktopPetOperationHelp"));
    }

    [STATestMethod]
    public void SettingsReturnAndExitAreOutsideTheScrollingContent()
    {
        var home = new ProductHomeView();
        var scroll = (DependencyObject)home.FindName("SettingsPage");
        foreach (var name in new[] { "BackFromSettingsButton", "SettingsExitButton", "RetrySaveSettingsButton", "RecoverSettingsButton" })
        {
            var element = (DependencyObject)home.FindName(name);
            Assert.IsNotNull(element);
            for (var parent = LogicalTreeHelper.GetParent(element); parent is not null; parent = LogicalTreeHelper.GetParent(parent))
                Assert.AreNotSame(scroll, parent, $"{name} 不应随设置内容滚走。");
        }
    }
}
