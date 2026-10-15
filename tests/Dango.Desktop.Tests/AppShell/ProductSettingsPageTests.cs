using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class ProductSettingsPageTests
{
    [STATestMethod]
    public void SettingsOpensInsideTheAcceptedShellAndReturnsToTheSameHome()
    {
        using var rig = new SettingsRig();
        rig.OpenSettings();
        Assert.AreEqual(Visibility.Collapsed, ((UIElement)rig.Window.FindName("LegacyShell")).Visibility);
        Assert.AreEqual(Visibility.Collapsed, ((UIElement)rig.Home.FindName("HomeContent")).Visibility);
        Assert.AreEqual(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting((DependencyObject)rig.Home.FindName("SettingsFeedbackText")));
        Assert.AreEqual(Visibility.Visible, ((UIElement)rig.Window.FindName("DesktopPage")).Visibility);
        rig.Click("BackFromSettingsButton");
        Assert.IsFalse(rig.Home.IsSettingsOpen);
        Assert.AreEqual(Visibility.Visible, ((UIElement)rig.Home.FindName("HomeContent")).Visibility);
        Assert.AreEqual(Visibility.Collapsed, ((UIElement)rig.Home.FindName("SettingsShell")).Visibility);
        Assert.AreEqual(Visibility.Visible, ((UIElement)rig.Window.FindName("OverviewPage")).Visibility);
    }

    [STATestMethod]
    public void SettingsHasNoAppearanceEntryWhileHomeKeepsWardrobe()
    {
        using var rig = new SettingsRig();
        rig.OpenSettings();
        foreach (var name in new[] { "SproutHatButton", "TopHatButton", "BowlerHatButton" }) Assert.IsNull(rig.Home.FindName(name));
        rig.Click("BackFromSettingsButton");
        rig.Click("WardrobeButton");
        Assert.IsTrue(rig.Home.IsEditing);
        Assert.AreEqual(HomeEditor.Wardrobe, rig.Home.SelectedEditor);
    }

    [STATestMethod]
    public void PresetAppliesAndPersistsWithoutChangingLegacyPreferences()
    {
        using var rig = new SettingsRig();
        rig.OpenSettings();
        rig.Click("PortraitSettingsButton");
        Assert.AreEqual(480d, rig.Window.Width);
        Assert.AreEqual(800d, rig.Window.Height);
        var stored = new ControlCenterWindowSettingsStore().Load(rig.Path);
        Assert.AreEqual(ControlCenterWindowPreset.Portrait, stored.SelectedPreset);
        Assert.AreEqual("已选", AutomationProperties.GetItemStatus(rig.Button("PortraitSettingsButton")));
        Assert.AreEqual(DesktopDisplayMode.Wallpaper, stored.DisplayMode);
        Assert.AreEqual(KeyboardControlSettings.CreateDefault(), stored.KeyboardControl);
        Assert.IsTrue(stored.InteractionEnabled);
        Assert.IsFalse(File.ReadAllText(rig.Path).Contains("\"IsVisible\"", StringComparison.Ordinal));
    }

    [STATestMethod]
    public void EscapeLeavesSettingsAndRestoresHomeFocus()
    {
        using var rig = new SettingsRig();
        rig.ShowOffscreen();
        rig.OpenSettings();
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(rig.Window)!, 0, Key.Escape)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        rig.Home.RaiseEvent(key);
        Assert.IsTrue(key.Handled);
        Assert.IsFalse(rig.Home.IsSettingsOpen);
        Assert.IsTrue(((FrameworkElement)rig.Home.FindName("Stage")).IsKeyboardFocused);
    }

    [STATestMethod]
    public void EnteringSettingsFromEditorMovesFocusToVisibleReturnAction()
    {
        using var rig = new SettingsRig();
        rig.ShowOffscreen();
        rig.Window.ShowOverview();
        rig.Click("FaceButton");
        rig.Button("SettingsButton").Focus();
        rig.Click("SettingsButton");
        Pump(100);
        Assert.IsFalse(rig.Home.IsEditing);
        Assert.IsTrue(rig.Button("BackFromSettingsButton").IsKeyboardFocused);
    }

    [STATestMethod]
    public void DiagnosticsUseCurrentRoomInsteadOfLegacyHost()
    {
        using var rig = new SettingsRig();
        rig.Scene = new("桌宠房 · 本地单人", "暂时隐藏", "fictional room failure", CanRestoreDisplay: true, Diagnostics: "fictional room surfaces");
        rig.Window.ShowDiagnostics();
        var text = ((TextBox)rig.Home.FindName("SettingsDiagnosticsText")).Text;
        StringAssert.Contains(text, "桌宠房 · 本地单人");
        StringAssert.Contains(text, "fictional room surfaces");
        StringAssert.Contains(text, "fictional room failure");
        Assert.IsFalse(text.Contains("legacy host diagnostics", StringComparison.Ordinal));
        Assert.AreEqual("暂时隐藏", ((TextBlock)rig.Home.FindName("SettingRunState")).Text);
        Assert.AreEqual(0, rig.SaveCalls);
    }

    [STATestMethod]
    public void UnavailableActionsCannotBeInvokedByRoutedClickDuringHandoff()
    {
        using var rig = new SettingsRig();
        rig.Scene = new("普通房", "正在切换场景，请稍候");
        rig.OpenSettings();
        rig.Click("RestoreRoomDisplayButton");
        rig.Click("ReinitializeSceneButton");
        Assert.AreEqual(0, rig.RestoreCalls);
        Assert.AreEqual(0, rig.ReinitializeCalls);
        Assert.AreEqual(0, rig.SaveCalls);
    }

    [STATestMethod]
    public void RecoveryChecksCapabilitiesAgainAfterConfirmationAndCancellationDoesNothing()
    {
        using var rig = new SettingsRig();
        rig.Scene = new("普通房", "需要恢复", "fictional error", CanReinitialize: true);
        rig.OpenSettings();
        rig.Window.ConfirmSceneReinitialization = () => false;
        rig.Click("ReinitializeSceneButton");
        Assert.AreEqual(0, rig.ReinitializeCalls);
        rig.Scene = new("普通房", "需要恢复", "fictional error", CanReinitialize: true, Revision: 1);
        rig.Window.ConfirmSceneReinitialization = () => { rig.Scene = rig.Scene with { Revision = 2 }; return true; };
        rig.Click("ReinitializeSceneButton");
        Assert.AreEqual(0, rig.ReinitializeCalls, "确认期间场景更换后不能把旧确认用于新场景。");
        rig.Window.ConfirmSceneReinitialization = () => { rig.Scene = new("普通房", "正在保存家具，请稍候"); return true; };
        rig.Click("ReinitializeSceneButton");
        Assert.AreEqual(0, rig.ReinitializeCalls);
    }

    [STATestMethod]
    public void RestoreAndReinitializeAreSeparateAndFailuresRemainVisibleWithoutSaving()
    {
        using var rig = new SettingsRig();
        rig.Scene = new("桌宠房", "暂时隐藏", CanRestoreDisplay: true, CanReinitialize: true);
        rig.OpenSettings();
        rig.Click("RestoreRoomDisplayButton");
        Assert.AreEqual(1, rig.RestoreCalls);
        Assert.AreEqual(0, rig.ReinitializeCalls);
        rig.FailRecovery = true;
        rig.Click("ReinitializeSceneButton");
        Assert.AreEqual(1, rig.ReinitializeCalls);
        Assert.AreEqual(Visibility.Visible, rig.FeedbackBar().Visibility);
        StringAssert.Contains(rig.FeedbackText().Text, "恢复未完成");
        Assert.AreEqual(0, rig.SaveCalls);
    }

    [STATestMethod]
    public void FailedPresetSaveRemainsVisibleAndRetryDoesNotRepeatSceneCommands()
    {
        var writer = new FailOnceWriter();
        using var rig = new SettingsRig(writer);
        rig.OpenSettings();
        rig.Click("PortraitSettingsButton");
        Assert.AreEqual(480d, rig.Window.Width, "保存失败不撤销已经生效的会话尺寸。");
        Assert.IsTrue(rig.Session.HasWarning);
        Assert.AreEqual(1, writer.Calls);
        Assert.AreEqual(Visibility.Visible, rig.FeedbackBar().Visibility);
        Assert.AreEqual(Visibility.Visible, ((Border)rig.Home.FindName("SaveWarningRow")).Visibility);
        StringAssert.Contains(rig.FeedbackText().Text, "未保存");
        rig.Click("RetrySaveSettingsButton");
        Assert.IsFalse(rig.Session.HasWarning);
        Assert.AreEqual(3, writer.Calls);
        Assert.AreEqual(0, rig.RestoreCalls);
        Assert.AreEqual(0, rig.ReinitializeCalls);
        Assert.AreEqual(1, rig.SaveCalls);
        Assert.AreEqual(Visibility.Collapsed, rig.FeedbackBar().Visibility);
        Assert.AreEqual(ControlCenterWindowPreset.Portrait, new ControlCenterWindowSettingsStore().Load(rig.Path).SelectedPreset);
    }

    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private sealed class SettingsRig : IDisposable
    {
        public SettingsRig(IAtomicTextWriter? writer = null)
        {
            Path = System.IO.Path.Combine(TestRepository.Root, "artifacts", "settings-refresh", $"fictional-{Guid.NewGuid():N}.json");
            Session = new(Path, new ControlCenterWindowSettingsStore(writer));
            var definition = VerificationDefinition.CreateP1003WorkerW();
            Window = new(new ApplicationCommandController(new LegacyHost(), ApplicationUiMode.Product), definition,
                new VerificationSessionStore(definition), System.IO.Path.Combine(TestRepository.Root, "artifacts", "fictional-draft.json"),
                () => { }, Session, () => { SaveCalls++; Session.TrySave(); });
            Home = Window.ProductHome;
            Window.BindSettingsScene(() => Scene,
                () => { RestoreCalls++; if (FailRecovery) throw new InvalidOperationException("fictional"); },
                () => { ReinitializeCalls++; if (FailRecovery) throw new InvalidOperationException("fictional"); });
            Window.ConfirmSceneReinitialization = () => true;
        }
        public string Path { get; }
        public ApplicationSettingsSession Session { get; }
        public ControlCenterWindow Window { get; }
        public ProductHomeView Home { get; }
        public ProductSettingsSceneSnapshot Scene { get; set; } = new("普通房 · 窗口", "正在运行");
        public int RestoreCalls { get; private set; }
        public int ReinitializeCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public bool FailRecovery { get; set; }
        public Button Button(string name) => (Button)Home.FindName(name);
        public void Click(string name) => Button(name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        public FrameworkElement FeedbackBar() => (FrameworkElement)Home.FindName("SettingsFeedbackBar");
        public TextBlock FeedbackText() => (TextBlock)Home.FindName("SettingsFeedbackText");
        public void OpenSettings() { Window.ShowOverview(); Click("SettingsButton"); }
        public void ShowOffscreen() { Window.Left = -10000; Window.Top = -10000; Window.ShowInTaskbar = false; Window.Show(); }
        public void Dispose() { Window.PrepareForExit(); Window.Close(); }
    }

    private sealed class FailOnceWriter : IAtomicTextWriter
    {
        public int Calls { get; private set; }
        public void Write(string path, string content)
        {
            if (++Calls == 1) throw new IOException("fictional write failure");
            new AtomicTextFileWriter().Write(path, content);
        }
    }

    private sealed class LegacyHost : IDesktopHostControl
    {
        public DesktopHostSnapshot GetSnapshot() => new("Wallpaper", false, true, "legacy host", null);
        public void SetVisibility(bool value) => Assert.Fail("设置页不应再控制旧宿主。");
        public void SetInteractionEnabled(bool value) => Assert.Fail("设置页不应再控制旧宿主输入。");
        public string CreateDiagnosticSummary() => "legacy host diagnostics";
    }
}
