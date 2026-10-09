using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using Dango.Client.Sync.Pets;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Protocol;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class AccountPanelViewTests
{
    [STATestMethod]
    public void FieldsStayOutOfPresentationStateAndCanBeExplicitlyCleared()
    {
        var view = new AccountPanelView();
        Element<TextBox>(view, "AccountEmail").Text = " invited@fictional.invalid ";
        Element<PasswordBox>(view, "AccountPassword").Password = "Fictional-Only-123";
        Element<PasswordBox>(view, "AccountConfirmPassword").Password = "Fictional-Only-123";
        Element<PasswordBox>(view, "AccountInvitationProof").Password = "fictional-proof";
        Element<TextBox>(view, "AccountInvitationId").Text = " fictitious-invitation-id ";
        Element<CheckBox>(view, "AccountRememberLogin").IsChecked = true;
        Assert.AreEqual("invited@fictional.invalid", view.Email);
        Assert.AreEqual("Fictional-Only-123", view.Password);
        Assert.AreEqual(view.Password, view.ConfirmPassword);
        Assert.AreEqual("fictional-proof", view.InvitationProof);
        Assert.AreEqual("fictitious-invitation-id", view.InvitationId);
        Assert.IsTrue(view.RememberLogin);
        Assert.AreEqual(128, Element<PasswordBox>(view, "AccountPassword").MaxLength);
        Assert.IsFalse(typeof(AccountPanelState).GetProperties().Any(p => p.Name is "Password" or "InvitationProof" or "ConfirmPassword"));

        view.Apply(new(AccountPanelPage.Login, "请检查输入"));
        Assert.AreEqual("Fictional-Only-123", view.Password, "A validation response must preserve the draft until the host clears it.");
        view.ClearSecrets();
        Assert.AreEqual(string.Empty, view.Password);
        Assert.AreEqual(string.Empty, view.ConfirmPassword);
        Assert.AreEqual(string.Empty, view.InvitationProof);
        Assert.AreEqual("invited@fictional.invalid", view.Email);
    }

    [STATestMethod]
    public void BusyOperationsAndUnverifiedRegistrationCannotClaimCompletionButLocalCloseRemainsAvailable()
    {
        var view = new AccountPanelView();
        var actions = new List<AccountPanelAction>();
        view.Requested += actions.Add;
        view.Apply(new(AccountPanelPage.Mail));
        Assert.AreEqual("刷新验证状态", AutomationProperties.GetName(Element<Button>(view, "AccountPrimary")));
        Click(view, "AccountPrimary");
        CollectionAssert.AreEqual(new[] { AccountPanelAction.RefreshRegistration }, actions);
        Assert.IsFalse(Buttons(view).Any(b => AutomationProperties.GetName(b) == "完成注册"));

        actions.Clear();
        view.Apply(new(AccountPanelPage.CompleteRegistration, CanCompleteRegistration: false));
        Assert.IsFalse(Element<Button>(view, "AccountPrimary").IsEnabled);
        Click(view, "AccountPrimary");
        Assert.AreEqual(0, actions.Count);
        view.Apply(new(AccountPanelPage.CompleteRegistration, CanCompleteRegistration: true));
        Click(view, "AccountPrimary");
        CollectionAssert.AreEqual(new[] { AccountPanelAction.CompleteRegistration }, actions);

        actions.Clear();
        view.Apply(new(AccountPanelPage.Login, Busy: true));
        Assert.IsFalse(Element<TextBox>(view, "AccountEmail").IsEnabled);
        Click(view, "AccountPrimary");
        Assert.AreEqual(0, actions.Count);
        var local = Buttons(view).Single(b => AutomationProperties.GetName(b) == "继续本地游玩");
        Assert.IsTrue(local.IsEnabled);
        local.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        CollectionAssert.AreEqual(new[] { AccountPanelAction.Close }, actions);
    }

    [STATestMethod]
    public void ConfigurationSelectionRequiresExplicitConfirmationAndReReadingClearsTheChoice()
    {
        var view = new AccountPanelView();
        var actions = new List<AccountPanelAction>();
        view.Requested += actions.Add;
        var review = Review();
        view.Apply(new(AccountPanelPage.ConfigurationChoice, Review: review));
        Assert.IsFalse(Element<Button>(view, "AccountPrimary").IsEnabled);
        Click(view, "AccountPrimary");
        Assert.AreEqual(0, actions.Count);
        Element<RadioButton>(view, "AccountCloudChoice").IsChecked = true;
        Assert.AreEqual(0, actions.Count, "Choosing a source must not apply it.");
        Click(view, "AccountPrimary");
        CollectionAssert.AreEqual(new[] { AccountPanelAction.AdoptCloud }, actions);

        actions.Clear();
        view.Apply(new(AccountPanelPage.ConfigurationChoice, Busy: true, Review: review));
        Assert.IsTrue(Element<RadioButton>(view, "AccountCloudChoice").IsChecked == true);
        Assert.IsFalse(Element<RadioButton>(view, "AccountCloudChoice").IsEnabled);
        Click(view, "AccountPrimary");
        Assert.AreEqual(0, actions.Count);
        view.Apply(new(AccountPanelPage.ConfigurationChoice, Review: review));
        Element<RadioButton>(view, "AccountLocalChoice").IsChecked = true;
        Click(view, "AccountPrimary");
        CollectionAssert.AreEqual(new[] { AccountPanelAction.AdoptLocal }, actions);

        view.Apply(new(AccountPanelPage.ConfigurationChoice, Review: Review()));
        Assert.IsFalse(Element<Button>(view, "AccountPrimary").IsEnabled, "A newly read review requires a new explicit choice.");
        Assert.IsFalse(Element<RadioButton>(view, "AccountLocalChoice").IsChecked == true);
        Assert.IsFalse(Element<RadioButton>(view, "AccountCloudChoice").IsChecked == true);
        var text = string.Join("\n", Descendants(view).OfType<TextBlock>().Select(t => t.Text));
        StringAssert.Contains(text, "#112233"); StringAssert.Contains(text, "#EEDDCC");
        StringAssert.Contains(text, "大小 110%"); StringAssert.Contains(text, "眼距 25%");
    }

    [STATestMethod]
    [DataRow(AccountPanelPage.Login)]
    [DataRow(AccountPanelPage.Register)]
    [DataRow(AccountPanelPage.Mail)]
    [DataRow(AccountPanelPage.CompleteRegistration)]
    [DataRow(AccountPanelPage.PasswordReset)]
    [DataRow(AccountPanelPage.Account)]
    [DataRow(AccountPanelPage.ConfigurationChoice)]
    public void AllPagesConstrainTheirContentAtNarrowWidthAndUseVerticalScroll(AccountPanelPage page)
    {
        var view = new AccountPanelView();
        view.Apply(new(page, "虚构服务状态：验证未完成；本地入口仍然可用。", Review: Review()));
        var size = new Size(336, 300);
        view.Measure(size); view.Arrange(new Rect(size)); view.UpdateLayout();
        Assert.AreEqual(560, view.MaxWidth);
        Assert.AreEqual(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(view));
        Assert.AreEqual(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetControlTabNavigation(view));
        var scroll = Element<ScrollViewer>(view, "AccountScroll");
        Assert.AreEqual(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        Assert.AreEqual(0, scroll.ScrollableWidth, .1);
        foreach (var element in Descendants(view).OfType<FrameworkElement>().Where(e => e is Button or TextBox or PasswordBox or RadioButton))
        {
            if (element.ActualWidth == 0) continue;
            var bounds = element.TransformToAncestor(view).TransformBounds(new Rect(element.RenderSize));
            Assert.IsTrue(bounds.Left >= -1 && bounds.Right <= size.Width + 1, $"{element.GetType().Name} extends past the narrow panel: {bounds}.");
        }
        if (page is AccountPanelPage.Register or AccountPanelPage.ConfigurationChoice)
            Assert.IsTrue(scroll.ScrollableHeight > 0, "Long forms must scroll rather than shrink or clip their actions.");
    }

    [STATestMethod]
    public void SurfaceAndFieldsFollowInheritedPaletteChanges()
    {
        var view = new AccountPanelView();
        var parent = new Border { Child = view };
        parent.Resources["HomeSoft"] = System.Windows.Media.Brushes.Black;
        parent.Resources["HomeInk"] = System.Windows.Media.Brushes.White;
        parent.Resources["HomeLine"] = System.Windows.Media.Brushes.White;
        parent.Resources["HomeAccent"] = System.Windows.Media.Brushes.Yellow;
        parent.Measure(new Size(336, 400)); parent.Arrange(new Rect(0, 0, 336, 400)); parent.UpdateLayout();
        Assert.AreSame(System.Windows.Media.Brushes.Black, ((Border)view.Content).Background);
        Assert.AreSame(System.Windows.Media.Brushes.Black, Element<TextBox>(view, "AccountEmail").Background);
        Assert.AreSame(System.Windows.Media.Brushes.White, Element<TextBox>(view, "AccountEmail").Foreground);
        Assert.AreSame(System.Windows.Media.Brushes.Black, Element<PasswordBox>(view, "AccountPassword").Background);
        parent.Resources["HomeSoft"] = System.Windows.Media.Brushes.White;
        parent.Resources["HomeInk"] = System.Windows.Media.Brushes.Black;
        Assert.AreSame(System.Windows.Media.Brushes.White, ((Border)view.Content).Background);
        Assert.AreSame(System.Windows.Media.Brushes.Black, Element<PasswordBox>(view, "AccountPassword").Foreground);
    }

    [STATestMethod]
    [DataRow(AccountPanelPage.Account)]
    [DataRow(AccountPanelPage.ConfigurationChoice)]
    public void AccessorySectionFitsNarrowPanelAndScrollsEveryActionIntoViewWithoutChangingBodyChoice(AccountPanelPage page)
    {
        var view = new AccountPanelView(); var accessory = new AccessoryAccountPanelView();
        view.Resources["HomeSoft"] = Brushes.FloralWhite;
        view.Resources["HomeInk"] = Brushes.Black;
        view.Resources["HomeMuted"] = Brushes.DimGray;
        view.Resources["HomeLine"] = Brushes.DarkGray;
        view.Resources["HomeAccent"] = Brushes.DarkOrange;
        view.BindAccessoryAccountContent(accessory);
        var review = Review();
        view.Apply(new(page, Review: review));
        var accountActions = new List<AccountPanelAction>(); var accessoryActions = new List<AccessoryAccountAction>();
        view.Requested += accountActions.Add; accessory.Requested += accessoryActions.Add;
        if (page == AccountPanelPage.ConfigurationChoice) Element<RadioButton>(view, "AccountCloudChoice").IsChecked = true;
        accessory.Apply(new("本机穿搭已保存，账号保存结果尚未确认。请核对保存结果；身体外观的来源选择保持独立。", true,
            CanBackup: true, CanRestore: true, CanRefresh: true, CanRetry: true, CanReconcile: true));
        var size = new Size(336, 360);
        view.Measure(size); view.Arrange(new Rect(size)); view.UpdateLayout();
        var scroll = Element<ScrollViewer>(view, "AccountScroll");
        Assert.AreEqual(0, scroll.ScrollableWidth, .1);
        Assert.IsTrue(scroll.ScrollableHeight > 0, "The added section must use the existing account scroll surface.");
        foreach (var button in Descendants(accessory).OfType<Button>())
        {
            var bounds = button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize));
            Assert.IsTrue(bounds.Left >= -1 && bounds.Right <= size.Width + 1, $"Accessory action exceeds narrow width: {bounds}.");
            var inBody = button.TransformToAncestor((FrameworkElement)scroll.Content).TransformBounds(new Rect(button.RenderSize));
            scroll.ScrollToVerticalOffset(inBody.Top); view.UpdateLayout();
            var visible = button.TransformToAncestor(scroll).TransformBounds(new Rect(button.RenderSize));
            Assert.IsTrue(visible.Top >= -1 && visible.Bottom <= scroll.ActualHeight + 1, $"Cannot reach action through scrolling: {visible}.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        Assert.AreEqual(5, accessoryActions.Count);
        Assert.AreEqual(0, accountActions.Count, "Accessory actions must never confirm the appearance source choice.");
        accessory.Apply(new("账号保存结果尚未确认，请先核对。", true, CanReconcile: true));
        Assert.IsFalse(((Button)accessory.FindName("AccessoryBackupLocal")).IsEnabled);
        Assert.IsFalse(((Button)accessory.FindName("AccessoryRestoreAccount")).IsEnabled);
        if (page == AccountPanelPage.ConfigurationChoice)
        {
            Assert.IsTrue(Element<RadioButton>(view, "AccountCloudChoice").IsChecked == true);
            Assert.IsTrue(Element<Button>(view, "AccountPrimary").IsEnabled);
        }
        view.Measure(size); view.Arrange(new Rect(size)); view.UpdateLayout();
        var output = Path.Combine(AppContext.BaseDirectory, "artifacts", "accessory-c2-layout");
        Directory.CreateDirectory(output);
        foreach (var (suffix, offset) in new[] { ("top", 0.0), ("bottom", scroll.ScrollableHeight) })
        {
            scroll.ScrollToVerticalOffset(offset); view.UpdateLayout();
            var bitmap = new RenderTargetBitmap(336, 360, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"{page}-{suffix}.png")); encoder.Save(file);
        }
    }

    private static PetConfigurationReview Review()
    {
        var local = new Local(); var remote = new Remote();
        return new PetConfigurationChoiceCoordinator(remote, local, new Journal()).BeginAsync().GetAwaiter().GetResult();
    }
    private static JsonElement Configuration(string color) => JsonSerializer.SerializeToElement(new
    {
        appearanceSchemaVersion = 3,
        bodyColorHex = color,
        eyeColorHex = "#334455",
        eyeSizePercent = 110,
        eyeOvalityPercent = 10,
        eyeHeightControlPercent = 50,
        eyeSpacingControlPercent = 25
    });
    private static T Element<T>(AccountPanelView view, string name) where T : class =>
        view.FindName(name) as T ?? throw new AssertFailedException($"Missing {name}.");
    private static void Click(AccountPanelView view, string name) => Element<Button>(view, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<Button> Buttons(DependencyObject root) => Descendants(root).OfType<Button>();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
    private sealed class Local : ILocalPetConfigurationRepository
    {
        public Task<LocalPetConfigurationSnapshot> CaptureAsync(CancellationToken ct) => Task.FromResult(new LocalPetConfigurationSnapshot(false, ReadOnlyMemory<byte>.Empty, Configuration("#112233")));
        public Task ApplyAsync(LocalPetConfigurationSnapshot expected, JsonElement configuration, CancellationToken ct) => throw new AssertFailedException("The view must not write local data.");
    }
    private sealed class Remote : IRemotePetConfigurationClient
    {
        public Uri Origin => new("https://fictional.invalid");
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid PetId { get; } = Guid.NewGuid();
        public Task<PetConfigurationSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(new PetConfigurationSnapshot(UserId, PetId, 1, Configuration("#EEDDCC")));
        public Task<PetConfigurationSnapshot> UpdateAsync(long expectedVersion, JsonElement configuration, CancellationToken ct) => throw new AssertFailedException("The view must not write remote data.");
    }
    private sealed class Journal : IPetConfigurationJournal
    {
        public Task<Guid> SaveOriginalsAsync(PetConfigurationOriginals originals, CancellationToken ct) => throw new AssertFailedException("The view must not write archives.");
        public Task<PetConfigurationOriginals> LoadOriginalsAsync(Guid id, CancellationToken ct) => throw new AssertFailedException("The view must not read archives.");
        public Task WriteReceiptAsync(Guid id, PetConfigurationReceipt receipt, CancellationToken ct) => throw new AssertFailedException("The view must not write receipts.");
    }
}
