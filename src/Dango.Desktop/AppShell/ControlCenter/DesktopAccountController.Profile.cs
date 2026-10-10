using Dango.Client.Sync.Accounts;
using Dango.Protocol;

namespace Dango.Desktop.AppShell.ControlCenter;

internal sealed partial class DesktopAccountController
{
    private IAccountProfileClient? profileClient;
    private AccountProfileSnapshot? profile;
    internal string? Nickname => profile?.Nickname;

    // Restore the homepage identity without opening an empty login panel at startup.
    internal void InitializeRememberedSession()
    {
        if (disposed || origin is null || restored || rememberedCleanupNeeded) return;
        _ = RunAsync(RestoreAccountAsync, allowClosed: true);
    }

    private async Task RestoreAccountAsync(FormalAccountClient active, CancellationToken ct)
    {
        if (rememberedCleanupNeeded)
        {
            Show(AccountPanelPage.Login, "本次内存会话已关闭，但记住登录材料尚未清除。请重试清除；本次不会自动恢复它。");
            return;
        }
        var account = await active.RestoreSessionAsync(ct);
        ct.ThrowIfCancellationRequested();
        registration = await active.RestoreRegistrationAsync(ct);
        ct.ThrowIfCancellationRequested();
        restored = true;
        if (account is not null)
        {
            AcceptIdentity(active, account);
            await LoadProfileAndCompareAsync(active, ct);
        }
        else if (registration?.RegistrationId is not null) await RefreshRegistration(active, ct);
        else Show(AccountPanelPage.Login, registration is null ? null : "保留了上次注册请求。进入受邀注册后将使用原请求继续。");
    }

    private async Task RefreshProfileAsync(FormalAccountClient active, CancellationToken ct)
    {
        if (identity is null || active.CurrentIdentity != identity || profileClient is null)
            throw new AccountClientException(AccountFailure.Authentication, "authentication_required");
        var latest = await profileClient.ReadAsync(ct);
        ct.ThrowIfCancellationRequested();
        profile = latest;
        changed(true, configurationReady);
    }

    private async Task LoadProfileAndCompareAsync(FormalAccountClient active, CancellationToken ct)
    {
        await RefreshProfileAsync(active, ct);
        if (profile?.Nickname is null)
        {
            Show(AccountPanelPage.Nickname);
            if (!opened) AccountAttentionRequested?.Invoke();
            return;
        }
        await BeginChoice(active, ct);
    }

    private async Task SaveNicknameAsync(FormalAccountClient active, string input, CancellationToken ct)
    {
        if (identity is null || active.CurrentIdentity != identity || profileClient is null || profile is null)
            throw new AccountClientException(AccountFailure.Authentication, "authentication_required");
        var requested = AccountNickname.Normalize(input);
        AccountProfileSnapshot updated;
        try { updated = await profileClient.UpdateAsync(profile.ProfileVersion, requested, ct); }
        catch (AccountClientException fault) when (fault.Failure == AccountFailure.Unavailable || fault.Code == "account_profile_conflict")
        {
            // A lost response may have committed. Reading also resolves another device's profile change.
            var observed = await profileClient.ReadAsync(ct);
            ct.ThrowIfCancellationRequested();
            profile = observed;
            changed(true, configurationReady);
            if (observed.Nickname != requested)
            {
                Show(AccountPanelPage.Nickname, fault.Code == "account_profile_conflict"
                    ? "昵称已在其他设备修改。本次未覆盖，你可以确认输入后再次保存。"
                    : "本次保存尚未确认。账号昵称已重新读取，请确认后再次保存。");
                return;
            }
            updated = observed;
        }
        ct.ThrowIfCancellationRequested();
        profile = updated;
        changed(true, configurationReady);
        if (!configurationReady) await BeginChoice(active, ct);
        else Show(AccountPanelPage.Account, "昵称已保存。");
    }
}
