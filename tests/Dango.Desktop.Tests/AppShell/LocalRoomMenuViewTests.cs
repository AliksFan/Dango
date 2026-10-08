using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Dango.Desktop.AppShell.Rooms;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using Grid = System.Windows.Controls.Grid;
using TextBlock = System.Windows.Controls.TextBlock;
using UserControl = System.Windows.Controls.UserControl;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class LocalRoomMenuViewTests
{
    [STATestMethod]
    public void MenuKeepsOnlyTheThreeActionsAndCanTakeInitialFocusItself()
    {
        var menu = new LocalRoomMenuView();
        Assert.IsInstanceOfType<UserControl>(menu);
        Assert.IsTrue(menu.Focusable, "The shared menu itself must accept initial focus without focusing an action button.");
        var buttons = Descendants<Button>(menu).ToArray();
        CollectionAssert.AreEquivalent(new[]
        {
            "RoomReturnButton", "RoomControlCenterButton",
            "RoomRecoverButton",
        }, buttons.Select(button => button.Name).ToArray());
        foreach (var removed in new[]
        {
            "RoomContinueButton", "RoomStandardViewButton", "RoomNearViewButton", "RoomWholeViewButton",
        })
            Assert.IsFalse(Descendants<FrameworkElement>(menu).Any(element => element.Name == removed),
                "The shared menu must remove the old continuation and view-preset controls: " + removed);
    }

    [STATestMethod]
    public void EachMenuActionDispatchesItsOwnExistingCommandExactlyOnce()
    {
        var menu = new LocalRoomMenuView();
        var dispatched = new List<string>();
        menu.ReturnRequested += () => dispatched.Add("return");
        menu.ControlCenterRequested += () => dispatched.Add("control-center");
        menu.RecoverRequested += () => dispatched.Add("recover");
        foreach (var (name, expected) in new[]
        {
            ("RoomReturnButton", "return"),
            ("RoomControlCenterButton", "control-center"),
            ("RoomRecoverButton", "recover"),
        })
        {
            dispatched.Clear();
            Find<Button>(menu, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            CollectionAssert.AreEqual(new[] { expected }, dispatched.ToArray(),
                "A menu action must dispatch its actual command without also requesting a different action.");
        }
    }

    [STATestMethod]
    public void SharedMenuHasACentredCardOverATranslucentShadeAndCompleteGameplayInstructions()
    {
        var menu = new LocalRoomMenuView();
        var overlay = Find<Grid>(menu, "RoomMenuOverlay");
        var card = Find<Border>(menu, "RoomMenuCard");
        Assert.IsTrue(overlay.Background is SolidColorBrush { Color.A: > 0 and < 255 },
            "The room menu must shade rather than replace the active room scene behind it.");
        Assert.AreEqual(System.Windows.HorizontalAlignment.Center, card.HorizontalAlignment);
        Assert.AreEqual(VerticalAlignment.Center, card.VerticalAlignment);
        var instructions = Find<TextBlock>(menu, "RoomInstructions").Text;
        foreach (var key in new[] { "A", "D", "S" })
            Assert.IsTrue(Regex.IsMatch(instructions, @"(?<![A-Za-z])" + key + @"(?![A-Za-z])"),
                "The actual instruction text must describe gameplay key " + key + ".");
        AssertMentions(instructions, "space jump", "空格", "Space");
        AssertMentions(instructions, "wheel zoom", "滚轮", "wheel");
        AssertMentions(instructions, "Shift petting modifier", "Shift");
        AssertMentions(instructions, "left-button petting", "左键", "左鼠标", "left");
        AssertMentions(instructions, "horizontal movement", "左右", "横向", "横移", "水平", "horizontal");
        AssertMentions(instructions, "Esc menu dismissal", "Esc");
    }

    [STATestMethod]
    public void StatusRefreshDescribesTheCurrentSceneWithoutChangingItsCamera()
    {
        var menu = new LocalRoomMenuView();
        var scene = new LocalRoomSceneView();
        scene.Camera.Resize(900, 600, LocalRoomSceneView.WorldHeight, LocalRoomSceneView.SafeActivityHeight);
        scene.Camera.SelectPreset(RoomViewPreset.Near);
        var nearScale = scene.Camera.Scale;
        menu.RefreshStatus(scene);
        var label = Find<TextBlock>(menu, "RoomStatusLabel");
        AssertMentions(label.Text, "ordinary room status", "普通房");
        AssertMentions(label.Text, "local single-player scope", "本地单人");
        StringAssert.Contains(label.Text, scene.Camera.ZoomPercent.ToString("0", CultureInfo.CurrentCulture) + "%");
        Assert.AreEqual(RoomViewPreset.Near, scene.Camera.Preset);
        Assert.AreEqual(nearScale, scene.Camera.Scale, 1e-9, "A status refresh must not change the room view.");
        var before = label.Text;
        scene.Camera.SelectPreset(RoomViewPreset.Whole);
        var wholeScale = scene.Camera.Scale;
        menu.RefreshStatus(scene);
        StringAssert.Contains(label.Text, scene.Camera.ZoomPercent.ToString("0", CultureInfo.CurrentCulture) + "%");
        Assert.AreNotEqual(before, label.Text, "The status must refresh when the actual room view changes.");
        Assert.AreEqual(RoomViewPreset.Whole, scene.Camera.Preset);
        Assert.AreEqual(wholeScale, scene.Camera.Scale, 1e-9);
    }

    private static void AssertMentions(string text, string meaning, params string[] alternatives) =>
        Assert.IsTrue(alternatives.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)),
            "The visible menu instructions/status must include " + meaning + ".");

    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants<T>(root).Single(element => element.Name == name);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
    }
}
