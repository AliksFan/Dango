using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Furniture;
using Dango.Desktop.Rendering.Visuals;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class OrdinaryRoomFurnitureEditorPanelTests
{
    [STATestMethod]
    public void PanelReusesHomeResourcesAndKeepsTwoColumnWarehouseAndNamedIconActions()
    {
        var resources = new ProductHomeView().Resources;
        var panel = new OrdinaryRoomFurnitureEditorPanel(resources);
        Assert.AreSame(resources["HomeBackground"], panel.TryFindResource("HomeBackground"));
        Assert.AreSame(resources["FaceIconButton"], Find<Button>(panel, "RoomFurnitureConfirm").Style);
        var warehouse = Find<UniformGrid>(panel, "RoomFurnitureWarehouse");
        Assert.AreEqual(2, warehouse.Columns);
        Assert.AreEqual(2, warehouse.Children.Count, "The warehouse contains the official platform and ball, with repeat placement through the same two entries.");
        foreach (var button in Descendants<Button>(panel))
        {
            Assert.IsTrue(AutomationProperties.GetName(button).Length > 0, button.Name);
            Assert.IsNotNull(button.ToolTip, button.Name);
            if (button.Name is "RoomFurniturePlatform" or "RoomFurnitureBall")
            {
                Assert.IsInstanceOfType<System.Windows.Controls.Image>(button.Content, "The warehouse shows its actual official artwork rather than a tool icon.");
                var thumbnail = (System.Windows.Controls.Image)button.Content;
                Assert.AreSame(button.Name == "RoomFurniturePlatform" ? OfficialWoodPlatformVisual.CurrentImage : OfficialRoundBallVisual.CurrentImage,
                    thumbnail.Source, "The warehouse uses the exact artwork that the room draws.");
                Assert.IsTrue(thumbnail.Source.IsFrozen);
                Assert.IsTrue(thumbnail.Width > 20, "The furniture image must not retain the tiny tool-icon bounds.");
            }
            else
            {
                Assert.IsInstanceOfType<System.Windows.Shapes.Path>(button.Content, "Operation tools keep the approved line icons.");
                Assert.AreEqual(32d, button.Width); Assert.AreEqual(32d, button.Height);
            }
        }
    }

    [STATestMethod]
    public void PreviewCollisionBusyAndRecoverStatesGateActualControls()
    {
        var panel = new OrdinaryRoomFurnitureEditorPanel(new ProductHomeView().Resources);
        panel.Refresh(true, true, false, true, true, true, false, false, "家具穿入团子，不能确认。", OrdinaryRoomFurnitureTool.Select);
        Assert.IsFalse(Find<Button>(panel, "RoomFurnitureConfirm").IsEnabled);
        Assert.AreEqual(Visibility.Visible, Find<Button>(panel, "RoomFurnitureCancel").Visibility);
        Assert.IsTrue(Find<Button>(panel, "RoomFurnitureDelete").IsEnabled);
        Assert.AreEqual("家具穿入团子，不能确认。", Find<TextBlock>(panel, "RoomFurnitureStatus").Text);
        panel.Refresh(false, true, true, true, true, true, true, false, "正在保存…", OrdinaryRoomFurnitureTool.Select);
        foreach (var button in Descendants<Button>(panel)) Assert.IsFalse(button.IsEnabled, button.Name + " must not admit an overlapping command.");
        panel.Refresh(false, false, false, false, true, false, false, true, "已保存，等待核对运行结果。", OrdinaryRoomFurnitureTool.Glove);
        Assert.IsTrue(Find<Button>(panel, "RoomFurnitureRecover").IsEnabled);
        Assert.IsTrue(Find<Button>(panel, "RoomFurnitureExit").IsEnabled);
        Assert.IsFalse(Find<Button>(panel, "RoomFurnitureGlove").IsEnabled);
        Assert.AreEqual(Visibility.Collapsed, Find<Button>(panel, "RoomFurnitureConfirm").Visibility);
    }

    [STATestMethod]
    public void MinimumRoomHeightScrollsSectionsWhileCommitAndFailureStayFixed()
    {
        var panel = new OrdinaryRoomFurnitureEditorPanel(new ProductHomeView().Resources);
        panel.Refresh(true, true, false, true, false, true, true, false, "家具穿入团子，不能确认。", OrdinaryRoomFurnitureTool.Select);
        panel.Measure(new Size(216, 352)); panel.Arrange(new Rect(0, 0, 216, 352)); panel.UpdateLayout();
        var scroller = Find<ScrollViewer>(panel, "RoomFurnitureSections");
        var confirm = Find<Button>(panel, "RoomFurnitureConfirm");
        var status = Find<TextBlock>(panel, "RoomFurnitureStatus");
        Assert.IsFalse(scroller.IsAncestorOf(confirm), "Committing never scrolls away with the warehouse.");
        Assert.IsFalse(scroller.IsAncestorOf(status), "A rejection reason remains available outside section scrolling.");
        foreach (var element in new FrameworkElement[] { confirm, status, Find<Button>(panel, "RoomFurnitureExit") })
        {
            var origin = element.TranslatePoint(new Point(), panel);
            Assert.IsTrue(origin.Y >= 0 && origin.Y + element.ActualHeight <= 352.01, element.Name + " was clipped at the formal window minimum.");
        }
        Assert.IsTrue(scroller.ScrollableHeight > 0);
        scroller.ScrollToEnd(); panel.UpdateLayout();
        Assert.AreEqual(scroller.ScrollableHeight, scroller.VerticalOffset, .01);
        var warehouseItem = Find<Button>(panel, "RoomFurniturePlatform");
        var warehouseOrigin = warehouseItem.TranslatePoint(new Point(), scroller);
        Assert.IsTrue(warehouseOrigin.Y >= -.01 && warehouseOrigin.Y + warehouseItem.ActualHeight <= scroller.ViewportHeight + .01,
            "The full warehouse thumbnail and its click target must be reachable at the section scroll end.");
        var bar = VisualDescendants<ScrollBar>(scroller).First(item => item.Orientation == System.Windows.Controls.Orientation.Vertical);
        Assert.AreEqual(16d, bar.Width);
    }

    [STATestMethod]
    public void MenuRejectsFurnitureWithoutCapabilityAndKeepsReasonVisible()
    {
        var menu = new LocalRoomMenuView { ShowCameraSettings = true };
        var dispatched = 0; menu.FurnitureRequested += () => dispatched++;
        menu.SetFurnitureAvailability(false, "布局被另一进程占用。");
        var button = (Button)menu.FindName("RoomFurnitureButton");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(0, dispatched);
        Assert.IsFalse(button.IsEnabled);
        Assert.AreEqual("布局被另一进程占用。", ((TextBlock)menu.FindName("RoomFurnitureUnavailable")).Text);
        Assert.AreEqual(Visibility.Visible, ((TextBlock)menu.FindName("RoomFurnitureUnavailable")).Visibility);
        menu.SetFurnitureAvailability(true, "布置家具 · 本地单人");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(1, dispatched);
        Assert.AreEqual(Visibility.Collapsed, ((TextBlock)menu.FindName("RoomFurnitureUnavailable")).Visibility);
    }

    [STATestMethod]
    public void EditingCannotStartBeforeHealthyExistingRoomWorld()
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        using var driver = new LocalRoomSceneDriver(new DesktopSceneSession(), runtime);
        var window = new LocalRoomWindow(driver);
        var scene = window.Scene;
        var camera = scene.CaptureFurnitureCamera();
        Assert.IsFalse(window.BeginFurnitureEditing());
        Assert.IsFalse(window.IsFurnitureEditing);
        Assert.AreSame(scene, window.Scene);
        Assert.AreEqual(camera, scene.CaptureFurnitureCamera());
        Assert.IsFalse(runtime.IsRunning);
        window.PrepareForExit(); window.Close();
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
    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T value) yield return value;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }
}
