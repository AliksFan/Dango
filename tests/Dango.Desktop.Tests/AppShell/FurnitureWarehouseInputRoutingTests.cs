using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Core.Furniture;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Furniture;
using Button = System.Windows.Controls.Button;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class FurnitureWarehouseInputRoutingTests
{
    [STATestMethod]
    public void TemplatedMouseDownRouteCreatesExactlyOnePreviewOnSecondLeftPress()
    {
        var panel = CreatePanel();
        var requests = new List<FurnitureItemKind>();
        panel.AddRequested += requests.Add;
        foreach (var (name, kind) in new[]
        {
            ("RoomFurniturePlatform", FurnitureItemKind.StaticPlatform),
            ("RoomFurnitureBall", FurnitureItemKind.RoundBall)
        })
        {
            var button = Find<Button>(panel, name);
            var chrome = TemplateChrome(button);
            var generatedCounts = new List<int>();
            button.AddHandler(Control.MouseDoubleClickEvent,
                new MouseButtonEventHandler((_, e) => generatedCounts.Add(e.ClickCount)), true);
            var priorRequests = requests.Count;

            MousePress(chrome, MouseButton.Left, 1);
            Assert.AreEqual(priorRequests, requests.Count, "The first press must only focus the warehouse entry.");
            var second = MousePress(chrome, MouseButton.Left, 2);
            CollectionAssert.AreEqual(new[] { 1 }, generatedCounts,
                "WPF synthesizes MouseDoubleClick with a fresh argument whose ClickCount is 1; do not inject that high-level event.");
            Assert.AreEqual(priorRequests + 1, requests.Count,
                "The real template mouse-down route must produce one pending preview.");
            Assert.AreEqual(kind, requests[^1]);
            Assert.IsTrue(second.Handled, "Warehouse input must be consumed before it reaches room input.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            MousePress(chrome, MouseButton.Left, 3);
            Assert.AreEqual(priorRequests + 1, requests.Count,
                "The trailing Click and third press must not create another preview.");
        }
    }

    [STATestMethod]
    public void RoutedDoubleClickRejectsRightButtonAndDisabledWarehouseEntries()
    {
        var panel = CreatePanel();
        var requests = new List<FurnitureItemKind>();
        panel.AddRequested += requests.Add;
        var button = Find<Button>(panel, "RoomFurnitureBall");
        var chrome = TemplateChrome(button);
        MousePress(chrome, MouseButton.Right, 2);
        Assert.AreEqual(0, requests.Count);

        panel.Refresh(true, true, false, FurnitureItemKind.RoundBall, true, 30,
            false, false, false, "", OrdinaryRoomFurnitureTool.Select);
        Assert.IsFalse(button.IsEnabled);
        MousePress(chrome, MouseButton.Left, 2);
        Assert.AreEqual(0, requests.Count, "A disabled entry must reject even an explicitly routed event.");
        panel.Refresh(true, true, true, null, false, 0,
            false, false, false, "", OrdinaryRoomFurnitureTool.Select);
        MousePress(chrome, MouseButton.Left, 2);
        Assert.AreEqual(0, requests.Count, "An in-flight operation must not create another draft.");
    }

    [STATestMethod]
    public void KeyboardEnterUsesSamePreviewRequestWithoutMouseOrAutoRepeatDuplication()
    {
        var panel = CreatePanel();
        var requests = new List<FurnitureItemKind>();
        panel.AddRequested += requests.Add;
        var button = Find<Button>(panel, "RoomFurnitureBall");
        var chrome = TemplateChrome(button);
        var first = Enter(chrome, repeat: false);
        Assert.IsTrue(first.Handled);
        CollectionAssert.AreEqual(new[] { FurnitureItemKind.RoundBall }, requests);
        Enter(chrome, repeat: true);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(1, requests.Count);
    }

    private static OrdinaryRoomFurnitureEditorPanel CreatePanel()
    {
        var panel = new OrdinaryRoomFurnitureEditorPanel(new ProductHomeView().Resources);
        panel.Refresh(true, true, false, null, false, 0,
            false, false, false, "", OrdinaryRoomFurnitureTool.Select);
        panel.Measure(new Size(216, 600));
        panel.Arrange(new Rect(0, 0, 216, 600));
        panel.UpdateLayout();
        return panel;
    }

    private static FrameworkElement TemplateChrome(Button button)
    {
        button.ApplyTemplate();
        var chrome = (FrameworkElement)button.Template.FindName("Chrome", button);
        Assert.IsNotNull(chrome);
        Assert.AreSame(button, chrome.TemplatedParent);
        Assert.IsTrue(chrome.IsHitTestVisible);
        Assert.IsFalse(((System.Windows.Controls.Image)button.Content).IsHitTestVisible,
            "The rendered furniture image deliberately delegates hit testing to the button template.");
        return chrome;
    }

    private static MouseButtonEventArgs MousePress(UIElement source, MouseButton changedButton, int count)
    {
        // The MouseDevice owns ClickCount on the raw down event. Let WPF
        // perform left/right cracking, ButtonBase handling and double-click
        // synthesis; never assign ClickCount on MouseDoubleClick itself.
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, changedButton)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        };
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(args, count);
        source.RaiseEvent(args);
        if (!args.Handled)
        {
            args.RoutedEvent = Mouse.MouseDownEvent;
            source.RaiseEvent(args);
        }
        return args;
    }

    private static KeyEventArgs Enter(UIElement source, bool repeat)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, new UnconnectedPresentationSource(), Environment.TickCount, Key.Enter)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        typeof(KeyEventArgs).GetMethod("SetRepeat", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(args, [repeat]);
        source.RaiseEvent(args);
        return args;
    }

    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants<T>(root).Single(element => element.Name == name);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class UnconnectedPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = new DrawingVisual();
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
