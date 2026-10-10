using System.IO;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Pets;
using Dango.Client.Sync.Rooms;
using Dango.Protocol;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>One UI owner; obsolete operations cannot publish identity, configuration or UI state.</summary>
internal sealed partial class DesktopAccountController : IDisposable
{
    private readonly AccountPanelView panel;
    private readonly Uri? origin;
    private readonly IAccountSecretStore secrets;
    private readonly Func<IRemotePetConfigurationClient, PetConfigurationChoiceCoordinator> createChoice;
    private readonly Func<CancellationToken, Task<LocalPetConfigurationSnapshot>> captureLocal;
    private readonly Func<Uri, IAccountSecretStore, FormalAccountClient> createClient;
    private readonly Action close;
    private readonly Action<bool, bool> changed;
    private FormalAccountClient? client;
    private AccountIdentity? identity;
    private IRoomManagementClient? rooms;
    private IRoomAccessClient? roomAccess;
    private RegistrationAttempt? registration;
    private PetConfigurationChoiceCoordinator? choice;
    private PetConfigurationReview? review;
    private CancellationTokenSource pending = new();
    private readonly SemaphoreSlim operations = new(1, 1);
    private long generation;
    private bool busy, opened, disposed, restored, configurationReady;
    private bool registrationRejected, rememberedCleanupNeeded;
    private AccountPanelPage page = AccountPanelPage.Login;

    internal DesktopAccountController(AccountPanelView panel, Uri? origin, IAccountSecretStore secrets,
        Func<IRemotePetConfigurationClient, PetConfigurationChoiceCoordinator> createChoice,
        Func<CancellationToken, Task<LocalPetConfigurationSnapshot>> captureLocal,
        Action close, Action<bool, bool> changed,
        Func<Uri, IAccountSecretStore, FormalAccountClient>? createClient = null,
        Func<IRemotePetConfigurationClient, PetConfigurationSnapshot, PetConfigurationLiveSyncCoordinator>? createSync = null)
    {
        this.panel = panel; this.origin = origin; this.secrets = secrets; this.createChoice = createChoice;
        this.captureLocal = captureLocal; this.close = close; this.changed = changed;
        this.createClient = createClient ?? ((address, store) => new(address, store));
        this.createSync = createSync;
        configurationTimer.Tick += OnConfigurationTick;
        panel.Requested += OnRequested;
    }

    internal bool IsLoggedIn => identity is not null;
    internal bool ConfigurationReady => configurationReady;
    internal IRoomManagementClient? RoomClient => identity is null || profile?.Nickname is null ? null : rooms;
    internal IRoomAccessClient? RoomAccessClient => identity is null || profile?.Nickname is null ? null : roomAccess;
    internal event Action? AccountAttentionRequested;

    internal void RejectRoomAuthentication(IRoomManagementClient rejected)
    {
        if (disposed || !ReferenceEquals(rejected, rooms) || client is null) return;
        ClearAuthentication(client);
        Show(AccountPanelPage.Login, "会话已失效，请重新登录。本机外观保留。");
    }

    internal void RejectRoomAuthentication(IRoomAccessClient rejected)
    {
        if (disposed || !ReferenceEquals(rejected, roomAccess) || client is null) return;
        ClearAuthentication(client);
        Show(AccountPanelPage.Login, "会话已失效，请重新登录。本机外观保留。");
    }

