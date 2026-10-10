using System.Globalization;
using Dango.Client.Sync.Rooms;
using Dango.Protocol;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>Owns bounded directory reads and one exact, session-local mutation until its outcome is known.</summary>
internal sealed class DesktopRoomController : IDisposable
{
    private readonly RoomPanelView panel;
    private readonly Func<IRoomManagementClient?> binding;
    private readonly Action close, account;
    private readonly Action<IRoomManagementClient> authenticationLost;
    private readonly SemaphoreSlim operations = new(1, 1);
    private CancellationTokenSource cancellation = new();
    private IRoomManagementClient? client;
    private PendingWrite? pending;
    private RoomPanelState state = new(RoomPanelPage.Directory);
    private long generation;
    private bool opened, busy, disposed;
    internal RoomPanelState CurrentState => state with { Busy = busy };
    internal event Action<RoomManagementView?>? OnlineRequested;

    internal DesktopRoomController(RoomPanelView panel, Func<IRoomManagementClient?> binding,
        Action close, Action account, Action<IRoomManagementClient> authenticationLost)
    {
        this.panel = panel; this.binding = binding; this.close = close; this.account = account;
        this.authenticationLost = authenticationLost;
        panel.Requested += OnRequested;
        panel.SceneRequested += OnSceneRequested;
    }

