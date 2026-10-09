using Dango.Server.Accessories;

namespace Dango.Server.Hosting;

public static class AccessoryEndpoints
{
    public static void Map(WebApplication app)
    {
        var pet = app.MapGroup("/api/v1/me/pet").RequireAuthorization();
        pet.MapPost("/accessory-assets", async (HttpContext context, AccessoryAccountService service, CancellationToken ct) =>
        {
            var userId = FormalHost.UserId(context); var sessionId = FormalHost.SessionId(context);
            var sourceDigest = await AccessoryEndpointInput.PreparePngAsync(context,
                token => service.AuthenticateAsync(userId, sessionId, token), ct);
            return Results.Json(await service.UploadAsync(userId, sessionId, context.Request.Body, sourceDigest, ct), AccessoryPersistence.Json);
        });
        pet.MapGet("/accessory-assets/{assetId}", async (string assetId, HttpContext context, AccessoryAccountService service, CancellationToken ct) =>
            Results.Bytes(await service.DownloadAsync(FormalHost.UserId(context), FormalHost.SessionId(context), assetId, ct), "image/png"));
        pet.MapGet("/accessories", async (HttpContext context, AccessoryAccountService service, CancellationToken ct) =>
        {
            var current = await service.GetAsync(FormalHost.UserId(context), FormalHost.SessionId(context), ct);
            return current is null ? Results.NoContent() : Results.Json(current, AccessoryPersistence.Json);
        });
        pet.MapPut("/accessories", async (HttpContext context, AccessoryAccountService service, CancellationToken ct) =>
        {
            var userId = FormalHost.UserId(context); var sessionId = FormalHost.SessionId(context);
            var request = await AccessoryEndpointInput.ReadCommitAsync(context,
                token => service.AuthenticateAsync(userId, sessionId, token), ct);
            return Results.Json(await service.CommitAsync(userId, sessionId, request, ct), AccessoryPersistence.Json);
        });
        pet.MapGet("/accessories/commits/{key}", async (string key, HttpContext context, AccessoryAccountService service, CancellationToken ct) =>
            Results.Json(await service.ResolveAsync(FormalHost.UserId(context), FormalHost.SessionId(context), key, ct), AccessoryPersistence.Json));
    }
}
