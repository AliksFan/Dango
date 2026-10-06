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
public sealed class LocalRoomWindowReturnAppearanceTests
{
    private static DangoAppearanceConfiguration InitialAppearance =>
        new(new(41, 83, 127), new(119, 37, 73), new(91, 28, 56, 18));
    private static DangoAppearanceConfiguration EditedAppearance =>
        new(new(152, 74, 110), new(35, 105, 81), new(124, -42, 36, 30));

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void WindowReturnAllowsFaceApplyAndReentryRendersSavedAppearanceOnTheSameEntity()
    {
        var root = NewRoot();
        var appearancePath = Path.Combine(root, "appearance.json");
        var seed = new AppearanceSettingsSession(appearancePath);
        seed.StagePreview(InitialAppearance);
        Assert.IsTrue(seed.TrySave());
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"), isolatedLocalRoomReview: true);
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowControlCenter();
        var home = Field<ControlCenterWindow>(host, "controlCenterWindow").ProductHome;
        var appearance = home.BoundAppearance!;
        home.ReduceMotion = true; // The separate test exercises interruption of real transitions.
        WaitUntil(() => home.OwnsLiveFrameDriver && E<DeformableDangoVisual>(home, "LiveDangoVisual").IsVisible);
        var entity = runtime.EntityIds.Single();
        AssertRenderedAppearance(runtime, E<DeformableDangoVisual>(home, "LiveDangoVisual"), entity, InitialAppearance);

        Click(home, "EnterRoomButton");
        var porch = E<PorchSceneView>(home, "PorchScene");
        WaitUntil(() => home.IsInPorch && home.OwnsLiveFrameDriver && porch.LiveBody.IsVisible);
        AssertRenderedAppearance(runtime, porch.LiveBody, entity, InitialAppearance);
        host.EnterLocalRoom();
        WaitForPresentation(host, LocalRoomPresentation.Window);
        var driver = Field<LocalRoomSceneDriver>(host, "localRoomDriver");
        var window = host.LocalWindow!;
        var roomOwner = runtime.FrameDriver;
        Assert.IsNotNull(roomOwner);
        AssertWindowOwner(runtime, driver, home, roomOwner!);
        AssertRenderedAppearance(runtime, Field<DeformableDangoVisual>(window.Scene, "body"), entity, InitialAppearance);

        ClickRoomMenu(window, "RoomControlCenterButton");
        AssertAway(home);
        AssertWindowOwner(runtime, driver, home, roomOwner!);
        home.ShowSettingsPage();
        Click(home, "BackFromSettingsButton");
        Assert.IsFalse(home.IsSettingsOpen);
        AssertAway(home);
        AssertWindowOwner(runtime, driver, home, roomOwner!);

        ClickRoomMenu(window, "RoomReturnButton");
        WaitForPresentation(host, LocalRoomPresentation.Porch);
        Assert.IsFalse(home.IsDangoAway);
        Assert.IsTrue(home.IsInPorch);
        Assert.IsTrue(home.OwnsLiveFrameDriver);
        Assert.IsFalse(driver.OwnsRuntime);
        Assert.AreNotSame(roomOwner, runtime.FrameDriver);
        AssertRenderedAppearance(runtime, porch.LiveBody, entity, InitialAppearance);
        host.ShowControlCenter();
        WaitUntil(() => !home.IsInPorch && home.OwnsLiveFrameDriver && E<DeformableDangoVisual>(home, "LiveDangoVisual").IsVisible);

        Click(home, "FaceButton");
        Assert.IsTrue(home.IsEditing, "Returning from a real healthy porch frame must restore face editing.");
        Assert.AreEqual(HomeEditor.Face, home.SelectedEditor);
        Assert.IsTrue(E<FrameworkElement>(home, "FaceEditorTools").IsEnabled);
        // Keep the OS colour dialog deterministic; edits still use the actual picker-button callbacks.
        home.AppearanceColorPicker = _ => EditedAppearance.BodyColor;
        Click(home, "BodyColorPickerButton");
        home.AppearanceColorPicker = _ => EditedAppearance.EyeColor;
        Click(home, "EyeColorPickerButton");
        E<Slider>(home, "EyeSizeSlider").Value = EditedAppearance.EyeLayout.SizePercent;
        E<Slider>(home, "EyeOvalSlider").Value = EditedAppearance.EyeLayout.OvalPercent;
        E<Slider>(home, "EyeHeightSlider").Value = EditedAppearance.EyeLayout.HeightControlPercent;
        E<Slider>(home, "EyeSpacingSlider").Value = EditedAppearance.EyeLayout.SpacingControlPercent;
        Assert.AreEqual(EditedAppearance, home.DraftAppearance);
        Assert.IsTrue(appearance.IsDirty);
        Assert.AreEqual(InitialAppearance, new AppearanceSettingsSession(appearancePath).SavedAppearance,
            "A real post-return preview must not save before Apply.");
        Assert.IsTrue(E<Button>(home, "ApplyButton").IsEnabled);
        Click(home, "ApplyButton");
        Assert.AreEqual(EditedAppearance, appearance.SavedAppearance);
        Assert.AreEqual(EditedAppearance, new AppearanceSettingsSession(appearancePath).SavedAppearance);
        Assert.IsFalse(appearance.IsDirty);
        WaitUntil(() => RenderedEyesMatch(runtime, EditedAppearance));
        AssertRenderedAppearance(runtime, E<DeformableDangoVisual>(home, "LiveDangoVisual"), entity, EditedAppearance);
        Click(home, "CancelAppearanceButton");
        Assert.IsFalse(home.IsEditing);
        Assert.AreEqual(EditedAppearance, home.DraftAppearance, "Closing an applied editor must keep the newly saved choice.");

        Click(home, "EnterRoomButton");
        WaitUntil(() => home.IsInPorch && home.OwnsLiveFrameDriver && porch.LiveBody.IsVisible);
        var previousStep = runtime.LastFrame!.Bodies.Single().Attachments!.StepIndex;
        var appliedStep = previousStep;
        void ObserveAppliedFrame()
        {
            if (window.Scene.HasHealthyFrame && runtime.LastFrame?.Bodies.Single().Attachments is { } attachments)
                appliedStep = Math.Max(appliedStep, attachments.StepIndex);
        }
        window.Scene.FrameApplied += ObserveAppliedFrame;
        try
        {
            host.EnterLocalRoom();
            WaitForPresentation(host, LocalRoomPresentation.Window);
            WaitUntil(() => appliedStep > previousStep && RenderedEyesMatch(runtime, EditedAppearance));
            Assert.IsTrue(window.Scene.HasHealthyFrame);
            Assert.IsTrue(window.IsVisible);
            AssertWindowOwner(runtime, driver, home, roomOwner!);
            Assert.AreEqual(entity, runtime.EntityIds.Single());
            AssertRenderedAppearance(runtime, Field<DeformableDangoVisual>(window.Scene, "body"), entity, EditedAppearance);
            Assert.AreEqual(EditedAppearance, new AppearanceSettingsSession(appearancePath).SavedAppearance);
        }
        finally { window.Scene.FrameApplied -= ObserveAppliedFrame; }
    }

