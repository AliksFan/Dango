using System.Security.Claims;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Dango.Server.Rooms.Access;

[Authorize]
public sealed class FormalRoomHub(RoomAdmissionService service, SessionService sessions,
    RoomConnectionRegistry registry, RoomConnectionAdmission admission) : Hub
{
    private Guid UserId => Guid.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private Guid SessionId => Guid.Parse(Context.User!.FindFirstValue(FormalHost.SessionClaim)!);

    public override async Task OnConnectedAsync()
    {
        await admission.PruneAsync(Context.ConnectionAborted);
        if (!await sessions.ValidateAsync(UserId, SessionId, Context.ConnectionAborted)
            || !registry.Add(Context.ConnectionId, UserId, SessionId, Context.Abort))
        { Context.Abort(); throw new HubException("room_connection_rejected"); }
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // Transport loss is not lease expiration. A new transport can attach within the deadline.
        registry.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task<RoomAccessResult> Attach(RoomLeaseRequest? request)
    {
        request = Required(request);
        var result = await service.AttachAsync(UserId, SessionId, request, Context.ConnectionId, Context.ConnectionAborted);
        registry.Bind(Context.ConnectionId, result.LeaseId, request.InstanceId, result.RoomId);
        return result;
    }
    public Task<RoomAccessResult> Heartbeat(RoomLeaseRequest? request) =>
        service.HeartbeatAsync(UserId, SessionId, Required(request), Context.ConnectionId, Context.ConnectionAborted);
    public Task<RoomAccessResult> TakeControl(RoomControlRequest? request) =>
        service.TakeoverAsync(UserId, SessionId, Required(request), Context.ConnectionId, Context.ConnectionAborted);
    public async Task<RoomAccessResult> Leave(RoomControlRequest? request)
    {
        request = Required(request);
        var result = await service.LeaveAsync(UserId, SessionId, request, Context.ConnectionId, Context.ConnectionAborted);
        registry.UnbindLease(Context.ConnectionId, request.LeaseId, request.InstanceId);
        return result;
    }
    public async Task<RoomAccessResult> EndLease(RoomLeaseEndRequest? request)
    {
        request = Required(request);
        var result = await service.EndLeaseAsync(UserId, SessionId, request, Context.ConnectionId, Context.ConnectionAborted);
        registry.UnbindLease(Context.ConnectionId, request.LeaseId, request.InstanceId);
        return result;
    }
    public Task<PersonalRoomSnapshot> PersonalSnapshot() => service.PersonalSnapshotAsync(UserId, SessionId, Context.ConnectionAborted);
    public Task<SharedRoomSnapshot> Snapshot(Guid roomId, RoomLeaseRequest? request) =>
        service.SnapshotAsync(UserId, SessionId, roomId, Required(request), Context.ConnectionId, Context.ConnectionAborted);
    private static T Required<T>(T? request) where T : class => request ?? throw new AccountFault("room_request_invalid");
}

public sealed class FormalRoomHubFilter(RoomConnectionRegistry registry) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (!registry.AllowCall(invocationContext.Context.ConnectionId)) throw new HubException("room_rate_limited");
        try { return await next(invocationContext); }
        catch (AccountFault fault)
        {
            if (fault.Status == 401)
            { registry.Remove(invocationContext.Context.ConnectionId); invocationContext.Context.Abort(); }
            throw new HubException(fault.Code);
        }
        catch (OperationCanceledException) when (invocationContext.Context.ConnectionAborted.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { throw new HubException("service_unavailable"); }
    }
}
