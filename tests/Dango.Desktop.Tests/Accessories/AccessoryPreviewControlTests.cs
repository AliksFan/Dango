using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
[DoNotParallelize]
public sealed class AccessoryPreviewControlTests
{
    [TestMethod]
    public void HorizontalOnlyUnboundedCameraRetainsFixedGroundBeyondBothFormerRoomEdges()
    {
        var camera = new RoomViewController(standardScale: 1);
        camera.EnableUnboundedFollow(4, 3);
        camera.Resize(160, 125, 400);
        camera.Update(-9000, 0, true);
        Assert.AreEqual(80, camera.Projection.WorldToScreen(new(-9000, 370)).X, .001);
        var groundedTop = camera.CameraY;
        camera.Update(12000, 20, true);
        Assert.AreEqual(80, camera.Projection.WorldToScreen(new(12000, 360)).X, .001);
        Assert.AreEqual(groundedTop, camera.CameraY, .001, "Low jumps must retain ground framing.");
        camera.Update(12000, 20, false);
        Assert.AreEqual(groundedTop, camera.CameraY, .001);
        camera.Update(-9000, 20, true);
        Assert.AreEqual(groundedTop, camera.CameraY, .001, "Landing must restore the ground reference.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ActualWardrobeUsesSpaceOnlyAndEscapeCancelsBufferedInputAndRestoresJumpSpeed()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-verification", "preview-controls", Guid.NewGuid().ToString("N"));
        var native = Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
        Assert.IsTrue(File.Exists(native));
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"), nativeLibraryPath: native);
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        ControlCenterWindow? reviewWindow = null;
        try
        {
            host.ShowControlCenter(); Pump(400);
            var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
            reviewWindow = window;
            var home = window.ProductHome;
            home.ShowEditor(HomeEditor.Wardrobe); Pump(160);
            Assert.IsTrue(home.OwnsLiveFrameDriver);
            Assert.IsTrue(runtime.UnboundedHorizontalPreview);
            Assert.AreEqual(.5, runtime.PreviewJumpSpeedScale);
            var id = runtime.EntityIds.Single();
            var before = runtime.CurrentSelectedCenter!.Value.X;
            Assert.IsTrue(Key(home, window, System.Windows.Input.Key.D, true).Handled); Pump(350);
            Assert.IsTrue(runtime.CurrentSelectedCenter!.Value.X > before + 2, "D must produce native walking.");
            Assert.IsTrue(home.IsEditing); Assert.AreEqual(HomeEditor.Wardrobe, home.SelectedEditor);
            Key(home, window, System.Windows.Input.Key.D, false);
            Key(home, window, System.Windows.Input.Key.W, true);
            Assert.IsFalse(Field<DirectionalControlIntent>(home, "liveIntent").Jump, "W must never initiate a jump.");
            Key(home, window, System.Windows.Input.Key.Space, true);
            Key(home, window, System.Windows.Input.Key.W, false);
            Assert.IsTrue(Field<DirectionalControlIntent>(home, "liveIntent").Jump);
            Key(home, window, System.Windows.Input.Key.Space, false);
            Assert.IsFalse(Field<DirectionalControlIntent>(home, "liveIntent").Jump);
            Key(home, window, System.Windows.Input.Key.W, true);
            Key(home, window, System.Windows.Input.Key.Space, true);
            Key(home, window, System.Windows.Input.Key.Space, false);
            Assert.IsFalse(Field<DirectionalControlIntent>(home, "liveIntent").Jump, "A held W cannot keep a released Space jump active.");
            Key(home, window, System.Windows.Input.Key.W, false);
            Assert.IsFalse(home.IsInPorch, "W belongs to preview controls and cannot navigate.");
            var panel = Field<AccessoryEditorPanel>(home, "accessoryPanel");
            panel.SelectSection(AccessoryEditorSection.Placement); Pump(20);
            var apply = Descendants<Button>(panel.SaveActions)
                .Single(button => AutomationProperties.GetName(button) == "应用");
            panel.SetBodyAnchor(new System.Windows.Point(.3, .4));
            apply.Focus(); Pump(20);
            Assert.IsTrue(apply.IsKeyboardFocused);
            Assert.IsFalse(Key(home, window, System.Windows.Input.Key.Space, true).Handled,
                "A deliberately focused action button must keep standard keyboard activation.");
            Assert.IsFalse(Field<DirectionalControlIntent>(home, "liveIntent").Jump);
            ((FrameworkElement)home.FindName("Stage")).Focus();
            var value = Descendants<TextBox>(panel).Single(text => AutomationProperties.GetName(text) == "配饰尺寸 % 数值");
            Key(home, window, System.Windows.Input.Key.A, true);
            value.Focus(); Pump(20);
            Assert.IsTrue(value.IsKeyboardFocused, "The real text field must obtain focus for this input boundary gate.");
            Assert.AreEqual(DirectionalControlIntent.None, Field<DirectionalControlIntent>(home, "liveIntent"));
            var saved = home.BoundAccessories!.SavedConfiguration;
            value.Text = "invalid";
            Assert.IsFalse(Key(home, window, System.Windows.Input.Key.D, true).Handled, "Text editing must not drive physics.");
            Assert.IsTrue(Key(home, window, System.Windows.Input.Key.Escape, true).Handled);
            Pump(120);
            Assert.IsFalse(home.IsEditing);
            Assert.AreEqual(saved, home.BoundAccessories.DraftConfiguration, "Esc from a text field must discard its unfinished buffer.");
            Assert.IsFalse(runtime.UnboundedHorizontalPreview);
            Assert.AreEqual(1, runtime.PreviewJumpSpeedScale);
            Assert.AreEqual(id, runtime.EntityIds.Single());
            home.ShowEditor(HomeEditor.Wardrobe); Pump(100);
            Assert.AreEqual(.5, runtime.PreviewJumpSpeedScale);
            Assert.AreEqual(saved, home.BoundAccessories.DraftConfiguration, "The cancelled input must not revive on re-entry.");
            Assert.AreNotEqual("invalid", value.Text);
        }
        finally { reviewWindow?.Close(); }
        Assert.AreEqual(1, runtime.PreviewJumpSpeedScale, "Releasing the live owner must restore the ordinary jump.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void MissingLowJumpExportShowsUnavailableAndEscStillLeavesThePreview()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-verification", "preview-controls", Guid.NewGuid().ToString("N"));
        var baseline = Path.Combine(TestRepository.Root, "artifacts", "native-spike", "physics-module-unified-build", "Release",
            DangoPhysicsWorld.NativeLibraryFileName);
        Assert.IsTrue(File.Exists(baseline));
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"), nativeLibraryPath: baseline);
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        ControlCenterWindow? reviewWindow = null;
        try
        {
            host.ShowControlCenter(); Pump(250);
            reviewWindow = Field<ControlCenterWindow>(host, "controlCenterWindow");
            var home = reviewWindow.ProductHome;
            home.ShowEditor(HomeEditor.Wardrobe); Pump(100);
            var error = Field<DangoPhysicsException>(home, "liveSurfacePhysicsError");
            Assert.AreEqual(DangoPhysicsFailureKind.MissingExport, error.Kind);
            StringAssert.Contains(error.Message, "dango_physics_world_set_preview_jump_speed_scale");
            var status = (TextBlock)home.FindName("LiveSceneStatusText");
            Assert.AreEqual(Visibility.Visible, status.Visibility);
            StringAssert.Contains(status.Text, "配饰预览物理模块不可用");
            Assert.AreNotEqual(Visibility.Visible, ((FrameworkElement)home.FindName("LiveDangoVisual")).Visibility);
            Assert.AreNotEqual(Visibility.Visible, ((FrameworkElement)home.FindName("PetShadow")).Visibility);
            Assert.AreEqual(1, runtime.PreviewJumpSpeedScale);
            ((FrameworkElement)home.FindName("Stage")).Focus();
            Assert.IsTrue(Key(home, reviewWindow, System.Windows.Input.Key.Escape, true).Handled);
            Pump(100);
            Assert.IsFalse(home.IsEditing);
            Assert.IsFalse(runtime.UnboundedHorizontalPreview);
            Assert.AreEqual(1, runtime.PreviewJumpSpeedScale);
            Assert.IsNull(runtime.LastError, "A missing optional preview capability must not fault ordinary Home physics.");
        }
        finally { reviewWindow?.Close(); }
    }

    private static KeyEventArgs Key(ProductHomeView home, Window window, System.Windows.Input.Key key, bool down)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key)
        { RoutedEvent = down ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent };
        home.RaiseEvent(args);
        return args;
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
