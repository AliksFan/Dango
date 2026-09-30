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
            Assert.AreEqual(Visibility.Collapsed, Element<UIElement>(product, "ReviewSidebar").Visibility);
            Assert.AreEqual(Visibility.Visible, Element<UIElement>(product, "ProductHeader").Visibility);
            Assert.AreEqual(Visibility.Visible, Element<UIElement>(product, "ProductHome").Visibility);
            Assert.AreEqual(Visibility.Visible, Element<UIElement>(review, "ReviewSidebar").Visibility);
            Assert.AreEqual(Visibility.Collapsed, Element<UIElement>(review, "ProductHeader").Visibility);

            Click(Element<Button>(product, "ProductSettingsNavigation"));
            Assert.AreEqual(Visibility.Visible, Element<UIElement>(product, "DesktopPage").Visibility);
            Click(Element<Button>(product, "ProductHomeNavigation"));
            Assert.AreEqual(Visibility.Visible, Element<UIElement>(product, "OverviewPage").Visibility);
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
            var home = Element<ProductHomeView>(window, "ProductHome");
            var retry = HomeElement<Button>(home, "RetryButton");
            var restore = HomeElement<Button>(home, "RestoreButton");

            Assert.AreEqual(Visibility.Visible, retry.Visibility);
            Assert.IsFalse(restore.IsEnabled);
            Assert.AreEqual("团子暂时不可用", HomeElement<TextBlock>(home, "DesktopStateText").Text);
            Assert.AreEqual(0, host.WorldCreateCalls);

            Click(retry);
            Assert.AreEqual(1, host.RetryCalls);
            Assert.AreEqual(0, host.WorldCreateCalls);

            window.ShowDiagnostics();
            Assert.AreEqual(Visibility.Visible, Element<UIElement>(window, "DiagnosticsPage").Visibility);
            Click(Element<Button>(window, "ProductExitButton"));
            Assert.AreEqual(1, exitCalls);
        }
        finally
        {
            Close(window);
        }
    }

    [STATestMethod]
    public void HomeVisibilityActionChangesTheRealHostStateAndReturnsConfirmedStatus()
    {
        var host = new ProductHost();
        var window = CreateWindow(host, ApplicationUiMode.Product, () => { });
        try
        {
            window.ShowOverview();
            var home = Element<ProductHomeView>(window, "ProductHome");
            var button = HomeElement<Button>(home, "VisibilityButton");

            Assert.AreEqual("隐藏桌面团子", button.Content);
            Click(button);

            Assert.AreEqual(1, host.VisibilityCalls);
            Assert.IsFalse(host.IsVisible);
            Assert.AreEqual("显示桌面团子", button.Content);
            Assert.AreEqual("团子已隐藏", HomeElement<TextBlock>(home, "DesktopStateText").Text);
            StringAssert.Contains(HomeElement<TextBlock>(home, "CommandStatusText").Text, "宿主确认");
        }
        finally
        {
            Close(window);
        }
    }

    [STATestMethod]
    public void RejectedHomeVisibilityActionKeepsTheObservedStateAndShowsFailure()
    {
        var host = new ProductHost { RejectVisibility = true };
        var window = CreateWindow(host, ApplicationUiMode.Product, () => { });
        try
        {
            window.ShowOverview();
            var home = Element<ProductHomeView>(window, "ProductHome");
            var button = HomeElement<Button>(home, "VisibilityButton");

            Click(button);

            Assert.IsTrue(host.IsVisible);
            Assert.AreEqual("隐藏桌面团子", button.Content);
            StringAssert.Contains(HomeElement<TextBlock>(home, "CommandStatusText").Text, "未确认");
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
            Assert.AreEqual(Visibility.Visible, Element<TextBlock>(window, "ProductSaveNotice").Visibility);

            Click(Element<Button>(window, "RetrySaveButton"));

            Assert.AreEqual(1, host.HatApplyCalls, "Retry save must not replay the product command.");
            Assert.AreEqual(2, writer.Calls, "Retry save must retry the writer once.");
            Assert.AreEqual("top-hat", session.Current.HatPresetId);
            Assert.AreEqual(Visibility.Collapsed, Element<TextBlock>(window, "ProductSaveNotice").Visibility);
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

    private static T HomeElement<T>(ProductHomeView home, string name) where T : class =>
        (home.FindName(name) as T) ?? throw new AssertFailedException($"Missing home control: {name}");

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
        private bool isVisible = true;

        public ProductHost(SceneInitializationState initializationState = SceneInitializationState.Ready) =>
            this.initializationState = initializationState;

        public int WorldCreateCalls { get; private set; }
        public int RetryCalls { get; private set; }
        public int HatApplyCalls { get; private set; }
        public int VisibilityCalls { get; private set; }
        public bool IsVisible => isVisible;
        public bool RejectVisibility { get; init; }
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
            "Wallpaper", isVisible, true, "fictional test host", initializationState == SceneInitializationState.Ready ? null : "模块不可用");

        public void SetVisibility(bool visible)
        {
            VisibilityCalls++;
            if (!RejectVisibility) isVisible = visible;
        }
        public void SetInteractionEnabled(bool isEnabled) { }
        public string CreateDiagnosticSummary() => "fictional safe diagnostics";
    }
}
