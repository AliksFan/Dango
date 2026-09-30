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
    public void ProductModeHidesDeveloperControlsWhileKeepingHatAndDiagnosticsAvailable()
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

            Assert.AreEqual(Visibility.Visible, Element<UIElement>(product, "HatCard").Visibility);
            Assert.AreNotEqual(Visibility.Collapsed, Element<UIElement>(product, "DiagnosticsNavigation").Visibility);
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

            Assert.AreEqual(Visibility.Visible, Element<Button>(window, "RetrySceneButton").Visibility);
            Assert.IsFalse(Element<Button>(window, "RestoreDangoButton").IsEnabled);
            Assert.AreEqual(0, host.WorldCreateCalls);

            Click(Element<Button>(window, "RetrySceneButton"));
            Assert.AreEqual(1, host.RetryCalls);
            Assert.AreEqual(0, host.WorldCreateCalls);

            Click(FindButton(window, "退出 Dango"));
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
            var hats = Element<ComboBox>(window, "HatPresetComboBox");
            hats.SelectedItem = hats.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == "top-hat");

            Assert.AreEqual(1, host.HatApplyCalls);
            Assert.AreEqual(1, writer.Calls);
            StringAssert.Contains(Element<TextBlock>(window, "SettingsSaveStatusText").Text, "未保存");

            Click(Element<Button>(window, "RetrySaveButton"));

            Assert.AreEqual(1, host.HatApplyCalls, "Retry save must not replay the product command.");
            Assert.AreEqual(2, writer.Calls, "Retry save must retry the writer once.");
            Assert.AreEqual("top-hat", session.Current.HatPresetId);
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
