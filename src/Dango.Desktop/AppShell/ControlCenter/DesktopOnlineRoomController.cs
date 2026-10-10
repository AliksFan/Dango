using Dango.Client.Sync.Rooms;
using Dango.Protocol;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>Owns one formal session and transient code flow on the UI dispatcher.</summary>
internal sealed class DesktopOnlineRoomController : IDisposable
{
    private readonly OnlineRoomPanelView panel;
    private readonly Func<IRoomAccessClient?> binding;
    private readonly Func<bool> configurationReady;
    private readonly Action close, directory, account;
    private readonly Action<IRoomAccessClient> authenticationLost;
    private readonly Guid instanceId = Guid.NewGuid();
    private readonly SemaphoreSlim operations = new(1, 1);
    private CancellationTokenSource cancellation = new();
    private IRoomAccessClient? client;
    private FormalRoomSession? session;
    private Action<FormalRoomSessionState>? subscription;
    private OnlineRoomPanelState state = new();
    private string? targetCode;
    private PendingCodeReset? pendingCodeReset;
    private long generation;
    private long viewGeneration;
    private Task suspended = Task.CompletedTask;
    private bool busy, opened, disposed;

    internal DesktopOnlineRoomController(OnlineRoomPanelView panel, Func<IRoomAccessClient?> binding,
        Func<bool> configurationReady, Action close, Action directory, Action account,
        Action<IRoomAccessClient> authenticationLost)
    {
        this.panel = panel; this.binding = binding; this.configurationReady = configurationReady;
        this.close = close; this.directory = directory; this.account = account;
        this.authenticationLost = authenticationLost; panel.Requested += OnRequested;
        panel.SceneRequested += OnSceneRequested;
    }

    internal OnlineRoomPanelState CurrentState => RenderState();

    internal void SynchronizeBinding()
    {
        if (disposed || ReferenceEquals(client, binding())) return;
        Reset();
    }

    internal void Open(RoomManagementView? target = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var current = binding();
        if (current is null) { Reset(); account(); return; }
        if (!ReferenceEquals(current, client))
        {
            Reset(); client = current; session = new(current, instanceId);
            var owner = session; var epoch = generation;
            subscription = snapshot => panel.Dispatcher.BeginInvoke(() =>
            {
                if (disposed || epoch != generation || !ReferenceEquals(owner, session)) return;
                if (owner.Current.FailureCode is "authentication_required" or "room_binding_changed")
                { Reset(); authenticationLost(current); return; }
                Publish();
            });
            session.Changed += subscription;
        }
        opened = true;
        if (target is { Status: "active" })
        {
            state = state with { Page = OnlineRoomPage.Status, OwnsTarget = true, Code = null,
                Target = new(target.RoomId, target.Kind, target.Name, target.Capacity, target.Version,
                    target.TemplateId, target.TemplateVersion), Message = null };
            targetCode = null;
        }
        else state = state with { Page = OnlineRoomPage.Status, Code = null, Message = null };
        Publish();
        if (session!.Current.Pending is null && pendingCodeReset is null)
            _ = RunAsync(ct => session.ResumeAsync(ct));
    }

    private async void OnRequested(OnlineRoomRequest request) => await HandleAsync(request);

    private void OnSceneRequested(OnlineRoomAction action)
    {
        if (disposed || action is not (OnlineRoomAction.OpenResolve or OnlineRoomAction.Refresh)) return;
        Open();
        if (session is null || session.Current.Pending is not null || pendingCodeReset is not null) return;
        if (action == OnlineRoomAction.OpenResolve)
        { state = state with { Page = OnlineRoomPage.ResolveCode, Code = null, Message = null }; Publish(); }
    }