    [STATestMethod]
    public void AwayDuringEditorAndPorchAnimationsKeepsEntriesHiddenAndCancelsUnsavedDraft()
    {
        if (!SystemParameters.ClientAreaAnimation)
            Assert.Inconclusive("Windows client-area animations are disabled; the real transition interruption is unverified.");
        var path = Path.Combine(NewRoot(), "appearance.json");
        var appearance = new AppearanceSettingsSession(path);
        appearance.StagePreview(InitialAppearance);
        Assert.IsTrue(appearance.TrySave());
        var home = new ProductHomeView { ReduceMotion = false };
        home.BindAppearance(appearance);
        var window = new Window { Content = home, Width = 1024, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            WaitUntil(() => home.IsLoaded && home.ActualWidth > 0 && home.ActualHeight > 0);
            Pump(100);
            Click(home, "FaceButton");
            Assert.IsFalse(home.ReduceMotion);
            WaitForActiveFade(E<UIElement>(home, "FaceEditorTools"));
            E<Slider>(home, "EyeSizeSlider").Value = EditedAppearance.EyeLayout.SizePercent;
            Assert.IsTrue(appearance.IsDirty);
            Assert.AreNotEqual(InitialAppearance, appearance.DraftAppearance);
            home.SetDangoAway(true);
            AssertAwayAndCancelled(home, appearance, path);
            Pump(650); // Beyond the production 440ms transition, including its completion callbacks.
            AssertAwayAndCancelled(home, appearance, path);
            AssertAwayRejectsEntries(home);

            home.SetDangoAway(false);
            home.ShowHomePage();
            Click(home, "FaceButton");
            Assert.IsTrue(home.IsEditing, "A cancelled animation must not permanently disable the returned editor.");
            Assert.IsTrue(E<FrameworkElement>(home, "FaceEditorTools").IsEnabled);
            Click(home, "CancelAppearanceButton");
            Pump(650);
            Click(home, "EnterRoomButton");
            Assert.IsTrue(home.IsInPorch);
            var porchContent = E<UIElement>(home, "PorchContent");
            WaitForActiveFade(porchContent);
            home.ShowHomePage(); // Start a genuine porch exit with a pending hide completion.
            WaitForActiveFade(porchContent);
            Assert.IsFalse(home.IsInPorch);
            home.SetDangoAway(true);
            AssertAwayAndCancelled(home, appearance, path);
            Pump(650);
            AssertAwayAndCancelled(home, appearance, path);
            AssertAwayRejectsEntries(home);
        }
        finally { window.Close(); }
    }

