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

    [STATestMethod]
    public void FaceAndRoomReviewAreMutuallyExclusiveAndUseDifferentMutexes()
    {
        App.ValidateIsolatedReviewSelection(["--face-review"]);
        App.ValidateIsolatedReviewSelection(["--local-room-review"]);
        Assert.ThrowsExactly<ArgumentException>(() => App.ValidateIsolatedReviewSelection(["--face-review", "--local-room-review"]));
        Assert.AreNotEqual(App.ResolveSingleInstanceName(["--face-review"]), App.ResolveSingleInstanceName(["--local-room-review"]));
        Assert.AreNotEqual(App.ResolveSingleInstanceName([]), App.ResolveSingleInstanceName(["--local-room-review"]));
        Assert.ThrowsExactly<ArgumentException>(() => new DangoApplicationHost(isolatedFaceReview: true, isolatedLocalRoomReview: true));
    }

    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void FaceAndRoomReviewKeepTheirOwnRoomEntry(bool faceReview)
    {
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(NewRoot(), "settings.json"),
            isolatedFaceReview: faceReview, isolatedLocalRoomReview: !faceReview);
        host.ShowControlCenter();
        var home = Field<ControlCenterWindow>(host, "controlCenterWindow").ProductHome;
        var porch = E<PorchSceneView>(home, "PorchScene");
        Assert.AreEqual(faceReview ? 0 : 2, porch.DoorCount,
            "Room review exposes the ordinary room and the independent single-player desktop-pet room; face review exposes neither.");
        Assert.AreEqual(faceReview, host.LocalWindow is null);
        Assert.IsFalse(porch.CanEnterRoom, "Neither isolated review can fabricate formal admission.");
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
    public void SavedAppearanceFollowsTheSameEntityAcrossRoomWindowWallpaperAndReturn()
    {
        var root = NewRoot();
        var appearance = new AppearanceSettingsSession(Path.Combine(root, "appearance.json"));
        appearance.StagePreview(Choice); Assert.IsTrue(appearance.TrySave());
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"), isolatedLocalRoomReview: true);
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowControlCenter();
        var home = Field<ControlCenterWindow>(host, "controlCenterWindow").ProductHome;
        home.ReduceMotion = true;
        WaitUntil(() => home.OwnsLiveFrameDriver && E<DeformableDangoVisual>(home, "LiveDangoVisual").Visibility == Visibility.Visible);
        var id = runtime.EntityIds.Single();
        AssertAppearance(runtime, E<DeformableDangoVisual>(home, "LiveDangoVisual"), id);
        home.ResumePorch();
        WaitUntil(() => home.IsInPorch && home.OwnsLiveFrameDriver && E<PorchSceneView>(home, "PorchScene").LiveBody.Visibility == Visibility.Visible);
        AssertAppearance(runtime, E<PorchSceneView>(home, "PorchScene").LiveBody, id);

        host.EnterLocalRoom();
        WaitForRoom(host, LocalRoomPresentation.Window);
        var driver = Field<LocalRoomSceneDriver>(host, "localRoomDriver");
        Assert.IsTrue(driver.OwnsRuntime);
        Assert.IsFalse(home.OwnsLiveFrameDriver);
        AssertAppearance(runtime, Field<DeformableDangoVisual>(host.LocalWindow!.Scene, "body"), id);
        var owner = runtime.FrameDriver;
        host.ShowControlCenter();
        AssertAway(home);
        Assert.AreSame(owner, runtime.FrameDriver, "Away Home must preserve the room's only frame driver.");
        Assert.IsFalse(home.OwnsLiveFrameDriver);

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
        Assert.IsFalse(home.IsDangoAway);
        Assert.IsTrue(home.OwnsLiveFrameDriver);
        Assert.IsFalse(driver.OwnsRuntime);
        Assert.AreNotSame(owner, runtime.FrameDriver);
        AssertAppearance(runtime, E<PorchSceneView>(home, "PorchScene").LiveBody, id);
        host.ShowControlCenter();
        WaitUntil(() => !home.IsInPorch && E<DeformableDangoVisual>(home, "LiveDangoVisual").Visibility == Visibility.Visible);
        AssertAppearance(runtime, E<DeformableDangoVisual>(home, "LiveDangoVisual"), id);
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
