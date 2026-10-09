using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Rooms.Management;

namespace Dango.Server.Hosting;

public static class RoomManagementEndpoints
{
    public static void Map(WebApplication app)
    {
        var rooms = app.MapGroup("/api/v1").RequireAuthorization();
        rooms.MapGet("/me/rooms", async (HttpContext ctx, RoomManagementService service, string? state,
            int? limit, Guid? after, CancellationToken ct) =>
        {
            RequireQuery(ctx, "state", "limit", "after");
            return Results.Ok(await service.ListAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx),
                state ?? "active", limit ?? 20, after, ct));
        });
        rooms.MapGet("/rooms/{id:guid}", async (Guid id, HttpContext ctx, RoomManagementService service, CancellationToken ct) =>
        {
            RequireQuery(ctx);
            return Results.Ok(await service.GetAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx), id, ct));
        });
        rooms.MapPost("/rooms", async (RoomCreateRequest request, HttpContext ctx, RoomManagementService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx), request, ct);
            return Results.Created($"/api/v1/rooms/{result.RoomId:D}", result);
        });
        rooms.MapPatch("/rooms/{id:guid}", async (Guid id, RoomUpdateRequest request, HttpContext ctx,
            RoomManagementService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx), id, request, ct)));
        rooms.MapPost("/rooms/{id:guid}/dissolve", async (Guid id, RoomDissolveRequest request, HttpContext ctx,
            RoomManagementService service, CancellationToken ct) =>
            Results.Ok(await service.DissolveAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx), id, request, ct)));
        rooms.MapPost("/rooms/{id:guid}/restore", async (Guid id, RoomRestoreRequest request, HttpContext ctx,
            RoomManagementService service, CancellationToken ct) =>
            Results.Ok(await service.RestoreAsync(FormalHost.UserId(ctx), FormalHost.SessionId(ctx), id, request, ct)));
    }

    private static void RequireQuery(HttpContext ctx, params string[] allowed)
    {
        if (ctx.Request.Query.Any(item => !allowed.Contains(item.Key, StringComparer.Ordinal) || item.Value.Count != 1
            || string.IsNullOrWhiteSpace(item.Value[0]))) throw new AccountFault("room_query_invalid");
    }
}