    internal async Task HandleAsync(OnlineRoomRequest request)
    {
        if (disposed || !opened) return;
        if (request.Action == OnlineRoomAction.Close) { Close(); return; }
        if (busy || session is null || client is null) return;
        if (request.Action is OnlineRoomAction.Directory or OnlineRoomAction.Account)
        {
            Close(); if (request.Action == OnlineRoomAction.Directory) directory(); else account(); return;
        }
        if (pendingCodeReset is not null || session.Current.Pending is not null)
        {
            if (request.Action == OnlineRoomAction.RetryPending)
                await RunAsync(ct => pendingCodeReset is { } reset ? ResetCodeAsync(reset, ct) : RetrySessionAsync(ct));
            return;
        }
        if (session.Current.RequiresReconciliation && request.Action is OnlineRoomAction.OpenEnter or OnlineRoomAction.Enter
            or OnlineRoomAction.Control or OnlineRoomAction.OpenLeave or OnlineRoomAction.Leave or OnlineRoomAction.EndLease)
            return;
        switch (request.Action)
        {
            case OnlineRoomAction.OpenResolve when state.Page == OnlineRoomPage.Status:
                state = state with { Page = OnlineRoomPage.ResolveCode, Code = null, Message = null }; Publish(); return;
            case OnlineRoomAction.OpenEnter when state.Page == OnlineRoomPage.Status && MatchingTarget(request) && configurationReady():
                state = state with { Page = OnlineRoomPage.ConfirmEnter, Message = null }; Publish(); return;
            case OnlineRoomAction.OpenLeave when state.Page == OnlineRoomPage.Status && RenderState().Controller && RenderState().HasLease:
                state = state with { Page = OnlineRoomPage.ConfirmLeave, Message = null }; Publish(); return;
            case OnlineRoomAction.OpenResetCode when state.Page == OnlineRoomPage.Status && MatchingTarget(request)
                && state.OwnsTarget && state.Target?.Kind == "publicShared":
                state = state with { Page = OnlineRoomPage.ConfirmResetCode, Message = null }; Publish(); return;
            case OnlineRoomAction.Back:
                panel.ClearSecrets(); state = state with { Page = OnlineRoomPage.Status, Code = null, Message = null }; Publish(); return;
        }
        await RunAsync(async ct =>
        {
            switch (request.Action)
            {
                case OnlineRoomAction.Refresh when state.Page == OnlineRoomPage.Status:
                    await session.ResumeAsync(ct); break;
                case OnlineRoomAction.Resolve when state.Page == OnlineRoomPage.ResolveCode:
                    var code = panel.AccessCode;
                    if (code.Length != 43) { state = state with { Message = "请填写完整的公共访问码。" }; break; }
                    var door = await client.ResolveAsync(new(code), ct);
                    ct.ThrowIfCancellationRequested();
                    targetCode = code; panel.ClearSecrets();
                    state = state with { Page = OnlineRoomPage.ConfirmEnter, Target = door, OwnsTarget = false,
                        Message = "房门已读取，尚未入房。请确认目标房间。", Code = null }; break;
                case OnlineRoomAction.Enter when state.Page == OnlineRoomPage.ConfirmEnter && MatchingTarget(request) && configurationReady():
                    await session.EnterAsync(state.Target!.RoomId, targetCode, ct);
                    ct.ThrowIfCancellationRequested(); targetCode = null;
                    state = state with { Page = OnlineRoomPage.Status, Message = null }; break;
                case OnlineRoomAction.Control when state.Page == OnlineRoomPage.Status && RenderState().HasLease && !RenderState().Controller:
                    await session.TakeControlAsync(ct); break;
                case OnlineRoomAction.Leave when state.Page == OnlineRoomPage.ConfirmLeave && RenderState().HasLease && RenderState().Controller:
                    await session.LeaveAsync(ct); ct.ThrowIfCancellationRequested();
                    state = state with { Page = OnlineRoomPage.Status, Message = null }; break;
                case OnlineRoomAction.EndLease when state.Page == OnlineRoomPage.Status && RenderState().HasLease:
                    await session.EndCurrentLeaseAsync(ct); break;
                case OnlineRoomAction.ResetCode when state.Page == OnlineRoomPage.ConfirmResetCode && MatchingTarget(request)
                    && state.OwnsTarget && state.Target?.Kind == "publicShared":
                    await ResetCodeAsync(new(state.Target.RoomId, new(Guid.NewGuid(), state.Target.Version)), ct); break;
            }
        });
    }