    internal void Open()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        opened = true;
        Show(identity is null ? AccountPanelPage.Login : profile?.Nickname is null ? AccountPanelPage.Nickname
            : review is not null ? AccountPanelPage.ConfigurationChoice : AccountPanelPage.Account);
        if (origin is null)
        {
            Show(AccountPanelPage.Login, "尚未配置可信服务器地址。账号服务暂不可用，本地单人仍可游玩。", unavailable: true);
            return;
        }
        if (rememberedCleanupNeeded)
        {
            Show(AccountPanelPage.Login, "本次内存会话已关闭，但记住登录材料尚未清除。请重试清除；本次不会自动恢复它。");
            return;
        }
        if (!restored) _ = RunAsync(RestoreAccountAsync);
        else if (identity is not null && (profile?.Nickname is null || !configurationReady))
            _ = RunAsync(LoadProfileAndCompareAsync);
    }

    private async void OnRequested(AccountPanelAction action)
    {
        if (disposed) return;
        if (action == AccountPanelAction.Close) { Close(); return; }
        if (busy && action != AccountPanelAction.Logout || !opened) return;
        if (action == AccountPanelAction.OpenAccount && identity is not null)
        { Show(profile?.Nickname is null ? AccountPanelPage.Nickname : AccountPanelPage.Account); return; }
        if (action is AccountPanelAction.OpenLogin or AccountPanelAction.OpenRegister or AccountPanelAction.OpenPasswordReset)
        {
            Show(action == AccountPanelAction.OpenLogin ? AccountPanelPage.Login : action == AccountPanelAction.OpenRegister
                ? AccountPanelPage.Register : AccountPanelPage.PasswordReset);
            return;
        }
        if (origin is null) { Show(page, "账号服务尚未配置，本地入口仍可使用。", unavailable: true); return; }
        // Capture only this submission. UI secrets are cleared before asynchronous I/O.
        var email = panel.Email.Trim();
        var password = panel.Password;
        var confirmation = panel.ConfirmPassword;
        var invitationId = panel.InvitationId;
        var invitationProof = panel.InvitationProof.Trim();
        var remember = panel.RememberLogin;
        var submittedNickname = panel.Nickname;
        panel.ClearSecrets();
        if (action == AccountPanelAction.Logout)
        {
            var old = client;
            var cleanupRetry = rememberedCleanupNeeded;
            // Logout can itself be cancelled while queued behind an older request.
            rememberedCleanupNeeded = true;
            StopConfigurationSync(); profile = null; profileClient = null;
            Invalidate(); client = null; identity = null; rooms = null; roomAccess = null; choice = null; review = null;
            configurationReady = false; restored = true; changed(false, false);
            try
            {
                await RunAsync(async (active, ct) =>
                {
                    await (old ?? active).LogoutAsync(ct);
                    rememberedCleanupNeeded = false;
                    ct.ThrowIfCancellationRequested();
                    Show(AccountPanelPage.Login, cleanupRetry
                        ? "本机记住登录材料已清除。旧服务器会话撤销状态请在恢复连接后核对；本机外观保留。"
                        : "已退出登录。本机外观保留。");
                }, logout: true);
            }
            finally { old?.Dispose(); }
            return;
        }
        await RunAsync(async (active, ct) =>
        {
            switch (action)
            {
                case AccountPanelAction.Login:
                    if (password.Length is 0 or > 128) throw new InvalidDataException("password_invalid");
                    var account = await active.LoginAsync(email, password, remember, ct);
                    ct.ThrowIfCancellationRequested();
                    AcceptIdentity(active, account);
                    await LoadProfileAndCompareAsync(active, ct);
                    break;
                case AccountPanelAction.OpenNickname:
                    await RefreshProfileAsync(active, ct);
                    Show(AccountPanelPage.Nickname);
                    break;
                case AccountPanelAction.SaveNickname:
                    await SaveNicknameAsync(active, submittedNickname, ct);
                    break;
                case AccountPanelAction.RefreshProfile:
                    await LoadProfileAndCompareAsync(active, ct);
                    break;
                case AccountPanelAction.StartRegistration:
                    if (registration is null)
                    {
                        if (!Guid.TryParse(invitationId, out var id) || id == Guid.Empty)
                            throw new InvalidDataException("invitation_invalid");
                        registration = RegistrationAttempt.Create(email, id, invitationProof);
                    }
                    await active.StartRegistrationAsync(registration, ct);
                    ct.ThrowIfCancellationRequested();
                    await RefreshRegistration(active, ct);
                    break;
                case AccountPanelAction.UseNewInvitation:
                    if (!registrationRejected) throw new InvalidDataException("registration_unknown");
                    await active.ForgetRegistrationAsync(ct);
                    ct.ThrowIfCancellationRequested();
                    registration = null; registrationRejected = false;
                    Show(AccountPanelPage.Register, "已清除本机旧继续信息，请填写新邀请。这不会取消服务器上已经存在的请求。");
                    break;
                case AccountPanelAction.RefreshRegistration:
                    if (registration?.RegistrationId is null)
                    {
                        if (registration is null) throw new InvalidDataException("registration_missing");
                        await active.StartRegistrationAsync(registration, ct);
                    }
                    await RefreshRegistration(active, ct);
                    break;
                case AccountPanelAction.ResendRegistration:
                    if (registration is null) throw new InvalidDataException("registration_missing");
                    await active.ResendRegistrationAsync(registration, ct);
                    ct.ThrowIfCancellationRequested();
                    await RefreshRegistration(active, ct);
                    break;
                case AccountPanelAction.CompleteRegistration:
                    if (registration is null) throw new InvalidDataException("registration_missing");
                    if (password.Length is < 12 or > 128 || password != confirmation)
                        throw new InvalidDataException("password_confirmation_invalid");
                    var local = await captureLocal(ct);
                    await active.CompleteRegistrationAsync(registration, password, local.NormalizedConfiguration, ct);
                    ct.ThrowIfCancellationRequested();
                    await active.ForgetRegistrationAsync(ct);
                    ct.ThrowIfCancellationRequested();
                    registration = null;
                    Show(AccountPanelPage.Login, "注册完成。请使用邮箱与刚设置的密码登录。");
                    break;
                case AccountPanelAction.RequestPasswordReset:
                    await active.RequestPasswordResetAsync(email, ct);
                    ct.ThrowIfCancellationRequested();
                    Show(AccountPanelPage.PasswordReset, "请求已受理；若账号符合条件将收到邮件。请在邮件页面完成重置。");
                    break;
                case AccountPanelAction.OpenConfiguration:
                    if (configurationReady)
                    {
                        await PumpConfigurationAsync();
                        ct.ThrowIfCancellationRequested();
                        Show(AccountPanelPage.Account, configurationStatus ?? "捏脸配置已确认。");
                        break;
                    }
                    await BeginChoice(active, ct);
                    break;
                case AccountPanelAction.RefreshConfigurations:
                    await BeginChoice(active, ct);
                    break;
                case AccountPanelAction.AdoptLocal:
                case AccountPanelAction.AdoptCloud:
                    if (choice is null || review is null) throw new InvalidDataException("configuration_review_required");
                    var result = await choice.ChooseAsync(review, action == AccountPanelAction.AdoptLocal
                        ? PetConfigurationChoice.Local : PetConfigurationChoice.Cloud, ct);
                    ct.ThrowIfCancellationRequested();
                    review = null;
                    if (result.Outcome is PetChoiceOutcome.LocalApplied or PetChoiceOutcome.CloudUpdated or PetChoiceOutcome.TargetSatisfied)
                    {
                        if (result.ReceiptSaved && result.ObservedCloud is not null)
                            await StartConfigurationSyncAsync(active, result.ObservedCloud, ct);
                        configurationReady = result.ReceiptSaved;
                        changed(true, configurationReady);
                        Show(AccountPanelPage.Account, result.ReceiptSaved ? "已采用所选外观配置。账号团子身份保持不变。"
                            : "配置已变更，但完成记录保存失败。请重新读取两份配置后确认。");
                    }
                    else
                    {
                        configurationReady = false; changed(true, false);
                        if (result.FailureCode == "authentication_required")
                        {
                            ClearAuthentication(active);
                            Show(AccountPanelPage.Login, "会话已失效，请重新登录。本机外观保留。");
                            break;
                        }
                        Show(AccountPanelPage.ConfigurationChoice, result.Outcome == PetChoiceOutcome.Conflict
                            ? "配置发生变化，本次未覆盖。请重新读取，再选择来源。"
                            : result.Outcome == PetChoiceOutcome.OutcomeUnknown
                                ? "提交结果未知。请先重新读取账号与本机配置，再明确选择。"
                                : "配置未能完成。原数据保留，请处理保存或连接问题后重新读取。");
                    }
                    break;
            }
        });
    }

    private void AcceptIdentity(FormalAccountClient active, AccountIdentity account)
    {
        StopConfigurationSync();
        rememberedCleanupNeeded = false;
        identity = account; configurationReady = false;
        profile = null; profileClient = active.CreateProfileClient(account);
        rooms = active.CreateRoomManagementClient(account);
        roomAccess = active.CreateRoomAccessClient(account);
        choice = createChoice(active.CreatePetClient(account));
        changed(true, false);
        Show(AccountPanelPage.Nickname, "已登录，正在读取账号昵称与外观。");
    }

    private async Task BeginChoice(FormalAccountClient active, CancellationToken ct)
    {
        if (identity is null || active.CurrentIdentity != identity || choice is null)
            throw new InvalidDataException("authentication_required");
        if (profile?.Nickname is null) { Show(AccountPanelPage.Nickname); return; }
        StopConfigurationSync(); configurationReady = false; changed(true, false);
        // Drain the previous account before capturing the new login's local baseline.
        // Successful offline saves during that wait must be compared, never overwritten by a stale baseline.
        await configurationPump;
        ct.ThrowIfCancellationRequested();
        var loadedReview = await choice.BeginAsync(ct);
        ct.ThrowIfCancellationRequested();
        // Login/restore can finish while local-only editing is still available. Re-read once, never accept a stale baseline.
        if (!loadedReview.Originals.Local.SameOriginal(await captureLocal(ct)))
        {
            loadedReview = await choice.BeginAsync(ct);
            if (!loadedReview.Originals.Local.SameOriginal(await captureLocal(ct)))
                throw new PetConfigurationChoiceException("local_configuration_changed");
        }
        ct.ThrowIfCancellationRequested();
        if (AppearanceContract.Equivalent(loadedReview.Originals.Local.NormalizedConfiguration,
            loadedReview.Originals.Cloud.Configuration))
        {
            review = null;
            await StartConfigurationSyncAsync(active, loadedReview.Originals.Cloud, ct);
            configurationReady = true;
            changed(true, true);
            Show(AccountPanelPage.Account, "本机与账号外观一致。");
            return;
        }
        review = loadedReview;
        Show(AccountPanelPage.ConfigurationChoice);
        if (!opened) AccountAttentionRequested?.Invoke();
    }

    private async Task RefreshRegistration(FormalAccountClient active, CancellationToken ct)
    {
        if (registration is null) throw new InvalidDataException("registration_missing");
        var status = await active.ReadRegistrationStatusAsync(registration, ct);
        ct.ThrowIfCancellationRequested();
        registrationRejected = false;
        if (status.State == AccountRegistrationState.Completed)
        {
            await active.ForgetRegistrationAsync(ct);
            ct.ThrowIfCancellationRequested();
            registration = null;
            Show(AccountPanelPage.Login, "注册已完成。请使用首次提交的密码登录；若不记得，请找回密码。");
        }
        else if (status.State == AccountRegistrationState.Verified)
            Show(AccountPanelPage.CompleteRegistration, "邮箱已验证。设置密码并采用当前已保存的本机外观。");
        else Show(AccountPanelPage.Mail, status.EmailStatus switch
        {
            "smtp_accepted" => "邮件已被 SMTP 接受，邮箱尚未验证。请在邮件打开的页面确认后刷新。",
            "smtp_not_configured" => "服务器尚未配置发信，本次没有发送。请等待服务配置后重试。",
            "send_failed" or "smtp_send_failed" or "smtp_permanent" or "smtp_attempt_limit" => "邮件发送失败。请待服务恢复后重发；重发频率由服务器检查。",
            "smtp_transient" => "邮件暂时发送失败，服务器正在安排有限重试。请稍后刷新状态。",
            "cancelled" => "验证邮件已取消。请刷新注册状态，不要把它当作已经送达。",
            var code when code.StartsWith("smtp_", StringComparison.Ordinal) => "邮件发送失败。请待服务恢复后刷新或重发。",
            _ => "验证邮件等待发送，邮箱尚未验证。请稍后刷新；重发频率由服务器检查。"
        });
    }

    private async Task RunAsync(Func<FormalAccountClient, CancellationToken, Task> operation, bool logout = false, bool allowClosed = false)
    {
        if (busy || !opened && !allowClosed || disposed || origin is null) return;
        var current = client ??= createClient(origin, secrets);
        var epoch = generation;
        var ct = pending.Token;
        busy = true; Show(page);
        var entered = false;
        try
        {
            // A replacement client cannot touch the same secure-store slots until the old operation has drained.
            await operations.WaitAsync(ct);
            entered = true;
            ct.ThrowIfCancellationRequested();
            if (epoch != generation || !opened && !allowClosed || disposed) return;
            await operation(current, ct);
        }
        catch (OperationCanceledException) { /* The next open rereads facts; cancellation does not prove a write failed. */ }
        catch (ObjectDisposedException) when (epoch != generation || disposed) { }
        catch (Exception ex) when (ex is AccountClientException or AccountNicknameException or PetConfigurationChoiceException or IOException or UnauthorizedAccessException)
        {
            // Persistent cleanup is a lifecycle fact, even after the panel that requested it closes.
            if (logout && ex is AccountClientException { LocalSecretCleared: { } cleared }) rememberedCleanupNeeded = !cleared;
            if (epoch == generation && !disposed && (opened || allowClosed))
            {
                review = null;
                if (logout && ex is AccountClientException { Failure: AccountFailure.Storage })
                {
                    rememberedCleanupNeeded = true;
                    Show(AccountPanelPage.Login, LogoutFailureMessage((AccountClientException)ex));
                }
                else if (!logout && registration is not null && ex is AccountClientException
                { Code: "invitation_invalid" or "registration_expired" or "registration_proof_invalid" or "registration_result_expired" or "registration_conflict" })
                {
                    registrationRejected = true;
                    Show(AccountPanelPage.Register, "旧注册请求已过期或被明确拒绝。可确认清除本机继续信息并改用新邀请；这不会取消服务器已有请求。");
                }
                else if (!logout && (ex is AccountClientException { Failure: AccountFailure.Authentication }
                    or PetConfigurationHttpException { Failure: PetHttpFailure.Authentication }))
                {
                    ClearAuthentication(current);
                    Show(AccountPanelPage.Login, Message(ex));
                }
                else Show(logout ? AccountPanelPage.Login
                    : ex is AccountClientException { Failure: AccountFailure.Unavailable } && registration is not null
                        ? AccountPanelPage.Mail : page,
                    logout ? LogoutFailureMessage(ex as AccountClientException) : Message(ex));
            }
        }
        finally
        {
            if (entered) operations.Release();
            if (epoch == generation && !disposed)
            {
                busy = false;
                if (opened) panel.Apply(State());
            }
        }
    }

    private string? lastMessage;
    private void ClearAuthentication(FormalAccountClient active)
    {
        Invalidate(); StopConfigurationSync(); profile = null; profileClient = null;
        active.Dispose(); client = null; identity = null; rooms = null; roomAccess = null; choice = null; review = null;
        restored = false; configurationReady = false; changed(false, false);
    }
    private void Show(AccountPanelPage target, string? message = null, bool unavailable = false)
    {
        page = target; lastMessage = message;
        if (opened && !disposed) panel.Apply(State(busy || unavailable));
    }
    private AccountPanelState State(bool isBusy = false) => new(page, lastMessage, isBusy,
        page == AccountPanelPage.CompleteRegistration, review, registrationRejected, rememberedCleanupNeeded,
        profile?.Nickname, identity is not null && profile?.Nickname is null, configurationStatus, profile is not null);

    private static string Message(Exception error) => error switch
    {
        AccountClientException { Failure: AccountFailure.Authentication } => "邮箱或密码未通过，或会话已失效。请重新登录。",
        AccountClientException { Failure: AccountFailure.Storage } => "安全存储失败，请重试。不能以保存失败作为完成或记住登录的证据。",
        AccountClientException { Failure: AccountFailure.Unavailable } => "服务器不可用或请求结果未知。注册请刷新原请求；本地单人仍可游玩。",
        AccountNicknameException or AccountClientException { Code: "nickname_invalid" } => "昵称需要 1–24 个字或表情，不可仅有空格、换行或控制字符。",
        PetConfigurationChoiceException { Code: "local_unsaved_changes" } => "本机外观还有草稿，请先应用或取消草稿再比较。",
        PetConfigurationChoiceException => "配置读取或保存失败。原配置保留，请检查连接和本机存档后重新读取。",
        InvalidDataException { Message: "password_confirmation_invalid" } => "密码需要 12–128 个字符，且两次输入一致。",
        InvalidDataException { Message: "password_invalid" } => "请填写有效密码。",
        InvalidDataException { Message: "invitation_invalid" } => "请填写有效邀请 ID 和邀请证明。",
        AccountClientException { Failure: AccountFailure.Rejected } => "请求未获通过，请检查输入或稍后重试。注册保留原请求，不自动重新创建。",
        _ => "操作未能完成，请检查安全存储或本机文件后重试。"
    };

    private static string LogoutFailureMessage(AccountClientException? error)
    {
        var local = error?.LocalSecretCleared switch
        {
            false => "本次内存会话已关闭，但记住登录材料尚未清除。请重试清除，本次不会自动恢复它。",
            true => "本机已退出，记住登录材料已清除。本机外观保留。",
            _ => "本次内存会话已关闭，本机记住材料的清理尚未确认。请重试清除，本次不会自动恢复它。"
        };
        return local + (error?.ServerRevocationConfirmed == true
            ? "服务器会话已撤销。" : "服务端撤销未确认，请恢复连接后核对。");
    }

    private void Invalidate()
    {
        generation++; pending.Cancel(); pending.Dispose(); pending = new(); busy = false;
    }

    internal void Close()
    {
        if (disposed) return;
        opened = false; Invalidate(); panel.ClearSecrets(); review = null;
        if (identity is null) { client?.Dispose(); client = null; restored = false; }
        close();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; opened = false; generation++; pending.Cancel(); pending.Dispose();
        StopConfigurationSync(); configurationTimer.Tick -= OnConfigurationTick;
        panel.Requested -= OnRequested; panel.ClearSecrets(); client?.Dispose(); client = null;
        identity = null; rooms = null; roomAccess = null;
    }
}
