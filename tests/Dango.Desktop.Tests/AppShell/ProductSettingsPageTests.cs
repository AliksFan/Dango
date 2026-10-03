using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class ProductSettingsPageTests
{
    [STATestMethod]
    public void SettingsOpensInsideTheAcceptedShellAndReturnsToTheSameHome()
    {
        var window = new ControlCenterWindow(
            new ApplicationCommandController(new UnavailableHost(), ApplicationUiMode.Product),
            VerificationDefinition.CreateP1003WorkerW(),
            new VerificationSessionStore(VerificationDefinition.CreateP1003WorkerW()),
            Path.Combine(TestRepository.Root, "artifacts", "fictional-settings-page-draft.json"), () => { });
        try
        {
            window.ShowOverview();
            var home = (ProductHomeView)window.FindName("ProductHome");
            Click((Button)home.FindName("SettingsButton"));
            var settings = home.FindName("SettingsPage") as UIElement;
            Assert.IsNotNull(settings, "Settings must use the accepted shell rather than the old sidebar.");
            Assert.AreEqual(Visibility.Visible, settings.Visibility);
            Assert.AreEqual(Visibility.Collapsed, ((UIElement)window.FindName("LegacyShell")).Visibility);
            Assert.AreEqual(Visibility.Collapsed, ((UIElement)home.FindName("HomeContent")).Visibility);
            Assert.AreEqual(AutomationLiveSetting.Polite,
                AutomationProperties.GetLiveSetting((DependencyObject)home.FindName("SettingsFeedbackText")),
                "Failure feedback should be announced without stealing keyboard focus.");
            Assert.AreEqual(Visibility.Visible, ((UIElement)window.FindName("DesktopPage")).Visibility,
                "The host page state should follow the visible product settings page.");
            Assert.AreEqual(Visibility.Collapsed, ((UIElement)home.FindName("HomeNavigation")).Visibility);
            Assert.AreEqual(2, ((Button)home.FindName("SettingsButton")).BorderThickness.Bottom);
            Click((Button)home.FindName("BackFromSettingsButton"));
            Assert.AreEqual(Visibility.Visible, ((UIElement)home.FindName("HomeContent")).Visibility);
            Assert.AreEqual(Visibility.Collapsed, settings.Visibility);
            Assert.AreEqual(Visibility.Visible, ((UIElement)window.FindName("OverviewPage")).Visibility);
            Assert.AreEqual(Visibility.Collapsed, ((UIElement)home.FindName("HomeNavigation")).Visibility);
            Assert.AreEqual(0, ((Button)home.FindName("SettingsButton")).BorderThickness.Bottom);
        }
        finally
        {
            window.PrepareForExit();
            window.Close();
        }
    }

    [STATestMethod]
    public void SettingsHasNoAppearanceEntryWhileHomeKeepsWardrobe()
    {
        using var rig = new SettingsRig();
        rig.OpenSettings();

        foreach (var name in new[] { "SproutHatButton", "TopHatButton", "BowlerHatButton" })
            Assert.IsNull(rig.Home.FindName(name), $"产品设置页不应再提供帽型入口：{name}");
        var settingsPage = (DependencyObject)rig.Home.FindName("SettingsPage");
        Assert.IsFalse(TextBlocks(settingsPage).Any(text => text.Text is "外观" or "现有帽型"),
            "产品设置页不应保留外观或现有帽型分区。");

        rig.Click("BackFromSettingsButton");
        rig.Click("WardrobeButton");
        Assert.IsTrue(rig.Home.IsEditing, "首页的换装入口仍需可用。");
        Assert.AreEqual(HomeEditor.Wardrobe, rig.Home.SelectedEditor);
        Assert.AreEqual(Visibility.Visible, ((UIElement)rig.Home.FindName("WardrobeOptions")).Visibility);
    }

    [STATestMethod]
    public void SettingsPresetChoiceAppliesToTheSameWindowAndSurvivesReload()
    {
        using var rig = new SettingsRig();
        rig.OpenSettings();

        foreach (var name in new[] { "Landscape1080pSettingsButton", "Landscape1440pSettingsButton", "PortraitSettingsButton" })
            Assert.IsNotNull(rig.Home.FindName(name), $"Missing one of the three fixed size choices: {name}.");

        rig.Click("PortraitSettingsButton");
        Assert.AreEqual(480d, rig.Window.Width);
        Assert.AreEqual(800d, rig.Window.Height);
        var stored = new ControlCenterWindowSettingsStore().Load(rig.Path);
        Assert.AreEqual("Portrait", stored.GetType().GetProperty("SelectedPreset")?.GetValue(stored)?.ToString(),
            "The chosen preset must survive restarting the control center.");
        Assert.AreEqual("已选", AutomationProperties.GetItemStatus(rig.Button("PortraitSettingsButton")));
    }

    [STATestMethod]
    public void EscapeLeavesSettingsAndRestoresHomeFocus()
    {
        using var rig = new SettingsRig();
        rig.Window.Left = -10000;
        rig.Window.Top = -10000;
        rig.Window.ShowInTaskbar = false;
        rig.Window.Show();
        rig.OpenSettings();

        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(rig.Window)!, 0, Key.Escape)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        rig.Home.RaiseEvent(key);

        Assert.IsTrue(key.Handled);
        Assert.IsFalse(rig.Home.IsSettingsOpen);
        Assert.AreEqual(Visibility.Visible, ((UIElement)rig.Window.FindName("OverviewPage")).Visibility);
        Assert.IsTrue(((FrameworkElement)rig.Home.FindName("Stage")).IsKeyboardFocused,
            "Returning from settings must focus the visible home stage, not a hidden navigation item.");
    }

    [STATestMethod]
    public void EnteringSettingsFromAnOpenEditorMovesFocusToVisibleSettingsContent()
    {
        using var rig = new SettingsRig();
        rig.Window.Left = -10000;
        rig.Window.Top = -10000;
        rig.Window.ShowInTaskbar = false;
        rig.Window.Show();
        rig.Window.ShowOverview();
        rig.Click("FaceButton");
        Assert.IsTrue(rig.Home.IsEditing);
        rig.Button("SettingsButton").Focus();

        rig.Click("SettingsButton");
        Pump(100);

        Assert.IsTrue(rig.Home.IsSettingsOpen);
        Assert.IsFalse(rig.Home.IsEditing);
        Assert.IsTrue(rig.Button("BackFromSettingsButton").IsKeyboardFocused,
            "Focus must not remain in the collapsed home editor.");
    }

    [STATestMethod]
    public void SettingsApplyToTheHostWhileOnlyDurablePreferencesSurviveASettingsReload()
    {
        using var rig = new SettingsRig();
        rig.OpenSettings();

        rig.Click("DisplayToggleButton");
        Assert.IsFalse(rig.Host.GetSnapshot().IsVisible);
        Assert.AreEqual("已隐藏", rig.Button("DisplayToggleButton").Content);
        StringAssert.Contains(AutomationProperties.GetName(rig.Button("DisplayToggleButton")), "已隐藏");
        Assert.IsFalse(File.ReadAllText(rig.Path).Contains("\"IsVisible\"", StringComparison.Ordinal),
            "桌面显示只控制本次会话，不应写成重启后继续隐藏的偏好。");

        rig.Click("InteractionToggleButton");
        Assert.IsFalse(rig.Host.GetSnapshot().IsInteractionEnabled);
        Assert.AreEqual("已关闭", rig.Button("InteractionToggleButton").Content);
        StringAssert.Contains(AutomationProperties.GetName(rig.Button("InteractionToggleButton")), "已关闭");

        rig.Click("WindowModeButton");
        Assert.AreEqual("Window", rig.Host.GetSnapshot().Mode);
        Assert.AreEqual("已选", AutomationProperties.GetItemStatus(rig.Button("WindowModeButton")));
        Assert.IsFalse(rig.Button("AboveIconsButton").IsEnabled,
            "普通窗口不应开放仅适用于壁纸层的图标层级。");
        Assert.AreEqual(DesktopDisplayMode.Window, new ControlCenterWindowSettingsStore().Load(rig.Path).DisplayMode);

        rig.Click("WallpaperModeButton");
        rig.Click("AboveIconsButton");
        Assert.AreEqual(DesktopObjectLayer.AboveDesktopIcons, rig.Host.GetLayeringSettings().DangoLayer);
        Assert.AreEqual("已选", AutomationProperties.GetItemStatus(rig.Button("AboveIconsButton")));

        rig.Click("KeyboardToggleButton");
        Assert.AreEqual(new KeyboardControlSettings(true, KeyboardControlScheme.Wasd),
            rig.Host.GetKeyboardControlSettings());
        StringAssert.Contains(AutomationProperties.GetName(rig.Button("KeyboardToggleButton")), "已开启");
        Assert.IsNull(rig.Home.FindName("ArrowsButton"), "不应再提供方向键方案。");
        Assert.IsNull(rig.Home.FindName("WasdButton"), "只有一套键位时不应再提供方案切换。");

        var restored = new ControlCenterWindowSettingsStore().Load(rig.Path);
        Assert.IsFalse(restored.InteractionEnabled);
        Assert.AreEqual(DesktopDisplayMode.Wallpaper, restored.DisplayMode);
        Assert.AreEqual(DesktopObjectLayer.AboveDesktopIcons, restored.DesktopLayeringExperiment.DangoLayer);
        Assert.AreEqual(new KeyboardControlSettings(true, KeyboardControlScheme.Wasd), restored.KeyboardControl);
        Assert.AreEqual("sprout", restored.HatPresetId, "设置页的其他操作不应意外改变既有帽型。");
        Assert.AreEqual(6, rig.SaveCalls, "每个得到宿主确认的操作才触发保存。");
    }

    [STATestMethod]
    public void RejectedHostCommandsRestoreEverySettingFromObservedStateWithoutSaving()
    {
        var host = new StatefulSettingsHost
        {
            IgnoreVisibilityRequests = true,
            IgnoreInteractionRequests = true,
            IgnoreModeRequests = true,
            ThrowAfterNextLayeringChange = true,
            IgnoreKeyboardRequests = true,
        };
        using var rig = new SettingsRig(host);
        rig.OpenSettings();

        rig.Click("DisplayToggleButton");
        Assert.IsTrue(host.GetSnapshot().IsVisible);
        Assert.AreEqual("已显示", rig.Button("DisplayToggleButton").Content);

        rig.Click("InteractionToggleButton");
        Assert.IsTrue(host.GetSnapshot().IsInteractionEnabled);
        Assert.AreEqual("已开启", rig.Button("InteractionToggleButton").Content);

        rig.Click("WindowModeButton");
        Assert.AreEqual("Wallpaper", host.GetSnapshot().Mode);
        Assert.AreEqual("已选", AutomationProperties.GetItemStatus(rig.Button("WallpaperModeButton")));

        rig.Click("AboveIconsButton");
        Assert.AreEqual(DesktopObjectLayer.BelowDesktopIcons, host.GetLayeringSettings().DangoLayer,
            "宿主在改变层级后报错时，控制器必须恢复原值。");
        Assert.AreEqual("已选", AutomationProperties.GetItemStatus(rig.Button("BelowIconsButton")));

        rig.Click("KeyboardToggleButton");
        Assert.AreEqual(KeyboardControlSettings.CreateDefault(), host.GetKeyboardControlSettings());
        Assert.AreEqual("已关闭", rig.Button("KeyboardToggleButton").Content);
        Assert.IsNull(rig.Home.FindName("ArrowsButton"));
        Assert.IsNull(rig.Home.FindName("WasdButton"));

        StringAssert.Contains(((TextBlock)rig.Home.FindName("SettingsCommandStatus")).Text, "未确认");
        Assert.AreEqual(0, rig.SaveCalls);
        Assert.IsFalse(File.Exists(rig.Path), "失败的宿主操作不得生成宣称已应用的设置文件。");
    }

    [STATestMethod]
    public void FailedSaveRemainsVisibleAndRetryDoesNotRepeatTheAppliedHostCommand()
    {
        var writer = new FailOnceWriter();
        using var rig = new SettingsRig(writer: writer);
        rig.OpenSettings();

        rig.Click("InteractionToggleButton");
        Assert.IsFalse(rig.Host.GetSnapshot().IsInteractionEnabled,
            "宿主已经确认的会话状态不应因磁盘写入失败而被假装撤销。");
        Assert.AreEqual(1, rig.Host.InteractionCalls);
        Assert.AreEqual(1, rig.SaveCalls);
        Assert.AreEqual(1, writer.Calls);
        Assert.IsTrue(rig.Session.HasWarning);
        Assert.AreEqual(Visibility.Visible, ((Border)rig.Home.FindName("SaveWarningRow")).Visibility);
        StringAssert.Contains(((TextBlock)rig.Home.FindName("SaveWarningText")).Text, "未保存");
        Assert.IsFalse(File.Exists(rig.Path));

        rig.Click("RetrySaveSettingsButton");
        Assert.AreEqual(1, rig.Host.InteractionCalls, "重试保存不能重放已经应用的宿主命令。");
        Assert.AreEqual(1, rig.SaveCalls, "保存重试应直接交给待写入的设置会话。");
        Assert.AreEqual(2, writer.Calls);
        Assert.IsFalse(rig.Session.HasWarning);
        Assert.AreEqual(Visibility.Collapsed, ((Border)rig.Home.FindName("SaveWarningRow")).Visibility);
        Assert.IsFalse(new ControlCenterWindowSettingsStore().Load(rig.Path).InteractionEnabled);
    }

    [STATestMethod]
    public void RetryWallpaperDoesNotSilentlyRetryAFailedSaveDuringTheSameClick()
    {
        var writer = new FailOnceWriter();
        using var rig = new SettingsRig(writer: writer);
        rig.OpenSettings();

        rig.Click("RetryWallpaperSettingsButton");

        Assert.AreEqual(1, rig.SaveCalls, "A single recovery click must attempt one settings write.");
        Assert.AreEqual(1, writer.Calls);
        Assert.IsTrue(rig.Session.HasWarning, "The first failed write must remain visible until an explicit retry.");
        Assert.AreEqual(Visibility.Visible, ((FrameworkElement)rig.Home.FindName("SettingsFeedbackBar")).Visibility);
    }

    [STATestMethod]
    public void RejectedCommandShowsFixedFailureFeedbackUntilTheNextConfirmedOperation()
    {
        var host = new StatefulSettingsHost { IgnoreVisibilityRequests = true };
        using var rig = new SettingsRig(host);
        rig.OpenSettings();
        var bar = rig.FeedbackBar();
        var message = rig.FeedbackText();
        Assert.IsFalse(IsDescendantOf(bar, (DependencyObject)rig.Home.FindName("SettingsPage")),
            "失败提示须在设置滚动内容之外，才能在首屏保持可见。");

        rig.Click("DisplayToggleButton");
        Assert.AreEqual(Visibility.Visible, bar.Visibility);
        StringAssert.Contains(message.Text, "未确认");
        Assert.IsFalse(message.Text.Contains("已由桌面宿主确认", StringComparison.Ordinal));
        Assert.IsTrue(host.GetSnapshot().IsVisible);

        host.IgnoreVisibilityRequests = false;
        rig.Click("DisplayToggleButton");
        Assert.IsFalse(host.GetSnapshot().IsVisible);
        Assert.AreEqual(Visibility.Collapsed, bar.Visibility,
            "下一次操作成功且设置无保存警告后，应移除旧失败提示。");
    }

    [STATestMethod]
    public void FailedSaveKeepsFixedWarningVisibleUntilRetryActuallyPersists()
    {
        var writer = new FailOnceWriter();
        using var rig = new SettingsRig(writer: writer);
        rig.OpenSettings();
        var bar = rig.FeedbackBar();
        var message = rig.FeedbackText();

        rig.Click("InteractionToggleButton");
        Assert.IsTrue(rig.Session.HasWarning);
        Assert.AreEqual(Visibility.Visible, bar.Visibility);
        StringAssert.Contains(message.Text, "未保存");
        Assert.IsFalse(message.Text.Contains("已保存到本机", StringComparison.Ordinal));

        rig.Click("RetrySaveSettingsButton");
        Assert.IsFalse(rig.Session.HasWarning);
        Assert.AreEqual(Visibility.Collapsed, bar.Visibility,
            "重试真正写入成功后，固定保存失败提示应消失。");
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static IEnumerable<TextBlock> TextBlocks(DependencyObject root)
    {
        if (root is TextBlock currentText) yield return currentText;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nestedText in TextBlocks(child)) yield return nestedText;
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        for (DependencyObject? current = child; current is not null;
             current = LogicalTreeHelper.GetParent(current) ?? VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }
        return false;
    }

    private sealed class SettingsRig : IDisposable
    {
        public SettingsRig(StatefulSettingsHost? host = null, IAtomicTextWriter? writer = null)
        {
            Host = host ?? new StatefulSettingsHost();
            Path = System.IO.Path.Combine(TestRepository.Root, "artifacts",
                $"fictional-settings-page-{Guid.NewGuid():N}.json");
            Session = new ApplicationSettingsSession(Path, new ControlCenterWindowSettingsStore(writer));
            var definition = VerificationDefinition.CreateP1003WorkerW();
            Window = new ControlCenterWindow(new ApplicationCommandController(Host, ApplicationUiMode.Product),
                definition, new VerificationSessionStore(definition),
                System.IO.Path.Combine(TestRepository.Root, "artifacts", "fictional-settings-page-draft.json"),
                () => { }, Session, SaveObservedSettings);
            Home = (ProductHomeView)Window.FindName("ProductHome");
        }

        public StatefulSettingsHost Host { get; }
        public string Path { get; }
        public ApplicationSettingsSession Session { get; }
        public ControlCenterWindow Window { get; }
        public ProductHomeView Home { get; }
        public int SaveCalls { get; private set; }

        public Button Button(string name) => (Button)Home.FindName(name);
        public FrameworkElement FeedbackBar() => Home.FindName("SettingsFeedbackBar") as FrameworkElement
            ?? throw new AssertFailedException("设置页缺少固定失败提示条 SettingsFeedbackBar。");
        public TextBlock FeedbackText() => Home.FindName("SettingsFeedbackText") as TextBlock
            ?? throw new AssertFailedException("设置页缺少固定失败文案 SettingsFeedbackText。");
        public void Click(string name) => ProductSettingsPageTests.Click(Button(name));

        public void OpenSettings()
        {
            Window.ShowOverview();
            Click("SettingsButton");
            Assert.AreEqual(Visibility.Visible, ((UIElement)Home.FindName("SettingsPage")).Visibility);
        }

        public void Dispose()
        {
            Window.PrepareForExit();
            Window.Close();
        }

        private void SaveObservedSettings()
        {
            SaveCalls++;
            var current = Session.Current;
            Session.TrySave(current with
            {
                InteractionEnabled = Host.GetSnapshot().IsInteractionEnabled,
                DisplayMode = Enum.Parse<DesktopDisplayMode>(Host.GetSnapshot().Mode),
                DesktopLayeringExperiment = Host.GetLayeringSettings(),
                KeyboardControl = Host.GetKeyboardControlSettings(),
                HatPresetId = Host.HatPresetId,
            });
        }
    }

    private sealed class FailOnceWriter : IAtomicTextWriter
    {
        public int Calls { get; private set; }

        public void Write(string path, string content)
        {
            Calls++;
            if (Calls == 1) throw new IOException("fictional write failure");
            new AtomicTextFileWriter().Write(path, content);
        }
    }

    private sealed class StatefulSettingsHost : IDesktopHostControl, IDesktopDisplayModeControl,
        IDesktopLayeringControl, IKeyboardControl, IDangoSceneControl
    {
        private DesktopHostSnapshot snapshot = new("Wallpaper", true, true, "fictional host", null);
        private DesktopLayeringSettings layering = DesktopLayeringSettings.CreateDefault();
        private KeyboardControlSettings keyboard = KeyboardControlSettings.CreateDefault();
        private string hatPresetId = "sprout";

        public bool IgnoreVisibilityRequests { get; set; }
        public bool IgnoreInteractionRequests { get; init; }
        public bool IgnoreModeRequests { get; init; }
        public bool ThrowAfterNextLayeringChange { get; set; }
        public bool IgnoreKeyboardRequests { get; init; }
        public int InteractionCalls { get; private set; }
        public SceneInitializationState InitializationState => SceneInitializationState.Ready;
        public string HatPresetId => hatPresetId;

        public DesktopHostSnapshot GetSnapshot() => snapshot;

        public void SetVisibility(bool isVisible)
        {
            if (!IgnoreVisibilityRequests) snapshot = snapshot with { IsVisible = isVisible };
        }

        public void SetInteractionEnabled(bool isEnabled)
        {
            InteractionCalls++;
            if (!IgnoreInteractionRequests) snapshot = snapshot with { IsInteractionEnabled = isEnabled };
        }

        public void SetMode(DesktopDisplayMode mode)
        {
            if (!IgnoreModeRequests) snapshot = snapshot with { Mode = mode.ToString() };
        }

        public DesktopLayeringSettings GetLayeringSettings() => layering;

        public void ApplyLayeringSettings(DesktopLayeringSettings settings)
        {
            layering = settings;
            if (!ThrowAfterNextLayeringChange) return;
            ThrowAfterNextLayeringChange = false;
            throw new InvalidOperationException("fictional partial layering failure");
        }

        public KeyboardControlSettings GetKeyboardControlSettings() => keyboard;

        public void ApplyKeyboardControlSettings(KeyboardControlSettings settings)
        {
            if (!IgnoreKeyboardRequests) keyboard = settings;
        }

        public bool TryApplyHatPreset(string id)
        {
            if (id is not ("sprout" or "top-hat" or "bowler")) return false;
            hatPresetId = id;
            return true;
        }

        public bool RetryInitialization() => false;
        public string CreateDiagnosticSummary() => "fictional safe diagnostics";
    }

    private sealed class UnavailableHost : IDesktopHostControl
    {
        public DesktopHostSnapshot GetSnapshot() => new("Window", false, false, "fictional unavailable host", "未启动");
        public void SetVisibility(bool isVisible) { }
        public void SetInteractionEnabled(bool isEnabled) { }
        public string CreateDiagnosticSummary() => "fictional diagnostics";
    }
}