    private static void AssertWindowOwner(StackedDangoSceneRuntime runtime, LocalRoomSceneDriver driver, ProductHomeView home, object expectedOwner)
    {
        Assert.IsTrue(driver.OwnsRuntime);
        Assert.IsFalse(home.OwnsLiveFrameDriver);
        Assert.AreSame(Field<object>(driver, "scheduler"), runtime.FrameDriver);
        Assert.AreSame(expectedOwner, runtime.FrameDriver);
    }

    private static void AssertRenderedAppearance(StackedDangoSceneRuntime runtime, DeformableDangoVisual visual, DangoEntityId entity,
        DangoAppearanceConfiguration expected)
    {
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.AreEqual(entity, runtime.AppearanceEntity);
        Assert.AreEqual(entity, visual.Tag);
        Assert.AreEqual(expected, runtime.EffectiveAppearance);
        Assert.IsNull(runtime.LastError);
        Assert.IsNull(runtime.AttachmentError);
        Assert.IsTrue(runtime.LastPhysicsFrame!.Succeeded);
        Assert.AreEqual(Visibility.Visible, visual.Visibility);
        Assert.AreEqual(Color.FromRgb(expected.BodyColor.R, expected.BodyColor.G, expected.BodyColor.B), ((SolidColorBrush)visual.BodyFill).Color);
        Assert.AreEqual(Color.FromRgb(expected.EyeColor.R, expected.EyeColor.G, expected.EyeColor.B), ((SolidColorBrush)visual.EyeFill).Color);
        WaitUntil(() => RenderedEyesMatch(runtime, expected));
        Assert.IsTrue(RenderedEyesMatch(runtime, expected), "Real native attachment geometry must reflect the saved eye size and oval values.");
    }

