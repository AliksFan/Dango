using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class UnifiedLiveHomeTests
{
    [STATestMethod]
    public void WEntersEmptyPorchOnlyFromUnexpandedHomeAndEscapeReturns()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = ReviewWindow(home);
        try
        {
            window.Show();
            Assert.IsFalse(InPorch(home));

            home.ShowEditor(HomeEditor.Face);
            var editorW = Press(home, window, Key.W);
            Assert.IsFalse(editorW.Handled, "W must not navigate while editing appearance.");
            Assert.IsFalse(InPorch(home));
            Assert.IsTrue(home.IsEditing);

            var editorEscape = Press(home, window, Key.Escape);
            Assert.IsTrue(editorEscape.Handled);
            Assert.IsFalse(home.IsEditing);

            home.ShowSettingsPage();
            var settingsW = Press(home, window, Key.W);
            Assert.IsFalse(settingsW.Handled, "W must not navigate while settings are open.");
            Assert.IsFalse(InPorch(home));
            home.ShowHomePage();

            Element<FrameworkElement>(home, "Stage").Focus();
            var homeW = Press(home, window, Key.W);
            Assert.IsTrue(homeW.Handled, "The focused home owns W as interaction, not jump.");
            Assert.IsTrue(InPorch(home));
            Assert.IsTrue(Element<FrameworkElement>(home, "Stage").IsKeyboardFocused,
                "Entering the porch must leave Space available for jumping instead of activating Back.");
            Assert.AreEqual(Visibility.Visible, Element<UIElement>(home, "PorchContent").Visibility);
            Assert.IsFalse(Element<UIElement>(home, "Launchers").IsHitTestVisible,
                "Appearance launchers must not remain interactive behind the porch.");
            Assert.IsFalse(Element<UIElement>(home, "TopNavigation").IsEnabled,
                "Invisible navigation must not remain reachable by Tab and Enter.");
            Assert.IsFalse(Element<UIElement>(home, "Launchers").IsEnabled);
            Assert.IsFalse(Element<UIElement>(home, "HomeFooter").IsEnabled);

            var porchEscape = Press(home, window, Key.Escape);
            Assert.IsTrue(porchEscape.Handled);
            Assert.IsFalse(InPorch(home));
            Assert.AreNotEqual(Visibility.Visible, Element<UIElement>(home, "PorchContent").Visibility);
            Assert.IsFalse(Element<UIElement>(home, "PorchContent").IsEnabled);
            Assert.IsTrue(Element<UIElement>(home, "Launchers").IsHitTestVisible);
            Assert.IsTrue(Element<UIElement>(home, "TopNavigation").IsEnabled);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void HomeUsesLiveDeformableBodyAndColorPreviewAffectsThatBody()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = ReviewWindow(home);
        try
        {
            window.Show();
            var preview = Element<DependencyObject>(home, "PetPreview");
            var body = Descendants<DeformableDangoVisual>(preview).SingleOrDefault();
            Assert.IsNotNull(body, "The home must render the real deformable body, not the old static Path sketch.");

            home.ShowEditor(HomeEditor.Face);
            Element<Button>(home, "PeachButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(Color.FromRgb(242, 182, 168), home.PreviewColor);
            Assert.AreEqual(home.PreviewColor, ((SolidColorBrush)body.BodyFill).Color,
                "The selected color must immediately reach the live body, not just a decorative sketch.");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void EmptyPorchHasNoEnabledRoomOrDoorEntry()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = ReviewWindow(home);
        try
        {
            window.Show();
            Element<FrameworkElement>(home, "Stage").Focus();
            Press(home, window, Key.W);
            Assert.IsTrue(InPorch(home));
            var porch = Element<DependencyObject>(home, "PorchContent");
            var enabled = Descendants<Button>(porch).Where(button => button.IsEnabled).ToArray();
            Assert.IsTrue(enabled.All(button =>
                    button.Name == "BackFromPorchButton" ||
                    AutomationProperties.GetName(button).Contains("返回", StringComparison.Ordinal)),
                "Until room permissions exist, no sample door may act like an enterable room.");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void HomeADKeysToggleMirroredEditorsWithoutChangingTheLiveMovementIntent()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = ReviewWindow(home);
        try
        {
            window.Show();
            Element<FrameworkElement>(home, "Stage").Focus();

            Assert.IsTrue(Press(home, window, Key.D).Handled);
            Assert.IsTrue(home.IsEditing);
            Assert.AreEqual(HomeEditor.Face, home.SelectedEditor);
            Assert.AreEqual(2, Grid.GetColumn(Element<UIElement>(home, "EditorPanel")));

            Assert.IsTrue(Press(home, window, Key.A).Handled);
            Assert.AreEqual(HomeEditor.Wardrobe, home.SelectedEditor);
            Assert.AreEqual(0, Grid.GetColumn(Element<UIElement>(home, "EditorPanel")));

            Assert.IsTrue(Press(home, window, Key.A).Handled);
            Assert.IsFalse(home.IsEditing, "Pressing the active shortcut closes its panel.");
            Assert.IsTrue(Press(home, window, Key.W).Handled,
                "W remains the entrance shortcut after closing an editor.");
            Assert.IsTrue(home.IsInPorch);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void MovingFocusToWindowChromeReleasesHeldPhysicalInput()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var chromeButton = new Button { Content = "fictional chrome" };
        var root = new Grid();
        root.Children.Add(home);
        root.Children.Add(chromeButton);
        var window = new Window { Content = root, Width = 1024, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            Element<FrameworkElement>(home, "Stage").Focus();
            var field = typeof(ProductHomeView).GetField("liveIntent", BindingFlags.NonPublic | BindingFlags.Instance)!;
            field.SetValue(home, new DirectionalControlIntent(false, false, false, true));

            chromeButton.Focus();

            Assert.IsFalse(home.IsKeyboardFocusWithin);
            Assert.AreEqual(DirectionalControlIntent.None, Field<DirectionalControlIntent>(home, "liveIntent"),
                "Releasing S while a chrome button owns focus must not leave squash stuck down.");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ProductHomeAndBothDesktopHostsShareTheApplicationRuntime()
    {
        var settingsPath = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "unified-home",
            $"fictional-{Guid.NewGuid():N}.json");
        using var host = new DangoApplicationHost(settingsFile: settingsPath,
            nativeLibraryPath: settingsPath + ".missing.dll");
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        var sceneSession = Field<DesktopSceneSession>(host, "sceneSession");
        var workerW = Field<object>(host, "workerWWindow");
        var windowHost = Field<object>(host, "sceneWindow");
        Assert.AreSame(runtime, Field<StackedDangoSceneRuntime>(workerW, "motionRuntime"));
        Assert.AreSame(runtime, Field<StackedDangoSceneRuntime>(windowHost, "motionRuntime"));
        Assert.AreSame(sceneSession, Field<DesktopSceneSession>(workerW, "sceneSession"));
        Assert.AreSame(sceneSession, Field<DesktopSceneSession>(windowHost, "sceneSession"));

        var controlCenter = (ControlCenterWindow)typeof(DangoApplicationHost)
            .GetMethod("GetOrCreateControlCenter", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(host, null)!;
        var home = Element<ProductHomeView>(controlCenter, "ProductHome");
        var property = typeof(ProductHomeView).GetProperty("BoundRuntime",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(property, "The product home needs an observable binding to the application's one runtime.");
        Assert.AreSame(runtime, property.GetValue(home), "Opening home must not create a second physical Dango.");
        var sessionProperty = typeof(ProductHomeView).GetProperty("BoundSceneSession",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(sessionProperty, "The home must reuse the desktop hosts' motion session.");
        Assert.AreSame(sceneSession, sessionProperty.GetValue(home));
        Assert.AreEqual(0, runtime.EntityIds.Count,
            "Binding the home must not claim that the unavailable native world has started.");
    }

    [TestMethod]
    public void RoomGateRejectsDesktopVisibilityBeforeEntry()
    {
        var wallpaper = new FakeDesktopHost();
        var window = new FakeDesktopHost();
        var coordinator = new DesktopHostCoordinator(wallpaper, window);
        ForbidRoomPresentation(coordinator);

        Assert.Throws<InvalidOperationException>(() => coordinator.SetVisibility(true));
        Assert.AreEqual(0, wallpaper.ShowCalls);
        Assert.AreEqual(0, window.ShowCalls);
        Assert.IsFalse(coordinator.GetSnapshot().IsVisible);
    }

    [TestMethod]
    public void RetryInitializationDoesNotShowDesktopWhileRoomPresentationIsForbidden()
    {
        var wallpaper = new FakeDesktopHost();
        var window = new FakeDesktopHost();
        using var runtime = StackedDangoSceneRuntime.CreateProduct("fictional-missing-native.dll");
        // This unit test isolates coordinator policy from the separate native-library integration gate.
        typeof(StackedDangoSceneRuntime).GetProperty(nameof(StackedDangoSceneRuntime.InitializationState))!
            .SetValue(runtime, SceneInitializationState.Ready);
        var coordinator = new DesktopHostCoordinator(wallpaper, window, runtime);
        ForbidRoomPresentation(coordinator);

        Assert.IsTrue(coordinator.RetryInitialization());
        Assert.AreEqual(0, wallpaper.ShowCalls);
        Assert.AreEqual(0, window.ShowCalls);
        Assert.IsFalse(coordinator.GetSnapshot().IsVisible);
    }

    [STATestMethod]
    public void SelectedBodyColorSurvivesConsecutiveRealFrameBindings()
    {
        using var runtime = new StackedDangoSceneRuntime("fictional-missing-native.dll");
        runtime.SetBodyPreviewColor(Color.FromRgb(242, 182, 168));
        var previewFill = runtime.BodyPreviewFill;
        Assert.IsNotNull(previewFill);
        var attachment = new PresentationAttachment(new PresentationPoint(0, 0), 0);
        var bodyFrame = new PhysicsPresentationFrame(true, [], attachment, attachment,
            PhysicsPresentationFallbackReason.HostUnavailable,
            new PhysicsPresentationDiagnostics(PhysicsPresentationBackend.AuthoredFallback, 0,
                PhysicsPresentationDiagnosticCode.Fallback));
        var frame = new StackedPhysicsPresentationFrame(
            [bodyFrame], [new LogicalRect(0, 0, 160, 120)], [new LogicalVector(0, 0)],
            0, [], false, 0)
        {
            ColorSlots = [1]
        };
        var body = new DeformableDangoVisual();
        var hat = new DangoHatVisual();

        DangoHostFrameBinding.Apply(body, hat, frame, 0, bodyFillOverride: previewFill);
        Assert.AreSame(previewFill, body.BodyFill);
        DangoHostFrameBinding.Apply(body, hat, frame, 0, bodyFillOverride: runtime.BodyPreviewFill);
        Assert.AreSame(previewFill, body.BodyFill,
            "A subsequent physics frame must not restore the old fixed color slot.");
    }

    [STATestMethod]
    public void FailedNativeFrameNeverAppearsAsALiveHomeDango()
    {
        using var runtime = new StackedDangoSceneRuntime("fictional-missing-native.dll");
        var home = new ProductHomeView();
        home.BindLiveScene(new DesktopSceneSession(), runtime);
        var attachment = new PresentationAttachment(new PresentationPoint(0, 0), 0);
        var failedBody = new PhysicsPresentationFrame(true, [], attachment, attachment,
            PhysicsPresentationFallbackReason.NativeUnavailable,
            new PhysicsPresentationDiagnostics(PhysicsPresentationBackend.AuthoredFallback, 0,
                PhysicsPresentationDiagnosticCode.Fallback));
        var failedFrame = new StackedPhysicsPresentationFrame(
            [failedBody], [new LogicalRect(0, 0, 160, 120)], [new LogicalVector(0, 0)],
            0, [], true, 0);

        typeof(ProductHomeView).GetMethod("ApplyLiveFrame", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(home, [failedFrame]);

        Assert.AreEqual(Visibility.Collapsed, Element<UIElement>(home, "LiveDangoVisual").Visibility,
            "A fallback drawing must not impersonate a running physical Dango.");
        Assert.AreEqual(Visibility.Visible, Element<UIElement>(home, "LiveSceneStatusText").Visibility);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void LiveHomeOwnsThePhysicalFrameDriverAndReusesItsEntityAfterHide()
    {
        var modulePath = Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
        if (!File.Exists(modulePath)) Assert.Inconclusive("统一原生物理模块不在测试输出中；实时帧集成未验证。");
        using var runtime = StackedDangoSceneRuntime.CreateProduct(modulePath);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var home = new ProductHomeView { ReduceMotion = true };
        home.BindLiveScene(new DesktopSceneSession(), runtime);
        var window = ReviewWindow(home);
        try
        {
            window.Show();
            Pump(250);
            Assert.IsTrue(home.OwnsLiveFrameDriver, "Visible home must drive the shared physical runtime.");
            Assert.IsNotNull(runtime.LastFrame, "At least one native physics frame must reach the home.");
            Assert.IsFalse(runtime.LastFrame.ConstraintsReleased);
            Assert.IsNull(typeof(StackedDangoSceneRuntime)
                    .GetField("wanderScreenWidth", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime),
                "Home keeps the live body in place without autonomous horizontal wandering.");
            var entity = runtime.EntityIds.Single();
            Assert.AreSame(runtime.BodyPreviewFill, Element<DeformableDangoVisual>(home, "LiveDangoVisual").BodyFill);
            SaveImage(home, "home-live.png");
            Element<FrameworkElement>(home, "Stage").Focus();
            foreach (var (key, expected) in new[]
                     {
                         (Key.S, new DirectionalControlIntent(false, false, false, true)),
                         (Key.Space, new DirectionalControlIntent(false, false, true, false)),
                     })
            {
                Assert.IsTrue(Press(home, window, key).Handled);
                Assert.AreEqual(expected, Field<DirectionalControlIntent>(home, "liveIntent"));
                Release(home, window, key);
                Assert.AreEqual(DirectionalControlIntent.None, Field<DirectionalControlIntent>(home, "liveIntent"));
            }
            home.ShowEditor(HomeEditor.Face);
            Element<Button>(home, "PeachButton").Focus();
            Element<Button>(home, "PeachButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsTrue(Element<FrameworkElement>(home, "Stage").IsKeyboardFocused,
                "Selecting a color must leave Space available for the live Dango.");
            Assert.IsTrue(Press(home, window, Key.S).Handled, "The open face panel keeps the body responsive.");
            Assert.IsTrue(Field<DirectionalControlIntent>(home, "liveIntent").Squash);
            Release(home, window, Key.S);
            Assert.IsTrue(Press(home, window, Key.Space).Handled);
            Assert.IsTrue(Field<DirectionalControlIntent>(home, "liveIntent").Jump);
            Release(home, window, Key.Space);
            home.CloseEditor();
            Element<FrameworkElement>(home, "Stage").Focus();
            Press(home, window, Key.W);
            Pump(520);
            Assert.IsTrue(home.IsInPorch);
            Assert.IsTrue(home.OwnsLiveFrameDriver);
            Assert.IsNotNull(typeof(StackedDangoSceneRuntime)
                    .GetField("wanderScreenWidth", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime),
                "The porch may restore natural movement after leaving the anchored home.");
            Assert.AreEqual(entity, runtime.EntityIds.Single());
            Assert.IsFalse(Field<DirectionalControlIntent>(home, "liveIntent").Jump,
                "W enters the porch and must never be sent as jump.");
            foreach (var (key, expected) in new[]
                     {
                         (Key.A, new DirectionalControlIntent(true, false, false, false)),
                         (Key.D, new DirectionalControlIntent(false, true, false, false)),
                         (Key.Space, new DirectionalControlIntent(false, false, true, false)),
                     })
            {
                Assert.IsTrue(Press(home, window, key).Handled);
                Assert.AreEqual(expected, Field<DirectionalControlIntent>(home, "liveIntent"));
                Release(home, window, key);
                Assert.AreEqual(DirectionalControlIntent.None, Field<DirectionalControlIntent>(home, "liveIntent"));
            }
            SaveImage(home, "porch-live.png");

            window.Hide();
            Assert.IsFalse(home.OwnsLiveFrameDriver, "A hidden control center must release its frame driver.");
            Assert.IsFalse(runtime.IsRunning);
            window.Show();
            Pump(250);
            Assert.IsTrue(home.OwnsLiveFrameDriver);
            Assert.AreEqual(entity, runtime.EntityIds.Single(), "Returning home must not spawn a second Dango.");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ProductStartupShowsTheLiveHomeButNoDesktopHostBeforeRoomEntry()
    {
        var modulePath = Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
        if (!File.Exists(modulePath)) Assert.Inconclusive("统一原生物理模块不在测试输出中；产品启动集成未验证。");
        var settingsPath = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "unified-home",
            $"startup-{Guid.NewGuid():N}.json");
        using var host = new DangoApplicationHost(settingsFile: settingsPath, nativeLibraryPath: modulePath);
        var controlCenter = (ControlCenterWindow)typeof(DangoApplicationHost)
            .GetMethod("GetOrCreateControlCenter", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(host, null)!;
        controlCenter.Left = controlCenter.Top = -10000;
        controlCenter.ShowInTaskbar = false;

        host.Start();
        Pump(250);

        var home = Element<ProductHomeView>(controlCenter, "ProductHome");
        Assert.IsTrue(controlCenter.IsVisible);
        Assert.IsTrue(home.OwnsLiveFrameDriver);
        Assert.IsFalse(Field<DesktopHostCoordinator>(host, "desktopHost").GetSnapshot().IsVisible,
            "Product startup must keep both desktop hosts hidden until real room entry exists.");
        Assert.AreEqual(1, Field<StackedDangoSceneRuntime>(host, "stackedRuntime").EntityIds.Count);
    }

    private static Window ReviewWindow(ProductHomeView home) => new()
    {
        Content = home,
        Width = 1024,
        Height = 640,
        Left = -10000,
        Top = -10000,
        ShowInTaskbar = false,
    };

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds),
        };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void SaveImage(ProductHomeView home, string name)
    {
        var bitmap = new RenderTargetBitmap(1024, 640, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(home);
        using var stream = TestEvidence.Create("unified-live-home", name);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static bool InPorch(ProductHomeView home)
    {
        var property = typeof(ProductHomeView).GetProperty("IsInPorch", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(property, "The home needs an observable porch state for navigation and frame ownership.");
        return (bool)property.GetValue(home)!;
    }

    private static KeyEventArgs Press(ProductHomeView home, Window window, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        home.RaiseEvent(args);
        return args;
    }

    private static void Release(ProductHomeView home, Window window, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyUpEvent,
        };
        home.RaiseEvent(args);
    }

    private static T Element<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new AssertFailedException($"Missing {name}");

    private static T Field<T>(object owner, string name) =>
        (T)(owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?
            .GetValue(owner) ?? throw new AssertFailedException($"Missing {name}"));

    private static void ForbidRoomPresentation(DesktopHostCoordinator coordinator)
    {
        var method = typeof(DesktopHostCoordinator).GetMethod("SetRoomPresentationAllowed");
        Assert.IsNotNull(method, "The desktop host needs a room gate independent of UI visibility.");
        method.Invoke(coordinator, [false]);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class FakeDesktopHost : IDesktopHostControl
    {
        private bool visible;
        public int ShowCalls { get; private set; }
        public DesktopHostSnapshot GetSnapshot() => new("Wallpaper", visible, false, "fictional", null);
        public void SetVisibility(bool isVisible)
        {
            if (isVisible) ShowCalls++;
            visible = isVisible;
        }
        public void SetInteractionEnabled(bool isEnabled) { }
        public string CreateDiagnosticSummary() => "fictional";
    }
}
