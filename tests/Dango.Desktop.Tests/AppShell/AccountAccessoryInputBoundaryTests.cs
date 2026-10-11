using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
[DoNotParallelize]
public sealed class AccountAccessoryInputBoundaryTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(1024d, 640d, 480d, 800d)]
    [DataRow(480d, 800d, 1024d, 640d)]
    public void AccountOverlayKeepsWardrobeDraftAndOwnsInputUntilItsFirstEscape(
        double width, double height, double resizedWidth, double resizedHeight)
    {
        var module = Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
        Assert.AreEqual("B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module))));
        using var runtime = StackedDangoSceneRuntime.CreateProduct(module);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var root = Path.Combine(TestRepository.Root, "artifacts", "main-absorption", "input-cases", Guid.NewGuid().ToString("N"));
        var session = new AccessorySettingsSession(root);
        Assert.IsTrue(session.TrySave());
        var saved = session.SavedConfiguration;
        var manifest = AccessorySettingsStore.GetManifestPath(root);
        var savedBytes = File.ReadAllBytes(manifest);
        var home = new ProductHomeView { ReduceMotion = true };
        home.BindAccessories(session);
        home.BindLiveScene(new DesktopSceneSession(), runtime);
        // The unconfigured account panel is an explicit UI fixture: no HTTP or credentials.
        home.AccountRequested += () => home.AccountPanel.Apply(new(AccountPanelPage.Login));
        var window = new Window { Content = home, Width = width, Height = height,
            Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show(); Pump(250);
            Assert.IsTrue(home.OwnsLiveFrameDriver);
            Assert.IsNotNull(runtime.LastFrame);
            Assert.IsFalse(runtime.LastFrame.ConstraintsReleased);
            var entity = runtime.EntityIds.Single();
            home.ShowEditor(HomeEditor.Wardrobe); Pump(120);
            var panel = Element<StackPanel>(home, "WardrobeOptions").Children.OfType<AccessoryEditorPanel>().Single();
            panel.SetBodyAnchor(new Point(.34, .59));
            Assert.IsTrue(panel.SelectSection(AccessoryEditorSection.Placement));
            var name = Descendants<TextBox>(panel).Single(input => AutomationProperties.GetName(input) == "配饰名称");
            Assert.IsTrue(name.Focus());
            name.Text = "虚构未保存名称";
            Assert.IsTrue(session.IsDirty);
            Assert.IsTrue(Element<FrameworkElement>(home, "Stage").Focus());
            // Leaving the name field commits it to the draft, while the disk remains unchanged.
            var draft = session.DraftConfiguration;
            Assert.AreEqual("虚构未保存名称", draft.Items.Single(item => item.Id == panel.SelectedItem!.Id).Name);
            Assert.IsTrue(Key(home, window, System.Windows.Input.Key.D, true).Handled);
            Assert.IsTrue(Intent(home).Right, "The native preview must first establish a real held input.");
            Pump(60);
            Assert.IsTrue(RuntimeIntent(runtime).Right);
            home.ShowAccountPanel(); Pump(40);
            var password = Element<PasswordBox>(home.AccountPanel, "AccountPassword");
            Assert.IsTrue(password.Focus());
            Assert.AreEqual(DirectionalControlIntent.None, Intent(home));
            Assert.AreEqual(DirectionalControlIntent.None, RuntimeIntent(runtime));
            foreach (var size in new[] { new Size(width, height), new Size(resizedWidth, resizedHeight) })
            {
                window.Width = size.Width; window.Height = size.Height; Pump(60);
                foreach (var key in new[] { System.Windows.Input.Key.A, System.Windows.Input.Key.D,
                    System.Windows.Input.Key.W, System.Windows.Input.Key.S, System.Windows.Input.Key.Space })
                {
                    Key(home, window, key, true); Key(home, window, key, false);
                    Assert.IsTrue(home.IsAccountPanelOpen);
                    Assert.IsTrue(password.IsKeyboardFocused);
                    Assert.IsTrue(home.IsEditing);
                    Assert.AreEqual(HomeEditor.Wardrobe, home.SelectedEditor);
                    Assert.IsFalse(home.IsInPorch);
                    Assert.AreEqual(DirectionalControlIntent.None, Intent(home));
                    Assert.AreEqual(DirectionalControlIntent.None, RuntimeIntent(runtime));
                    Assert.AreEqual(draft, session.DraftConfiguration);
                    Assert.AreEqual("虚构未保存名称", name.Text);
                    CollectionAssert.AreEqual(savedBytes, File.ReadAllBytes(manifest));
                }
                Element<FrameworkElement>(home, "Stage").RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0,
                    MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
                Assert.IsTrue(password.IsKeyboardFocused, "The scene must not steal account focus.");
                Assert.IsNull(runtime.LastError);
                Assert.IsTrue(runtime.LastPhysicsFrame is { Succeeded: true });
                Assert.IsTrue(runtime.LastFrame is { ConstraintsReleased: false });
            }
            Assert.IsTrue(Key(home, window, System.Windows.Input.Key.Escape, true).Handled);
            Assert.IsFalse(home.IsAccountPanelOpen);
            Assert.IsTrue(home.IsEditing, "The first Escape belongs to the account overlay.");
            Assert.AreEqual(draft, session.DraftConfiguration);
            Assert.AreEqual("虚构未保存名称", name.Text);
            Assert.IsTrue(name.Focus());
            Assert.IsTrue(Key(home, window, System.Windows.Input.Key.Escape, true).Handled);
            Assert.IsFalse(home.IsEditing, "The next Escape from a wardrobe field cancels the wardrobe.");
            Assert.AreEqual(saved, session.DraftConfiguration);
            CollectionAssert.AreEqual(savedBytes, File.ReadAllBytes(manifest));
            Assert.AreEqual(DirectionalControlIntent.None, Intent(home));
            Assert.AreEqual(entity, runtime.EntityIds.Single());
            Assert.IsTrue(home.OwnsLiveFrameDriver);
        }
        finally { window.Close(); }
        Assert.IsFalse(home.OwnsLiveFrameDriver);
        Assert.IsFalse(runtime.IsRunning);
    }

    private static T Element<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new AssertFailedException("Missing " + name);
    private static DirectionalControlIntent Intent(ProductHomeView home) =>
        (DirectionalControlIntent)typeof(ProductHomeView).GetField("liveIntent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(home)!;
    private static DirectionalControlIntent RuntimeIntent(StackedDangoSceneRuntime runtime) =>
        (DirectionalControlIntent)typeof(StackedDangoSceneRuntime).GetField("directionalIntent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Descendants<T>(child)) yield return nested;
    }
    private static KeyEventArgs Key(ProductHomeView home, Window window, System.Windows.Input.Key key, bool down)
    {
        var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key)
        { RoutedEvent = down ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent };
        home.RaiseEvent(input); return input;
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        EventHandler tick = (_, _) => frame.Continue = false;
        timer.Tick += tick;
        try { timer.Start(); Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); timer.Tick -= tick; }
    }
}