    private bool MatchingTarget(OnlineRoomRequest request) => request.RoomId is { } id && state.Target?.RoomId == id;

    private async Task RetrySessionAsync(CancellationToken ct)
    {
        await session!.RetryPendingAsync(ct);
        ct.ThrowIfCancellationRequested(); targetCode = null;
        state = state with { Page = OnlineRoomPage.Status, Message = null };
    }

    private async Task ResetCodeAsync(PendingCodeReset original, CancellationToken ct)
    {
        pendingCodeReset = original;
        var result = await client!.ResetCodeAsync(original.RoomId, original.Body, ct);
        ct.ThrowIfCancellationRequested();
        pendingCodeReset = null;
        // A replay can be historical. Do not treat its RoomVersion as a newly read target version.
        state = state with { Page = OnlineRoomPage.Code, Code = result,
            Target = null, OwnsTarget = false, Message = "服务器已确认本次重置。再次重置前请从目录重新读取房间版本。" };
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (disposed || !opened || busy || client is null) return;
        var epoch = generation; var viewEpoch = viewGeneration; var current = client; var ct = cancellation.Token;
        busy = true; state = state with { Message = null }; Publish();
        var entered = false;
        try
        {
            await operations.WaitAsync(ct); entered = true;
            await suspended.WaitAsync(ct);
            ct.ThrowIfCancellationRequested(); if (epoch != generation || viewEpoch != viewGeneration) return;
            await operation(ct);
        }
        catch (OperationCanceledException) { /* Session and code reset retain uncertain attempts. */ }
        catch (ObjectDisposedException) when (epoch != generation || viewEpoch != viewGeneration || disposed) { }
        catch (RoomAccessClientException error)
        {
            if (epoch != generation || viewEpoch != viewGeneration || disposed || !opened) return;
            if (error.Code is "authentication_required" or "room_binding_changed")
            { Reset(); authenticationLost(current); return; }
            if (pendingCodeReset is not null && error.Failure is RoomAccessFailure.Conflict or RoomAccessFailure.Rejected or RoomAccessFailure.Authentication)
            {
                pendingCodeReset = null;
                if (error.Failure == RoomAccessFailure.Conflict)
                    state = state with { Target = null, OwnsTarget = false, Page = OnlineRoomPage.Status };
            }
            if (session?.Current.Pending is null && pendingCodeReset is null
                && error.Failure is RoomAccessFailure.Unavailable or RoomAccessFailure.InvalidResponse)
                state = state with { Page = OnlineRoomPage.Status };
            state = state with { Message = FailureMessage(error.Code) };
        }
        finally
        {
            if (entered) operations.Release();
            if (epoch == generation && viewEpoch == viewGeneration && !disposed) { busy = false; Publish(); }
        }
    }

