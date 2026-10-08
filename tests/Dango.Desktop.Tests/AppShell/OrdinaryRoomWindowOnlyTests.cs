using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Tray;
using Forms = System.Windows.Forms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class OrdinaryRoomWindowOnlyTests
{
    [STATestMethod]
    public void ActualOrdinaryRoomMenuOffersGameplayAndRecoveryWithoutPresentationSwitches()
    {
        var menu = new LocalRoomMenuView();
        var controls = Descendants<FrameworkElement>(menu).ToArray();
        var buttons = controls.OfType<Button>().ToArray();
        CollectionAssert.AreEquivalent(new[] { "RoomReturnButton", "RoomControlCenterButton", "RoomRecoverButton" },
            buttons.Select(button => button.Name).ToArray(),
            "The room menu must expose the remaining real actions without a window/wallpaper selector.");
        Assert.IsFalse(controls.OfType<TextBlock>().Any(label => label.Text == "显示方式"),
            "A window-only room must not leave an empty or misleading presentation section.");
        Assert.IsFalse(buttons.Any(button => button.Content?.ToString() is "窗口" or "壁纸"));
        var cameraSettings = controls.Single(element => element.Name == "RoomCameraSettings");
        Assert.AreEqual(Visibility.Visible, cameraSettings.Visibility,
            "Removing wallpaper must preserve the ordinary room's camera controls.");
        var instructions = controls.OfType<TextBlock>().Single(label => label.Name == "RoomInstructions").Text;
        foreach (var instruction in new[] { "A / D", "滚轮", "Shift", "Esc", "右键" })
            StringAssert.Contains(instructions, instruction);
    }

    [STATestMethod]
    public void ActualOrdinaryRoomTrayKeepsRecoveryViewsAndReturnWithoutPresentationSwitches()
    {
        var actions = new LocalRoomTrayActions(() => true, () => true,
            () => { }, () => { }, () => { }, _ => { }, () => { }, () => false);
        using var tray = new DangoTrayIcon(new ApplicationCommandController(new InertDesktopHost()),
            () => { }, () => { }, () => { }, actions);
        var notifyIcon = (Forms.NotifyIcon)typeof(DangoTrayIcon)
            .GetField("notifyIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tray)!;
        var root = notifyIcon.ContextMenuStrip!;
        var ordinaryRoom = root.Items.OfType<Forms.ToolStripMenuItem>()
            .Single(item => item.Text == "普通房 · 本地单人");
        var items = Descendants(ordinaryRoom).ToArray();
        Assert.IsFalse(items.Any(item => item.Text is "显示方式" or "窗口" or "壁纸"),
            "The actual tray must not retain a route back to the removed ordinary-room wallpaper host.");
        CollectionAssert.AreEquivalent(new[]
        {
            "恢复房间", "隐藏房间", "恢复团子并重试", "房门 · 返回门廊", "视角",
        }, ordinaryRoom.DropDownItems.OfType<Forms.ToolStripMenuItem>().Select(item => item.Text).ToArray());
        CollectionAssert.AreEquivalent(new[] { "标准", "近景", "全景" },
            items.Single(item => item.Text == "视角").DropDownItems.OfType<Forms.ToolStripMenuItem>()
                .Select(item => item.Text).ToArray());
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
    }

    private static IEnumerable<Forms.ToolStripMenuItem> Descendants(Forms.ToolStripMenuItem root)
    {
        foreach (var child in root.DropDownItems.OfType<Forms.ToolStripMenuItem>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class InertDesktopHost : IDesktopHostControl
    {
        public DesktopHostSnapshot GetSnapshot() => new("Window", true, true, "test fixture", null);
        public void SetVisibility(bool isVisible) { }
        public void SetInteractionEnabled(bool isEnabled) { }
        public string CreateDiagnosticSummary() => "test fixture";
    }
}
