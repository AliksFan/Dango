using System.Windows;
using System.Windows.Controls;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class ProductControlCenterTests
{
    [STATestMethod]
    public void ProductModeHidesDeveloperControlsWhileKeepingDiagnosticsAvailable()
    {
        var product = CreateWindow(new ProductHost(), ApplicationUiMode.Product, () => { });
        var review = CreateWindow(new ProductHost(), ApplicationUiMode.DeveloperReview, () => { });
        try
        {
            product.ShowOverview();
            review.ShowOverview();

            foreach (var name in new[]
            {
                "VerificationNavigation", "ContinueVerificationButton", "PopulationCard", "SkeletonCard", "PhysicsCard",
            })
            {
                Assert.AreEqual(Visibility.Collapsed, Element<UIElement>(product, name).Visibility, name);
                Assert.AreNotEqual(Visibility.Collapsed, Element<UIElement>(review, name).Visibility, name);
            }

            var home = Element<ProductHomeView>(product, "ProductHome");
            Click((Button)home.FindName("SettingsButton"));
            Assert.AreEqual(Visibility.Visible, ((UIElement)home.FindName("SettingsPage")).Visibility);
            Assert.IsNull(home.FindName("SproutHatButton"), "产品设置页不应显示帽型入口。");
            Assert.AreEqual(Visibility.Collapsed, Element<UIElement>(product, "LegacyShell").Visibility);
        }
        finally
        {
            Close(product);
            Close(review);
        }
    }

    [STATestMethod]
    public void UnavailableSceneStillExposesOverviewDiagnosticsRetryAndExitWithoutCreatingAWorld()
    {
        var host = new ProductHost(SceneInitializationState.Unavailable);
        var exitCalls = 0;
        var window = CreateWindow(host, ApplicationUiMode.Product, () => exitCalls++);
        try
        {
            window.ShowOverview();
            window.ShowDiagnostics();
            var home = Element<ProductHomeView>(window, "ProductHome");

            Assert.AreEqual(Visibility.Visible, ((UIElement)home.FindName("SettingsPage")).Visibility);
            Assert.AreEqual(Visibility.Collapsed, Element<UIElement>(window, "LegacyShell").Visibility);
            Assert.AreEqual(Visibility.Visible, ((Button)home.FindName("RetryInitializationButton")).Visibility);
            Assert.IsFalse(((Button)home.FindName("RestoreDangoSettingsButton")).IsEnabled);
            Assert.AreEqual(0, host.WorldCreateCalls);

            Click((Button)home.FindName("RetryInitializationButton"));
            Assert.AreEqual(1, host.RetryCalls);
            Assert.AreEqual(0, host.WorldCreateCalls);

            Click((Button)home.FindName("SettingsExitButton"));
            Assert.AreEqual(1, exitCalls);
        }
        finally
        {
            Close(window);
        }
    }

    [STATestMethod]
    public void RetryingFailedHatSaveDoesNotReplayTheAlreadyAppliedHatCommand()
    {
        var writer = new FailOnceWriter();
        var path = Path.Combine(TestRepository.Root, "artifacts", $"fictional-product-settings-{Guid.NewGuid():N}.json");
        var session = new ApplicationSettingsSession(
            path,
            new ControlCenterWindowSettingsStore(writer));
        var host = new ProductHost();
        var controller = new ApplicationCommandController(host, ApplicationUiMode.Product);
        var window = new ControlCenterWindow(
            controller,
            VerificationDefinition.CreateP1003WorkerW(),
            new VerificationSessionStore(VerificationDefinition.CreateP1003WorkerW()),
            Path.Combine(TestRepository.Root, "artifacts", "fictional-verification-draft.json"),
            () => { },
            session,
            () => session.TrySave(session.Current with { HatPresetId = host.HatPresetId }),
            () => { });
        try
        {
            window.ShowOverview();
            var home = Element<ProductHomeView>(window, "ProductHome");
            Click((Button)home.FindName("SettingsButton"));
            var hats = Element<ComboBox>(window, "HatPresetComboBox");
            hats.SelectedItem = hats.Items.OfType<ComboBoxItem>()
                .Single(item => (string)item.Tag == "top-hat");

            Assert.AreEqual(1, host.HatApplyCalls);
            Assert.AreEqual(1, writer.Calls);
            StringAssert.Contains(Element<TextBlock>(window, "SettingsSaveStatusText").Text, "未保存");
            var notice = (Button)home.FindName("NoticeButton");
            Assert.AreEqual(Visibility.Visible, notice.Visibility, "A visible home must not conceal save failure.");
            Assert.AreEqual(Visibility.Visible, ((Border)home.FindName("SaveWarningRow")).Visibility);
            Assert.IsTrue(session.HasWarning);
            window.ShowDiagnostics();
            Assert.AreEqual(Visibility.Visible, ((UIElement)home.FindName("SettingsDiagnosticsText")).Visibility);

            Click((Button)home.FindName("RetrySaveSettingsButton"));

            Assert.AreEqual(1, host.HatApplyCalls, "Retry save must not replay the product command.");
            Assert.AreEqual(2, writer.Calls, "Retry save must retry the writer once.");
            Assert.AreEqual("top-hat", session.Current.HatPresetId);
            Assert.AreEqual("top-hat", new ControlCenterWindowSettingsStore().Load(path).HatPresetId);
            Assert.IsFalse(session.HasWarning);
            Assert.AreEqual(Visibility.Collapsed, notice.Visibility);
        }
        finally
        {
            Close(window);
        }
    }

    private static ControlCenterWindow CreateWindow(ProductHost host, ApplicationUiMode mode, Action requestExit) =>
        new(
            new ApplicationCommandController(host, mode),
            VerificationDefinition.CreateP1003WorkerW(),
            new VerificationSessionStore(VerificationDefinition.CreateP1003WorkerW()),
            Path.Combine(TestRepository.Root, "artifacts", $"fictional-draft-{Guid.NewGuid():N}.json"),
            requestExit);

    private static T Element<T>(ControlCenterWindow window, string name) where T : class =>
        (window.FindName(name) as T) ?? throw new AssertFailedException($"Missing compiled control: {name}");

    private static Button FindButton(DependencyObject root, string content)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button { Content: string value } button && value == content) return button;
            var result = FindButtonOrNull(child, content);
            if (result is not null) return result;
        }
        throw new AssertFailedException($"Missing button: {content}");
    }

    private static Button? FindButtonOrNull(DependencyObject root, string content)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button { Content: string value } button && value == content) return button;
            var result = FindButtonOrNull(child, content);
            if (result is not null) return result;
        }
        return null;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Close(ControlCenterWindow window)
    {
        window.PrepareForExit();
        window.Close();
    }

    private sealed class FailOnceWriter : IAtomicTextWriter
    {
        public int Calls { get; private set; }

        public void Write(string path, string content)
        {
            Calls++;
            if (Calls == 1) throw new IOException("fictional first write failure");
            new AtomicTextFileWriter().Write(path, content);
        }
    }

    private sealed class ProductHost : IDesktopHostControl, IDangoSceneControl
    {
        private SceneInitializationState initializationState;
        private string hatPresetId = "sprout";

        public ProductHost(SceneInitializationState initializationState = SceneInitializationState.Ready) =>
            this.initializationState = initializationState;

        public int WorldCreateCalls { get; private set; }
        public int RetryCalls { get; private set; }
        public int HatApplyCalls { get; private set; }
        public SceneInitializationState InitializationState => initializationState;
        public string HatPresetId => hatPresetId;

        public bool TryApplyHatPreset(string id)
        {
            HatApplyCalls++;
            if (id is not ("sprout" or "top-hat" or "bowler")) return false;
            hatPresetId = id;
            return true;
        }

        public bool RetryInitialization()
        {
            RetryCalls++;
            return false;
        }

        public DesktopHostSnapshot GetSnapshot() => new(
            "Wallpaper", true, true, "fictional test host", initializationState == SceneInitializationState.Ready ? null : "模块不可用");

        public void SetVisibility(bool isVisible) { }
        public void SetInteractionEnabled(bool isEnabled) { }
        public string CreateDiagnosticSummary() => "fictional safe diagnostics";
    }
}