    private OnlineRoomPanelState RenderState()
    {
        var current = session?.Current;
        var lease = current?.Personal?.Leases.FirstOrDefault(l => l.InstanceId == instanceId && l.ExpiresAt > current.Personal.ServerTime);
        return state with { Busy = busy, ConfigurationReady = configurationReady(),
            Personal = current?.Personal, Shared = current?.Shared,
            HasLease = current?.ServerAttached == true && lease is not null, Controller = current?.Role == FormalRoomRole.Control,
            Connected = current?.ServerAttached == true, Pending = current?.Pending is not null || pendingCodeReset is not null,
            RequiresReconciliation = current?.RequiresReconciliation == true,
            Status = current?.FailureCode is { } code ? FailureMessage(code) : current?.Phase switch
            {
                FormalRoomSessionPhase.Reading => "正在读取服务器状态…",
                FormalRoomSessionPhase.Mutating => "正在提交明确操作…",
                FormalRoomSessionPhase.Connecting => "正在连接正式房间服务…",
                FormalRoomSessionPhase.AwaitingPresentation => "服务器连接已建立。多人场景操作尚未接入。",
                FormalRoomSessionPhase.Disconnected => "连接已断开，正在有限次数地核对并恢复有效租约。",
                FormalRoomSessionPhase.ReentryRequired => "本设备没有有效租约。重新入房需要明确申请。",
                FormalRoomSessionPhase.Unknown => "操作结果未知，请核对保留的请求。",
                FormalRoomSessionPhase.ReconciliationRequired => "操作已确认，但当前服务器状态尚未读取完成。请先读取当前状态。",
                FormalRoomSessionPhase.Suspended => "本机连接已暂停；服务器归属以重新读取的结果为准。",
                FormalRoomSessionPhase.Conflict => "服务器状态已变化，请读取后再明确操作。",
                _ => "请读取服务器当前状态。"
            } };
    }
    private void Publish() { if (opened && !disposed) panel.Apply(RenderState()); }
    private static string FailureMessage(string code) => code switch
    {
        "room_full" => "房间已满，服务器未准入。",
        "room_device_limit" => "有效设备租约已达到上限，请先结束不再使用的设备租约。",
        "room_banned" or "room_access_forbidden" or "room_lease_forbidden" => "服务器拒绝本次房间访问；账号登录仍保留。",
        "room_code_rejected" => "公共访问码不可用，请向房主取得当前有效码。",
        "room_control_required" => "只有控制设备可以正式离房；观察设备可以结束本设备租约。",
        "room_assignment_conflict" or "room_control_conflict" or "room_transport_conflict" => "归属或控制状态已变化。请读取当前状态，再决定操作。",
        "room_request_conflict" => "请求键与原操作或会话不一致，服务器拒绝复用。请先读取当前状态。",
        "room_lease_expired" or "room_reentry_required" => "本设备租约已失效；不会自动重新入房或接管。",
        "request_protection_invalid" or "origin_invalid" => "请求保护校验未通过，请重新读取；本地单人仍可使用。",
        _ => "服务器状态尚未确认。请读取或核对保留的请求；本地单人仍可使用。"
    };

    internal void Close()
    {
        if (disposed) return;
        opened = false; viewGeneration++; busy = false;
        cancellation.Cancel(); cancellation.Dispose(); cancellation = new();
        // Keep the same session and its exact pending command, but stop all local connection work.
        panel.ClearSecrets(); targetCode = null;
        state = state with { Page = OnlineRoomPage.Status, Code = null, Message = null };
        if (!state.OwnsTarget) state = state with { Target = null };
        if (session is { } owner) { suspended = owner.SuspendAsync(); ObserveCleanup(suspended); }
        close();
    }
    internal void Reset()
    {
        if (disposed) return;
        generation++; viewGeneration++; opened = false; busy = false;
        cancellation.Cancel(); cancellation.Dispose(); cancellation = new();
        panel.ClearSecrets(); targetCode = null; pendingCodeReset = null; state = new();
        if (session is { } owner)
        { if (subscription is not null) owner.Changed -= subscription; ObserveCleanup(owner.DisposeAsync().AsTask()); }
        subscription = null; session = null; client = null;
        suspended = Task.CompletedTask;
    }
    private static async void ObserveCleanup(Task cleanup)
    {
        try { await cleanup; }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or RoomAccessClientException) { }
    }
    public void Dispose()
    {
        if (disposed) return;
        Reset(); disposed = true; cancellation.Cancel(); cancellation.Dispose(); panel.Requested -= OnRequested;
        panel.SceneRequested -= OnSceneRequested;
    }
    private sealed record PendingCodeReset(Guid RoomId, RoomCodeResetRequest Body);
}
