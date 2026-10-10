using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
[DoNotParallelize]
public sealed class HomeAccountNicknameLayoutTests
{
    [STATestMethod]
    [DataRow(336d, 620d)]
    [DataRow(390d, 740d)]
    [DataRow(430d, 500d)]
    [DataRow(460d, 820d)]
    [DataRow(520d, 820d)]
    [DataRow(520d, 400d)]
    [DataRow(960d, 540d)]
    public void NicknameAndAdjacentNavigationStayInsideTheActualWindow(double width, double height)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = new Window { Content = home, Width = width, Height = height, Left = -10000, Top = -10000, ShowInTaskbar = false };
        window.Show();
        try
        {
            foreach (var nickname in new[] { "小团子", new string('团', 24), string.Concat(Enumerable.Repeat("🐾", 24)) })
            {
                home.SetAccountStatus(true, true, nickname);
                window.UpdateLayout();
                var account = (Button)home.FindName("AccountButton");
                var label = (TextBlock)home.FindName("AccountNicknameText");
                Assert.AreEqual(nickname, label.Text, "Ellipsis must retain the entire underlying label.");
                Assert.AreEqual(TextTrimming.CharacterEllipsis, label.TextTrimming);
                Assert.AreEqual(nickname, UIElementAutomationPeer.CreatePeerForElement(account)!.GetName());
                StringAssert.Contains((string)account.ToolTip, nickname);
                Assert.IsTrue(account.ActualWidth <= 144.5, "The nickname button must remain bounded.");
                var tools = (StackPanel)home.FindName("ToolsNavigation");
                foreach (var button in tools.Children.OfType<Button>())
                {
                    var bounds = button.TransformToAncestor(home).TransformBounds(new Rect(button.RenderSize));
                    Assert.IsTrue(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= home.ActualWidth + 1
                        && bounds.Bottom <= home.ActualHeight + 1,
                        $"Navigation escaped with nickname '{nickname}': {bounds} / {home.RenderSize}, window={width}x{height}.");
                }
                var navigation = (Grid)home.FindName("TopNavigation");
                var accountBounds = account.TransformToAncestor(home).TransformBounds(new Rect(account.RenderSize));
                Assert.IsTrue(accountBounds.Right <= home.ActualWidth - navigation.Margin.Right - tools.Margin.Right + 1,
                    $"Nickname entered the reserved titlebar area: {accountBounds} / {home.RenderSize}, reserved={tools.Margin.Right}.");
                var brand = (TextBlock)home.FindName("Brand");
                var brandBounds = brand.TransformToAncestor(home).TransformBounds(new Rect(brand.RenderSize));
                var first = tools.Children.OfType<Button>().First();
                var firstBounds = first.TransformToAncestor(home).TransformBounds(new Rect(first.RenderSize));
                Assert.IsFalse(brandBounds.IntersectsWith(firstBounds), "The nickname must not push adjacent tools over the brand.");
            }
            home.SetAccountStatus(false, false);
            window.UpdateLayout();
            Assert.AreEqual("登录", ((TextBlock)home.FindName("AccountNicknameText")).Text);
            Assert.AreEqual("邮箱账号", AutomationProperties.GetName((Button)home.FindName("AccountButton")));
            Assert.AreEqual("邮箱登录", ((Button)home.FindName("AccountButton")).ToolTip);
        }
        finally { window.Close(); }
    }
}
