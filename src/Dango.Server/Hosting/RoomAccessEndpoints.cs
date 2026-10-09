using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Rooms.Access;

namespace Dango.Server.Hosting;

public static class RoomAccessEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapPost("/rooms/{id:guid}/enter", async (Guid id, RoomEnterRequest request, HttpContext ctx,
            RoomAdmissionService service, CancellationToken ct) => Results.Ok(await service.EnterAsync(
                FormalHost.UserId(ctx), FormalHost.SessionId(ctx), id, request, ct)));
        api.MapPost("/me/room/leave", async (RoomControlRequest request, HttpContext ctx, RoomAdmissionService service, CancellationToken ct) =>
            Results.Ok(await service.LeaveAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx), request, null, ct)));
        api.MapPost("/me/room/control", async (RoomControlRequest request, HttpContext ctx, RoomAdmissionService service, CancellationToken ct) =>
            Results.Ok(await service.TakeoverAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx), request, null, ct)));
        api.MapPost("/me/room/lease/end", async (RoomLeaseEndRequest request, HttpContext ctx, RoomAdmissionService service, CancellationToken ct) =>
            Results.Ok(await service.EndLeaseAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx), request, null, ct)));
        api.MapGet("/me/room/snapshot", async (HttpContext ctx, RoomAdmissionService service, CancellationToken ct) =>
        {
            RequireQuery(ctx);
            return Results.Ok(await service.PersonalSnapshotAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx), ct));
        });
        api.MapGet("/rooms/{id:guid}/snapshot", async (Guid id, Guid leaseId, Guid instanceId, HttpContext ctx,
            RoomAdmissionService service, CancellationToken ct) =>
        {
            RequireQuery(ctx, "leaseId", "instanceId");
            return Results.Ok(await service.SnapshotAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx), id,
                new(leaseId, instanceId), null, ct));
        });
        api.MapPost("/rooms/resolve", async (RoomCodeResolveRequest request, HttpContext ctx, PublicRoomAccessService service, CancellationToken ct) =>
            Results.Ok(await service.ResolveAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx), request, ct)));
        api.MapPost("/rooms/{id:guid}/code/reset", async (Guid id, RoomCodeResetRequest request, HttpContext ctx,
            PublicRoomAccessService service, CancellationToken ct) => Results.Ok(await service.ResetCodeAsync(
                FormalHost.UserId(ctx), FormalHost.SessionId(ctx), id, request, ct)));
        api.MapPost("/rooms/{id:guid}/users/{userId:guid}/remove", async (Guid id, Guid userId, RoomModerationRequest request,
            HttpContext ctx, PublicRoomAccessService service, CancellationToken ct) => Results.Ok(await service.RemoveAsync(
                FormalHost.UserId(ctx), FormalHost.SessionId(ctx), id, userId, request, ct)));
        api.MapPost("/rooms/{id:guid}/users/{userId:guid}/ban", async (Guid id, Guid userId, RoomModerationRequest request,
            HttpContext ctx, PublicRoomAccessService service, CancellationToken ct) => Results.Ok(await service.BanAsync(
                FormalHost.UserId(ctx), FormalHost.SessionId(ctx), id, userId, request, ct)));
        api.MapPost("/rooms/{id:guid}/users/{userId:guid}/unban", async (Guid id, Guid userId, RoomModerationRequest request,
            HttpContext ctx, PublicRoomAccessService service, CancellationToken ct) => Results.Ok(await service.UnbanAsync(
                FormalHost.UserId(ctx), FormalHost.SessionId(ctx), id, userId, request, ct)));
        app.MapHub<FormalRoomHub>("/api/v1/room-hub").RequireAuthorization();
    }

    private static void RequireQuery(HttpContext ctx, params string[] allowed)
    {
        if (ctx.Request.Query.Any(item => !allowed.Contains(item.Key, StringComparer.Ordinal) || item.Value.Count != 1
            || string.IsNullOrWhiteSpace(item.Value[0]))) throw new AccountFault("room_query_invalid");
    }
}
