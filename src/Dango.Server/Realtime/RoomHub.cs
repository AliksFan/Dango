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

    public async Task<JoinRoomResponse> JoinRoom(JoinRoomRequest? request) => request is null ? throw new HubException("invalid_command") : await Bind(subscribe =>
        room.JoinAsync(Context.ConnectionId, request, subscribe));

    public async Task<JoinRoomResponse> ResumeRoom(ResumeRoomRequest? request) => request is null ? throw new HubException("invalid_command") : await Bind(subscribe =>
        room.ResumeAsync(Context.ConnectionId, request, subscribe));

    public Task<CommandResult> SendCommand(RoomCommand command) => Adapt(() => room.CommandAsync(Context.ConnectionId, command));
    public Task<RoomSnapshot> GetSnapshot() => Adapt(() => room.SnapshotAsync(Context.ConnectionId));
    public Task<TimeSample> GetRoomTime() => Adapt(() => room.TimeAsync(Context.ConnectionId));

    private async Task<JoinRoomResponse> Bind(Func<Func<Task>, Task<JoinRoomResponse>> action)
    {
        var ownsSubscriptionAttempt = false;
        async Task Subscribe()
        {
            // The runtime holds this identity's JoinGate while invoking this callback. A successful
            // probe therefore describes the current binding, not a possibly superseded Items entry.
            try
            {
                await room.TimeAsync(Context.ConnectionId);
                return; // Same-transport retry owns no new group membership to roll back.
            }
            catch (RoomRejectedException ex) when (ex.Code == "not_joined") { }
            // A full room queue or any other probe failure propagates without adding a subscription.
            ownsSubscriptionAttempt = true;
            await Groups.AddToGroupAsync(Context.ConnectionId, RoomProtocol.RoomId, Context.ConnectionAborted);
        }

        try
        {
            var response = await Adapt(() => action(Subscribe));
            Context.Items[GenerationKey] = response.ConnectionGeneration;
            if (Context.ConnectionAborted.IsCancellationRequested)
            {
                await room.DisconnectAsync(Context.ConnectionId, response.ConnectionGeneration);
                throw new HubException("not_joined");
            }
            return response;
        }
        catch
        {
            if (ownsSubscriptionAttempt)
            {
                // Do not use ConnectionAborted: cleanup is also required after transport cancellation.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomProtocol.RoomId, cleanup.Token).WaitAsync(cleanup.Token); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Closing only this new transport lets SignalR remove any partially added group.
                    Context.Abort();
                }
            }
            throw;
        }
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