    private static bool RenderedEyesMatch(StackedDangoSceneRuntime runtime, DangoAppearanceConfiguration expected)
    {
        if (runtime.LastFrame?.Bodies.Single().Attachments?.Face is not { } face) return false;
        // Positive oval narrows X; negative oval narrows Y, both from the same saved diameter.
        // Blink openness is a separate field, so it does not require a manufactured neutral frame.
        var diameter = 12d / 160 * expected.EyeLayout.SizePercent / 100 * face.SizeScale;
        var shortAxis = 1 - Math.Abs(expected.EyeLayout.OvalPercent) / 200;
        var width = diameter * (expected.EyeLayout.OvalPercent > 0 ? shortAxis : 1);
        var height = diameter * (expected.EyeLayout.OvalPercent < 0 ? shortAxis : 1);
        return face.Left.Visible && face.Right.Visible && Math.Abs(face.Left.Width - width) < 1e-8 &&
            Math.Abs(face.Right.Width - width) < 1e-8 && Math.Abs(face.Left.Height - height) < 1e-8 && Math.Abs(face.Right.Height - height) < 1e-8;
    }

    private static void AssertAwayAndCancelled(ProductHomeView home, AppearanceSettingsSession appearance, string path)
    {
        AssertAway(home);
        Assert.IsFalse(home.IsEditing);
        Assert.IsFalse(appearance.IsDirty);
        Assert.AreEqual(InitialAppearance, appearance.DraftAppearance);
        Assert.AreEqual(InitialAppearance, new AppearanceSettingsSession(path).SavedAppearance);
    }

    private static void AssertAwayRejectsEntries(ProductHomeView home)
    {
        // Exercise production guards as well as the disabled/hidden input surfaces.
        foreach (var name in new[] { "FaceButton", "WardrobeButton", "EnterRoomButton" })
            E<Button>(home, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsFalse(home.IsEditing);
        Assert.IsFalse(home.IsInPorch);
        AssertAway(home);
    }

    private static void AssertAway(ProductHomeView home)
    {
        Assert.IsTrue(home.IsDangoAway);
        foreach (var name in new[] { "Presence", "Launchers", "EditorPanel", "FaceEditorTools", "EnterRoomButton", "PorchContent", "HomeFooter" })
            Assert.AreEqual(Visibility.Collapsed, E<UIElement>(home, name).Visibility, name);
        Assert.IsFalse(E<FrameworkElement>(home, "FaceEditorTools").IsEnabled);
        Assert.IsFalse(E<UIElement>(home, "FaceEditorTools").IsHitTestVisible);
        Assert.IsFalse(E<UIElement>(home, "Launchers").IsHitTestVisible);
        Assert.IsFalse(E<FrameworkElement>(home, "Launchers").IsEnabled);
        Assert.IsFalse(E<Button>(home, "EnterRoomButton").IsEnabled);
        Assert.AreEqual(Visibility.Visible, E<UIElement>(home, "RoomAwayNotice").Visibility);
        Assert.AreEqual("团子去玩拉！回来才能换衣服哦", E<TextBlock>(home, "RoomAwayNotice").Text);
    }

    private static void WaitForActiveFade(UIElement target)
    {
        WaitUntil(() => target.HasAnimatedProperties && target.Opacity > 0 && target.Opacity < 1, 1200);
        Assert.AreEqual(Visibility.Visible, target.Visibility);
    }

    private static void Click(FrameworkElement view, string name)
    {
        var button = E<Button>(view, name);
        Assert.IsTrue(button.IsEnabled, name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void ClickRoomMenu(LocalRoomWindow window, string name)
    {
        window.OpenMenu();
        Assert.IsTrue(window.IsMenuOpen);
        Click(Field<LocalRoomMenuView>(window, "menu"), name);
        Assert.IsFalse(window.IsMenuOpen);
    }

    private static void WaitForPresentation(DangoApplicationHost host, LocalRoomPresentation expected)
    {
        WaitUntil(() => !host.LocalHandoffPending, 4500);
        Assert.AreEqual(expected, host.LocalPresentation, host.LocalRoomFailure);
        Assert.IsNull(host.LocalRoomFailure);
    }

    private static void WaitUntil(Func<bool> condition, int milliseconds = 2500)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (!condition() && Environment.TickCount64 < until) Pump(20);
        Assert.IsTrue(condition(), "The actual scene or transition did not satisfy its gate before the bounded wait expired.");
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static string NewRoot()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "local-room", "window-return-appearance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static T E<T>(FrameworkElement view, string name) where T : class => (T)view.FindName(name);
    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
}
