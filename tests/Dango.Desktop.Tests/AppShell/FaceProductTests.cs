using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.StandardWindow;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Core.Coordinates;
using Dango.Physics;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Tests.Physics.Presentation.Attachments;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class FaceProductTests
{
    private static string NewPath()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "face-implementation", "product-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); return Path.Combine(root, "appearance.json");
    }
    private static DangoAppearanceConfiguration Choice => new(new(0, 0, 0), new(0, 0, 0), new(137, -83, 33, 12.5));
    private static T E<T>(ProductHomeView home, string name) where T : class => (T)home.FindName(name);
    private static void Click(ProductHomeView home, string name) => E<Button>(home, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static T Field<T>(object host, string name) => (T)host.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
    private static ControlCenterWindow HomeWindow(DangoApplicationHost host) =>
        (ControlCenterWindow)host.GetType().GetMethod("GetOrCreateControlCenter", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(host, null)!;
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }

    [STATestMethod]
    public void DialogCancelAndSliderPreviewNeverWriteThenApplyRestoresAllSixFields()
    {
        var path = NewPath(); var session = new AppearanceSettingsSession(path); var home = new ProductHomeView();
        home.BindAppearance(session); home.ShowEditor(HomeEditor.Face);
        home.AppearanceColorPicker = _ => null;
        Click(home, "BodyColorPickerButton"); Click(home, "EyeColorPickerButton");
        Assert.AreEqual(DangoAppearanceConfiguration.Default, session.DraftAppearance);
        home.AppearanceColorPicker = _ => new(0, 0, 0);
        Click(home, "BodyColorPickerButton"); Click(home, "EyeColorPickerButton");
        E<Slider>(home, "EyeSizeSlider").Value = 137; E<Slider>(home, "EyeOvalSlider").Value = -83;
        E<Slider>(home, "EyeHeightSlider").Value = 33; E<Slider>(home, "EyeSpacingSlider").Value = 12.5;
        Assert.AreEqual(Choice, session.DraftAppearance); Assert.IsFalse(File.Exists(path));
        Click(home, "ApplyButton");
        Assert.AreEqual(Choice, session.SavedAppearance); Assert.AreEqual(Choice, new AppearanceSettingsSession(path).SavedAppearance);
        Assert.IsFalse(session.HasPendingSave);
        Click(home, "DefaultAppearanceButton"); Assert.AreEqual(DangoAppearanceConfiguration.Default, session.DraftAppearance);
        Assert.AreEqual(Choice, new AppearanceSettingsSession(path).SavedAppearance);
        Click(home, "CancelAppearanceButton"); Assert.AreEqual(Choice, session.DraftAppearance);
    }

    [STATestMethod]
    public void TabsRemainReusableAndAnOldDialogCannotReviveCancelledDraft()
    {
        var session = new AppearanceSettingsSession(NewPath()); var home = new ProductHomeView(); home.BindAppearance(session); home.ShowEditor(HomeEditor.Face);
        Click(home, "AppearanceEyesTab"); Click(home, "AppearanceColorsTab"); Click(home, "AppearanceEyesTab");
        Assert.AreEqual(Visibility.Visible, E<StackPanel>(home, "AppearanceEyesPanel").Visibility);
        home.AppearanceColorPicker = _ => { home.CloseEditor(); return new(0, 0, 0); };
        Click(home, "BodyColorPickerButton"); Assert.AreEqual(session.SavedAppearance, session.DraftAppearance); Assert.IsFalse(session.IsDirty);
    }

    [STATestMethod]
    [DataRow("Close")]
    [DataRow("Wardrobe")]
    [DataRow("Settings")]
    [DataRow("Hide")]
    public void LeavingFaceCancelsTheWholeDraft(string action)
    {
        var session = new AppearanceSettingsSession(NewPath()); session.StagePreview(Choice); Assert.IsTrue(session.TrySave());
        var home = new ProductHomeView { ReduceMotion = true }; home.BindAppearance(session);
        var window = new Window { Content = home, Width = 1024, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show(); home.ShowEditor(HomeEditor.Face); Click(home, "DefaultAppearanceButton");
            Assert.AreEqual(DangoAppearanceConfiguration.Default, session.DraftAppearance);
            switch (action) { case "Close": home.CloseEditor(); break; case "Wardrobe": home.ShowEditor(HomeEditor.Wardrobe); break; case "Settings": home.ShowSettingsPage(); break; default: window.Hide(); break; }
            Assert.AreEqual(Choice, session.DraftAppearance); Assert.IsFalse(session.IsDirty);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void RealFileLockShowsFailureKeepsDraftAndAllowsSameButtonRetry()
    {
        var path = NewPath(); var session = new AppearanceSettingsSession(path); session.StagePreview(Choice); Assert.IsTrue(session.TrySave());
        var original = File.ReadAllBytes(path); var home = new ProductHomeView(); home.BindAppearance(session); home.ShowEditor(HomeEditor.Face);
        Click(home, "DefaultAppearanceButton");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Click(home, "ApplyButton"); Assert.AreEqual(Choice, session.SavedAppearance); Assert.AreEqual(DangoAppearanceConfiguration.Default, session.DraftAppearance);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path)); Assert.IsTrue(E<Button>(home, "ApplyButton").IsEnabled);
            StringAssert.Contains(E<TextBlock>(home, "AppearanceStatus").Text, "未保存");
        }
        Click(home, "ApplyButton"); Assert.AreEqual(DangoAppearanceConfiguration.Default, new AppearanceSettingsSession(path).SavedAppearance);
    }

    [STATestMethod]
    public void HostLoadsBeforeInitializationAndMainSettingsSaveCannotApplyAppearance()
    {
        var path = NewPath(); var saved = new AppearanceSettingsSession(path); saved.StagePreview(Choice); Assert.IsTrue(saved.TrySave()); var original = File.ReadAllBytes(path);
        var settingsPath = Path.Combine(Path.GetDirectoryName(path)!, "settings.json");
        using (var host = new DangoApplicationHost(settingsFile: settingsPath, roomModulePath: settingsPath + ".missing.dll"))
        {
            var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime"); Assert.AreEqual(Choice, runtime.EffectiveAppearance); Assert.AreEqual(0, runtime.EntityIds.Count);
            var home = HomeWindow(host).ProductHome; Assert.AreEqual(Choice, home.DraftAppearance); home.ShowEditor(HomeEditor.Face); Click(home, "DefaultAppearanceButton");
            host.GetType().GetMethod("SaveSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(host, null);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path)); Assert.IsTrue(home.BoundAppearance!.IsDirty);
        }
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
    }

    [STATestMethod]
    public void FocusedSlidersNeverMoveJumpOrEnterAndEscapeCancels()
    {
        var home = new ProductHomeView { ReduceMotion = true }; home.BindAppearance(new AppearanceSettingsSession(NewPath()));
        var window = new Window { Content = home, Width = 1024, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show(); home.ShowEditor(HomeEditor.Face); Click(home, "AppearanceEyesTab"); E<Slider>(home, "EyeHeightSlider").Focus();
            foreach (var key in new[] { Key.A, Key.D, Key.S, Key.Space, Key.W })
            {
                var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent }; home.RaiseEvent(args);
                Assert.IsTrue(home.IsEditing); Assert.IsFalse(home.IsInPorch); Assert.AreEqual(HomeEditor.Face, home.SelectedEditor); Assert.IsFalse(args.Handled);
            }
            var esc = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent }; home.RaiseEvent(esc);
            Assert.IsTrue(esc.Handled); Assert.IsFalse(home.IsEditing);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void BindingPreservesFallbackAndOnlyCustomizesPrimaryEntity()
    {
        var body = new DeformableDangoVisual(); var hat = new DangoHatVisual();
        var single = new StackedPhysicsPresentationFrame([EyeMotionTests.Body()], [new LogicalRect(0, 0, 160, 120)], [new LogicalVector(0, 0)], 0, [], true, 0);
        DangoHostFrameBinding.Apply(body, hat, single, 0, bodyFillOverride: System.Windows.Media.Brushes.Black, eyeFillOverride: System.Windows.Media.Brushes.Black, appearanceEntity: new(1));
        Assert.AreEqual(Visibility.Visible, body.Visibility);
        var pair = new StackedPhysicsPresentationFrame([EyeMotionTests.Body(), EyeMotionTests.Body()], [new LogicalRect(0, 0, 160, 120), new LogicalRect(160, 0, 160, 120)], [new LogicalVector(0, 0), new LogicalVector(0, 0)], 0, [], false, 0)
        { EntityIds = [new(1), new(2)], ColorSlots = [0, 1] };
        DangoHostFrameBinding.Apply(body, hat, pair, 0, bodyFillOverride: System.Windows.Media.Brushes.Black, eyeFillOverride: System.Windows.Media.Brushes.Black, appearanceEntity: new(1));
        Assert.AreSame(System.Windows.Media.Brushes.Black, body.BodyFill); Assert.AreSame(System.Windows.Media.Brushes.Black, body.EyeFill);
        DangoHostFrameBinding.Apply(body, hat, pair, 1, bodyFillOverride: System.Windows.Media.Brushes.Black, eyeFillOverride: System.Windows.Media.Brushes.Black, appearanceEntity: new(1));
        Assert.AreEqual(System.Windows.Media.Color.FromRgb(201, 226, 255), ((SolidColorBrush)body.BodyFill).Color);
        Assert.AreEqual(System.Windows.Media.Color.FromRgb(0x2B, 0x20, 0x1E), ((SolidColorBrush)body.EyeFill).Color);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void DefaultContinuousBodyKeepsSavedAppearanceAcrossHomePorchAndPreview()
    {
        var path = NewPath();
        var session = new AppearanceSettingsSession(path);
        session.StagePreview(Choice); Assert.IsTrue(session.TrySave());
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(Path.GetDirectoryName(path)!, "settings.json"));
        host.ShowControlCenter();
        ProductHostTestSupport.WaitForHome(host);
        var window = HomeWindow(host);
        var home = window.ProductHome;
        home.ReduceMotion = true;
        var owner = home.BoundBeltBody!;
        var epoch = home.DisplayedBeltSnapshot!.Epoch;
        var bodyEpoch = home.DisplayedBeltSnapshot.BodyEpoch;
        Assert.AreEqual(Choice, home.BoundBeltPresenter!.Appearance);
        Assert.IsNull(home.BoundRuntime, "The home must not start the retired product world.");
        home.ShowEditor(HomeEditor.Face);
        E<Slider>(home, "EyeSizeSlider").Value = 110;
        Assert.AreEqual(110, home.BoundBeltPresenter.Appearance.EyeLayout.SizePercent);
        Assert.AreEqual(bodyEpoch, home.DisplayedBeltSnapshot.BodyEpoch, "Eye edits cannot replace the physical body.");
        Click(home, "PreviewSleepButton");
        Wait(() => home.BoundBeltPresenter.Inspect().Body?.Attachments?.Face.Left.Openness <= .03);
        Click(home, "PreviewSleepButton");
        Wait(() => home.BoundBeltPresenter.Inspect().Body?.Attachments?.Face.Left.Openness > .9);
        home.CloseEditor();
        Assert.AreEqual(Choice, home.BoundBeltPresenter.Appearance);
        var steps = owner.Metrics.NativeStepCalls;
        Assert.IsTrue(Press(home, window, Key.F).Handled);
        Wait(() => home.IsInPorch && owner.Metrics.NativeStepCalls > steps);
        Assert.AreSame(owner, home.BoundBeltBody);
        Assert.AreEqual(epoch, home.DisplayedBeltSnapshot.Epoch);
        Assert.AreEqual(Choice, home.BoundBeltPresenter.Appearance);
        Assert.IsFalse(E<PorchSceneView>(home, "PorchScene").CanEnterRoom,
            "Local catalog entries do not fabricate formal multiplayer admission.");
        Assert.IsNull(home.RoomNavigation.Authority.RoomId);
        Assert.IsFalse(home.RoomNavigation.CanSendRoomInput);
        Assert.IsTrue(Press(home, window, Key.Escape).Handled);
        Wait(() => !home.IsInPorch && home.BoundBeltPresenter.View.IsVisible);
        Assert.AreSame(owner, home.BoundBeltBody);
        Assert.AreEqual(epoch, home.DisplayedBeltSnapshot.Epoch);
        Assert.AreEqual(Choice, new AppearanceSettingsSession(path).SavedAppearance);
    }

    private static void Wait(Func<bool> condition)
    {
        var until = Environment.TickCount64 + 8000;
        while (!condition() && Environment.TickCount64 < until) Pump(20);
        Assert.IsTrue(condition(), "The actual continuous body presentation did not reach its gate.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("LegacyModuleIntegration")]
    public void ExistingWindowAndWallpaperHostsKeepAppearanceAcrossExclusiveFrameHandoffs()
    {
        // Exercise main's existing desktop hosts independently. Product still forbids
        // showing them before room admission; this harness does not change that gate.
        var path = NewPath(); var appearance = new AppearanceSettingsSession(path);
        appearance.StagePreview(Choice); Assert.IsTrue(appearance.TrySave());
        using var runtime = StackedDangoSceneRuntime.CreateProduct(Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName));
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var sceneSession = new DesktopSceneSession();
        var home = new ProductHomeView { ReduceMotion = true }; home.BindLiveScene(sceneSession, runtime); home.BindAppearance(appearance);
        var homeWindow = new Window { Content = home, Width = 960, Height = 540, Left = -10000, Top = -10000, ShowInTaskbar = false };
        var sceneWindow = new DangoSceneWindow(sceneSession, runtime) { Left = -10000, Top = -10000, ShowInTaskbar = false };
        var wallpaper = new WorkerWWindow(sceneSession, runtime);
        var homeScheduler = Field<HostFrameScheduler>(home, "liveScheduler");
        var windowScheduler = Field<HostFrameScheduler>(sceneWindow, "motionScheduler");
        var wallpaperScheduler = Field<HostFrameScheduler>(wallpaper, "motionScheduler");
        try
        {
            homeWindow.Show(); Pump(350); var id = runtime.EntityIds.Single();
            AssertOnlyDriver(runtime, homeScheduler, windowScheduler, wallpaperScheduler);
            AssertLiveAppearance(runtime, E<DeformableDangoVisual>(home, "LiveDangoVisual"), id);
            homeWindow.Hide(); Assert.IsNull(runtime.FrameDriver);
            sceneWindow.SetVisibility(true); Pump(350);
            AssertOnlyDriver(runtime, windowScheduler, homeScheduler, wallpaperScheduler);
            Assert.IsTrue(windowScheduler.Snapshot.SuccessfulSteps > 0, "Window must produce real native frames.");
            AssertLiveAppearance(runtime, (DeformableDangoVisual)sceneWindow.FindName("DangoVisual"), id);
            sceneWindow.SetVisibility(false); Assert.IsNull(runtime.FrameDriver);
            wallpaper.SetVisibility(true); Pump(500);
            Assert.IsTrue(wallpaper.GetSnapshot().IsVisible, wallpaper.GetSnapshot().LastError);
            AssertOnlyDriver(runtime, wallpaperScheduler, homeScheduler, windowScheduler);
            Assert.IsTrue(wallpaperScheduler.Snapshot.SuccessfulSteps > 0, "Wallpaper must produce real native frames.");
            AssertLiveAppearance(runtime, (DeformableDangoVisual)wallpaper.FindName("DangoVisual"), id);
            wallpaper.ApplyLayeringSettings(DesktopLayeringSettings.CreateDefault() with { DangoLayer = DesktopObjectLayer.AboveDesktopIcons }); Pump(100);
            var overlay = Field<DesktopDangoSceneOverlayWindow>(wallpaper, "dangoAboveWindow");
            Assert.IsTrue(overlay.IsVisible);
            AssertLiveAppearance(runtime, Field<DeformableDangoVisual[]>(overlay, "bodies")[0], id);
            AssertOnlyDriver(runtime, wallpaperScheduler, homeScheduler, windowScheduler);
            wallpaper.SetVisibility(false); Assert.IsNull(runtime.FrameDriver);
            var steps = homeScheduler.Snapshot.SuccessfulSteps;
            homeWindow.Show(); Pump(350);
            AssertOnlyDriver(runtime, homeScheduler, windowScheduler, wallpaperScheduler);
            Assert.IsTrue(homeScheduler.Snapshot.SuccessfulSteps > steps, "Returning home must resume the same physical world.");
            AssertLiveAppearance(runtime, E<DeformableDangoVisual>(home, "LiveDangoVisual"), id);
            runtime.RegenerateDango(); Pump(350);
            AssertLiveAppearance(runtime, E<DeformableDangoVisual>(home, "LiveDangoVisual"), id);
            AssertOnlyDriver(runtime, homeScheduler, windowScheduler, wallpaperScheduler);
        }
        finally
        {
            homeWindow.Close(); home.ReleaseLiveScene();
            sceneWindow.PrepareForExit(); sceneWindow.Close(); wallpaper.Close();
        }
    }

    private static void AssertOnlyDriver(StackedDangoSceneRuntime runtime, HostFrameScheduler active, params HostFrameScheduler[] stopped)
    {
        Assert.AreSame(active, runtime.FrameDriver); Assert.IsTrue(active.IsRunning); Assert.IsTrue(active.OwnsRuntime);
        foreach (var scheduler in stopped) { Assert.IsFalse(scheduler.IsRunning); Assert.IsFalse(scheduler.OwnsRuntime); }
    }

    private static void AssertLiveAppearance(StackedDangoSceneRuntime runtime, DeformableDangoVisual visual, DangoEntityId id)
    {
        Assert.AreEqual(id, runtime.EntityIds.Single()); Assert.AreEqual(id, visual.Tag); Assert.AreEqual(id, runtime.AppearanceEntity);
        Assert.AreEqual(Choice, runtime.EffectiveAppearance); Assert.IsNull(runtime.LastError); Assert.IsNull(runtime.AttachmentError);
        Assert.IsTrue(runtime.LastPhysicsFrame!.Succeeded); Assert.AreEqual(Visibility.Visible, visual.Visibility);
        Assert.AreEqual(Color.FromRgb(0, 0, 0), ((SolidColorBrush)visual.BodyFill).Color);
        Assert.AreEqual(Color.FromRgb(0, 0, 0), ((SolidColorBrush)visual.EyeFill).Color);
        var face = runtime.LastFrame!.Bodies.Single().Attachments!.Face;
        Assert.IsTrue(face.Left.Visible && face.Right.Visible, "Saved eye controls must reach the actual native attachment frame.");
        Assert.AreEqual(12d / 160 * Choice.EyeLayout.SizePercent / 100 * face.SizeScale, face.Left.Width, 1e-8);
        Assert.AreEqual(face.Left.Width * (1 - Math.Abs(Choice.EyeLayout.OvalPercent) / 200), face.Left.Height, 1e-8);
    }

    private static KeyEventArgs Press(ProductHomeView home, Window window, Key key)
    {
        E<FrameworkElement>(home, "Stage").Focus();
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        typeof(ProductHomeView).GetMethod("OnPreviewKeyDown", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.Invoke(home, [home, args]);
        return args;
    }
}