    internal void Open()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        opened = true;
        var current = binding();
        if (current is null) { Reset(); account(); return; }
        if (!ReferenceEquals(current, client))
        {
            Invalidate(); client = current; pending = null; state = new(RoomPanelPage.Directory);
        }
        if (pending is not null) Show(RoomPanelPage.Pending, PendingMessage());
        else _ = RunAsync(ct => LoadDirectoryAsync(null, ct));
    }

    private async void OnRequested(RoomPanelRequest request) => await HandleAsync(request);

    private async void OnSceneRequested(RoomPanelRequest request) => await HandleSceneAsync(request);

    internal async Task HandleSceneAsync(RoomPanelRequest request)
    {
        if (disposed || busy) return;
        // Scene targets only start bounded reads or open forms; they never submit mutations.
        if (request.Action is not (RoomPanelAction.SelectRoom or RoomPanelAction.OpenCreate or
            RoomPanelAction.ActiveDirectory or RoomPanelAction.DissolvedDirectory)) return;
        var current = binding();
        if (current is null) { Reset(); account(); return; }
        opened = true;
        if (!ReferenceEquals(current, client))
        { Invalidate(); client = current; pending = null; state = new(RoomPanelPage.Directory); }
        if (pending is not null) { Show(RoomPanelPage.Pending, PendingMessage()); return; }
        if (request.Action == RoomPanelAction.SelectRoom)
        {
            // A stale or fabricated door cannot address another room outside this confirmed page.
            if (request.RoomId is not { } id || state.Rooms?.Any(r => r.RoomId == id) != true) return;
            await RunAsync(ct => LoadDetailAsync(id, ct));
        }
        else if (request.Action == RoomPanelAction.OpenCreate)
        { state = state with { DirectoryState = "active" }; Show(RoomPanelPage.Create); }
        else
        {
            state = state with { Page = RoomPanelPage.Directory,
                DirectoryState = request.Action == RoomPanelAction.DissolvedDirectory ? "dissolved" : "active",
                Selected = null, Rooms = null, Next = null, Message = null };
            await RunAsync(ct => LoadDirectoryAsync(null, ct));
        }
    }

    internal async Task HandleAsync(RoomPanelRequest request)
    {
        if (disposed || !opened) return;
        if (request.Action == RoomPanelAction.Close) { Close(); return; }
        if (busy) return;
        if (request.Action == RoomPanelAction.OpenAccount && pending is null) { Close(); account(); return; }
        if (pending is not null && request.Action != RoomPanelAction.RetryPending) return;
        if (client is null) { account(); return; }
        if (request.Action == RoomPanelAction.OpenOnline)
        {
            if (state.Page == RoomPanelPage.Directory && request.RoomId is null)
            { Close(); OnlineRequested?.Invoke(null); }
            else if (state.Page == RoomPanelPage.Detail && state.Selected is { Status: "active" } selected && selected.RoomId == request.RoomId)
            { Close(); OnlineRequested?.Invoke(selected); }
            return;
        }
        switch (request.Action)
        {
            case RoomPanelAction.OpenCreate when state.Page == RoomPanelPage.Directory && state.DirectoryState == "active":
                Show(RoomPanelPage.Create); return;
            case RoomPanelAction.OpenEdit when state.Page == RoomPanelPage.Detail && state.Selected?.Status == "active":
                Show(RoomPanelPage.Edit); return;
            case RoomPanelAction.OpenDissolve when state.Page == RoomPanelPage.Detail && state.Selected is { Status: "active", Kind: not "desktopPet" }:
                Show(RoomPanelPage.ConfirmDissolve); return;
        }
        await RunAsync(async ct =>
        {
            switch (request.Action)
            {
                case RoomPanelAction.ActiveDirectory:
                case RoomPanelAction.DissolvedDirectory:
                    state = new(RoomPanelPage.Directory, request.Action == RoomPanelAction.ActiveDirectory ? "active" : "dissolved");
                    await LoadDirectoryAsync(null, ct); break;
                case RoomPanelAction.Refresh:
                    await LoadDirectoryAsync(null, ct); break;
                case RoomPanelAction.Back:
                    if (state.Page is RoomPanelPage.Edit or RoomPanelPage.ConfirmDissolve && state.Selected is { } back)
                        await LoadDetailAsync(back.RoomId, ct);
                    else await LoadDirectoryAsync(null, ct);
                    break;
                case RoomPanelAction.NextPage when state.Page == RoomPanelPage.Directory && state.Next is { } after:
                    await LoadDirectoryAsync(after, ct); break;
                case RoomPanelAction.SelectRoom when state.Page == RoomPanelPage.Directory && request.RoomId is { } id
                    && state.Rooms?.Any(room => room.RoomId == id) == true:
                    await LoadDetailAsync(id, ct); break;
                case RoomPanelAction.RefreshConflict when state.Page == RoomPanelPage.Conflict && state.Selected is { } conflict:
                    await LoadDetailAsync(conflict.RoomId, ct); break;
                case RoomPanelAction.Create when state.Page == RoomPanelPage.Create:
                    if (!TryForm(out var name, out var capacity)) break;
                    await WriteAsync(new(null, new RoomCreateRequest(Guid.NewGuid(), panel.Kind, name, capacity)), ct); break;
                case RoomPanelAction.Update when state.Page == RoomPanelPage.Edit && state.Selected is { Status: "active" } edit:
                    if (!TryForm(out name, out capacity)) break;
                    await WriteAsync(new(edit.RoomId, new RoomUpdateRequest(Guid.NewGuid(), edit.Version, name, capacity)), ct); break;
                case RoomPanelAction.Dissolve when state.Page == RoomPanelPage.ConfirmDissolve
                    && state.Selected is { Status: "active", Kind: not "desktopPet" } dissolve:
                    await WriteAsync(new(dissolve.RoomId, new RoomDissolveRequest(Guid.NewGuid(), dissolve.Version, true)), ct); break;
                case RoomPanelAction.Restore when state.Page == RoomPanelPage.Detail
                    && state.Selected is { Status: "dissolved", Recoverable: true, Kind: not "desktopPet" } restore:
                    await WriteAsync(new(restore.RoomId, new RoomRestoreRequest(Guid.NewGuid(), restore.Version)), ct); break;
                case RoomPanelAction.RetryPending when pending is { } original:
                    await WriteAsync(original, ct); break;
            }
        });
    }

    private bool TryForm(out string name, out int capacity)
    {
        name = panel.RoomName;
        if (string.IsNullOrWhiteSpace(name) || !int.TryParse(panel.CapacityText, NumberStyles.None,
            CultureInfo.InvariantCulture, out capacity) || capacity is < 1 or > 12)
        {
            capacity = 0; Show(state.Page, "请填写房间名称和 1–12 的整数容量；桌宠房固定 2 席。"); return false;
        }
        return true;
    }

    private async Task LoadDirectoryAsync(Guid? after, CancellationToken ct)
    {
        var page = await client!.ListAsync(state.DirectoryState, after, 4, ct);
        ct.ThrowIfCancellationRequested();
        if (page.Rooms.Length > 4 || page.Rooms.Select(r => r.RoomId).Distinct().Count() != page.Rooms.Length ||
            page.Next is { } next && (next == after || page.Rooms.Length == 0))
            throw new RoomManagementClientException(RoomManagementFailure.InvalidResponse, "room_page_invalid");
        state = new(RoomPanelPage.Directory, state.DirectoryState, page.Rooms, page.Next);
        Publish();
    }

    private async Task LoadDetailAsync(Guid id, CancellationToken ct)
    {
        var detail = await client!.ReadAsync(id, ct);
        ct.ThrowIfCancellationRequested();
        state = state with { Page = RoomPanelPage.Detail, Selected = detail, Message = null };
        Publish();
    }

    private async Task WriteAsync(PendingWrite original, CancellationToken ct)
    {
        pending = original;
        // A replay acknowledges the original commit. Even after a receipt, retry preserves the key
        // until a current detail read succeeds; the receipt never replaces the current version.
        var receipt = original.Receipt ?? (original.Body switch
        {
            RoomCreateRequest create => await client!.CreateAsync(create, ct),
            RoomUpdateRequest update => await client!.UpdateAsync(original.RoomId!.Value, update, ct),
            RoomDissolveRequest dissolve => await client!.DissolveAsync(original.RoomId!.Value, dissolve, ct),
            RoomRestoreRequest restore => await client!.RestoreAsync(original.RoomId!.Value, restore, ct),
            _ => throw new InvalidOperationException()
        });
        original.Receipt = receipt;
        ct.ThrowIfCancellationRequested();
        var detail = await client!.ReadAsync(receipt.RoomId, ct);
        ct.ThrowIfCancellationRequested();
        if (!ReferenceEquals(pending, original)) return;
        pending = null;
        state = state with
        {
            Page = RoomPanelPage.Detail,
            Selected = detail,
            Rooms = null,
            Next = null,
            Message = "服务器已确认操作，当前房间详情已重新读取。管理成功不表示已经进入在线场景。"
        };
        Publish();
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (disposed || !opened || busy || client is null) return;
        var epoch = generation;
        var ct = cancellation.Token;
        var current = client;
        busy = true; Publish();
        var entered = false;
        try
        {
            await operations.WaitAsync(ct); entered = true;
            ct.ThrowIfCancellationRequested();
            if (epoch != generation) return;
            await operation(ct);
        }
        catch (OperationCanceledException) { /* A cancelled write retains the original attempt. */ }
        catch (ObjectDisposedException) when (epoch != generation || disposed) { }
        catch (RoomManagementClientException ex)
        {
            if (epoch != generation || disposed || !opened) return;
            if (ex.Failure == RoomManagementFailure.Authentication)
            {
                Reset(); authenticationLost(current); return;
            }
            if (pending?.Receipt is not null && ex is { Failure: RoomManagementFailure.Rejected, Code: "room_not_found" })
            {
                pending = null;
                state = new(RoomPanelPage.Directory, state.DirectoryState,
                    Message: "历史操作已经确认，但当前房间已不存在或不可访问。请刷新本人目录。");
                Publish();
            }
            else if (pending?.Receipt is not null)
                Show(RoomPanelPage.Pending, "操作回执已收到，但当前详情读取未完成。继续原请求将只重新读取详情。");
            else if (ex.Failure == RoomManagementFailure.Conflict)
            {
                pending = null;
                if (state.Selected is null) Show(state.Page, FailureMessage(ex));
                else Show(RoomPanelPage.Conflict, "房间已变化或约束不满足。请重新读取详情，再明确决定是否修改；不会覆盖新版本。");
            }
            else if (ex.Failure == RoomManagementFailure.Rejected)
            {
                pending = null; Show(state.Page, FailureMessage(ex));
            }
            else if (pending is not null) Show(RoomPanelPage.Pending, PendingMessage());
            else Show(state.Page, "服务器不可用或响应不可用。请刷新；本地单人仍可游玩。");
        }
        finally
        {
            if (entered) operations.Release();
            if (epoch == generation && !disposed)
            {
                busy = false;
                if (opened) Publish();
            }
        }
    }

    private static string FailureMessage(RoomManagementClientException ex) => ex.Code switch
    {
        "room_quota_reached" => "有效共同房已达到额度。请刷新目录，管理已有房间。",
        "room_capacity_conflict" => "容量不能小于当前占用或保留席位。请重新读取详情。",
        "room_recovery_expired" => "恢复期限已到，服务器未恢复房间。",
        "room_not_found" => "房间不可访问或已经不存在。请刷新本人目录。",
        _ => "请求未获通过。请检查名称、容量或当前房间状态，再明确提交。"
    };

    private string PendingMessage() => pending?.Receipt is null
        ? "上次写入结果未知。原请求与正文已保留；继续原请求会使用相同请求键核对，不会重新创建一笔操作。关闭仍可本地游玩。"
        : "操作回执已收到，当前详情尚未确认。继续原请求只重新读取详情。";

    private void Show(RoomPanelPage page, string? message = null)
    { state = state with { Page = page, Message = message }; Publish(); }
    private void Publish()
    { if (opened && !disposed) panel.Apply(state with { Busy = busy }); }
    private void Invalidate()
    { generation++; cancellation.Cancel(); cancellation.Dispose(); cancellation = new(); busy = false; }
    internal void Close()
    { if (disposed) return; opened = false; Invalidate(); close(); }
    internal void Reset()
    { if (disposed) return; opened = false; Invalidate(); client = null; pending = null; state = new(RoomPanelPage.Directory); }
    public void Dispose()
    {
        if (disposed) return;
        Reset(); disposed = true; cancellation.Cancel(); cancellation.Dispose();
        panel.Requested -= OnRequested;
        panel.SceneRequested -= OnSceneRequested;
    }
    private sealed class PendingWrite(Guid? roomId, object body)
    {
        internal Guid? RoomId { get; } = roomId;
        internal object Body { get; } = body;
        internal RoomOperationResult? Receipt { get; set; }
    }
}
