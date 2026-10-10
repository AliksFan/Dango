using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dango.Core.Furniture;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Furniture;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class MultiFurnitureUiTests
{
    [STATestMethod]
    public void WarehouseSingleClickAddsNothingAndOneDoubleClickEmitsOnlyOnePreviewRequest()
    {
        var panel = new OrdinaryRoomFurnitureEditorPanel(new ProductHomeView().Resources);
        panel.Refresh(true, true, false, null, false, 0, false, false, false, "", OrdinaryRoomFurnitureTool.Select);
        var requests = new List<FurnitureItemKind>();
        panel.AddRequested += requests.Add;
        var platform = Find<Button>(panel, "RoomFurniturePlatform");
        var ball = Find<Button>(panel, "RoomFurnitureBall");
        platform.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ball.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(0, requests.Count, "Single-click, including the first click of a double-click, must not insert furniture.");
        RaiseMousePress(ball, 2);
        ball.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RaiseMousePress(ball, 3);
        Assert.AreEqual(1, requests.Count, "Trailing Click and the third mouse press must not duplicate the add request.");
        Assert.AreEqual(FurnitureItemKind.RoundBall, requests[0]);
        RaiseMousePress(platform, 2);
        Assert.AreEqual(2, requests.Count);
        Assert.AreEqual(FurnitureItemKind.StaticPlatform, requests[1]);
    }

    [STATestMethod]
    public void ThirtyFurnitureDisablesBothWarehouseEntriesWhileKeepingMoveAndDeleteAvailable()
    {
        var panel = new OrdinaryRoomFurnitureEditorPanel(new ProductHomeView().Resources);
        panel.Refresh(true, true, false, FurnitureItemKind.RoundBall, true, 30, false, false, false, "", OrdinaryRoomFurnitureTool.Select);
        Assert.AreEqual("30/30", Find<TextBlock>(panel, "RoomFurnitureCount").Text);
        foreach (var name in new[] { "RoomFurniturePlatform", "RoomFurnitureBall" })
            Assert.IsFalse(Find<Button>(panel, name).IsEnabled, "The cap counts mixed instances rather than entries in the warehouse.");
        foreach (var name in new[] { "RoomFurnitureSelect", "RoomFurnitureGlove", "RoomFurnitureDelete", "RoomFurnitureUp" })
            Assert.IsTrue(Find<Button>(panel, name).IsEnabled, name);
        var requests = 0; panel.AddRequested += _ => requests++;
        RaiseMousePress(Find<Button>(panel, "RoomFurnitureBall"), 2);
        Assert.AreEqual(0, requests, "Disabled mouse events must not issue a hidden add command.");
        panel.Refresh(true, true, false, FurnitureItemKind.RoundBall, true, 29, false, false, false, "", OrdinaryRoomFurnitureTool.Select);
        Assert.IsTrue(Find<Button>(panel, "RoomFurnitureBall").IsEnabled);
        Assert.AreEqual("29/30", Find<TextBlock>(panel, "RoomFurnitureCount").Text);
    }

    [STATestMethod]
    public void NewPreviewCannotDeleteAnUncommittedInstanceAndKeepsCommitAreaOutsideScrolling()
    {
        var panel = new OrdinaryRoomFurnitureEditorPanel(new ProductHomeView().Resources);
        panel.Refresh(true, true, false, FurnitureItemKind.RoundBall, false, 29, true, false, false,
            "家具碰撞重叠，不能确认。", OrdinaryRoomFurnitureTool.Select);
        Assert.IsFalse(Find<Button>(panel, "RoomFurnitureDelete").IsEnabled);
        Assert.IsFalse(Find<Button>(panel, "RoomFurnitureConfirm").IsEnabled);
        Assert.IsTrue(Find<Button>(panel, "RoomFurnitureCancel").IsEnabled);
        panel.Measure(new Size(216, 352)); panel.Arrange(new Rect(0, 0, 216, 352)); panel.UpdateLayout();
        var scroller = Find<ScrollViewer>(panel, "RoomFurnitureSections");
        scroller.ScrollToEnd(); panel.UpdateLayout();
        foreach (var name in new[] { "RoomFurniturePlatform", "RoomFurnitureBall" })
        {
            var entry = Find<Button>(panel, name);
            var origin = entry.TranslatePoint(new Point(), scroller);
            Assert.IsTrue(origin.Y >= -.01 && origin.Y + entry.ActualHeight <= scroller.ViewportHeight + .01,
                name + " must remain fully reachable at the minimum room height.");
        }
        foreach (var name in new[] { "RoomFurnitureConfirm", "RoomFurnitureCancel", "RoomFurnitureStatus" })
        {
            var control = Find<FrameworkElement>(panel, name);
            Assert.IsFalse(scroller.IsAncestorOf(control));
            var origin = control.TranslatePoint(new Point(), panel);
            Assert.IsTrue(origin.Y >= 0 && origin.Y + control.ActualHeight <= 352.01, name);
        }
    }

    [STATestMethod]
    public void SceneKeepsEveryInstanceAndSelectsByGuidWithCircularBallHitTest()
    {
        var scene = Scene();
        var items = Enumerable.Range(0, 30).Select(index => new OrdinaryRoomFurnitureRenderItem(Guid.NewGuid(),
            index % 2 == 0 ? FurnitureItemKind.StaticPlatform : FurnitureItemKind.RoundBall,
            new FurnitureAnchor(400 + index * 450, 180), index * .1)).ToArray();
        scene.SetFurnitureFrame(items, null, false);
        Assert.AreEqual(30, scene.FurnitureItems.Count);
        Assert.IsNull(scene.FurnitureApplied, "The legacy single-platform convenience must not silently take the first multi-item instance.");
        for (var index = 0; index < items.Length; index++)
        {
            var center = scene.AnchorToScreen(items[index].Anchor);
            if (items[index].Kind == FurnitureItemKind.StaticPlatform)
                center.Y += FurnitureLayoutContract.OrdinaryRoom.PlatformHeight * scene.CurrentProjection.Scale / 2;
            Assert.AreEqual(items[index].InstanceId, scene.HitTestFurniture(center), "Every repeated preset keeps its distinct GUID.");
        }
        var ball = items[27];
        scene.SetFurnitureSelected(ball.InstanceId);
        Assert.AreEqual(ball.InstanceId, scene.FurnitureSelectedInstanceId);
        var ballCenter = scene.AnchorToScreen(ball.Anchor);
        var radius = FurnitureLayoutContract.OrdinaryRoom.BallRadius * scene.CurrentProjection.Scale;
        Assert.IsNull(scene.HitTestFurniture(new Point(ballCenter.X + .9 * radius, ballCenter.Y + .9 * radius)),
            "The transparent corner of the ball's bounding square is not furniture.");
        Assert.AreEqual(ball.InstanceId, scene.HitTestFurniture(new Point(ballCenter.X + .5 * radius, ballCenter.Y + .5 * radius)));
    }

    [STATestMethod]
    public void BallFrameTracksRealCenterAndPreviewDoesNotReplaceItsMovingBody()
    {
        var scene = Scene();
        var id = Guid.NewGuid();
        var initial = new OrdinaryRoomFurnitureRenderItem(id, FurnitureItemKind.RoundBall, new(850, 180), 0);
        var preview = new FurnitureItemInstance(id, FurnitureItemKind.RoundBall, new(1200, 220));
        var frames = new[] { initial };
        scene.SetFurnitureFrame(frames, preview, true);
        var moved = initial with { Anchor = new(980, 110), AngleRadians = Math.PI / 2 };
        frames[0] = moved;
        Assert.AreEqual(initial, scene.FurnitureItems[0], "Rendering owns a copied frame, not a later-mutated collection.");
        scene.SetFurnitureFrame(frames, preview, true);
        Assert.AreEqual(moved, scene.FurnitureItems[0]);
        Assert.AreEqual(preview, scene.FurniturePreviewItem);
        Assert.AreEqual(id, scene.HitTestFurniture(scene.AnchorToScreen(moved.Anchor)));
        Assert.IsNull(scene.HitTestFurniture(scene.AnchorToScreen(preview.Anchor)), "A ghost target does not replace or become the native ball.");
        Assert.IsTrue(scene.IsFurniturePreviewHit(scene.AnchorToScreen(preview.Anchor)));
        Assert.AreEqual(-90d, LocalRoomSceneView.BallWpfAngleDegrees(moved.AngleRadians), 1e-9,
            "Native counter-clockwise q rotation must use the opposite sign on a y-down WPF surface.");
    }

    private static LocalRoomSceneView Scene()
    {
        var scene = new LocalRoomSceneView();
        scene.EnableWindowCamera();
        scene.Measure(new Size(960, 600)); scene.Arrange(new Rect(0, 0, 960, 600)); scene.UpdateLayout();
        return scene;
    }

    private static void RaiseMousePress(Button button, int count)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent };
        // Set metadata only on the raw mouse press. WPF Control must synthesize
        // MouseDoubleClick itself; a third press never synthesizes that event.
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(args, count);
        button.RaiseEvent(args);
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
}
