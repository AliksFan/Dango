using System.Reflection;
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
public sealed class ImeGameplayInputBoundaryTests
{
    [STATestMethod]
    public void ImeWOnStageEntersPorchAndImeEscapeReturnsHome()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = WindowFor(home);
        try
        {
            window.Show(); Pump();
            var stage = Element<FrameworkElement>(home, "Stage");
            Assert.IsTrue(stage.Focus());
            Assert.IsTrue(ImeKey(stage, window, Key.W, true).Handled);
            Assert.IsTrue(home.IsInPorch);
            Assert.IsTrue(stage.IsKeyboardFocused);
            Assert.IsTrue(ImeKey(stage, window, Key.Escape, true).Handled);
            Assert.IsFalse(home.IsInPorch);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    [DataRow(Key.A, HomeEditor.Wardrobe)]
    [DataRow(Key.D, HomeEditor.Face)]
    public void ImeHomeNavigationKeepsThePhysicalKeyMeaning(Key key, HomeEditor editor)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = WindowFor(home);
        try
        {
            window.Show(); Pump();
            var stage = Element<FrameworkElement>(home, "Stage");
            Assert.IsTrue(stage.Focus());
            Assert.IsTrue(ImeKey(stage, window, key, true).Handled);
            Assert.IsTrue(home.IsEditing);
            Assert.AreEqual(editor, home.SelectedEditor);
            Assert.IsFalse(home.IsInPorch);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(Key.A, false)]
    [DataRow(Key.D, false)]
    [DataRow(Key.S, false)]
    [DataRow(Key.Space, false)]
    [DataRow(Key.A, true)]
    [DataRow(Key.D, true)]
    [DataRow(Key.S, true)]
    [DataRow(Key.Space, true)]
    public void ImeDirectionalKeysSetAndReleaseLiveIntent(Key key, bool wardrobe)
    {
        using var rig = new Rig();
        if (wardrobe) rig.Home.ShowEditor(HomeEditor.Wardrobe);
        else Assert.IsTrue(ImeKey(rig.Stage, rig.Window, Key.W, true).Handled);
        Assert.IsTrue(rig.Stage.Focus());
        Assert.IsTrue(ImeKey(rig.Stage, rig.Window, key, true).Handled);
        Assert.AreEqual(ExpectedIntent(key), Intent(rig.Home));
        Assert.IsTrue(ImeKey(rig.Stage, rig.Window, key, false).Handled);
        Assert.AreEqual(DirectionalControlIntent.None, Intent(rig.Home));
        Assert.AreEqual(wardrobe, rig.Home.IsEditing);
        Assert.AreEqual(!wardrobe, rig.Home.IsInPorch);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(Key.A)]
    [DataRow(Key.D)]
    [DataRow(Key.S)]
    [DataRow(Key.Space)]
    public void ImeWardrobeKeyUpReleasesHeldIntentAfterFocusMovesToAButton(Key key)
    {
        using var rig = new Rig();
        rig.Home.ShowEditor(HomeEditor.Wardrobe);
        Assert.IsTrue(rig.Stage.Focus());
        Assert.IsTrue(ImeKey(rig.Stage, rig.Window, key, true).Handled);
        Assert.AreEqual(ExpectedIntent(key), Intent(rig.Home));
        var button = Element<Button>(rig.Home, "AccessoryCloseEditorButton");
        Assert.IsTrue(button.Focus());
        Assert.AreEqual(ExpectedIntent(key), Intent(rig.Home));
        Assert.IsTrue(ImeKey(button, rig.Window, key, false).Handled);
        Assert.AreEqual(DirectionalControlIntent.None, Intent(rig.Home));
        Assert.IsTrue(rig.Home.IsEditing);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ImeGameplayAndCompositionEscapeKeepAccessoryNameFocusAndDraft()
    {
        using var rig = new Rig();
        rig.Home.ShowEditor(HomeEditor.Wardrobe);
        var panel = Element<StackPanel>(rig.Home, "WardrobeOptions").Children.OfType<AccessoryEditorPanel>().Single();
        panel.SetBodyAnchor(new Point(.34, .59));
        Assert.IsTrue(panel.SelectSection(AccessoryEditorSection.Placement));
        var name = Descendants<TextBox>(panel).Single(input => AutomationProperties.GetName(input) == "配饰名称");
        Assert.IsTrue(name.Focus());
        name.Text = "虚构输入中的配饰名称";
        var draft = rig.Accessories.DraftConfiguration;
        foreach (var key in GameplayAndEscape)
        {
            Assert.IsFalse(ImeKey(name, rig.Window, key, true).Handled);
            ImeKey(name, rig.Window, key, false);
            Assert.IsTrue(name.IsKeyboardFocused);
            Assert.AreEqual("虚构输入中的配饰名称", name.Text);
            Assert.AreEqual(draft, rig.Accessories.DraftConfiguration);
            Assert.AreEqual(DirectionalControlIntent.None, Intent(rig.Home));
            Assert.IsTrue(rig.Home.IsEditing);
            Assert.AreEqual(HomeEditor.Wardrobe, rig.Home.SelectedEditor);
            Assert.IsFalse(rig.Home.IsInPorch);
        }
        // An ordinary Escape retains the separately approved wardrobe cancel contract.
        var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(rig.Window)!, 0, Key.Escape)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        name.RaiseEvent(escape);
        Assert.IsTrue(escape.Handled);
        Assert.IsFalse(rig.Home.IsEditing);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void AccountOverlayKeepsImeKeysInPasswordOrNicknameAndPreservesWardrobeDraft(bool nickname)
    {
        using var rig = new Rig();
        rig.Home.ShowEditor(HomeEditor.Wardrobe);
        var panel = Element<StackPanel>(rig.Home, "WardrobeOptions").Children.OfType<AccessoryEditorPanel>().Single();
        panel.SetBodyAnchor(new Point(.34, .59));
        var draft = rig.Accessories.DraftConfiguration;
        Assert.IsTrue(rig.Stage.Focus());
        Assert.IsTrue(ImeKey(rig.Stage, rig.Window, Key.D, true).Handled);
        Assert.IsTrue(Intent(rig.Home).Right);
        rig.Home.AccountRequested += () => rig.Home.AccountPanel.Apply(nickname
            ? new(AccountPanelPage.Nickname, Nickname: "虚构昵称", ProfileResolved: true)
            : new(AccountPanelPage.Login));
        rig.Home.ShowAccountPanel(); Pump();
        Control input;
        if (nickname)
        {
            var field = Element<TextBox>(rig.Home.AccountPanel, "AccountNickname");
            field.Text = "虚构未保存昵称"; input = field;
        }
        else
        {
            var field = Element<PasswordBox>(rig.Home.AccountPanel, "AccountPassword");
            field.Password = "FictionalImeFixtureOnly"; input = field;
        }
        Assert.IsTrue(input.Focus());
        Assert.AreEqual(DirectionalControlIntent.None, Intent(rig.Home));
        foreach (var key in GameplayAndEscape)
        {
            Assert.IsFalse(ImeKey(input, rig.Window, key, true).Handled);
            ImeKey(input, rig.Window, key, false);
            Assert.IsTrue(input.IsKeyboardFocused);
            Assert.IsTrue(rig.Home.IsAccountPanelOpen);
            Assert.IsTrue(rig.Home.IsEditing);
            Assert.AreEqual(HomeEditor.Wardrobe, rig.Home.SelectedEditor);
            Assert.IsFalse(rig.Home.IsInPorch);
            Assert.AreEqual(draft, rig.Accessories.DraftConfiguration);
            Assert.AreEqual(DirectionalControlIntent.None, Intent(rig.Home));
            if (input is TextBox text) Assert.AreEqual("虚构未保存昵称", text.Text);
            else Assert.AreEqual("FictionalImeFixtureOnly", ((PasswordBox)input).Password);
        }
    }

    private static readonly Key[] GameplayAndEscape = [Key.A, Key.D, Key.W, Key.S, Key.Space, Key.Escape];
    private static DirectionalControlIntent ExpectedIntent(Key key) => new(key == Key.A, key == Key.D, key == Key.Space, key == Key.S);
    private static DirectionalControlIntent Intent(ProductHomeView home) =>
        (DirectionalControlIntent)typeof(ProductHomeView).GetField("liveIntent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(home)!;
    private static T Element<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new AssertFailedException("Missing " + name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Descendants<T>(child)) yield return nested;
    }
    private static KeyEventArgs ImeKey(UIElement target, Window window, Key key, bool down)
    {
        var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key)
        { RoutedEvent = down ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent };
        // This internal method constructs WPF's real IME event wrapper; it does
        // not change business state or replace the separate native-window gate.
        typeof(KeyEventArgs).GetMethod("MarkImeProcessed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(input, null);
        Assert.AreEqual(Key.ImeProcessed, input.Key);
        Assert.AreEqual(key, input.ImeProcessedKey);
        target.RaiseEvent(input);
        return input;
    }
    private static Window WindowFor(ProductHomeView home) => new()
    { Content = home, Width = 1024, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false };
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        EventHandler tick = (_, _) => frame.Continue = false;
        timer.Tick += tick;
        try { timer.Start(); Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); timer.Tick -= tick; }
    }

    private sealed class Rig : IDisposable
    {
        internal readonly ProductHomeView Home = new() { ReduceMotion = true };
        internal readonly AccessorySettingsSession Accessories = new(Path.Combine(TestRepository.Root,
            "artifacts", "main-absorption", "ime-input-cases", Guid.NewGuid().ToString("N")));
        internal readonly Window Window;
        internal FrameworkElement Stage => Element<FrameworkElement>(Home, "Stage");
        private readonly StackedDangoSceneRuntime runtime;
        internal Rig()
        {
            runtime = StackedDangoSceneRuntime.CreateProduct(Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName));
            Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
            Home.BindAccessories(Accessories);
            Home.BindLiveScene(new DesktopSceneSession(), runtime);
            Window = WindowFor(Home);
            Window.Show(); Pump();
            Assert.IsTrue(Home.OwnsLiveFrameDriver);
            Assert.IsTrue(Stage.Focus());
        }
        public void Dispose() { Window.Close(); Home.ReleaseLiveScene(); runtime.Dispose(); }
    }
}
