using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Client.Sync.Pets;
using Dango.Protocol;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Control = System.Windows.Controls.Control;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;
using TextBox = System.Windows.Controls.TextBox;

namespace Dango.Desktop.AppShell.ControlCenter;

public enum AccountPanelPage { Login, Register, Mail, CompleteRegistration, PasswordReset, Account, ConfigurationChoice }
public enum AccountPanelAction
{
    Login, Logout, StartRegistration, RefreshRegistration, ResendRegistration, CompleteRegistration,
    RequestPasswordReset, AdoptLocal, AdoptCloud, RefreshConfigurations, OpenLogin, OpenRegister,
    OpenPasswordReset, OpenConfiguration, Close
}

/// <summary>Presentation state contains no passwords, invitation proofs, or session credentials.</summary>
public sealed record AccountPanelState(AccountPanelPage Page, string? Message = null, bool Busy = false,
    bool CanCompleteRegistration = false, PetConfigurationReview? Review = null);

/// <summary>Centered account panel content. The host owns its overlay, focus restoration, and all operations.</summary>
public sealed class AccountPanelView : UserControl
{
    private readonly StackPanel body = new() { Margin = new Thickness(18) };
    private readonly TextBox email = Input("AccountEmail", "邮箱", 254);
    private readonly TextBox invitationId = Input("AccountInvitationId", "邀请 ID", 36);
    private readonly PasswordBox invitationProof = Secret("AccountInvitationProof", "邀请证明", 128);
    private readonly PasswordBox password = Secret("AccountPassword", "密码", 128);
    private readonly PasswordBox confirmPassword = Secret("AccountConfirmPassword", "再次输入密码", 128);
    private readonly CheckBox rememberLogin = new() { Name = "AccountRememberLogin", Content = "记住登录", Margin = new Thickness(0, 4, 0, 12) };
    private readonly Button primary = new() { Name = "AccountPrimary", MinHeight = 40, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock message = Text(string.Empty);
    private readonly List<string> transientNames = [];
    private AccountPanelState state = new(AccountPanelPage.Login);
    private AccountPanelAction? primaryAction;
    private PetConfigurationChoice? choice;

    public AccountPanelView()
    {
        MaxWidth = 560;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei");
        FontSize = 14;
        UseLayoutRounding = true;
        SetResourceReference(ForegroundProperty, "HomeInk");
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(this, KeyboardNavigationMode.Cycle);
        NameScope.SetNameScope(this, new NameScope());
        foreach (var element in new FrameworkElement[] { email, invitationId, invitationProof, password, confirmPassword, rememberLogin, primary })
            RegisterName(element.Name, element);
        message.Name = "AccountMessage";
        RegisterName(message.Name, message);
        message.SetResourceReference(TextBlock.ForegroundProperty, "HomeInk");
        primary.SetResourceReference(Button.BackgroundProperty, "HomeSoft");
        primary.SetResourceReference(Button.BorderBrushProperty, "HomeAccent");
        primary.Click += (_, _) => { if (primary.IsEnabled && primaryAction is { } action) Request(action); };
        PreviewKeyDown += OnInputKeyDown;
        var scroll = new ScrollViewer
        {
            Name = "AccountScroll", Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false
        };
        RegisterName(scroll.Name, scroll);
        var surface = new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(253, 252, 249)), Child = scroll
        };
        surface.SetResourceReference(Border.BorderBrushProperty, "HomeLine");
        surface.SetResourceReference(Border.BackgroundProperty, "HomeSoft");
        Content = surface;
        Apply(state);
    }

    public event Action<AccountPanelAction>? Requested;
    public string Email => email.Text.Trim();
    public string Password => password.Password;
    public string ConfirmPassword => confirmPassword.Password;
    public string InvitationId => invitationId.Text.Trim();
    public string InvitationProof => invitationProof.Password;
    public bool RememberLogin => rememberLogin.IsChecked == true;

    public void ClearSecrets()
    {
        password.Clear();
        confirmPassword.Clear();
        invitationProof.Clear();
    }

    public void Apply(AccountPanelState next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (!Enum.IsDefined(next.Page)) throw new ArgumentOutOfRangeException(nameof(next));
        var pageChanged = next.Page != state.Page;
        if (pageChanged || !ReferenceEquals(next.Review, state.Review)) choice = null;
        state = next;
        body.Children.Clear();
        foreach (var name in transientNames) UnregisterName(name);
        transientNames.Clear();
        primaryAction = null;
        primary.IsEnabled = !state.Busy;
        foreach (var field in new Control[] { email, invitationId, invitationProof, password, confirmPassword, rememberLogin })
            field.IsEnabled = !state.Busy;
        AddHeader();
        message.Text = state.Message ?? (state.Busy ? "正在处理，请稍候…" : string.Empty);
        message.Visibility = string.IsNullOrEmpty(message.Text) ? Visibility.Collapsed : Visibility.Visible;
        body.Children.Add(message);
        switch (state.Page)
        {
            case AccountPanelPage.Login:
                AddField("邮箱", email); AddField("密码", password); body.Children.Add(rememberLogin);
                AddPrimary("登录", AccountPanelAction.Login);
                AddNavigation("受邀注册", AccountPanelAction.OpenRegister);
                AddNavigation("找回密码", AccountPanelAction.OpenPasswordReset);
                break;
            case AccountPanelPage.Register:
                AddNote("使用受邀邮箱注册。请求受理后，需要在邮件中的浏览器页面验证邮箱。");
                AddField("受邀邮箱", email); AddField("邀请 ID", invitationId); AddField("邀请证明", invitationProof);
                AddPrimary("发送验证邮件", AccountPanelAction.StartRegistration);
                AddNavigation("已有账号，去登录", AccountPanelAction.OpenLogin);
                break;
            case AccountPanelPage.Mail:
                AddNote("请打开邮件中的验证链接。发信请求受理、邮件投递和邮箱验证是不同状态；验证后刷新继续。");
                AddPrimary("刷新验证状态", AccountPanelAction.RefreshRegistration);
                AddNavigation("重新发送验证邮件", AccountPanelAction.ResendRegistration);
                break;
            case AccountPanelPage.CompleteRegistration:
                AddNote("设置 12–128 位密码。完成注册将沿用本机已应用的外观，随后按标准流程登录。");
                AddField("密码", password); AddField("再次输入密码", confirmPassword);
                AddPrimary("完成注册", AccountPanelAction.CompleteRegistration, state.CanCompleteRegistration);
                break;
            case AccountPanelPage.PasswordReset:
                AddNote("提交后，请在邮件的浏览器页面设置新密码。这里不会显示邮箱是否已经注册。");
                AddField("邮箱", email);
                AddPrimary("请求重置密码邮件", AccountPanelAction.RequestPasswordReset);
                AddNavigation("返回登录", AccountPanelAction.OpenLogin);
                break;
            case AccountPanelPage.Account:
                AddNote("账号已登录。在线房间需要先明确选择配置来源；退出登录会保留已经应用的本机外观。");
                AddPrimary("查看并选择配置", AccountPanelAction.OpenConfiguration);
                AddNavigation("退出登录", AccountPanelAction.Logout);
                break;
            case AccountPanelPage.ConfigurationChoice:
                AddConfigurationChoice();
                break;
        }
        AddNavigation("继续本地游玩", AccountPanelAction.Close, allowBusy: true);
        if (pageChanged) ((ScrollViewer)FindName("AccountScroll")).ScrollToTop();
    }

    private void AddHeader()
    {
        var titles = new Dictionary<AccountPanelPage, string>
        {
            [AccountPanelPage.Login] = "邮箱登录", [AccountPanelPage.Register] = "受邀注册",
            [AccountPanelPage.Mail] = "等待邮箱验证", [AccountPanelPage.CompleteRegistration] = "完成注册",
            [AccountPanelPage.PasswordReset] = "找回密码", [AccountPanelPage.Account] = "账号",
            [AccountPanelPage.ConfigurationChoice] = "选择配置来源"
        };
        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Text(titles[state.Page]); title.FontSize = 22;
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        var close = MakeButton("关闭", AccountPanelAction.Close, allowBusy: true);
        close.Margin = new Thickness(10, 0, 0, 0);
        close.Padding = new Thickness(8, 4, 8, 4);
        Grid.SetColumn(close, 1); header.Children.Add(close); body.Children.Add(header);
    }

    private void AddConfigurationChoice()
    {
        AddNote("保留账号团子的唯一身份，只选择身体、眼睛颜色与四个眼形参数。选择卡片不会覆盖数据，确认后才采用。");
        if (state.Review is null)
        {
            AddNote("尚未读取两份配置。读取不会改写本机或账号配置。");
            AddPrimary("读取两份配置", AccountPanelAction.RefreshConfigurations);
            return;
        }
        try
        {
            var local = AppearanceContract.ValidateAndClone(state.Review.Originals.Local.NormalizedConfiguration);
            var cloud = AppearanceContract.ValidateAndClone(state.Review.Originals.Cloud.Configuration);
            AddConfigurationCard("本机配置", "AccountLocalChoice", PetConfigurationChoice.Local, local);
            AddConfigurationCard("账号配置", "AccountCloudChoice", PetConfigurationChoice.Cloud, cloud);
            AddPrimary("确认采用所选配置", choice == PetConfigurationChoice.Cloud ? AccountPanelAction.AdoptCloud : AccountPanelAction.AdoptLocal,
                choice is not null);
            AddNavigation("重新读取两份配置", AccountPanelAction.RefreshConfigurations);
        }
        catch (AppearanceContractException)
        {
            AddNote("配置版本或参数不可用，请重新读取。原数据未改变。");
            AddPrimary("重新读取两份配置", AccountPanelAction.RefreshConfigurations);
        }
    }

    private void AddConfigurationCard(string title, string name, PetConfigurationChoice source, JsonElement configuration)
    {
        var details = new StackPanel();
        details.Children.Add(Text(title));
        var colors = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
        foreach (var (label, property) in new[] { ("身体", "bodyColorHex"), ("眼睛", "eyeColorHex") })
        {
            var hex = configuration.GetProperty(property).GetString()!.ToUpperInvariant();
            var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 4) };
            group.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
                BorderBrush = System.Windows.Media.Brushes.Gray, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) });
            group.Children.Add(Text($"{label} {hex}", new Thickness(5, 0, 0, 0)));
            colors.Children.Add(group);
        }
        details.Children.Add(colors);
        var properties = new[] { ("大小", "eyeSizePercent"), ("椭圆度", "eyeOvalityPercent"), ("高度", "eyeHeightControlPercent"), ("眼距", "eyeSpacingControlPercent") };
        var parameters = string.Join("  ·  ", properties.Select(p => $"{p.Item1} {configuration.GetProperty(p.Item2).GetDouble().ToString("0.#", CultureInfo.InvariantCulture)}%"));
        details.Children.Add(Text(parameters));
        var radio = new RadioButton { Name = name, GroupName = "AccountConfigurationSource", Content = details,
            IsChecked = choice == source, IsEnabled = !state.Busy, Padding = new Thickness(10), Margin = new Thickness(0, 5, 0, 5),
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(radio, title);
        radio.Checked += (_, _) =>
        {
            if (state.Busy) return;
            choice = source;
            primaryAction = source == PetConfigurationChoice.Local ? AccountPanelAction.AdoptLocal : AccountPanelAction.AdoptCloud;
            primary.IsEnabled = true;
        };
        RegisterTransient(radio);
        var card = new Border { Child = radio, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 3, 0, 3) };
        card.SetResourceReference(Border.BorderBrushProperty, "HomeLine");
        body.Children.Add(card);
    }

    private void AddField(string label, Control field)
    {
        body.Children.Add(Text(label, new Thickness(0, 12, 0, 5)));
        body.Children.Add(field);
    }

    private void AddNote(string value)
    {
        var note = Text(value, new Thickness(0, 0, 0, 10));
        note.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted");
        body.Children.Add(note);
    }

    private void AddPrimary(string label, AccountPanelAction action, bool enabled = true)
    {
        primary.Content = Text(label);
        AutomationProperties.SetName(primary, label);
        primaryAction = action;
        primary.IsEnabled = !state.Busy && enabled;
        body.Children.Add(primary);
    }

    private void AddNavigation(string label, AccountPanelAction action, bool allowBusy = false)
    {
        var button = MakeButton(label, action, allowBusy);
        button.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(button);
    }

    private Button MakeButton(string label, AccountPanelAction action, bool allowBusy)
    {
        var button = new Button { Content = Text(label), MinHeight = 36, IsEnabled = !state.Busy || allowBusy,
            Padding = new Thickness(10, 6, 10, 6) };
        AutomationProperties.SetName(button, label);
        button.SetResourceReference(Button.BorderBrushProperty, "HomeLine");
        button.Click += (_, _) => Request(action);
        return button;
    }

    private void Request(AccountPanelAction action)
    {
        if (state.Busy && action != AccountPanelAction.Close) return;
        if (action == AccountPanelAction.CompleteRegistration && !state.CanCompleteRegistration) return;
        if (action is AccountPanelAction.AdoptLocal or AccountPanelAction.AdoptCloud && choice is null) return;
        Requested?.Invoke(action);
    }

    private void OnInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.IsRepeat || Keyboard.Modifiers != ModifierKeys.None || !primary.IsEnabled) return;
        if (e.OriginalSource is not TextBox and not PasswordBox) return;
        if (primaryAction is { } action) { e.Handled = true; Request(action); }
    }

    private void RegisterTransient(FrameworkElement element)
    {
        RegisterName(element.Name, element);
        transientNames.Add(element.Name);
    }

    private static TextBlock Text(string value, Thickness? margin = null) => new()
    {
        Text = value, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0)
    };

    private static TextBox Input(string name, string label, int maxLength)
    {
        var control = new TextBox { Name = name, MaxLength = maxLength, MinHeight = 36, Padding = new Thickness(8, 6, 8, 6),
            VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(control, label);
        control.SetResourceReference(TextBox.ForegroundProperty, "HomeInk");
        control.SetResourceReference(TextBox.BackgroundProperty, "HomeSoft");
        control.SetResourceReference(TextBox.BorderBrushProperty, "HomeLine");
        return control;
    }

    private static PasswordBox Secret(string name, string label, int maxLength)
    {
        var control = new PasswordBox { Name = name, MaxLength = maxLength, MinHeight = 36, Padding = new Thickness(8, 6, 8, 6),
            VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(control, label);
        control.SetResourceReference(PasswordBox.ForegroundProperty, "HomeInk");
        control.SetResourceReference(PasswordBox.BackgroundProperty, "HomeSoft");
        control.SetResourceReference(PasswordBox.BorderBrushProperty, "HomeLine");
        return control;
    }
}
