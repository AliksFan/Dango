using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class LocalRoomAppearanceIntegrationTests
{
    private static DangoAppearanceConfiguration Choice => new(new(41, 83, 127), new(119, 37, 73), new(137, -83, 33, 12.5));

    [TestMethod]
    public void RetiredFaceAndRoomArgumentsCannotSelectAnOldProductWorld()
    {
        foreach (var argument in new[] { "--face-review", "--local-room-review" })
            Assert.ThrowsExactly<ArgumentException>(() => App.ValidateStartupArguments([argument]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ValidateStartupArguments(["--face-review", "--local-room-review"]));
    }

    [STATestMethod]
    public void DefaultProductPublishesTheLocalCatalogWithoutFabricatingFormalAdmission()
    {
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(NewRoot(), "settings.json"));
        host.ShowControlCenter();
        ProductHostTestSupport.WaitForHome(host);
        var home = Field<ControlCenterWindow>(host, "controlCenterWindow").ProductHome;
        var porch = E<PorchSceneView>(home, "PorchScene");
        Assert.AreEqual(3, porch.DoorCount, "The product exposes desktop pet, its saved ordinary room and create entry.");
        Assert.AreEqual(1, home.CatalogSnapshot!.ActiveCount);
        Assert.IsNotNull(host.LocalWindow);
        Assert.IsNotNull(home.BoundBeltBody);
        Assert.IsFalse(porch.CanEnterRoom, "Local catalog entries cannot fabricate formal admission.");
    }

    [STATestMethod]
    public void AwayHomeCancelsAppearanceAndRestoresEditorVisibilityRules()
    {
        var appearance = new AppearanceSettingsSession(Path.Combine(NewRoot(), "appearance.json"));
        appearance.StagePreview(Choice); Assert.IsTrue(appearance.TrySave());
        var home = new ProductHomeView { ReduceMotion = true };
        home.BindAppearance(appearance);
        home.ShowEditor(HomeEditor.Face);
        E<Slider>(home, "EyeSizeSlider").Value = 110;
        Assert.IsTrue(appearance.IsDirty);
        Assert.AreEqual(Visibility.Collapsed, E<UIElement>(home, "Launchers").Visibility);

        home.SetDangoAway(true);
        Assert.IsFalse(home.IsEditing);
        Assert.IsFalse(appearance.IsDirty);
        Assert.AreEqual(Choice, appearance.DraftAppearance);
        AssertAway(home);
        home.ShowEditor(HomeEditor.Face);
        home.ShowEditor(HomeEditor.Wardrobe);
        Assert.IsFalse(home.IsEditing);
        home.ShowSettingsPage(); home.ShowHomePage();
        AssertAway(home);

        home.SetDangoAway(false);
        Assert.AreEqual(Visibility.Visible, E<UIElement>(home, "Launchers").Visibility);
        Assert.IsTrue(E<Button>(home, "EnterRoomButton").IsEnabled);
        home.ShowEditor(HomeEditor.Face);
        Assert.AreEqual(Visibility.Visible, E<UIElement>(home, "FaceEditorTools").Visibility);
        Assert.IsTrue(E<FrameworkElement>(home, "FaceEditorTools").IsEnabled);
        Assert.AreEqual(Visibility.Collapsed, E<UIElement>(home, "Launchers").Visibility);
        home.CloseEditor();
        Assert.AreEqual(Visibility.Visible, E<UIElement>(home, "Launchers").Visibility);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void SavedAppearanceFollowsDefaultHomeAndRoomAcrossWindowWallpaperAndReturn()
    {
        var root = NewRoot();
        var appearance = new AppearanceSettingsSession(Path.Combine(root, "appearance.json"));
        appearance.StagePreview(Choice); Assert.IsTrue(appearance.TrySave());
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"));
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom);
        Assert.AreEqual(0, runtime.EntityIds.Count, "The room owner must stay deferred while the default home starts.");
        host.ShowControlCenter();
        ProductHostTestSupport.WaitForHome(host);
        var home = Field<ControlCenterWindow>(host, "controlCenterWindow").ProductHome;
        home.ReduceMotion = true;
        var homeOwner = home.BoundBeltBody;
        Assert.AreEqual(Choice, home.BoundBeltPresenter!.Appearance);
        home.ResumePorch();
        WaitUntil(() => home.IsInPorch && home.PorchScene.HasBeltBody);
        Assert.AreEqual(Choice, home.BoundBeltPresenter.Appearance);
        host.EnterLocalRoom();
        WaitForRoom(host, LocalRoomPresentation.Window);
        var driver = Field<LocalRoomSceneDriver>(host, "localRoomDriver");
        var id = runtime.EntityIds.Single();
        Assert.IsTrue(driver.OwnsRuntime);
        Assert.IsFalse(homeOwner!.Metrics.Running, "Entry must pause the home worker before the room can run.");
        AssertAppearance(runtime, Field<DeformableDangoVisual>(host.LocalWindow!.Scene, "body"), id);
        var owner = runtime.FrameDriver;
        host.ShowControlCenter();
        AssertAway(home);
        Assert.AreSame(owner, runtime.FrameDriver);
        Assert.IsFalse(homeOwner.Metrics.Running);
        host.SwitchLocalRoomPresentation(LocalRoomPresentation.Wallpaper);
        WaitForRoom(host, LocalRoomPresentation.Wallpaper);
        var wallpaper = Field<LocalRoomWallpaperHost>(host, "localRoomWallpaper");
        Assert.IsTrue(wallpaper.Scene.HasHealthyFrame);
        Assert.IsTrue(driver.OwnsRuntime);
        Assert.AreSame(owner, runtime.FrameDriver);
        AssertAppearance(runtime, Field<DeformableDangoVisual>(wallpaper.Scene, "body"), id);
        host.SwitchLocalRoomPresentation(LocalRoomPresentation.Window);
        WaitForRoom(host, LocalRoomPresentation.Window);
        AssertAppearance(runtime, Field<DeformableDangoVisual>(host.LocalWindow.Scene, "body"), id);
        host.ReturnToPorch();
        WaitForRoom(host, LocalRoomPresentation.Porch);
        WaitUntil(() => homeOwner.Metrics.Running && home.PorchScene.HasBeltBody);
        Assert.IsFalse(home.IsDangoAway);
        Assert.IsFalse(driver.OwnsRuntime);
        Assert.IsNull(runtime.FrameDriver, "Returning to the independent home worker must release the room scheduler.");
        Assert.AreSame(homeOwner, home.BoundBeltBody);
        Assert.AreEqual(Choice, home.BoundBeltPresenter.Appearance);
        host.ShowControlCenter();
        WaitUntil(() => !home.IsInPorch && home.BoundBeltPresenter.View.IsVisible);
        Assert.AreEqual(Choice, home.BoundBeltPresenter.Appearance);
        Assert.AreEqual(Choice, new AppearanceSettingsSession(Path.Combine(root, "appearance.json")).SavedAppearance);
    }

    private static void AssertAway(ProductHomeView home)
    {
        Assert.IsTrue(home.IsDangoAway);
        foreach (var name in new[] { "Presence", "Launchers", "EditorPanel", "FaceEditorTools", "EnterRoomButton", "PorchContent", "HomeFooter" })
            Assert.AreEqual(Visibility.Collapsed, E<UIElement>(home, name).Visibility, name);
        Assert.IsFalse(E<FrameworkElement>(home, "FaceEditorTools").IsEnabled);
        Assert.IsFalse(E<UIElement>(home, "FaceEditorTools").IsHitTestVisible);
        Assert.IsFalse(E<Button>(home, "EnterRoomButton").IsEnabled);
        Assert.AreEqual(Visibility.Visible, E<UIElement>(home, "RoomAwayNotice").Visibility);
        Assert.AreEqual("团子去玩拉！回来才能换衣服哦", E<TextBlock>(home, "RoomAwayNotice").Text);
    }

    private static void AssertAppearance(StackedDangoSceneRuntime runtime, DeformableDangoVisual visual, DangoEntityId id)
    {
        Assert.AreEqual(id, runtime.EntityIds.Single());
        Assert.AreEqual(id, runtime.AppearanceEntity);
        Assert.AreEqual(id, visual.Tag);
        Assert.AreEqual(Choice, runtime.EffectiveAppearance);
        Assert.IsNull(runtime.LastError);
        Assert.IsNull(runtime.AttachmentError);
        Assert.IsTrue(runtime.LastPhysicsFrame!.Succeeded);
        Assert.AreEqual(Visibility.Visible, visual.Visibility);
        Assert.AreEqual(Color.FromRgb(41, 83, 127), ((SolidColorBrush)visual.BodyFill).Color);
        Assert.AreEqual(Color.FromRgb(119, 37, 73), ((SolidColorBrush)visual.EyeFill).Color);
        var face = runtime.LastFrame!.Bodies.Single().Attachments!.Face;
        Assert.IsTrue(face.Left.Visible && face.Right.Visible);
        Assert.AreEqual(12d / 160 * Choice.EyeLayout.SizePercent / 100 * face.SizeScale, face.Left.Width, 1e-8);
        Assert.AreEqual(face.Left.Width * (1 - Math.Abs(Choice.EyeLayout.OvalPercent) / 200), face.Left.Height, 1e-8);
    }

    private static void WaitForRoom(DangoApplicationHost host, LocalRoomPresentation expected)
    {
        WaitUntil(() => !host.LocalHandoffPending, 4500);
        Assert.AreEqual(expected, host.LocalPresentation, host.LocalRoomFailure);
        Assert.IsNull(host.LocalRoomFailure);
    }

    private static void WaitUntil(Func<bool> condition, int milliseconds = 2000)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (!condition() && Environment.TickCount64 < until) Pump(20);
        Assert.IsTrue(condition(), "The actual scene did not become ready before the bounded wait expired.");
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static string NewRoot()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "local-room", "appearance-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static T E<T>(ProductHomeView home, string name) where T : class => (T)home.FindName(name);
    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
}
