using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Belt;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryEditorHomePauseTests
{
    [STATestMethod]
    [DataRow("cancel")]
    [DataRow("exception")]
    [DataRow("hidden")]
    [DataRow("settings")]
    public void UnifiedModalStopsRealHomeOwnerAndRestoresOnlyAnEligiblePage(string ending)
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "dango_belt_product.dll")))
            Assert.Inconclusive("The product native module is required for this owner lifecycle test.");
        var root = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "accessory-home-pause", Guid.NewGuid().ToString("N"));
        var home = new ProductHomeView { ReduceMotion = true };
        home.BindAppearance(new AppearanceSettingsSession(Path.Combine(root, "appearance.json")));
        home.BindAccessories(new AccessorySettingsSession(Path.Combine(root, "accessories")));
        home.BindBeltBody(Path.Combine(root, "settings.json"));
        // Entirely off screen, never activated and never acquires global input.
        var window = new Window
        {
            Content = home, Width = 960, Height = 640, Left = -32000, Top = -32000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
        };
        // These page APIs normally focus Stage/Settings. Focus is irrelevant to
        // this owner lifecycle test, so cancel it before WPF can activate an HWND.
        window.PreviewGotKeyboardFocus += (_, e) => e.Handled = true;
        home.ShowEditor(HomeEditor.Wardrobe);
        try
        {
            window.Show(); window.UpdateLayout();
            Assert.IsFalse(window.IsActive, "The offscreen fixture must not activate its window.");
            var panel = ((Panel)home.FindName("WardrobeOptions")).Children.OfType<AccessoryEditorPanel>().Single();
            var runtime = home.BoundBeltBody!;
            Wait(() => runtime.Metrics.Running && runtime.Metrics.NativeStepCalls > 0, runtime);
            var notifications = new List<bool>();
            panel.ModalOpenChanged += () => notifications.Add(panel.IsUnifiedEditorOpen);
            var calls = 0;
            panel.UnifiedEditor = (_, _, _, _) =>
            {
                calls++;
                Assert.IsFalse(window.IsActive);
                Assert.IsTrue(panel.IsUnifiedEditorOpen);
                panel.EditSelectedImage();
                Assert.AreEqual(1, calls, "Reentry cannot open a second modal or clear the first modal's pause.");
                Wait(() => !runtime.Metrics.Running, runtime);
                Assert.IsTrue(home.BoundBeltPresenter!.Paused);
                AssertPaused(runtime);
                if (ending == "hidden") home.Visibility = Visibility.Hidden;
                if (ending == "settings") home.ShowSettingsPage();
                if (ending == "exception") throw new InvalidOperationException("Fictional editor failure");
                return null;
            };
            var before = home.BoundAccessories!.DraftConfiguration;
            panel.EditSelectedImage();
            Assert.AreEqual(1, calls);
            CollectionAssert.AreEqual(new[] { true, false }, notifications);
            Assert.IsFalse(panel.IsUnifiedEditorOpen);
            Assert.IsNull(panel.ModalPreviewResult);
            Assert.AreEqual(before, home.BoundAccessories.DraftConfiguration, "Cancel/failure cannot apply the outer draft.");
            if (ending is "hidden" or "settings")
            {
                Assert.IsFalse(runtime.Metrics.Running, "Closing the modal must not resume a page which lost its own eligibility.");
                AssertPaused(runtime);
                if (ending == "hidden") home.Visibility = Visibility.Visible;
                else home.ShowHomePage();
            }
            else
            {
                Assert.AreEqual(HomeEditor.Wardrobe, home.SelectedEditor);
                Assert.IsTrue(home.IsEditing, "The outer wardrobe stays available after its child editor closes.");
            }
            var resumedAt = runtime.Metrics.NativeStepCalls;
            Wait(() => runtime.Metrics.Running && runtime.Metrics.NativeStepCalls > resumedAt, runtime);
            Assert.IsFalse(window.IsActive);
            Assert.IsFalse(home.BoundBeltPresenter!.Paused);
            Assert.IsFalse(Directory.Exists(root), "The fixture must not create persistent application data.");
        }
        finally { home.UnbindBeltBody(); window.Close(); }
    }

    private static void AssertPaused(BeltBodyRuntime runtime)
    {
        // A barrier acknowledges the already executing call without issuing a
        // second pause, so this cannot hide a missing production pause request.
        var barrier = runtime.OwnerBarrierAsync();
        Wait(() => barrier.IsCompleted, runtime);
        barrier.GetAwaiter().GetResult();
        var before = runtime.Metrics.NativeStepCalls;
        var watch = Stopwatch.StartNew();
        Wait(() => watch.ElapsedMilliseconds >= 100, runtime);
        Assert.AreEqual(before, runtime.Metrics.NativeStepCalls, "The actual home native owner must stay stopped throughout the modal callback.");
    }

    private static void Wait(Func<bool> condition, BeltBodyRuntime runtime)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 5000)
        {
            // A fully off-screen surface may not receive CompositionTarget.Rendering.
            // Drain presentation snapshots so a running owner cannot merely block on a full queue.
            while (runtime.TryDequeue(out _)) { }
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        Assert.IsTrue(condition(), "The home owner did not reach the expected lifecycle state.");
    }
}
