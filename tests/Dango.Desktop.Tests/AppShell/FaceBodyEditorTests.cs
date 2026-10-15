using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class FaceBodyEditorTests
{
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    private static string NewPath() => Path.Combine(TestRepository.Root, "artifacts", "face-body", "f5", Guid.NewGuid().ToString("N"), "appearance.json");
    private static T E<T>(ProductHomeView home, string name) where T : class => (T)home.FindName(name);
    private static void Click(ProductHomeView home, string name) => E<Button>(home, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static ProductHomeView Home(AppearanceSettingsSession session)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        home.BindAppearance(session);
        home.ShowEditor(HomeEditor.Face);
        Click(home, "AppearanceBodyTab");
        return home;
    }

    [STATestMethod]
    public void BodySlidersOnlyStageAndCannotSaveWithoutARealPreview()
    {
        var path = NewPath(); var session = new AppearanceSettingsSession(path); var home = Home(session);
        var size = E<Slider>(home, "BodySizeSlider");
        Assert.AreEqual(80d, size.Minimum); Assert.AreEqual(120d, size.Maximum); Assert.AreEqual(100d, size.Value);
        Assert.AreEqual(1d, size.TickFrequency); Assert.IsTrue(size.IsSnapToTickEnabled);
        Assert.AreEqual(Visibility.Visible, E<StackPanel>(home, "AppearanceBodyPanel").Visibility);
        size.Value = 120; E<Slider>(home, "BodySoftnessSlider").Value = 0; E<Slider>(home, "BodyLandingBuildSlider").Value = 100;
        Assert.AreEqual(new DangoBodyControls(120, 0, 100), session.DraftAppearance.Body);
        Assert.IsFalse(E<Button>(home, "ApplyButton").IsEnabled); Assert.IsFalse(File.Exists(path));
        StringAssert.Contains(E<TextBlock>(home, "AppearancePreviewNotice").Text, "尚未更新");
        Click(home, "PreviewBodyButton");
        Assert.IsNull(session.PreviewedBody); Assert.IsTrue(session.RequiresBodyPreview); Assert.IsFalse(File.Exists(path));
        Click(home, "ApplyButton"); Assert.IsFalse(File.Exists(path));
        home.CloseEditor(); Assert.AreEqual(session.SavedAppearance, session.DraftAppearance);
    }

    [STATestMethod]
    [DataRow(640, 360)]
    [DataRow(1024, 576)]
    [DataRow(360, 640)]
    public void BodyParametersScrollIndependentlyAndKeepSaveActionsFixed(int width, int height)
    {
        var home = Home(new AppearanceSettingsSession(NewPath()));
        var size = new Size(width, height);
        void Layout() { home.Measure(size); home.Arrange(new Rect(size)); home.UpdateLayout(); }
        Rect Bounds(string name)
        {
            var element = E<FrameworkElement>(home, name);
            return element.TransformToAncestor(home).TransformBounds(new Rect(element.RenderSize));
        }
        Layout();
        foreach (var name in new[] { "BodySizeSlider", "BodySoftnessSlider", "BodyLandingBuildSlider" })
            Assert.IsTrue(Bounds(name).Width >= 50, $"{name} requires usable dragging width.");
        var save = Bounds("ApplyButton"); var tab = Bounds("AppearanceBodyTab");
        var scroller = E<ScrollViewer>(home, "OptionsBody");
        var probe = new Border { Height = 800 };
        var options = E<StackPanel>(home, "AppearanceBodyPanel"); options.Children.Add(probe);
        try
        {
            Layout(); Assert.IsTrue(scroller.ScrollableHeight > 0);
            scroller.ScrollToBottom(); home.UpdateLayout(); Assert.IsTrue(scroller.VerticalOffset > 0);
            Assert.AreEqual(save, Bounds("ApplyButton")); Assert.AreEqual(tab, Bounds("AppearanceBodyTab"));
            Assert.IsFalse(scroller.IsAncestorOf(E<Button>(home, "ApplyButton")));
        }
        finally { options.Children.Remove(probe); }
        scroller.ScrollToTop();
        Layout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(home);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var evidence = TestEvidence.Create("face-body-layout", $"body-{width}x{height}.png");
        encoder.Save(evidence);
    }

    [STATestMethod, TestCategory("NativeIntegration")]
    public void RealPreviewAppliesExactBodyColorEyesStayLiveAndSaveSurvivesReopen()
    {
        var path = NewPath(); var session = new AppearanceSettingsSession(path);
        using var runtime = StackedDangoSceneRuntime.CreateProduct(Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll"));
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var home = Home(session); home.BindLiveScene(new DesktopSceneSession(), runtime);
        E<Slider>(home, "BodySizeSlider").Value = 120;
        Assert.AreEqual(DangoBodyControls.Default, runtime.PhysicsBody);
        Click(home, "PreviewBodyButton");
        var applied = new DangoBodyControls(120, 50, 50);
        Assert.AreEqual(applied, runtime.PhysicsBody); Assert.AreEqual((applied, DangoBodyMaterial.ModelVersion), session.PreviewedBody!.Value);
        Assert.IsTrue(E<Button>(home, "ApplyButton").IsEnabled);
        var ids = runtime.EntityIds.ToArray();
        Click(home, "AppearanceEyesTab"); E<Slider>(home, "EyeSizeSlider").Value = 130;
        home.AppearanceColorPicker = _ => new(0, 0, 0); Click(home, "BodyColorPickerButton");
        CollectionAssert.AreEqual(ids, runtime.EntityIds.ToArray()); Assert.AreEqual(applied, runtime.PhysicsBody);
        Assert.AreEqual(130d, runtime.EffectiveAppearance.EyeLayout.SizePercent);
        Assert.IsFalse(session.RequiresBodyPreview);
        Click(home, "ApplyButton");
        Assert.AreEqual(session.DraftAppearance, new AppearanceSettingsSession(path).SavedAppearance);
        Click(home, "DefaultAppearanceButton"); Assert.AreEqual(applied, runtime.PhysicsBody);
        Assert.IsTrue(session.RequiresBodyPreview); Assert.IsFalse(E<Button>(home, "ApplyButton").IsEnabled);
        home.CloseEditor(); Assert.AreEqual(applied, runtime.PhysicsBody); Assert.AreEqual(session.SavedAppearance, runtime.EffectiveAppearance);
    }

    [STATestMethod, TestCategory("NativeIntegration")]
    [DataRow("Close")]
    [DataRow("Wardrobe")]
    [DataRow("Settings")]
    [DataRow("Hidden")]
    public void CancellingAPreviewRebuildsTheSavedPhysicalBody(string destination)
    {
        var session = new AppearanceSettingsSession(NewPath());
        using var runtime = StackedDangoSceneRuntime.CreateProduct(Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll"));
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var home = Home(session); home.BindLiveScene(new DesktopSceneSession(), runtime);
        E<Slider>(home, "BodySizeSlider").Value = 80; Click(home, "PreviewBodyButton");
        Assert.AreEqual(80d, runtime.PhysicsBody.SizePercent);
        switch (destination)
        {
            case "Close": home.CloseEditor(); break;
            case "Wardrobe": home.ShowEditor(HomeEditor.Wardrobe); break;
            case "Settings": home.ShowSettingsPage(); break;
            default: Assert.IsTrue(home.CancelAppearancePreview()); break; // The same path is used by IsVisibleChanged/Unloaded.
        }
        Assert.AreEqual(session.SavedAppearance.Body, runtime.PhysicsBody);
        Assert.AreEqual(session.SavedAppearance, runtime.EffectiveAppearance);
        Assert.AreEqual(session.SavedAppearance, session.DraftAppearance);
        Assert.IsFalse(session.HasPendingSave);
    }

    [STATestMethod, TestCategory("NativeIntegration")]
    public void FailedCancelKeepsDraftBlocksNavigationAndExposesExplicitSavedRestore()
    {
        var path = NewPath(); var session = new AppearanceSettingsSession(path);
        using var runtime = StackedDangoSceneRuntime.CreateProduct(Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll"));
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var home = Home(session); home.BindLiveScene(new DesktopSceneSession(), runtime);
        E<Slider>(home, "BodySizeSlider").Value = 120; Click(home, "PreviewBodyButton");
        typeof(StackedDangoSceneRuntime).GetProperty(nameof(runtime.LastError))!.SetValue(runtime, "fictional injected fault");
        home.CloseEditor();
        Assert.IsTrue(home.IsEditing); Assert.AreEqual(120d, runtime.PhysicsBody.SizePercent);
        Assert.AreEqual(120d, session.DraftAppearance.Body.SizePercent); Assert.IsFalse(File.Exists(path));
        Assert.AreEqual(Visibility.Visible, E<FrameworkElement>(home, "BodyRestoreFeedback").Visibility);
        StringAssert.Contains(E<TextBlock>(home, "AppearanceStatus").Text, "尚未恢复");
        home.ShowSettingsPage(); Assert.IsFalse(home.IsSettingsOpen);
        home.ShowEditor(HomeEditor.Wardrobe); Assert.AreEqual(HomeEditor.Face, home.SelectedEditor);
        Click(home, "RestoreSavedBodyButton");
        Assert.IsNull(runtime.LastError); Assert.AreEqual(session.SavedAppearance.Body, runtime.PhysicsBody);
        Assert.AreEqual(session.SavedAppearance, session.DraftAppearance);
        Assert.AreEqual(Visibility.Collapsed, E<FrameworkElement>(home, "BodyRestoreFeedback").Visibility);
    }

    [STATestMethod, TestCategory("NativeIntegration")]
    [DataRow(1024, 640, 80)]
    [DataRow(1024, 640, 120)]
    [DataRow(640, 900, 120)]
    public void RealWindowBodyPreviewKeepsItsPaintedContactAndCenteredSize(int width, int height, int size)
    {
        var session = new AppearanceSettingsSession(NewPath());
        using var runtime = StackedDangoSceneRuntime.CreateProduct(Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll"));
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var home = Home(session);
        home.BindLiveScene(new DesktopSceneSession(), runtime);
        var window = new Window { Content = home, Width = width, Height = height,
            Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            Pump(100);
            E<Slider>(home, "BodySizeSlider").Value = size;
            Click(home, "PreviewBodyButton");
            Pump(1300);
            Assert.IsNull(runtime.LastError);
            Assert.IsTrue(runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded);
            var body = E<DeformableDangoVisual>(home, "LiveDangoVisual");
            Assert.AreEqual(Visibility.Visible, body.Visibility);
            Assert.AreEqual(DangoContourFailure.None, body.LastContourFailure);
            var area = E<FrameworkElement>(home, "PetVisualArea");
            var painted = body.BodyPath.TransformToAncestor(area).TransformBounds(body.BodyPath.Data.Bounds);
            var shadow = E<FrameworkElement>(home, "PetShadow");
            var shadowBounds = shadow.TransformToAncestor(area).TransformBounds(new Rect(shadow.RenderSize));
            Assert.AreEqual(shadowBounds.Top + shadowBounds.Height / 2 - 1, painted.Bottom, 1.5,
                "Every body size must keep the actual painted bottom on the fixed shadow.");
            var canvas = E<FrameworkElement>(home, "LiveSceneCanvas");
            var localCenter = body.TransformToAncestor(canvas).Transform(new Point(80, 60));
            Assert.AreEqual(canvas.Width / 2, localCenter.X, 1e-8,
                "Body size must not shift the home preview sideways.");
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(home.ActualWidth),
                (int)Math.Ceiling(home.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(home);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var evidence = TestEvidence.Create("face-body-live", $"body-{width}x{height}-{size}.png");
            encoder.Save(evidence);
        }
        finally { window.Close(); home.ReleaseLiveScene(); }
    }
}
