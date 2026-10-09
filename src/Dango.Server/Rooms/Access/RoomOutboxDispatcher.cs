using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Rooms.Access;

public interface IRoomNoticeSender
{
    Task SendAsync(string connectionId, RoomChangeNotice notice, CancellationToken ct);
}
public sealed class RoomNoticeSender(IHubContext<FormalRoomHub> hub) : IRoomNoticeSender
{
    public Task SendAsync(string connectionId, RoomChangeNotice notice, CancellationToken ct) =>
        hub.Clients.Client(connectionId).SendAsync("RoomChanged", notice, ct);
}
public interface IRoomOutboxCheckpoint
{
    Task ReachedAsync(string step, Guid eventId, CancellationToken ct);
}
public sealed class RoomOutboxCheckpoint : IRoomOutboxCheckpoint
{
    public Task ReachedAsync(string step, Guid eventId, CancellationToken ct) => Task.CompletedTask;
}
public sealed record RoomOutboxCursor(DateTimeOffset OccurredAt, Guid EventId);
public sealed record RoomDispatchResult(int Examined, int Published, int Failed, RoomOutboxCursor? NextCursor = null);

public sealed class RoomOutboxDispatcher(IServiceScopeFactory scopes, RoomConnectionRegistry registry,
    IRoomNoticeSender sender, IRoomOutboxCheckpoint checkpoint, TimeProvider clock)
{
    public const int MaximumBatchSize = 20;
    public static Guid[]? ValidateScope(int limit, IReadOnlyCollection<Guid>? eventIds)
    {
        if (limit is < 1 or > MaximumBatchSize) throw new ArgumentOutOfRangeException(nameof(limit));
        if (eventIds is null) return null;
        var ids = eventIds.ToArray();
        if (ids.Length is < 1 or > MaximumBatchSize || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("An outbox scope requires distinct nonempty event IDs.", nameof(eventIds));
        return ids;
    }

    public async Task<RoomDispatchResult> DispatchBatchAsync(int limit, IReadOnlyCollection<Guid>? eventIds, CancellationToken ct,
        RoomOutboxCursor? after = null)
    {
        var ids = ValidateScope(limit, eventIds);
        if (after?.EventId == Guid.Empty) throw new ArgumentException("An outbox cursor requires a nonempty ID.", nameof(after));
        await using var queryScope = scopes.CreateAsyncScope();
        var queryDb = queryScope.ServiceProvider.GetRequiredService<DangoDbContext>();
        var query = queryDb.RoomDomainEvents.AsNoTracking().Where(e => e.PublishedAt == null);
        if (ids is not null) query = query.Where(e => ids.Contains(e.Id));
        if (after is not null) query = query.Where(e => e.OccurredAt > after.OccurredAt
            || (e.OccurredAt == after.OccurredAt && e.Id.CompareTo(after.EventId) > 0));
        var candidates = await query.OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).Take(limit).Select(e => new { e.Id, e.OccurredAt }).ToArrayAsync(ct);
        var published = 0; var failed = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try { if (await PublishAsync(candidate.Id, ct)) published++; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is not OutOfMemoryException) { failed++; }
        }
        return new(candidates.Length, published, failed, candidates.Length == 0 ? null : new(candidates[^1].OccurredAt, candidates[^1].Id));
    }

    private async Task<bool> PublishAsync(Guid id, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var bounded = deadline.Token;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DangoDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(bounded);
        var item = await db.RoomDomainEvents.FromSqlInterpolated(
            $"SELECT * FROM dango.domain_outbox WHERE id = {id} AND published_at IS NULL FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(bounded);
        if (item is null) return false;
        var notice = new RoomChangeNotice(item.Id, item.RoomId, item.RoomVersion, item.EventType);
        foreach (var connection in registry.Candidates(item.RoomId, item.TargetUserIds))
            await SendAuthorizedAsync(connection, notice, item.TargetUserIds.Contains(connection.UserId), bounded);
        await checkpoint.ReachedAsync("after_send_before_publish", item.Id, bounded);
        item.PublishedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(bounded);
        await transaction.CommitAsync(bounded);
        return true;
    }

    private async Task SendAuthorizedAsync(RoomConnection connection, RoomChangeNotice notice, bool personal, CancellationToken ct)
    {
        if (!registry.IsCurrent(connection)) return;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DangoDbContext>();
        var environment = scope.ServiceProvider.GetRequiredService<AccountEnvironment>();
        try
        {
            await RoomAccessPersistence.ActorAsync(db, environment, clock, new RoomAccessCheckpoint(), connection.UserId,
                connection.SessionId, notice.RoomId, null, async context =>
                {
                    await checkpoint.ReachedAsync("recipient_after_locks", notice.EventId, ct);
                    if (!registry.IsCurrent(connection) || !await RoomAccessPersistence.SessionValidAsync(db,
                        connection.UserId, connection.SessionId, environment, clock, ct)) return false;
                    if (!personal)
                    {
                        if (context.State.RoomId != notice.RoomId) return false;
                        var effective = await RoomAccessPersistence.EffectiveLeasesAsync(db, context, environment, clock, ct);
                        var lease = effective.SingleOrDefault(l => l.Id == connection.LeaseId && l.InstanceId == connection.InstanceId
                            && l.SessionId == connection.SessionId && l.TransportId == connection.Id);
                        if (lease is null || await db.RoomBans.AsNoTracking().AnyAsync(b => b.RoomId == notice.RoomId
                            && b.UserId == connection.UserId && b.Active, ct)) return false;
                        context.RequiredLeaseAtCommit = lease.Id;
                    }
                    // Holding credentials and room/state gates serializes revoke/ban/dissolve with this bounded send.
                    using var sendDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    sendDeadline.CancelAfter(TimeSpan.FromSeconds(1));
                    try { await sender.SendAsync(connection.Id, notice, sendDeadline.Token).WaitAsync(sendDeadline.Token); }
                    catch (OperationCanceledException)
                    { registry.RemoveIfCurrent(connection); throw; }
                    return true;
                }, ct);
        }
        catch (AccountFault fault) when (fault.Status is 401 or 403 or 404 or 409)
        { if (fault.Status == 401) registry.RemoveIfCurrent(connection); }
    }
}
