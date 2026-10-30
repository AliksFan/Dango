using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.Desktop.Tests.AppShell;

[TestClass, DoNotParallelize]
public sealed class HomeShortcutAndTransitionTests
{
    [TestMethod]
    [DataRow(1100, 760, HomeEditor.Face)]
    [DataRow(1100, 760, HomeEditor.Wardrobe)]
    [DataRow(1024, 560, HomeEditor.Face)]
    [DataRow(1024, 560, HomeEditor.Wardrobe)]
    [DataRow(480, 800, HomeEditor.Face)]
    [DataRow(480, 800, HomeEditor.Wardrobe)]
    public void BothEditorSlidesSurviveTheFirstRealLayout(int width, int height, HomeEditor editor) => Sta(() =>
    {
        var home = new ProductHomeView { ReduceMotion = false };
        var root = Path.Combine(TestRepository.Root, "artifacts", "home-porch-accessory-repair", "animation", Guid.NewGuid().ToString("N"));
        home.BindAccessories(new AccessorySettingsSession(root));
        using var source = new HwndSource(new HwndSourceParameters("Fictional hidden editor transition")
        { Width = width, Height = height, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = home };
        void Layout()
        {
            home.Measure(new Size(width, height)); home.Arrange(new Rect(0, 0, width, height)); home.UpdateLayout();
        }
        Layout(); Pump(); Layout();
        Assert.IsFalse(IsWindowVisible(source.Handle), "The animation gate must never display its native source.");
        Assert.IsTrue(home.IsLoaded);
        {
            home.ShowEditor(editor);
            var translation = (TranslateTransform)home.FindName("PanelTranslation");
            Console.WriteLine($"{width} {editor} immediately animated={translation.HasAnimatedProperties} x={translation.X}");
            Layout();
            Console.WriteLine($"{width} {editor} after layout animated={translation.HasAnimatedProperties} x={translation.X}");
            if (SystemParameters.ClientAreaAnimation)
            {
                Assert.IsTrue(translation.HasAnimatedProperties, $"{editor}: first layout must retain the requested slide.");
                // Opening real editor content can settle the scroll/view chrome
                // width before the first painted animation frame.
                home.Measure(new Size(width - 16, height));
                home.Arrange(new Rect(0, 0, width - 16, height)); home.UpdateLayout();
                Assert.IsTrue(translation.HasAnimatedProperties, $"{editor}: settling layout must retain the requested slide.");
                Pump(40);
                var moving = translation.X;
                var start = height > width * 1.2 ? 120 : 46;
                Assert.IsTrue(editor == HomeEditor.Face ? moving > 0 && moving < start : moving < 0 && moving > -start,
                    $"{editor}: first moving frame must be between the mirrored start and target: {moving}.");
                Pump(520);
                Assert.AreEqual(0d, translation.X, .001, "Slide must finish at its target.");
            }
            else Assert.AreEqual(0d, translation.X, "Reduced system animation must reach the target immediately.");
            home.ReduceMotion = true; home.CloseEditor(); Layout(); home.ReduceMotion = false;
        }
        source.RootVisual = null;
    });

    [TestMethod]
    public void NavigationKeysToggleEditorsPreserveDraftBoundaryAndUseFForPorch() => Sta(() =>
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var root = Path.Combine(TestRepository.Root, "artifacts", "home-porch-accessory-repair", "shortcuts", Guid.NewGuid().ToString("N"));
        var session = new AccessorySettingsSession(root);
        home.BindAccessories(session);
        home.EnableLocalRoomDoor();
        foreach (var key in new[] { Key.A, Key.D, Key.W }) Assert.IsFalse(home.TryHandleNavigationKey(key, false));
        Assert.IsFalse(home.IsEditing || home.IsInPorch);
        Assert.IsTrue(home.TryHandleNavigationKey(Key.Q, false));
        Assert.IsTrue(home.IsEditing);
        Assert.AreEqual(HomeEditor.Wardrobe, home.SelectedEditor);
        var panel = (AccessoryEditorPanel)typeof(ProductHomeView).GetField("accessoryPanel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(home)!;
        panel.SetBodyAnchor(new Point(.27, .41));
        Assert.IsTrue(session.IsDirty);
        Assert.IsTrue(home.TryHandleNavigationKey(Key.Q, true));
        Assert.IsTrue(home.IsEditing && session.IsDirty, "A repeat must not toggle or discard the draft.");
        Assert.IsFalse(home.TryHandleNavigationKey(Key.F, false), "Editing blocks entry into the porch.");
        Assert.IsTrue(home.TryHandleNavigationKey(Key.E, false));
        Assert.AreEqual(HomeEditor.Face, home.SelectedEditor);
        Assert.AreEqual(session.SavedConfiguration, session.DraftConfiguration, "Switching editors cancels the accessory draft.");
        Assert.IsTrue(home.TryHandleNavigationKey(Key.E, false));
        Assert.IsFalse(home.IsEditing, "The active editor key retains the existing close behavior.");
        Assert.IsTrue(home.TryHandleNavigationKey(Key.F, false));
        Assert.IsTrue(home.IsInPorch);
        var entries = 0;
        home.LocalRoomRequested += () => entries++;
        Assert.IsTrue(home.TryHandleNavigationKey(Key.F, true));
        Assert.AreEqual(0, entries, "The held entry key must not enter a room after opening the porch.");
        Assert.IsTrue(home.TryHandleNavigationKey(Key.F, false));
        Assert.AreEqual(1, entries);
        Assert.IsFalse(home.TryHandleNavigationKey(Key.W, false), "Porch W belongs to the body input route.");
    });

    [TestMethod]
    public void NavigationDoesNotRunInSettingsDialogsAwayStateOrWindowMenu() => Sta(() =>
    {
        var home = new ProductHomeView { ReduceMotion = true };
        void Blocked()
        {
            foreach (var key in new[] { Key.Q, Key.E, Key.F }) Assert.IsFalse(home.TryHandleNavigationKey(key, false));
            Assert.IsFalse(home.IsEditing);
        }
        home.ShowSettingsPage(); Blocked(); home.ShowHomePage();
        home.SetDangoAway(true); Blocked(); home.SetDangoAway(false);
        home.SetBeltWindowMenuOpen(true); Blocked(); home.SetBeltWindowMenuOpen(false);
        Assert.IsTrue(home.TryHandleNavigationKey(Key.F, false));
        typeof(ProductHomeView).GetMethod("OpenRoomDialog", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(home, ["虚构房间操作", false]);
        Assert.IsTrue(home.IsRoomDialogOpen);
        Blocked();
    });

    [TestMethod]
    public void LayoutDuringRapidReverseStillHidesOldToolsAndReducedMotionFinishesImmediately() => Sta(() =>
    {
        var home = new ProductHomeView();
        using var source = new HwndSource(new HwndSourceParameters("Fictional hidden reverse transition")
        { Width = 1100, Height = 760, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = home };
        void Layout(double width)
        { home.Measure(new Size(width, 760)); home.Arrange(new Rect(0, 0, width, 760)); home.UpdateLayout(); }
        Layout(1100); Pump();
        home.ShowEditor(HomeEditor.Face); Pump(30);
        home.CloseEditor(); Layout(1090);
        home.ShowEditor(HomeEditor.Wardrobe); Layout(1080); Pump(520);
        Assert.IsTrue(home.IsEditing);
        Assert.AreEqual(Visibility.Visible, ((UIElement)home.FindName("AccessoryEditorTools")).Visibility);
        Assert.AreEqual(Visibility.Hidden, ((UIElement)home.FindName("FaceEditorTools")).Visibility,
            "Layout refresh must preserve the completion that hides the previous editor tools.");
        home.CloseEditor(); Layout(1070); Pump(520);
        Assert.AreEqual(Visibility.Hidden, ((UIElement)home.FindName("EditorPanel")).Visibility);
        Assert.AreEqual(Visibility.Hidden, ((UIElement)home.FindName("AccessoryEditorTools")).Visibility);
        home.ShowEditor(HomeEditor.Wardrobe); home.ReduceMotion = true;
        var translation = (TranslateTransform)home.FindName("PanelTranslation");
        Assert.IsFalse(translation.HasAnimatedProperties);
        Assert.AreEqual(0d, translation.X);
        source.RootVisual = null;
    });

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);
}
