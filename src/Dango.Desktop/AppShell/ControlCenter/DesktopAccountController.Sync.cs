using System.Windows.Threading;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Pets;
using Dango.Protocol;

namespace Dango.Desktop.AppShell.ControlCenter;

internal sealed partial class DesktopAccountController
{
    private readonly Func<IRemotePetConfigurationClient, PetConfigurationSnapshot, PetConfigurationLiveSyncCoordinator>? createSync;
    private readonly DispatcherTimer configurationTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private PetConfigurationLiveSyncCoordinator? configurationSync;
    private Task configurationPump = Task.CompletedTask;
    private long syncGeneration;
    private int syncFailures;
    private string? configurationStatus;

    private async Task StartConfigurationSyncAsync(FormalAccountClient active, PetConfigurationSnapshot cloud, CancellationToken ct)
    {
        StopConfigurationSync();
        await configurationPump.WaitAsync(ct);
        ct.ThrowIfCancellationRequested();
        if (disposed || identity is null || active.CurrentIdentity != identity)
            throw new AccountClientException(AccountFailure.Authentication, "authentication_required");
        if (createSync is null) return;
        configurationSync = createSync(active.CreatePetClient(identity), cloud);
        configurationStatus = "捏脸已同步。";
        syncFailures = 0;
        configurationTimer.Interval = TimeSpan.FromSeconds(2);
        configurationTimer.Start();
    }

    internal void NotifyAppearanceSaved()
    {
        if (disposed || configurationSync is null) return;
        configurationSync.NotifyLocalSaved();
        configurationStatus = "捏脸已保存到本机，正在同步。";
        if (opened && !busy && page == AccountPanelPage.Account) panel.Apply(State());
        _ = PumpConfigurationAsync();
    }

    private async void OnConfigurationTick(object? sender, EventArgs args) => await PumpConfigurationAsync();

    internal Task PumpConfigurationAsync()
    {
        if (disposed || configurationSync is null || !configurationPump.IsCompleted) return configurationPump;
        configurationPump = PumpConfigurationCoreAsync(configurationSync, syncGeneration);
        return configurationPump;
    }

    private async Task PumpConfigurationCoreAsync(PetConfigurationLiveSyncCoordinator sync, long epoch)
    {
        PetLiveSyncState state;
        try { state = await sync.PumpAsync(); }
        catch (ObjectDisposedException) when (disposed || epoch != syncGeneration) { return; }
        if (disposed || epoch != syncGeneration || !ReferenceEquals(sync, configurationSync)) return;
        if (state.Status == PetLiveSyncStatus.Stopped)
        {
            if (state.FailureCode is "authentication_required" or "pet_binding_changed" && client is not null)
            {
                ClearAuthentication(client);
                Show(AccountPanelPage.Login, "会话已失效，请重新登录。本机外观保留。");
            }
            return;
        }
        var previousStatus = configurationStatus;
        configurationStatus = state.Status switch
        {
            PetLiveSyncStatus.Synchronized when state.LastLocalSaveOutcome == PetChoiceOutcome.Conflict
                => "已采用账号最新捏脸。本次本机修改未上传，原始配置已保留。",
            PetLiveSyncStatus.Synchronized => "捏脸已同步。",
            PetLiveSyncStatus.PausedForDraft => "捏脸草稿保留；应用或取消后继续同步。",
            PetLiveSyncStatus.Pending or PetLiveSyncStatus.Synchronizing => "正在同步捏脸。",
            PetLiveSyncStatus.Offline => "连接暂不可用，捏脸已保留在本机；恢复后继续核对同步。",
            _ => "捏脸同步尚未完成，原始配置保留；正在核对结果。"
        };
        if (state.Status is PetLiveSyncStatus.Offline or PetLiveSyncStatus.Failed)
            syncFailures = Math.Min(syncFailures + 1, 4);
        else syncFailures = 0;
        configurationTimer.Interval = TimeSpan.FromSeconds(Math.Min(30, 2 * (1 << syncFailures)));
        // Updating status must preserve in-flight form drafts and their chosen page.
        if (opened && !busy && page == AccountPanelPage.Account && previousStatus != configurationStatus)
            panel.Apply(State());
    }

    private void StopConfigurationSync()
    {
        syncGeneration++;
        configurationTimer.Stop();
        var old = configurationSync;
        configurationSync = null;
        configurationStatus = null;
        old?.Stop();
        if (old is not null) _ = DisposeAfterPumpAsync(old, configurationPump);
    }

    private static async Task DisposeAfterPumpAsync(PetConfigurationLiveSyncCoordinator sync, Task pump)
    {
        try { await pump; }
        finally { sync.Dispose(); }
    }
}
