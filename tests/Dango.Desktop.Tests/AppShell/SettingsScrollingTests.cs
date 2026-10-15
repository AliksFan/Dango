using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class SettingsScrollingTests
{
    [STATestMethod]
    [DataRow(320, 360)]
    [DataRow(480, 640)]
    [DataRow(940, 480)]
    public void FailureAndNavigationActionsStayVisibleWhenContentScrolls(int width, int height)
    {
        var home = new ProductHomeView();
        home.UpdateSettings(new(new("普通房", "正在运行"), true, false, "本次已应用，但未保存。请重试保存，或不保存退出。"));
        home.ShowSettingsPage();
        foreach (var name in new[] { "HomeOperationHelp", "RoomOperationHelp", "DesktopPetOperationHelp" })
            ((Expander)home.FindName(name)).IsExpanded = true;
        var size = new Size(width, height);
        home.Measure(size); home.Arrange(new Rect(size)); home.UpdateLayout();
        var scroll = (ScrollViewer)home.FindName("SettingsPage");
        var actions = new[] { "BackFromSettingsButton", "SettingsExitButton", "RetrySaveSettingsButton" }
            .ToDictionary(name => name, name => Bounds(home, (FrameworkElement)home.FindName(name)));
        Assert.IsTrue(scroll.ScrollableHeight > 0);
        scroll.ScrollToBottom(); home.UpdateLayout();
        Assert.IsTrue(scroll.VerticalOffset > 0);
        foreach (var (name, before) in actions)
        {
            var after = Bounds(home, (FrameworkElement)home.FindName(name));
            Assert.AreEqual(before, after, name);
            Assert.AreEqual(after.Height, Rect.Intersect(new Rect(size), after).Height, 1, $"{name} 被裁切。");
        }
    }

    [STATestMethod]
    public void WheelOverReadOnlyDiagnosticsScrollsThePageOnce()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        using var rig = new ScrollRig(home);
        home.SetDiagnosticsText(string.Join("\n", Enumerable.Repeat("fictional safe details", 30)));
        home.ShowSettingsPage(showDiagnostics: true); Pump(35);
        rig.Scroll.ScrollToVerticalOffset(150); Pump(25);
        var before = rig.Scroll.VerticalOffset;
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        ((TextBox)home.FindName("SettingsDiagnosticsText")).RaiseEvent(wheel);
        Pump(35);
        Assert.IsTrue(wheel.Handled);
        var expected = SystemParameters.WheelScrollLines < 0 ? rig.Scroll.ViewportHeight : SystemParameters.WheelScrollLines * 16d;
        Assert.AreEqual(Math.Min(before + expected, rig.Scroll.ScrollableHeight), rig.Scroll.VerticalOffset, 1,
            "只读诊断不应吞掉滚轮或使页面重复滚动。");
    }

    [STATestMethod]
    public void ReducedMotionWheelScrollsImmediatelyAndThumbInputDoesNotReplayWheel()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        using var rig = new ScrollRig(home);
        var scroll = rig.Scroll;
        Wheel(scroll, -120); Pump(35);
        Assert.IsTrue(scroll.VerticalOffset > 0);
        var offset = scroll.VerticalOffset;
        Pump(180);
        Assert.AreEqual(offset, scroll.VerticalOffset, 0.1, "减少动画模式不应在停止输入后继续滚动。");
        home.ReduceMotion = false;
        Wheel(scroll, -120); Pump(30);
        scroll.RaiseEvent(new ScrollEventArgs(ScrollEventType.ThumbTrack, 60) { RoutedEvent = ScrollBar.ScrollEvent });
        scroll.ScrollToVerticalOffset(60); Pump(20);
        var direct = scroll.VerticalOffset;
        Pump(220);
        Assert.AreEqual(direct, scroll.VerticalOffset, 0.1, "直接操作滚动条后不能补播旧滚轮。");
    }

    [STATestMethod]
    public void ReversingWheelRespondsFromVisiblePositionAndHiddenPageStopsScrolling()
    {
        var home = new ProductHomeView();
        using var rig = new ScrollRig(home);
        var scroll = rig.Scroll;
        scroll.ScrollToVerticalOffset(130); Pump(25);
        Wheel(scroll, -120); Pump(35);
        var beforeReverse = scroll.VerticalOffset;
        Wheel(scroll, 120); Pump(170);
        Assert.IsTrue(scroll.VerticalOffset < beforeReverse, "反向滚轮应及时改变方向。");
        Wheel(scroll, -120); Pump(30);
        home.ShowHomePage();
        var beforeHide = scroll.VerticalOffset;
        Pump(200);
        Assert.AreEqual(beforeHide, scroll.VerticalOffset, 0.1, "隐藏页面后应停止滚轮插值。");
    }

    private static Rect Bounds(FrameworkElement root, FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    private static void Wheel(UIElement target, int delta) => target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.MouseWheelEvent });
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private sealed class ScrollRig : IDisposable
    {
        private readonly Window window;
        public ScrollRig(ProductHomeView home)
        {
            window = new() { Content = home, Width = 480, Height = 500, Left = -10000, Top = -10000, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            window.Show();
            home.ShowSettingsPage();
            foreach (var name in new[] { "HomeOperationHelp", "RoomOperationHelp", "DesktopPetOperationHelp" }) ((Expander)home.FindName(name)).IsExpanded = true;
            Pump(40);
            Scroll = (SettingsScrollViewer)home.FindName("SettingsPage");
        }
        public SettingsScrollViewer Scroll { get; }
        public void Dispose() => window.Close();
    }
}
