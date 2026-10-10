using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Protocol;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class RoomPanelViewTests
{
    [STATestMethod]
    public void DirectoryOnlyArrangesSceneDoorsAndRetainsPagingControls()
    {
        var view = new RoomPanelView(); var requests = Observe(view);
        var first = Room("publicShared"); var second = Room("privateShared") with { Name = first.Name };
        view.Apply(new(RoomPanelPage.Directory, Rooms: [first, second]));
        Assert.IsNull(view.FindName("RoomNextPage"), "No next cursor means no invented page button.");
        Assert.IsNull(view.FindName("RoomOpenCreate"), "The build sign, rather than a generic management menu, opens creation.");
        Assert.IsNull(view.FindName("RoomSelectRoom_" + second.RoomId.ToString("N")), "Only actual scene doors select rooms.");
        Assert.IsNull(view.FindName("RoomOpenAccount"));
        Assert.IsNull(view.FindName("RoomOpenOnline"));
        view.Apply(new(RoomPanelPage.Directory, "dissolved", [second with { Status = "dissolved" }], Guid.NewGuid()));
        Assert.IsNotNull(view.FindName("RoomNextPage"));
        Assert.IsNull(view.FindName("RoomOpenCreate"));
        Assert.IsNull(view.FindName("RoomSelectRoom_" + first.RoomId.ToString("N")));
        Element<Button>(view, "RoomNextPage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(RoomPanelAction.NextPage, requests.Single().Action);
        Assert.IsFalse(Buttons(view).Any(b => AutomationProperties.GetName(b).Contains("进入")), "Management must not fake admission.");
    }

    [STATestMethod]
    public void CreateDefaultsAndDraftSurviveBusyErrorsPendingAndConflict()
    {
        var view = new RoomPanelView();
        view.Apply(new(RoomPanelPage.Create));
        Assert.AreEqual("6", view.CapacityText); Assert.AreEqual("privateShared", view.Kind);
        Element<ComboBox>(view, "RoomKind").SelectedIndex = -1;
        Assert.AreEqual(string.Empty, view.Kind, "A missing selection cannot silently become a private room.");
        Element<ComboBox>(view, "RoomKind").SelectedIndex = 0;
        Element<TextBox>(view, "RoomName").Text = " 虚构草稿 ";
        Element<TextBox>(view, "RoomCapacity").Text = "9";
        Element<ComboBox>(view, "RoomKind").SelectedIndex = 1;
        view.Apply(new(RoomPanelPage.Create, Message: "虚构请求处理中", Busy: true));
        Assert.AreEqual("虚构草稿", view.RoomName); Assert.AreEqual("9", view.CapacityText); Assert.AreEqual("publicShared", view.Kind);
        Assert.IsFalse(Element<TextBox>(view, "RoomName").IsEnabled);
        Assert.IsFalse(Element<ComboBox>(view, "RoomKind").IsEnabled);
        view.Apply(new(RoomPanelPage.Create, Message: "容量冲突，原输入保留"));
        Assert.AreEqual("9", view.CapacityText);
        view.Apply(new(RoomPanelPage.Pending));
        view.Apply(new(RoomPanelPage.Conflict));
        view.Apply(new(RoomPanelPage.Create));
        Assert.AreEqual("虚构草稿", view.RoomName); Assert.AreEqual("9", view.CapacityText); Assert.AreEqual("publicShared", view.Kind);
        view.Apply(new(RoomPanelPage.Directory, Rooms: []));
        view.Apply(new(RoomPanelPage.Create));
        Assert.AreEqual(string.Empty, view.RoomName); Assert.AreEqual("6", view.CapacityText); Assert.AreEqual("privateShared", view.Kind);
    }

    [STATestMethod]
    public void DetailToEditPrefillsAndCannotChangeTypeOrDesktopCapacity()
    {
        var view = new RoomPanelView(); var room = Room("publicShared");
        view.Apply(new(RoomPanelPage.Detail, Selected: room));
        view.Apply(new(RoomPanelPage.Edit, Selected: room));
        Assert.AreEqual(room.Name, view.RoomName); Assert.AreEqual("6", view.CapacityText); Assert.AreEqual("publicShared", view.Kind);
        Assert.IsFalse(Element<ComboBox>(view, "RoomKind").IsEnabled);
        Element<TextBox>(view, "RoomName").Text = "保留的新名称";
        view.Apply(new(RoomPanelPage.Edit, Selected: room with { Name = "服务器新名称", Version = 2 }, Message: "版本已变化"));
        Assert.AreEqual("保留的新名称", view.RoomName);
        view.Apply(new(RoomPanelPage.Conflict, Selected: room));
        view.Apply(new(RoomPanelPage.Edit, Selected: room));
        Assert.AreEqual("保留的新名称", view.RoomName);

        var desktop = Room("desktopPet") with { Capacity = 2 };
        view.Apply(new(RoomPanelPage.Detail, Selected: desktop));
        Assert.IsNull(view.FindName("RoomOpenDissolve_" + desktop.RoomId.ToString("N")));
        view.Apply(new(RoomPanelPage.Edit, Selected: desktop));
        Assert.AreEqual("desktopPet", view.Kind); Assert.AreEqual("2", view.CapacityText);
        Assert.IsFalse(Element<TextBox>(view, "RoomCapacity").IsEnabled);
        Assert.IsTrue(Element<TextBox>(view, "RoomName").IsEnabled);
    }

    [STATestMethod]
    public void DissolveOnlyRequestsAfterTheDedicatedConfirmationAndDesktopRoomIsProtected()
    {
        var view = new RoomPanelView(); var requests = Observe(view); var room = Room("publicShared");
        view.Apply(new(RoomPanelPage.Detail, Selected: room));
        Click(view, "RoomOpenDissolve_" + room.RoomId.ToString("N"));
        Assert.AreEqual(new RoomPanelRequest(RoomPanelAction.OpenDissolve, room.RoomId), requests.Single());
        view.Apply(new(RoomPanelPage.ConfirmDissolve, Selected: room));
        Assert.AreEqual(1, requests.Count, "Showing a confirmation cannot issue dissolution.");
        var text = VisibleText(view);
        StringAssert.Contains(text, "旧公共访问码立即失效"); StringAssert.Contains(text, "7 天");
        StringAssert.Contains(text, "不会把团子迁回"); StringAssert.Contains(text, "不会复活旧访问码");
        Click(view, "RoomPrimary");
        Assert.AreEqual(new RoomPanelRequest(RoomPanelAction.Dissolve, room.RoomId), requests.Last());
        view.Apply(new(RoomPanelPage.ConfirmDissolve, Selected: Room("desktopPet") with { Capacity = 2 }));
        Assert.IsFalse(Buttons(view).Any(b => AutomationProperties.GetName(b) == "确认解散房间"));
        Click(view, "RoomPrimary"); Assert.AreEqual(2, requests.Count);
    }

    [STATestMethod]
    public void RestoreIsAvailableOnlyForRecoverableDissolvedSharedRooms()
    {
        var view = new RoomPanelView(); var requests = Observe(view);
        var room = Room("privateShared") with { Status = "dissolved", Recoverable = true, RecoverUntil = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero) };
        view.Apply(new(RoomPanelPage.Detail, "dissolved", Selected: room));
        StringAssert.Contains(VisibleText(view), "服务器会重新检查 7 天期限与当前额度");
        Click(view, "RoomPrimary"); Assert.AreEqual(new RoomPanelRequest(RoomPanelAction.Restore, room.RoomId), requests.Single());
        view.Apply(new(RoomPanelPage.Detail, "dissolved", Selected: room with { Recoverable = false }));
        Assert.IsFalse(Buttons(view).Any(b => AutomationProperties.GetName(b) == "恢复房间"));
        Click(view, "RoomPrimary"); Assert.AreEqual(1, requests.Count);
        view.Apply(new(RoomPanelPage.Detail, Selected: room with { Status = "active" }));
        Assert.IsFalse(Buttons(view).Any(b => AutomationProperties.GetName(b) == "恢复房间"));
    }

    [STATestMethod]
    public void PendingAndConflictOfferOnlyTheirExplicitRecoveryActions()
    {
        var view = new RoomPanelView(); var requests = Observe(view);
        view.Apply(new(RoomPanelPage.Pending));
        CollectionAssert.AreEquivalent(new[] { "关闭房间管理", "重试保留的请求" }, Buttons(view).Select(AutomationProperties.GetName).ToArray());
        Click(view, "RoomPrimary"); Assert.AreEqual(RoomPanelAction.RetryPending, requests.Last().Action);
        view.Apply(new(RoomPanelPage.Conflict));
        CollectionAssert.AreEquivalent(new[] { "关闭房间管理", "读取最新状态", "返回" }, Buttons(view).Select(AutomationProperties.GetName).ToArray());
        Click(view, "RoomPrimary"); Assert.AreEqual(RoomPanelAction.RefreshConflict, requests.Last().Action);
    }

    [STATestMethod]
    [DataRow(RoomPanelPage.Directory)]
    [DataRow(RoomPanelPage.Create)]
    [DataRow(RoomPanelPage.Detail)]
    [DataRow(RoomPanelPage.Edit)]
    [DataRow(RoomPanelPage.ConfirmDissolve)]
    [DataRow(RoomPanelPage.Pending)]
    [DataRow(RoomPanelPage.Conflict)]
    public void BusyDisablesEveryActionExceptCloseAndNarrowContentScrollsWithoutHorizontalOverflow(RoomPanelPage page)
    {
        var view = new RoomPanelView(); var requests = Observe(view); var room = Room("publicShared");
        view.Apply(new(page, Rooms: [room], Next: Guid.NewGuid(), Selected: room, Message: "虚构忙碌状态：服务器仍需验证权限和版本。", Busy: true));
        var size = new Size(336, 280); view.Measure(size); view.Arrange(new Rect(size)); view.UpdateLayout();
        Assert.AreEqual(560, view.MaxWidth);
        Assert.AreEqual(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(view));
        var scroll = Element<ScrollViewer>(view, "RoomScroll");
        Assert.AreEqual(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        Assert.AreEqual(0, scroll.ScrollableWidth, .1);
        foreach (var button in Buttons(view))
        {
            Assert.AreEqual(button.Name == "RoomClose", button.IsEnabled);
            var bounds = button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize));
            Assert.IsTrue(bounds.Left >= -1 && bounds.Right <= size.Width + 1, $"Button overflows: {bounds}.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        CollectionAssert.AreEqual(new[] { new RoomPanelRequest(RoomPanelAction.Close) }, requests);
        if (page is RoomPanelPage.Directory or RoomPanelPage.Create or RoomPanelPage.ConfirmDissolve)
            Assert.IsTrue(scroll.ScrollableHeight > 0, "The constrained viewport must scroll long content.");
    }

    [STATestMethod]
    public void EscapeClosesEvenWhenBusyAndGameKeysDoNotBubblePastThePanel()
    {
        var view = new RoomPanelView(); var requests = Observe(view);
        view.Apply(new(RoomPanelPage.Create, Busy: true));
        var escape = KeyEvent(Key.Escape, Keyboard.PreviewKeyDownEvent);
        view.RaiseEvent(escape);
        Assert.IsTrue(escape.Handled); Assert.AreEqual(RoomPanelAction.Close, requests.Single().Action);
        foreach (var key in new[] { Key.W, Key.A, Key.D, Key.S, Key.Space })
        {
            var down = KeyEvent(key, Keyboard.KeyDownEvent); view.RaiseEvent(down); Assert.IsTrue(down.Handled);
            var up = KeyEvent(key, Keyboard.KeyUpEvent); view.RaiseEvent(up); Assert.IsTrue(up.Handled);
        }
        Assert.AreEqual(1, requests.Count, "Game keys must not send room operations.");
    }

    private static System.Windows.Input.KeyEventArgs KeyEvent(Key key, RoutedEvent routed) => new(Keyboard.PrimaryDevice, new TestSource(), 0, key) { RoutedEvent = routed };
    private static RoomManagementView Room(string kind) => new(Guid.NewGuid(), kind, "虚构房间", 6, "active", 1, 1, 1, null, false, "shared-default", 1);
    private static List<RoomPanelRequest> Observe(RoomPanelView view) { var requests = new List<RoomPanelRequest>(); view.Requested += requests.Add; return requests; }
    private static T Element<T>(RoomPanelView view, string fieldName) where T : class => view.FindName(fieldName) as T ?? throw new AssertFailedException($"Missing {fieldName}.");
    private static void Click(RoomPanelView view, string fieldName) => Element<Button>(view, fieldName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string VisibleText(RoomPanelView view) => string.Join("\n", Descendants(view).OfType<TextBlock>().Select(t => t.Text));
    private static IEnumerable<Button> Buttons(DependencyObject root) => Descendants(root).OfType<Button>();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var descendant in Descendants(child)) yield return descendant;
    }
    private sealed class TestSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
