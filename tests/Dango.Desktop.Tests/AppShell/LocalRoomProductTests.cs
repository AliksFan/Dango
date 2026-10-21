using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class LocalRoomProductTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ActualWindowHandoffWaitsForFrameAndPreservesEntityThroughHideReturnAndZoom()
    {
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom, "Product rooms must use the continuous body module.");
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host); Pump(450);
        var homeWindow = Field<ControlCenterWindow>(host, "controlCenterWindow");
        var home = homeWindow.ProductHome;
        home.ResumePorch(); Pump(400);
        Assert.IsTrue((home.BoundBeltBody?.Metrics.Running == true));
        Assert.AreEqual(0, runtime.EntityIds.Count, "Home startup cannot create the room world.");
        host.EnterLocalRoom();
        Assert.IsTrue(homeWindow.IsVisible, "Show alone must never hide the source.");
        ProductHostTestSupport.WaitFor(() => host.LocalHandoffPending, "Actual room preparation must cross the paused-home barrier.");
        Assert.IsFalse((home.BoundBeltBody?.Metrics.Running == true));
        homeWindow.Width += 10; homeWindow.UpdateLayout();
        Assert.IsFalse((home.BoundBeltBody?.Metrics.Running == true), "The visible suspended source cannot reclaim ownership.");
        Pump(450);
        Assert.IsFalse(host.LocalHandoffPending, host.LocalRoomFailure);
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation);
        Assert.IsFalse(homeWindow.IsVisible);
        Assert.IsTrue(host.LocalWindow!.Scene.HasHealthyFrame);
        var id = runtime.EntityIds.Single();
        var roomEpoch = runtime.LastBeltRoomFrame!.OwnerEpoch;
        var snapshot = Field<Dango.Desktop.DesktopHost.DesktopSceneSession>(host, "sceneSession").Snapshot;
        var ground = runtime.LocalGroundY;
        foreach (var preset in new[] { RoomViewPreset.Near, RoomViewPreset.Whole, RoomViewPreset.Standard })
            host.LocalWindow.Scene.SelectView(preset);
        host.LocalWindow.Scene.ApplyWheel(-120);
        host.LocalWindow.Width += 200; host.LocalWindow.UpdateLayout();
        Assert.AreSame(snapshot, Field<Dango.Desktop.DesktopHost.DesktopSceneSession>(host, "sceneSession").Snapshot,
            "Camera/resize cannot call AttachSurface or move the logical entity.");
        Assert.AreEqual(ground, runtime.LocalGroundY);
        Capture(host.LocalWindow.Scene, "room-window.png");
        host.LocalWindow.OpenMenu(); host.LocalWindow.UpdateLayout();
        Capture((FrameworkElement)host.LocalWindow.Content, "room-menu.png");
        host.LocalWindow.CloseMenu(); host.LocalWindow.FocusScene();
        var driver = Field<LocalRoomSceneDriver>(host, "localRoomDriver");
        Assert.IsTrue(driver.Key(Key.D, true, false)); Pump(220);
        host.HideLocalRoom();
        Assert.IsNull(runtime.FrameDriver);
        Assert.IsFalse(runtime.IsRunning);
        Assert.AreEqual(Dango.Core.Animation.DirectionalControlIntent.None, Field<Dango.Core.Animation.DirectionalControlIntent>(driver, "intent"));
        host.RestoreLocalRoom();
        Assert.IsTrue(host.LocalHandoffPending, "Restore must wait for a fresh frame, never reuse cached readiness.");
        Pump(350);
        Assert.AreEqual(id, runtime.EntityIds.Single());
        Assert.AreEqual(roomEpoch, runtime.LastBeltRoomFrame!.OwnerEpoch, "Hide and restore must retain the actual physical owner.");
        host.ReturnToPorch();
        host.HideLocalRoom();
        Assert.IsNull(runtime.FrameDriver, "Cancelling return must also release the pending porch owner.");
        Assert.IsFalse(runtime.IsRunning);
        Assert.IsFalse(homeWindow.IsVisible);
        host.RestoreLocalRoom(); Pump(350);
        host.ReturnToPorch();
        Assert.IsTrue(host.LocalWindow.IsVisible, "Return also keeps its source until the porch is ready.");
        Pump(400);
        Assert.AreEqual(LocalRoomPresentation.Porch, host.LocalPresentation, host.LocalRoomFailure);
        Assert.IsTrue(home.IsInPorch, "The room door returns to porch, not home.");
        Assert.IsTrue((home.BoundBeltBody?.Metrics.Running == true));
        Assert.IsNull(runtime.FrameDriver, "The room scheduler must be released when the home worker resumes.");
        Assert.IsNull(home.RoomNavigation.Authority.RoomId, "Local room cannot fabricate formal authority.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void NoFrameTimeoutKeepsRecoveryAndRetryCanRender()
    {
        using var host = CreateHost();
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host); Pump(350);
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        window.ProductHome.ResumePorch(); Pump(350);
        host.LocalWindow!.Scene.Visibility = Visibility.Hidden;
        host.EnterLocalRoom(); Pump(100);
        Assert.IsTrue(window.IsVisible);
        Assert.IsTrue(host.LocalHandoffPending);
        Pump(3200);
        Assert.IsFalse(host.LocalHandoffPending);
        Assert.IsNotNull(host.LocalRoomFailure);
        Assert.IsTrue(window.IsVisible);
        Assert.IsTrue(window.ProductHome.IsInPorch);
        Assert.IsTrue((window.ProductHome.BoundBeltBody?.Metrics.Running == true));
        host.LocalWindow.Scene.Visibility = Visibility.Visible;
        host.EnterLocalRoom(); Pump(450);
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        Assert.IsTrue(host.LocalWindow.Scene.HasHealthyFrame);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void RunningFaultReleasesOwnerAndExplicitRecoveryKeepsEntity()
    {
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom, "Product rooms must use the continuous body module.");
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host); Pump(350);
        host.EnterLocalRoom(); Pump(400);
        var id = runtime.EntityIds.Single();
        typeof(StackedDangoSceneRuntime).GetProperty(nameof(StackedDangoSceneRuntime.LastError))!.SetValue(runtime, "fictional local-room fault");
        Pump(150);
        Assert.IsNotNull(host.LocalRoomFailure);
        Assert.IsNull(runtime.FrameDriver);
        Assert.IsFalse(runtime.IsRunning);
        Assert.IsFalse(host.LocalWindow!.Scene.HasHealthyFrame);
        host.RecoverLocalRoom(); Pump(450);
        Assert.IsNull(runtime.LastError);
        Assert.AreEqual(id, runtime.EntityIds.Single());
        Assert.IsTrue(host.LocalWindow.Scene.HasHealthyFrame, host.LocalRoomFailure);
        Assert.IsFalse(host.LocalHandoffPending);
        host.ShowDiagnostics();
        var main = Field<ControlCenterWindow>(host, "controlCenterWindow");
        Assert.IsTrue(main.ProductHome.IsSettingsOpen, "The existing diagnostics command must open its actual page.");
        var roomOwner = runtime.FrameDriver;
        Assert.IsNotNull(roomOwner, "Opening diagnostics must preserve the active room owner.");
        var homeButton = (System.Windows.Controls.Button)main.ProductHome.FindName("HomeNavigation");
        homeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Pump(350);
        Assert.IsFalse(main.ProductHome.IsSettingsOpen);
        Assert.IsFalse(main.ProductHome.IsInPorch);
        Assert.IsFalse((main.ProductHome.BoundBeltBody?.Metrics.Running == true), "Away Home cannot create a second frame owner.");
        Assert.AreSame(roomOwner, runtime.FrameDriver);
        Assert.IsTrue(main.ProductHome.IsDangoAway);
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation);
        Assert.IsNotNull(typeof(StackedDangoSceneRuntime).GetField("localHorizontalBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime));
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ControlCenterCanOpenDuringRoomWithoutReturningOrShowingAnotherDango()
    {
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom, "Product rooms must use the continuous body module.");
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host); Pump(350);
        host.EnterLocalRoom(); Pump(450);
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        var owner = runtime.FrameDriver;
        var entity = runtime.EntityIds.Single();
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        var home = window.ProductHome;
        host.ShowControlCenter(); Pump(100);
        Assert.IsTrue(window.IsVisible);
        Assert.IsTrue(host.LocalWindow!.IsVisible);
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation);
        Assert.IsTrue(home.IsDangoAway);
        Assert.IsFalse((home.BoundBeltBody?.Metrics.Running == true));
        Assert.AreSame(owner, runtime.FrameDriver);
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        foreach (var name in new[] { "Presence", "Launchers", "EditorPanel", "PorchContent", "HomeFooter" })
            Assert.AreEqual(Visibility.Collapsed, ((UIElement)home.FindName(name)).Visibility, name);
        var notice = (System.Windows.Controls.TextBlock)home.FindName("RoomAwayNotice");
        Assert.AreEqual(Visibility.Visible, notice.Visibility);
        Assert.AreEqual("团子去玩拉！回来才能换衣服哦", notice.Text);
        Capture(home, "room-away-home.png");
        home.ShowEditor(HomeEditor.Face);
        Assert.IsFalse(home.IsEditing, "Programmatic and keyboard routes must also block appearance edits while away.");
        home.ShowSettingsPage(); home.ShowHomePage(); Pump(100);
        Assert.IsTrue(home.IsDangoAway);
        Assert.AreSame(owner, runtime.FrameDriver);
        host.HideLocalRoom(); Pump(100);
        Assert.IsTrue(window.IsVisible, "Hiding the room must not hide an independently opened control center.");
        Assert.IsTrue(home.IsDangoAway);
        Assert.IsNull(runtime.FrameDriver);
        host.ShowControlCenter(); Pump(100);
        Assert.IsNull(runtime.FrameDriver, "Opening Home must not resume a hidden room through another owner.");
        host.RestoreLocalRoom(); Pump(450);
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        var porch = (PorchSceneView)home.FindName("PorchScene");
        porch.Visibility = Visibility.Hidden;
        host.ReturnToPorch(); Pump(150);
        Assert.IsTrue(host.LocalHandoffPending, "A hidden target body cannot acknowledge its first frame.");
        Pump(3750);
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation, "A failed return must recover its room source.");
        Assert.IsTrue(home.IsDangoAway, "A failed porch first frame cannot restore appearance editing while the dango is still in its room.");
        Assert.IsFalse((home.BoundBeltBody?.Metrics.Running == true));
        home.ShowEditor(HomeEditor.Wardrobe);
        Assert.IsFalse(home.IsEditing);
        porch.Visibility = Visibility.Visible;
        host.ReturnToPorch(); Pump(450);
        Assert.IsFalse(home.IsDangoAway);
        Assert.IsTrue(home.IsInPorch);
        Assert.IsTrue((home.BoundBeltBody?.Metrics.Running == true));
        host.ShowControlCenter(); Pump(150);
        Assert.AreEqual(Visibility.Visible, ((UIElement)home.FindName("Presence")).Visibility);
        Assert.AreEqual(Visibility.Visible, ((UIElement)home.FindName("Launchers")).Visibility);
        Assert.AreEqual(Visibility.Collapsed, notice.Visibility);
    }

    [STATestMethod]
    public void LocalDoorIsExplicitAndDoesNotBecomeFormalAdmission()
    {
        var porch = new PorchSceneView();
        Assert.AreEqual(0, porch.DoorCount);
        porch.SetLocalRoomEnabled(true);
        Assert.AreEqual(1, porch.DoorCount);
        var count = 0;
        porch.LocalRoomRequested += () => count++;
        Assert.IsTrue(porch.InteractNearestDoor());
        Assert.AreEqual(1, count);
        Assert.IsFalse(porch.CanEnterRoom);
        porch.SetReviewMode(true);
        Assert.AreEqual(4, porch.DoorCount);
    }

    private static DangoApplicationHost CreateHost()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "local-room", "tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new(settingsFile: Path.Combine(root, "settings.json"));
    }
    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Capture(FrameworkElement scene, string name)
    {
        var bitmap = new RenderTargetBitmap((int)scene.ActualWidth, (int)scene.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var drawing = new System.Windows.Media.DrawingVisual();
        using (var dc = drawing.RenderOpen())
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(scene), null, new Rect(0, 0, scene.ActualWidth, scene.ActualHeight));
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(TestRepository.Root, "artifacts", "local-room", "visual"); Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
    }
}
