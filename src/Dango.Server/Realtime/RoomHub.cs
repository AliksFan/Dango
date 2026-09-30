using Dango.Protocol;
using Dango.Server.Rooms;
using Microsoft.AspNetCore.SignalR;

namespace Dango.Server.Realtime;

public sealed class ConnectionAdmission
{
    private readonly SemaphoreSlim slots = new(16, 16);
    public bool TryEnter() => slots.Wait(0);
    public void Exit() => slots.Release();
}

public sealed class RoomHub(RoomRuntime room, ConnectionAdmission admission) : Hub
{
    private const string GenerationKey = "room-generation";
    private const string AdmissionKey = "room-admission";

    public override Task OnConnectedAsync()
    {
        if (!admission.TryEnter()) { Context.Abort(); return Task.CompletedTask; }
        Context.Items[AdmissionKey] = true;
        return base.OnConnectedAsync();
    }

    public async Task<JoinRoomResponse> JoinRoom(JoinRoomRequest? request) => request is null ? throw new HubException("invalid_command") : await Bind(() =>
        room.JoinAsync(Context.ConnectionId, request, () => Groups.AddToGroupAsync(Context.ConnectionId, RoomProtocol.RoomId, Context.ConnectionAborted)));

    public async Task<JoinRoomResponse> ResumeRoom(ResumeRoomRequest? request) => request is null ? throw new HubException("invalid_command") : await Bind(() =>
        room.ResumeAsync(Context.ConnectionId, request, () => Groups.AddToGroupAsync(Context.ConnectionId, RoomProtocol.RoomId, Context.ConnectionAborted)));

    public Task<CommandResult> SendCommand(RoomCommand command) => Adapt(() => room.CommandAsync(Context.ConnectionId, command));
    public Task<RoomSnapshot> GetSnapshot() => Adapt(() => room.SnapshotAsync(Context.ConnectionId));
    public Task<TimeSample> GetRoomTime() => Adapt(() => room.TimeAsync(Context.ConnectionId));

    private async Task<JoinRoomResponse> Bind(Func<Task<JoinRoomResponse>> action)
    {
        var response = await Adapt(action);
        Context.Items[GenerationKey] = response.ConnectionGeneration;
        if (Context.ConnectionAborted.IsCancellationRequested)
        {
            await room.DisconnectAsync(Context.ConnectionId, response.ConnectionGeneration);
            throw new HubException("not_joined");
        }
        return response;
    }

    private static async Task<T> Adapt<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (RoomRejectedException ex) { throw new HubException(ex.Code); }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            if (Context.Items.TryGetValue(GenerationKey, out var generation) && generation is long value)
                await room.DisconnectAsync(Context.ConnectionId, value);
        }
        catch (RoomRejectedException) { /* No movement renewal occurs; finite leases still end. */ }
        finally
        {
            if (Context.Items.Remove(AdmissionKey)) admission.Exit();
        }
        await base.OnDisconnectedAsync(exception);
    }
}

public sealed class SignalRRoomEventSink(IHubContext<RoomHub> hub) : IRoomEventSink
{
    public Task SendAsync(RoomEvent roomEvent, IReadOnlyList<RoomRecipient> recipients, CancellationToken cancellationToken) =>
        hub.Clients.Clients(recipients.Select(r => r.ConnectionId).ToArray()).SendAsync("RoomEvent", roomEvent, cancellationToken);

    public async Task SupersedeAsync(RoomRecipient oldRecipient, CancellationToken cancellationToken)
    {
        await hub.Groups.RemoveFromGroupAsync(oldRecipient.ConnectionId, RoomProtocol.RoomId, cancellationToken);
        await hub.Clients.Client(oldRecipient.ConnectionId).SendAsync("SessionSuperseded", new SessionSuperseded(oldRecipient.Generation), cancellationToken);
    }
}
