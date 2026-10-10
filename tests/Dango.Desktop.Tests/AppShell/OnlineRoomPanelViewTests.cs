using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Protocol;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class OnlineRoomPanelViewTests
{
    [STATestMethod]
    public void AdmissionNeedsAnExplicitSecondIntentAndConfigurationSelection()
    {
        var view = new OnlineRoomPanelView(); var requests = Observe(view); var target = Door();
        view.Apply(new(Target: target));
        Click(view, OnlineRoomAction.OpenEnter); Assert.AreEqual(0, requests.Count);
        view.Apply(new(Target: target, ConfigurationReady: true));
        Click(view, OnlineRoomAction.OpenEnter);
        Assert.AreEqual(new OnlineRoomRequest(OnlineRoomAction.OpenEnter, target.RoomId), requests.Single());
        Assert.IsNull(view.FindName("OnlineRoomEnter"));
        view.Apply(new(OnlineRoomPage.ConfirmEnter, Target: target, ConfigurationReady: true));
        Assert.AreEqual(1, requests.Count, "Rendering a confirmation cannot perform admission.");
        Click(view, OnlineRoomAction.Enter);
        Assert.AreEqual(new OnlineRoomRequest(OnlineRoomAction.Enter, target.RoomId), requests.Last());
        var stale = Field<Button>(view, "OnlineRoomEnter");
        view.Apply(new(OnlineRoomPage.ConfirmEnter, Target: Door(), ConfigurationReady: true));
        stale.RaiseEvent(new(Button.ClickEvent)); Assert.AreEqual(2, requests.Count);
    }

    [STATestMethod]
    public void ObservationCannotLeaveOrResetAnotherOwnersPublicCode()
    {
        var view = new OnlineRoomPanelView(); var requests = Observe(view);
        view.Apply(new(Target: Door(), HasLease: true, Connected: true));
        Assert.IsNull(view.FindName("OnlineRoomOpenLeave"));
        Assert.IsNull(view.FindName("OnlineRoomOpenResetCode"));
        Click(view, OnlineRoomAction.Control); Click(view, OnlineRoomAction.EndLease);
        CollectionAssert.AreEqual(new[] { OnlineRoomAction.Control, OnlineRoomAction.EndLease }, requests.Select(r => r.Action).ToArray());
        view.Apply(new(OnlineRoomPage.ConfirmLeave, HasLease: true, Controller: false));
        Click(view, OnlineRoomAction.Leave); Assert.AreEqual(2, requests.Count);
        view.Apply(new(OnlineRoomPage.ConfirmLeave, HasLease: true, Controller: true));
        Click(view, OnlineRoomAction.Leave); Assert.AreEqual(OnlineRoomAction.Leave, requests.Last().Action);
    }

    [STATestMethod]
    public void ResolvingDoesNotEnterAndTransientSecretsAreClearedOnNavigation()
    {
        var view = new OnlineRoomPanelView(); var requests = Observe(view);
        view.Apply(new(OnlineRoomPage.ResolveCode));
        Field<PasswordBox>(view, "OnlineRoomCodeInput").Password = new string('A', 43);
        Click(view, OnlineRoomAction.Resolve);
        Assert.AreEqual(OnlineRoomAction.Resolve, requests.Single().Action);
        Assert.IsFalse(requests.Any(r => r.Action == OnlineRoomAction.Enter));
        view.Apply(new(OnlineRoomPage.ConfirmEnter, Target: Door())); Assert.AreEqual(string.Empty, view.AccessCode);
        view.Apply(new(OnlineRoomPage.Code, Code: new(Guid.NewGuid(), Guid.NewGuid(), 2, 1, new string('B', 43), DateTimeOffset.UtcNow.AddHours(24))));
        Assert.AreEqual(43, Field<TextBox>(view, "OnlineRoomCodeOutput").Text.Length);
        view.ClearSecrets(); Assert.AreEqual(string.Empty, Field<TextBox>(view, "OnlineRoomCodeOutput").Text);
        view.Apply(new()); Assert.AreEqual(string.Empty, view.AccessCode);
    }

    [STATestMethod]
    public void UnknownResultAllowsOnlyExactRecoveryOrLeavingThePanel()
    {
        var view = new OnlineRoomPanelView(); var requests = Observe(view);
        view.Apply(new(OnlineRoomPage.ConfirmEnter, Target: Door(), ConfigurationReady: true, Pending: true));
        Assert.IsNull(view.FindName("OnlineRoomEnter"));
        Click(view, OnlineRoomAction.RetryPending); Click(view, OnlineRoomAction.Close);
        CollectionAssert.AreEqual(new[] { OnlineRoomAction.RetryPending, OnlineRoomAction.Close }, requests.Select(r => r.Action).ToArray());
    }

    [STATestMethod]
    public void KnownReceiptWithUnconfirmedCurrentStateBlocksNewMutationsButAllowsRead()
    {
        var view = new OnlineRoomPanelView(); var requests = Observe(view);
        view.Apply(new(Target: Door(), ConfigurationReady: true, HasLease: true, Controller: true, RequiresReconciliation: true));
        Click(view, OnlineRoomAction.OpenEnter); Click(view, OnlineRoomAction.OpenLeave); Click(view, OnlineRoomAction.EndLease);
        Assert.AreEqual(0, requests.Count);
        Click(view, OnlineRoomAction.Refresh);
        Assert.AreEqual(OnlineRoomAction.Refresh, requests.Single().Action);
    }

    [STATestMethod]
    [DataRow(OnlineRoomPage.Status)]
    [DataRow(OnlineRoomPage.ConfirmEnter)]
    [DataRow(OnlineRoomPage.ResolveCode)]
    [DataRow(OnlineRoomPage.ConfirmLeave)]
    [DataRow(OnlineRoomPage.ConfirmResetCode)]
    [DataRow(OnlineRoomPage.Code)]
    public void BusyAndNarrowStatesCannotSubmitOrOverflow(OnlineRoomPage page)
    {
        var view = new OnlineRoomPanelView(); var requests = Observe(view);
        view.Apply(new(page, Target: Door(), Busy: true, ConfigurationReady: true, OwnsTarget: true, HasLease: true, Controller: true));
        var size = new Size(336, 280); view.Measure(size); view.Arrange(new Rect(size)); view.UpdateLayout();
        Assert.AreEqual(0, Field<ScrollViewer>(view, "OnlineRoomScroll").ScrollableWidth, .1);
        foreach (var button in Descendants(view).OfType<Button>())
        {
            Assert.AreEqual(button.Name == "OnlineRoomClose", button.IsEnabled);
            button.RaiseEvent(new(Button.ClickEvent));
        }
        CollectionAssert.AreEqual(new[] { new OnlineRoomRequest(OnlineRoomAction.Close) }, requests);
        Assert.IsFalse(requests.Any(r => r.Action is OnlineRoomAction.Leave or OnlineRoomAction.EndLease));
    }

    [STATestMethod]
    [DataRow(1120, 740)]
    [DataRow(820, 620)]
    public void FormalStatusLivesInThePorchAndReturningRestoresLocalInputs(int width, int height)
    {
        var home = new ProductHomeView(); var closed = 0; home.OnlineRoomPanelClosed += () => closed++;
        home.ShowOnlineRoomPanel();
        var size = new Size(width, height); home.Measure(size); home.Arrange(new Rect(size)); home.UpdateLayout();
        Assert.IsTrue(home.IsOnlineRoomPanelOpen); Assert.IsTrue(home.IsAccountPanelOpen);
        Assert.IsTrue(home.IsInPorch);
        Assert.IsTrue(Field<FrameworkElement>(home, "HomeContent").IsEnabled, "The scene contains its operation area and remains enabled.");
        Assert.AreEqual(Visibility.Collapsed, Field<FrameworkElement>(home, "AccountOverlay").Visibility);
        Assert.AreSame(Field<FrameworkElement>(home, "PorchContent"), Field<FrameworkElement>(home, "PorchOperations").Parent);
        var bounds = home.OnlineRoomPanel.TransformToAncestor(home).TransformBounds(new Rect(home.OnlineRoomPanel.RenderSize));
        Assert.IsTrue(bounds.Left >= 0 && bounds.Right <= width);
        Assert.IsFalse(Descendants(home.OnlineRoomPanel).OfType<TextBlock>().Any(t => t.Text.Contains("场景已就绪", StringComparison.Ordinal)));
        home.ShowRoomPanel(); Assert.AreEqual(1, closed); Assert.IsTrue(home.IsRoomPanelOpen);
        home.HideAccountPanel(); Assert.IsFalse(home.IsAccountPanelOpen);
        Assert.IsTrue(Field<FrameworkElement>(home, "HomeContent").IsEnabled);
    }

    private static RoomDoorView Door() => new(Guid.NewGuid(), "publicShared", "虚构测试共同房", 6, 1, "shared-default", 1);
    private static List<OnlineRoomRequest> Observe(OnlineRoomPanelView view)
    { var values = new List<OnlineRoomRequest>(); view.Requested += values.Add; return values; }
    private static void Click(OnlineRoomPanelView view, OnlineRoomAction action) => Field<Button>(view, "OnlineRoom" + action).RaiseEvent(new(Button.ClickEvent));
    private static T Field<T>(FrameworkElement root, string name) where T : class => root.FindName(name) as T ?? throw new AssertFailedException(name);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Descendants(child)) yield return nested;
    }
}
