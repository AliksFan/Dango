using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Dango.Core.Furniture;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Furniture;
using Dango.Desktop.Rendering.Visuals;
using Button = System.Windows.Controls.Button;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class FurnitureEditingEscapeTests
{
    [STATestMethod]
    public void EscapeFromWarehouseFocusExitsEditingAndCancelsOnlyTheUnconfirmedPreview() => CheckEscape(true);

    [STATestMethod]
    public void EscapeWithoutPreviewExitsEditingRatherThanOpeningTheRoomMenu() => CheckEscape(false);

    private static void CheckEscape(bool withPreview)
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "furniture-feedback", "escape-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"),
            roomModulePath: Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE") ??
                Path.Combine(AppContext.BaseDirectory, "dango_belt_room.dll"));
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom, "Product rooms must use the continuous body module.");
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host); Pump(100);
        Field<ControlCenterWindow>(host, "controlCenterWindow").ProductHome.ResumePorch(); Pump(100);
        host.EnterLocalRoom();
        Until(() => !host.LocalHandoffPending && host.LocalWindow?.Scene.HasHealthyFrame == true);
        var window = host.LocalWindow!;
        var furniture = runtime.Furniture!;
        var entity = runtime.EntityIds.Single();
        var generation = furniture.WorldGeneration;
        var originalCamera = window.Scene.CaptureFurnitureCamera();
        var originalLayout = furniture.Layout;
        var catalog = new LocalRoomCatalogStore(root);
        var path = catalog.LayoutPath(catalog.ReadCurrent().Rooms.Single().RoomId);
        var originalBytes = File.ReadAllBytes(path);
        Assert.IsTrue(window.BeginFurnitureEditing(), furniture.Status);
        if (withPreview)
            furniture.PreviewNewFurniture(FurnitureItemKind.RoundBall, new(1440, 132));
        Pump(30);
        AssertSidebarIsAvoided();
        if (!withPreview)
        {
            window.Width = 640; window.Height = 420; Pump(40);
            AssertSidebarIsAvoided();
            window.Width = 960; window.Height = 600; Pump(40);
            AssertSidebarIsAvoided();
        }
        window.FocusScene();
        var target = Find<Button>(window, "RoomFurnitureBall");
        Assert.IsTrue(target.Focus(), "Exercise Esc while a warehouse entry owns keyboard focus.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Escape)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(args);
        Assert.IsTrue(args.Handled);
        Assert.IsFalse(window.IsFurnitureEditing, "A fresh Esc must leave the editor, including its pending preview.");
        Assert.IsFalse(window.IsMenuOpen, "Exiting furniture must not also open the full room menu.");
        Assert.IsFalse(furniture.HasPreview);
        Assert.IsNull(furniture.SelectedInstanceId);
        Assert.AreEqual(originalLayout, furniture.Layout);
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(path), "Esc cannot commit a preview or rewrite the initialized layout.");
        Assert.AreEqual(originalCamera, window.Scene.CaptureFurnitureCamera());
        Assert.AreEqual(generation, furniture.WorldGeneration);
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.IsTrue(window.Scene.IsKeyboardFocused);
        Assert.IsTrue(window.IsVisible);
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation);
        Assert.IsTrue(runtime.IsRunning);

        void AssertSidebarIsAvoided()
        {
            window.UpdateLayout();
            window.Scene.PanCamera(-100000);
            var layer = Find<Border>(window, "RoomFurnitureLayer");
            var sidebarLeft = layer.TranslatePoint(new System.Windows.Point(), window.Scene).X;
            Assert.AreEqual(sidebarLeft - 12 - LocalRoomSceneView.SideBoundaryInset * window.Scene.CurrentProjection.Scale,
                window.Scene.EnvironmentEdges.Right, .01,
                "The actual window must reserve its measured sidebar and gap at the rightmost camera position.");
        }
    }

    private static T Field<T>(object value, string name) =>
        (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;

    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants<T>(root).Single(item => item.Name == name);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        do { Pump(30); } while (!condition() && watch.ElapsedMilliseconds < 4500);
        Assert.IsTrue(condition(), "The actual ordinary room did not become ready.");
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
