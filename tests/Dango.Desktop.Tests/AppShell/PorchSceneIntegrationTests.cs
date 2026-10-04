using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Core.Animation;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class PorchSceneIntegrationTests
{
    [TestMethod]
    public void PorchReviewCannotBeCombinedWithOtherReviewModes()
    {
        App.ValidateIsolatedReviewSelection(["--porch-scene-review"]);
        foreach (var other in new[] { "--home-ui-review", "--settings-ui-review", "--physics-host-review", "--eye-hat-review", "--unified-live-home-review" })
            Assert.Throws<ArgumentException>(() => App.ValidateIsolatedReviewSelection(["--porch-scene-review", other]));
    }

    [STATestMethod]
    public void PorchOccupiesTheWholeStageRatherThanAnEditorColumn()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = WindowFor(home);
        try
        {
            window.Show();
            Enter(home, window);
            var porch = (FrameworkElement)home.FindName("PorchContent");
            var stage = (FrameworkElement)home.FindName("Stage");
            Assert.AreEqual(0, Grid.GetColumn(porch), "The porch is a world viewport, not a right sidebar.");
            Assert.AreEqual(3, Grid.GetColumnSpan(porch));
            Assert.AreEqual(stage.ActualWidth, porch.ActualWidth, .5);
            Assert.IsNotNull(home.FindName("PorchScene"), "The viewport must render doors and the same live entity.");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void FictionalDoorsRequireExplicitReviewModeAndCannotEnterRooms()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = WindowFor(home);
        try
        {
            window.Show();
            var scene = home.FindName("PorchScene");
            Assert.IsNotNull(scene, "Missing world scene is not an empty real directory.");
            Assert.AreEqual(0, Property<int>(scene, "DoorCount"));
            var setReview = typeof(ProductHomeView).GetMethod("SetPorchReviewMode", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(setReview, "Review fixtures must be explicitly enabled and isolated.");
            setReview.Invoke(home, [true]);
            Assert.AreEqual(4, Property<int>(scene, "DoorCount"));
            Enter(home, window);
            Assert.IsTrue(Property<bool>(scene, "IsReviewMode"));
            Assert.IsFalse(Property<bool>(scene, "CanEnterRoom"), "A preview door is not admission.");
            Assert.AreEqual(Property<double>(scene, "BackWallGroundY"), Property<double>(scene, "DoorBottomY"), .01);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void InputControlsOwnEscapeAndWAndDoorClickRestoresGameplayFocus()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = WindowFor(home);
        try
        {
            window.Show();
            home.SetPorchReviewMode(true);
            Enter(home, window);
            var stage = (Grid)home.FindName("Stage");
            var input = new TextBox { Width = 80, Height = 28 };
            stage.Children.Add(input);
            input.Focus();
            Assert.IsFalse(Press(home, window, Key.Escape).Handled);
            Assert.IsTrue(home.IsInPorch);
            Assert.IsFalse(Press(home, window, Key.W).Handled);
            stage.Children.Remove(input);
            ((Button)home.FindName("BackFromPorchButton")).Focus();
            var scene = (PorchSceneView)home.FindName("PorchScene");
            // Exercise the scene's handled-event path, which previously bypassed Stage's focus restoration.
            typeof(PorchSceneView).GetMethod("OnMouseLeftButtonDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(scene, [new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Mouse.MouseDownEvent }]);
            Assert.IsTrue(stage.IsKeyboardFocused);
            Assert.IsTrue(home.IsInPorch);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void LatchedPhysicsFaultDoesNotPreventLocalNavigationOrShowASavedBody(bool faultInPorch)
    {
        using var fixture = new NativeHome();
        fixture.Advance(90);
        if (faultInPorch) { Enter(fixture.Home, fixture.Window); fixture.Advance(60); }
        typeof(StackedDangoSceneRuntime).GetProperty(nameof(StackedDangoSceneRuntime.LastError))!
            .SetValue(fixture.Runtime, "fictional latched physics fault");
        var stepsBeforeFault = fixture.Scheduler.Snapshot.SuccessfulSteps;
        if (faultInPorch) Press(fixture.Home, fixture.Window, Key.Escape);
        Enter(fixture.Home, fixture.Window);
        fixture.Advance(10);
        var scene = (PorchSceneView)fixture.Home.FindName("PorchScene");
        Assert.AreEqual(Visibility.Collapsed, scene.LiveBody.Visibility);
        Assert.AreEqual(stepsBeforeFault, fixture.Scheduler.Snapshot.SuccessfulSteps);
        Assert.AreEqual(DirectionalControlIntent.None, LiveIntent(fixture.Home));
        fixture.Home.ShowSettingsPage();
        Assert.IsTrue(fixture.Home.IsSettingsOpen);
        fixture.Home.ShowHomePage();
        Assert.IsFalse(fixture.Home.IsInPorch);
        Assert.AreEqual(Visibility.Collapsed, ((UIElement)fixture.Home.FindName("LiveDangoVisual")).Visibility,
            "A latched fault cannot redisplay the last healthy home body.");
        Assert.IsNull(fixture.Home.RoomNavigation.Authority.RoomId);
        fixture.Home.ShowSettingsPage();
        fixture.Runtime.RegenerateDango(); // The existing explicit Restore Dango setting.
        fixture.Home.ShowHomePage();
        fixture.Advance(90);
        Assert.IsNull(fixture.Runtime.LastError);
        Assert.IsTrue(fixture.Scheduler.Snapshot.SuccessfulSteps > stepsBeforeFault + 45,
            "Explicit runtime recovery must release the UI fault latch and resume real home frames.");
        Assert.AreEqual(Visibility.Visible, ((UIElement)fixture.Home.FindName("LiveDangoVisual")).Visibility);
        var stepsBeforeReentry = fixture.Scheduler.Snapshot.SuccessfulSteps;
        Enter(fixture.Home, fixture.Window); fixture.Advance(90);
        AssertLivePorchFrames(fixture, scene, stepsBeforeReentry, "explicit fault recovery then porch entry");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void JumpReleaseAfterFocusMovesToButtonClearsIntentAndKeepsNativeButtonSpace()
    {
        using var fixture = new NativeHome();
        fixture.Advance(90);
        Enter(fixture.Home, fixture.Window); fixture.Advance(60);
        Assert.IsTrue(Press(fixture.Home, fixture.Window, Key.Space).Handled);
        fixture.Advance(1);
        var button = (Button)fixture.Home.FindName("BackFromPorchButton");
        button.Focus();
        Release(fixture.Home, fixture.Window, Key.Space);
        fixture.Advance(1);
        Assert.AreEqual(DirectionalControlIntent.None, LiveIntent(fixture.Home), "Key release must clear jump even after an internal focus change.");
        Assert.IsFalse(Press(fixture.Home, fixture.Window, Key.Space).Handled,
            "A new Space on a focused button belongs to that button.");
        Assert.IsFalse(Release(fixture.Home, fixture.Window, Key.Space).Handled);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void OldModuleRunsHomeButRuntimeResetCannotPretendPorchCapabilitiesExist()
    {
        using var fixture = new NativeHome(Environment.GetEnvironmentVariable("DANGO_PHYSICS_BASELINE_PATH")
            ?? throw new InvalidOperationException("The unchanged accepted native baseline is required."));
        fixture.Advance(90);
        Assert.AreEqual(Visibility.Visible, ((UIElement)fixture.Home.FindName("LiveDangoVisual")).Visibility);
        Enter(fixture.Home, fixture.Window); fixture.Advance(10);
        var scene = (PorchSceneView)fixture.Home.FindName("PorchScene");
        Assert.AreEqual(Visibility.Collapsed, scene.LiveBody.Visibility);
        Assert.IsNull(fixture.Runtime.LastError, "A missing optional capability is separate from a runtime step fault.");
        fixture.Runtime.RegenerateDango();
        var before = fixture.Scheduler.Snapshot.SuccessfulSteps;
        fixture.Advance(90);
        Assert.AreEqual(before, fixture.Scheduler.Snapshot.SuccessfulSteps);
        Assert.AreEqual(Visibility.Collapsed, scene.LiveBody.Visibility);
        Assert.IsNotNull(typeof(ProductHomeView).GetField("liveSurfaceFailure", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Home));
        Press(fixture.Home, fixture.Window, Key.Escape); fixture.Advance(90);
        Assert.AreEqual(Visibility.Visible, ((UIElement)fixture.Home.FindName("LiveDangoVisual")).Visibility);
        Assert.IsNull(fixture.Home.RoomNavigation.Authority.RoomId);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void RealPorchFramesShareEntityCameraAndThreeViewportLayouts()
    {
        using var fixture = new NativeHome();
        var home = fixture.Home;
        fixture.Advance(90);
        var entity = fixture.Runtime.EntityIds.Single();
        Enter(home, fixture.Window);
        fixture.Advance(90);
        var scene = (PorchSceneView)home.FindName("PorchScene");
        Assert.AreEqual(entity, scene.LiveBody.Tag);
        Assert.AreEqual(Visibility.Visible, scene.LiveBody.Visibility);
        Assert.IsTrue(scene.InteractNearestDoor());
        Assert.IsNull(home.RoomNavigation.Authority.RoomId);
        Assert.IsFalse(home.RoomNavigation.CanSendRoomInput);
        Save(home, "review-960.png");
        var initial = scene.PetWorldX;
        Press(home, fixture.Window, Key.D);
        fixture.Advance(330);
        Release(home, fixture.Window, Key.D);
        Assert.IsTrue(scene.PetWorldX > initial + 140, "The real entity, not only background, must move.");
        Assert.IsTrue(scene.CameraX > 0);
        var raw = DangoHostFrameBinding.Bounds(fixture.Runtime.LastFrame!, 0);
        Assert.AreEqual(raw.Center.X, scene.PetWorldX, .001);
        var screen = scene.LiveBody.TranslatePoint(new System.Windows.Point(80, 60), scene);
        Assert.AreEqual((raw.Center.X - scene.CameraX) * scene.SceneScale, screen.X, .5);
        Save(home, "review-follow.png");
        foreach (var (preset, width, height, name) in new[]
        {
            (ControlCenterWindowPreset.Landscape1440p, 1280d, 720d, "review-1280.png"),
            (ControlCenterWindowPreset.Portrait, 520d, 820d, "review-portrait.png"),
        })
        {
            home.SetWindowPreset(preset);
            fixture.Window.Width = width; fixture.Window.Height = height;
            fixture.Window.UpdateLayout(); fixture.Advance(15);
            Assert.AreEqual(3, Grid.GetColumnSpan((UIElement)home.FindName("PorchContent")));
            Assert.AreEqual(scene.BackWallGroundY, scene.DoorBottomY, .001);
            Assert.AreEqual(entity, fixture.Runtime.EntityIds.Single());
            Save(home, name);
        }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void LongPhysicalMovementStaysInsideWorldAndRepeatedRoundTripsDoNotAccumulateOffset()
    {
        using var fixture = new NativeHome();
        fixture.Advance(90);
        var entity = fixture.Runtime.EntityIds.Single();
        Enter(fixture.Home, fixture.Window);
        fixture.Advance(60);
        var scene = (PorchSceneView)fixture.Home.FindName("PorchScene");
        Press(fixture.Home, fixture.Window, Key.D);
        fixture.Advance(1600);
        Release(fixture.Home, fixture.Window, Key.D);
        fixture.Advance(90);
        Assert.IsTrue(scene.PetWorldX > 1750, "The last door must be physically reachable.");
        Assert.IsTrue(scene.PetWorldX < 2100, "A real boundary must stop long-held movement.");
        Assert.IsNull(fixture.Runtime.LastError);
        for (var i = 0; i < 3; i++)
        {
            var saved = scene.PetWorldX;
            Press(fixture.Home, fixture.Window, Key.Escape);
            fixture.Advance(90);
            var stepsBeforeEntry = fixture.Scheduler.Snapshot.SuccessfulSteps;
            Enter(fixture.Home, fixture.Window);
            Assert.AreEqual(fixture.Runtime.CurrentSelectedCenter!.Value.X, scene.PetWorldX, .001,
                "Even before the next rendered frame, resumed position must reflect the real handoff placement.");
            fixture.Advance(90);
            AssertLivePorchFrames(fixture, scene, stepsBeforeEntry, $"round trip {i + 1}");
            Assert.AreEqual(saved, scene.PetWorldX, 12, "Rehosting must not add native CoreX twice.");
            Assert.AreEqual(entity, fixture.Runtime.EntityIds.Single());
            Assert.IsNull(fixture.Runtime.LastError);
        }
        var hiddenPosition = scene.PetWorldX;
        fixture.Window.Hide();
        Assert.IsFalse(fixture.Home.OwnsLiveFrameDriver);
        fixture.Home.ShowHomePage(); // Tray-style presentation change while no frame owner exists.
        var stepsBeforeRestore = fixture.Scheduler.Snapshot.SuccessfulSteps;
        fixture.Window.Show(); fixture.Advance(90);
        Assert.IsTrue(fixture.Scheduler.Snapshot.SuccessfulSteps > stepsBeforeRestore + 45,
            "Restoring Home must resume real steps, not just display a saved projection.");
        var stepsBeforeReentry = fixture.Scheduler.Snapshot.SuccessfulSteps;
        Enter(fixture.Home, fixture.Window); fixture.Advance(90);
        AssertLivePorchFrames(fixture, scene, stepsBeforeReentry, "hidden Home restore then porch reentry");
        Assert.AreEqual(hiddenPosition, scene.PetWorldX, 12);
        Assert.AreEqual(entity, fixture.Runtime.EntityIds.Single());
        Assert.IsTrue(Press(fixture.Home, fixture.Window, Key.A).Handled, "Restored stage must own A input.");
        var stepsBeforeLeftMovement = fixture.Scheduler.Snapshot.SuccessfulSteps;
        fixture.Advance(2300);
        Release(fixture.Home, fixture.Window, Key.A);
        fixture.Advance(90);
        Save(fixture.Home, "review-left-boundary.png");
        AssertLivePorchFrames(fixture, scene, stepsBeforeLeftMovement, "left movement after hidden restore");
        Assert.IsTrue(scene.PetWorldX > 60 && scene.PetWorldX < 230,
            $"left x={scene.PetWorldX}, error={fixture.Runtime.LastError}, camera={scene.CameraX}, center={fixture.Runtime.CurrentSelectedCenter}, faults={fixture.Runtime.FrameFaultState}, scheduler={fixture.Scheduler.Snapshot}, intent={typeof(ProductHomeView).GetField("liveIntent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Home)}");
        Assert.IsNull(fixture.Runtime.LastError);
    }

    private static void AssertLivePorchFrames(NativeHome fixture, PorchSceneView scene, long stepsBefore, string context)
    {
        Assert.IsTrue(fixture.Home.OwnsLiveFrameDriver, context);
        var nativeFailure = typeof(ProductHomeView).GetField("liveSurfacePhysicsError", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Home);
        Assert.IsNull(typeof(ProductHomeView).GetField("liveSurfaceFailure", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Home), $"{context}: {nativeFailure}; center={fixture.Runtime.CurrentSelectedCenter}");
        Assert.AreEqual(Visibility.Visible, scene.LiveBody.Visibility, context);
        Assert.IsTrue(fixture.Scheduler.Snapshot.SuccessfulSteps > stepsBefore + 45,
            $"{context}: real steps must advance; before={stepsBefore}, after={fixture.Scheduler.Snapshot.SuccessfulSteps}.");
        Assert.IsNotNull(fixture.Runtime.LastFrame, context);
        Assert.AreEqual(DangoHostFrameBinding.Bounds(fixture.Runtime.LastFrame!, 0).Center.X, scene.PetWorldX, .001, context);
    }

    private sealed class Source : IHostFrameSource
    {
        public event EventHandler? Rendering;
        internal void Raise(long ticks) => Rendering?.Invoke(this, (EventArgs)Activator.CreateInstance(typeof(RenderingEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [TimeSpan.FromTicks(ticks)], null)!);
    }

    private sealed class NativeHome : IDisposable
    {
        internal readonly ProductHomeView Home = new() { ReduceMotion = true };
        internal readonly Window Window;
        internal readonly StackedDangoSceneRuntime Runtime;
        internal readonly HostFrameScheduler Scheduler;
        private readonly Source source = new();
        private long ticks;
        internal NativeHome(string? modulePath = null)
        {
            var path = modulePath ?? Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
            Assert.IsTrue(File.Exists(path), "Native acceptance requires the explicit isolated module.");
            Runtime = StackedDangoSceneRuntime.CreateProduct(path);
            Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
            Home.BindLiveScene(new DesktopSceneSession(), Runtime);
            Func<T> Method<T>(string name) => typeof(ProductHomeView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.CreateDelegate<Func<T>>(Home);
            var apply = typeof(ProductHomeView).GetMethod("ApplyLiveFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Action<StackedPhysicsPresentationFrame>>(Home);
            var scheduler = new HostFrameScheduler(Runtime, Method<bool>("PrepareLiveFrame"), Method<PhysicsPresentationInput>("CreateLiveInput"), apply,
                source, () => ticks, TimeSpan.TicksPerSecond);
            Scheduler = scheduler;
            typeof(ProductHomeView).GetField("liveScheduler", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Home, scheduler);
            Home.SetPorchReviewMode(true);
            Window = WindowFor(Home);
            Window.Show();
        }
        internal void Advance(int frames)
        {
            for (var i = 0; i < frames; i++) { ticks += HostFrameClock.FixedStep.Ticks; source.Raise(ticks); }
            Window.UpdateLayout();
        }
        public void Dispose() { Window.Close(); Home.ReleaseLiveScene(); Runtime.Dispose(); }
    }

    private static void Save(ProductHomeView home, string name)
    {
        home.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(home.ActualWidth), (int)Math.Ceiling(home.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(home);
        using var stream = TestEvidence.Create("porch-scene-e1", name);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
    }

    internal static Window WindowFor(ProductHomeView home) => new()
    { Content = home, Width = 960, Height = 540, Left = -10000, Top = -10000, ShowInTaskbar = false };

    private static void Enter(ProductHomeView home, Window window)
    {
        ((FrameworkElement)home.FindName("Stage")).Focus();
        Press(home, window, Key.W);
        window.UpdateLayout();
        Assert.IsTrue(home.IsInPorch);
    }

    private static KeyEventArgs Press(ProductHomeView home, Window window, Key key)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        typeof(ProductHomeView).GetMethod("OnPreviewKeyDown", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(home, [home, e]);
        return e;
    }

    private static KeyEventArgs Release(ProductHomeView home, Window window, Key key)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key)
        { RoutedEvent = Keyboard.PreviewKeyUpEvent };
        home.RaiseEvent(e);
        return e;
    }

    private static T Property<T>(object target, string name) => (T)target.GetType()
        .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;

    private static DirectionalControlIntent LiveIntent(ProductHomeView home) => (DirectionalControlIntent)typeof(ProductHomeView)
        .GetField("liveIntent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(home)!;
}
